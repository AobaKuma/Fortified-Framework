#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
FFF local assembly builder (Fortified Feature Framework).

Purpose: build every C# project of the local RimWorld mods in one shot, in the
right order, and turn MSBuild's wall of text into a short, file-grouped report.

What it does
------------
1. DISCOVER   walks the mod root this script lives in, plus the sibling mod
              folders next to it, and collects every .csproj that is not inside
              obj/ bin/ .vs/ *Backup*/ ... (see PRUNE_DIRS).

2. ORDER      reads each project's AssemblyName + OutputPath, then links
              <ProjectReference> and <Reference><HintPath> entries that point at
              another discovered project's output DLL. FortifiedCE references
              ..\\..\\1.6\\Assemblies\\Fortified.dll, so Fortified is always built
              first. Cycles are reported and the graph falls back to path order
              instead of dying.

3. GUARD      refuses to clobber a DLL that RimWorld currently has loaded: the
              game keeps Assemblies\\*.dll memory-mapped, the write fails halfway
              and you are left with a truncated DLL and a mod that no longer
              loads. If a RimWorld process is running you get a prompt (or a
              clean abort when there is no console to prompt on).

4. BUILD      `dotnet build <proj> -c Release`, one project at a time, with a
              timeout. First failure aborts the run (--keep-going relaxes this to
              "skip only the projects that depend on the broken one").

5. REPORT     parses the output for `File.cs(12,34): error CS0103: ...`, dedupes
              (MSBuild repeats a diagnostic once per target), groups by file and
              writes BuildReport.txt next to this script.

Usage
-----
    python build_dlls.py                   build every discovered project
    python build_dlls.py Fortified         only projects whose name matches
    python build_dlls.py --list            show what would be built, then exit
    python build_dlls.py --dry-run         full pipeline minus the dotnet calls
    python build_dlls.py --self            only this mod, ignore siblings
    python build_dlls.py -c Debug          configuration (default: Release)
    python build_dlls.py --keep-going      do not abort the whole run on failure
    python build_dlls.py --yes             never prompt (unattended / CI)
    python build_dlls.py --no-restore      offline; skip the NuGet restore
    python build_dlls.py --warnings        list warnings in the report too
    python build_dlls.py --verbose         stream the raw MSBuild output

Positional arguments are either a path (a folder to scan, or a .csproj to build)
or a case-insensitive substring matched against the project name.

Exit codes: 0 all good, 1 at least one project failed, 2 environment/usage
problem (no dotnet, nothing found, aborted by the user), 130 Ctrl-C.

Everything here is stdlib-only and read-only outside the build outputs, so it is
safe to run on a whim.
"""

import argparse
import os
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from collections import OrderedDict

REPORT_NAME = "BuildReport.txt"
DEFAULT_CONFIG = "Release"
DEFAULT_TIMEOUT = 900  # seconds per project; a cold NuGet restore is slow

# Never descend into these. Matched case-insensitively against the folder name;
# names containing "backup" are pruned as well (1.5_Backup, MigrationBackup...).
PRUNE_DIRS = {
    "obj", "bin", ".git", ".vs", ".idea", ".vscode", "packages", "node_modules",
    "unityproject", "textures", "sounds", "languages", "assemblies",
    "publicizedassemblies", "leagecy", "legacy", "__pycache__",
}

# A sibling folder is treated as a buildable mod only if it holds one of these.
SOURCE_DIR_HINTS = {"_source", "_sources", "source", "sources", "src"}

PROCESS_HINTS = ("rimworldwin64", "rimworld")


# ---------------------------------------------------------------------------
# console
# ---------------------------------------------------------------------------

def _make_stream_utf8_safe(stream):
    """Windows consoles default to cp950 here; a stray glyph must not kill a build."""
    try:
        stream.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass


_make_stream_utf8_safe(sys.stdout)
_make_stream_utf8_safe(sys.stderr)


def say(msg=""):
    try:
        print(msg, flush=True)
    except Exception:
        # Absolute last resort: never let logging abort a build.
        try:
            sys.stdout.write(str(msg).encode("ascii", "replace").decode("ascii") + "\n")
        except Exception:
            pass


def rule(char="-", width=74):
    say(char * width)


# ---------------------------------------------------------------------------
# project model
# ---------------------------------------------------------------------------

class Project(object):
    def __init__(self, path):
        self.path = path                     # absolute .csproj path
        self.dir = os.path.dirname(path)
        self.name = os.path.splitext(os.path.basename(path))[0]
        self.assembly_name = self.name
        self.output_dir = None               # absolute, or None when unresolvable
        self.output_dll = None
        self.mod_root = None
        self.raw_refs = []                   # absolute paths this project references
        self.project_refs = []               # absolute .csproj paths
        self.deps = set()                    # resolved Project keys
        self.parse_error = None

        # filled during the run
        self.status = "pending"              # pending|ok|failed|skipped|dry
        self.duration = 0.0
        self.errors = []
        self.warnings = []
        self.exit_code = None
        self.output_before = None            # (mtime, size) of the DLL pre-build

    @property
    def key(self):
        return os.path.normcase(self.path)

    def __repr__(self):
        return "<Project %s>" % self.name


def _strip_ns(tag):
    return tag.split("}", 1)[-1] if "}" in tag else tag


def _text(elem):
    return (elem.text or "").strip()


def _expand_msbuild(value, project_dir):
    """Resolve the handful of MSBuild macros that show up in mod csproj files.

    Anything left with a $( in it is unresolvable from here; the caller treats
    that as "output location unknown" rather than guessing a wrong path.
    """
    if not value:
        return None
    v = value.strip()
    v = v.replace("$(MSBuildThisFileDirectory)", project_dir + os.sep)
    v = v.replace("$(MSBuildProjectDirectory)", project_dir)
    v = v.replace("$(ProjectDir)", project_dir + os.sep)
    if "$(" in v:
        return None
    v = v.replace("\\", os.sep).replace("/", os.sep)
    if not os.path.isabs(v):
        v = os.path.join(project_dir, v)
    try:
        return os.path.normpath(os.path.abspath(v))
    except Exception:
        return None


def parse_project(path):
    """Read a .csproj. Never raises: a broken file becomes a project with
    parse_error set, so discovery keeps going and the report says why."""
    proj = Project(path)
    try:
        # csproj files here are UTF-8 with BOM; utf-8-sig handles both.
        with open(path, "r", encoding="utf-8-sig", errors="replace") as fh:
            text = fh.read()
        root = ET.fromstring(text)
    except Exception as exc:
        proj.parse_error = "%s: %s" % (type(exc).__name__, exc)
        return proj

    props = {}
    for elem in root.iter():
        tag = _strip_ns(elem.tag)
        if tag in ("AssemblyName", "OutputPath", "TargetFramework",
                   "AppendTargetFrameworkToOutputPath", "Configuration"):
            # Last one wins, mirroring MSBuild's top-to-bottom evaluation.
            # Conditioned PropertyGroups are ignored: OutputPath in these
            # projects is unconditional, and a wrong guess only degrades the
            # post-build size check, never the build itself.
            val = _text(elem)
            if val:
                props[tag] = val
        elif tag == "ProjectReference":
            inc = elem.get("Include")
            resolved = _expand_msbuild(inc, proj.dir)
            if resolved:
                proj.project_refs.append(resolved)
        elif tag == "HintPath":
            resolved = _expand_msbuild(_text(elem), proj.dir)
            if resolved:
                proj.raw_refs.append(resolved)

    proj.assembly_name = props.get("AssemblyName") or proj.name
    out = _expand_msbuild(props.get("OutputPath"), proj.dir)
    if out:
        append_tfm = props.get("AppendTargetFrameworkToOutputPath", "true")
        if append_tfm.strip().lower() != "false" and props.get("TargetFramework"):
            out = os.path.join(out, props["TargetFramework"])
        proj.output_dir = out
        proj.output_dll = os.path.join(out, proj.assembly_name + ".dll")
    return proj


# ---------------------------------------------------------------------------
# discovery
# ---------------------------------------------------------------------------

def should_prune(dirname):
    low = dirname.lower()
    return low in PRUNE_DIRS or "backup" in low or low.startswith(".")


def walk_for_csproj(root, limit=4000):
    """Depth-limited, prune-heavy walk. Returns absolute .csproj paths."""
    found = []
    visited = 0
    for dirpath, dirnames, filenames in os.walk(root):
        visited += 1
        if visited > limit:
            say("  ! stopped scanning %s after %d folders" % (root, limit))
            break
        dirnames[:] = [d for d in dirnames if not should_prune(d)]
        for fn in filenames:
            if fn.lower().endswith(".csproj"):
                found.append(os.path.normpath(os.path.join(dirpath, fn)))
    return found


def looks_like_dev_mod(folder):
    try:
        entries = os.listdir(folder)
    except OSError:
        return False
    lowered = {e.lower() for e in entries}
    if lowered & SOURCE_DIR_HINTS:
        return True
    return any(e.lower().endswith((".csproj", ".sln")) for e in entries)


def locate_roots(script_dir, self_only):
    """Own mod root first, then sibling mod folders that look like dev mods.

    Layout assumed: <Mods>/<ModName>/_Tools/BuildDLL/build_dlls.py
    If the script has been moved somewhere else, fall back to its own folder so
    an explicit path argument still works.
    """
    roots = []
    mod_root = os.path.abspath(os.path.join(script_dir, "..", ".."))
    mods_dir = os.path.dirname(mod_root)

    if os.path.isdir(mod_root):
        roots.append(mod_root)
    else:
        roots.append(script_dir)
        return roots

    if self_only:
        return roots

    if os.path.isdir(mods_dir):
        try:
            siblings = sorted(os.listdir(mods_dir))
        except OSError as exc:
            say("  ! cannot list sibling mods (%s); building this mod only" % exc)
            return roots
        for name in siblings:
            path = os.path.join(mods_dir, name)
            if not os.path.isdir(path):
                continue
            if os.path.normcase(path) == os.path.normcase(mod_root):
                continue
            if should_prune(name):
                continue
            if looks_like_dev_mod(path):
                roots.append(path)
    return roots


def discover(roots):
    projects = OrderedDict()
    for root in roots:
        for path in walk_for_csproj(root):
            proj = parse_project(path)
            proj.mod_root = root
            if proj.key in projects:
                continue
            projects[proj.key] = proj
    return projects


# ---------------------------------------------------------------------------
# dependency graph
# ---------------------------------------------------------------------------

def link_dependencies(projects):
    """A depends on B when A references B's output DLL, or lists B as a
    ProjectReference. Everything else (RimWorld's managed DLLs, CE) is external
    and ignored."""
    by_output = {}
    for proj in projects.values():
        if proj.output_dll:
            by_output.setdefault(os.path.normcase(proj.output_dll), proj)

    for proj in projects.values():
        for ref in proj.raw_refs:
            other = by_output.get(os.path.normcase(ref))
            if other is not None and other.key != proj.key:
                proj.deps.add(other.key)
        for ref in proj.project_refs:
            other = projects.get(os.path.normcase(ref))
            if other is not None and other.key != proj.key:
                proj.deps.add(other.key)


def topo_order(projects):
    """Kahn's algorithm, ties broken by path so the order is reproducible.
    Returns (ordered_projects, cycle_keys)."""
    remaining = {k: set(p.deps & set(projects.keys())) for k, p in projects.items()}
    ordered = []
    while remaining:
        ready = sorted([k for k, deps in remaining.items() if not deps],
                       key=lambda k: projects[k].path.lower())
        if not ready:
            break  # cycle: everything left depends on something still pending
        for key in ready:
            ordered.append(projects[key])
            del remaining[key]
        for deps in remaining.values():
            deps.difference_update(ready)
    cycle = sorted(remaining.keys(), key=lambda k: projects[k].path.lower())
    for key in cycle:
        ordered.append(projects[key])  # build them anyway, in path order
    return ordered, cycle


def dependents_of(target, ordered):
    """Transitive closure of "projects that would consume target's DLL"."""
    doomed = {target.key}
    changed = True
    while changed:
        changed = False
        for proj in ordered:
            if proj.key in doomed:
                continue
            if proj.deps & doomed:
                doomed.add(proj.key)
                changed = True
    doomed.discard(target.key)
    return [p for p in ordered if p.key in doomed]


# ---------------------------------------------------------------------------
# environment guards
# ---------------------------------------------------------------------------

def find_dotnet():
    exe = shutil.which("dotnet")
    if exe:
        return exe
    # Common Windows install locations, in case PATH is not set up for this shell.
    for candidate in (
        r"C:\Program Files\dotnet\dotnet.exe",
        r"C:\Program Files (x86)\dotnet\dotnet.exe",
        os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"),
    ):
        if candidate and os.path.isfile(candidate):
            return candidate
    return None


def rimworld_processes():
    """Best-effort: names of running RimWorld-ish processes. Never raises."""
    names = []
    try:
        if os.name == "nt":
            out = subprocess.run(
                ["tasklist", "/FO", "CSV", "/NH"],
                capture_output=True, text=True, timeout=20,
                encoding="utf-8", errors="replace",
            ).stdout or ""
            for line in out.splitlines():
                first = line.split('","')[0].strip('" ').lower()
                if any(hint in first for hint in PROCESS_HINTS):
                    names.append(first)
        else:
            out = subprocess.run(
                ["ps", "-eo", "comm"],
                capture_output=True, text=True, timeout=20,
                encoding="utf-8", errors="replace",
            ).stdout or ""
            for line in out.splitlines():
                low = line.strip().lower()
                if any(hint in low for hint in PROCESS_HINTS):
                    names.append(low)
    except Exception:
        return []  # detection is a courtesy, not a gate
    return sorted(set(names))


def dll_is_locked(path):
    """True when the file exists but cannot be opened for writing.
    On Windows a DLL loaded by RimWorld gives a sharing violation here."""
    if not path or not os.path.isfile(path):
        return False
    try:
        with open(path, "ab"):
            return False
    except OSError:
        return True
    except Exception:
        return False


def confirm(question, assume_yes):
    if assume_yes:
        say("  (--yes) %s -> continuing" % question)
        return True
    if not sys.stdin or not sys.stdin.isatty():
        say("  no console to ask on; aborting. Re-run with --yes to override.")
        return False
    try:
        answer = input("  %s [y/N] " % question).strip().lower()
    except (EOFError, KeyboardInterrupt):
        say("")
        return False
    return answer in ("y", "yes")


# ---------------------------------------------------------------------------
# MSBuild output parsing
# ---------------------------------------------------------------------------

# File.cs(12,34): error CS0103: message [C:\path\Proj.csproj]
DIAG_FILE = re.compile(
    r"^\s*(?P<file>.+?)\((?P<line>\d+)(?:,(?P<col>\d+))?\)\s*:\s*"
    r"(?P<sev>error|warning)\s+(?P<code>[A-Za-z]+[0-9]+)\s*:\s*(?P<msg>.*?)"
    r"(?:\s*\[(?P<proj>[^\]]+)\])?\s*$",
    re.IGNORECASE,
)
# MSBUILD : error MSB1009: ... / CSC : error CS1041: ...
DIAG_PLAIN = re.compile(
    r"^\s*(?P<origin>[^:]*?)\s*:\s*(?P<sev>error|warning)\s+"
    r"(?P<code>[A-Za-z]+[0-9]+)\s*:\s*(?P<msg>.*?)"
    r"(?:\s*\[(?P<proj>[^\]]+)\])?\s*$",
    re.IGNORECASE,
)


class Diagnostic(object):
    __slots__ = ("file", "line", "col", "sev", "code", "msg")

    def __init__(self, file, line, col, sev, code, msg):
        self.file = file
        self.line = line
        self.col = col
        self.sev = sev
        self.code = code
        self.msg = msg

    @property
    def ident(self):
        return (os.path.normcase(self.file or ""), self.line, self.col,
                self.code.upper(), self.msg)

    def where(self):
        if self.line:
            return "%s:%s%s" % (self.file, self.line,
                                (":%s" % self.col) if self.col else "")
        return self.file or "(build)"


def parse_diagnostics(lines):
    """Returns (errors, warnings), deduped and in first-seen order."""
    errors, warnings = [], []
    seen = set()
    for raw in lines:
        line = raw.rstrip("\r\n")
        if not line.strip():
            continue
        m = DIAG_FILE.match(line)
        if m and len(m.group("file") or "") <= 260:
            diag = Diagnostic(
                (m.group("file") or "").strip(),
                int(m.group("line")),
                int(m.group("col")) if m.group("col") else None,
                m.group("sev").lower(), m.group("code"), (m.group("msg") or "").strip(),
            )
        else:
            m = DIAG_PLAIN.match(line)
            if not m:
                continue
            origin = (m.group("origin") or "").strip()
            # Guard against prose that happens to contain " : error X1: ".
            if len(origin) > 120:
                continue
            diag = Diagnostic(origin, None, None, m.group("sev").lower(),
                              m.group("code"), (m.group("msg") or "").strip())
        if diag.ident in seen:
            continue
        seen.add(diag.ident)
        (errors if diag.sev == "error" else warnings).append(diag)
    return errors, warnings


def group_by_file(diags):
    grouped = OrderedDict()
    for d in diags:
        grouped.setdefault(d.file or "(build)", []).append(d)
    for items in grouped.values():
        items.sort(key=lambda d: (d.line or 0, d.col or 0, d.code))
    return grouped


# ---------------------------------------------------------------------------
# the build itself
# ---------------------------------------------------------------------------

def snapshot(path):
    try:
        st = os.stat(path)
        return (st.st_mtime, st.st_size)
    except OSError:
        return None


def run_build(proj, dotnet, args):
    cmd = [dotnet, "build", proj.path, "-c", args.configuration,
           "--nologo", "-p:GenerateFullPaths=true"]
    if args.no_restore:
        cmd.append("--no-restore")
    if args.extra:
        cmd.extend(args.extra)

    proj.output_before = snapshot(proj.output_dll) if proj.output_dll else None
    started = time.time()
    lines = []
    try:
        popen = subprocess.Popen(
            cmd, cwd=proj.dir, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace", bufsize=1,
        )
    except FileNotFoundError:
        proj.status = "failed"
        proj.exit_code = -1
        proj.errors = [Diagnostic("(build)", None, None, "error", "TOOL0",
                                  "dotnet could not be launched")]
        return
    except Exception as exc:
        proj.status = "failed"
        proj.exit_code = -1
        proj.errors = [Diagnostic("(build)", None, None, "error", "TOOL0", str(exc))]
        return

    deadline = started + args.timeout
    timed_out = False
    try:
        assert popen.stdout is not None
        for line in popen.stdout:
            lines.append(line)
            if args.verbose:
                say("    | " + line.rstrip())
            elif re.search(r"\b(error|warning)\s+[A-Za-z]+[0-9]+\s*:", line, re.I):
                say("    | " + line.rstrip())
            if time.time() > deadline:
                timed_out = True
                break
    except KeyboardInterrupt:
        popen.kill()
        raise
    finally:
        if timed_out:
            try:
                popen.kill()
            except Exception:
                pass
        try:
            popen.wait(timeout=30)
        except Exception:
            pass

    proj.duration = time.time() - started
    proj.exit_code = popen.returncode
    proj.errors, proj.warnings = parse_diagnostics(lines)

    if timed_out:
        proj.status = "failed"
        proj.errors.append(Diagnostic("(build)", None, None, "error", "TIMEOUT",
                                      "build exceeded %ds and was killed" % args.timeout))
        return

    if popen.returncode != 0:
        proj.status = "failed"
        if not proj.errors:
            tail = [l.rstrip() for l in lines[-8:] if l.strip()]
            detail = ("dotnet build failed without a parsable diagnostic. Tail: "
                      + " / ".join(tail)) if tail else "dotnet build failed."
            proj.errors.append(Diagnostic(
                "(build)", None, None, "error", "EXIT%d" % (popen.returncode or 0),
                detail))
        return

    # Exit code 0 but the DLL did not move: warn rather than declare success.
    proj.status = "ok"
    if proj.output_dll:
        after = snapshot(proj.output_dll)
        if after is None:
            proj.status = "failed"
            proj.errors.append(Diagnostic(
                "(build)", None, None, "error", "NOOUT",
                "build reported success but %s does not exist" % proj.output_dll))
        elif proj.output_before is not None and after == proj.output_before:
            proj.warnings.append(Diagnostic(
                "(build)", None, None, "warning", "NOCHANGE",
                "output DLL was not rewritten (up to date, or the write was blocked)"))


# ---------------------------------------------------------------------------
# report
# ---------------------------------------------------------------------------

def write_report(path, ordered, args, elapsed, roots, cycle):
    lines = []
    add = lines.append
    add("FFF build report")
    add("generated : %s" % time.strftime("%Y-%m-%d %H:%M:%S"))
    add("config    : %s" % args.configuration)
    add("roots     : %s" % ", ".join(roots))
    add("elapsed   : %.1fs" % elapsed)
    add("")
    add("=" * 74)
    add("SUMMARY")
    add("=" * 74)
    for proj in ordered:
        add("  %-8s %-24s %6.1fs  E%-3d W%-3d  %s" % (
            proj.status.upper(), proj.name, proj.duration,
            len(proj.errors), len(proj.warnings),
            proj.output_dll or "(output path unknown)"))
    if cycle:
        add("")
        add("  ! circular references between: %s"
            % ", ".join(sorted(os.path.basename(k) for k in cycle)))

    for proj in ordered:
        if not proj.errors and not (args.warnings and proj.warnings):
            continue
        add("")
        add("=" * 74)
        add("%s  (%s)" % (proj.name, proj.path))
        add("=" * 74)
        if proj.errors:
            add("")
            add("ERRORS (%d)" % len(proj.errors))
            for fname, diags in group_by_file(proj.errors).items():
                add("")
                add("  %s" % fname)
                for d in diags:
                    loc = ("line %s" % d.line) if d.line else "-"
                    add("    %-10s %-8s %s" % (loc, d.code, d.msg))
        if args.warnings and proj.warnings:
            add("")
            add("WARNINGS (%d)" % len(proj.warnings))
            for fname, diags in group_by_file(proj.warnings).items():
                add("")
                add("  %s" % fname)
                for d in diags:
                    loc = ("line %s" % d.line) if d.line else "-"
                    add("    %-10s %-8s %s" % (loc, d.code, d.msg))

    text = "\n".join(lines) + "\n"
    try:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(text)
        return path
    except OSError as exc:
        say("  ! could not write %s (%s)" % (path, exc))
        return None


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

def build_arg_parser():
    p = argparse.ArgumentParser(
        prog="build_dlls.py",
        description="Build the local RimWorld mod assemblies, in dependency order.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument("targets", nargs="*",
                   help="folder to scan, .csproj to build, or a name substring")
    p.add_argument("-c", "--configuration", default=DEFAULT_CONFIG,
                   help="build configuration (default: %s)" % DEFAULT_CONFIG)
    p.add_argument("--self", action="store_true",
                   help="only this mod folder; ignore sibling mods")
    p.add_argument("--list", action="store_true",
                   help="show the discovered projects and build order, then exit")
    p.add_argument("--dry-run", action="store_true",
                   help="do everything except invoking dotnet")
    p.add_argument("--keep-going", action="store_true",
                   help="on failure keep building projects that do not depend on it")
    p.add_argument("--yes", "-y", action="store_true",
                   help="answer every prompt with yes (unattended runs)")
    p.add_argument("--no-restore", action="store_true",
                   help="pass --no-restore to dotnet (offline builds)")
    p.add_argument("--no-process-check", action="store_true",
                   help="skip the RimWorld-is-running guard")
    p.add_argument("--warnings", action="store_true",
                   help="include warnings in BuildReport.txt")
    p.add_argument("--verbose", action="store_true",
                   help="stream the raw dotnet output")
    p.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT,
                   help="per-project timeout in seconds (default: %d)" % DEFAULT_TIMEOUT)
    p.add_argument("--report", default=None,
                   help="path of the report file (default: %s next to this script)"
                        % REPORT_NAME)
    p.add_argument("--extra", nargs=argparse.REMAINDER, default=[],
                   help="everything after --extra is forwarded to dotnet build")
    return p


def main(argv):
    args = build_arg_parser().parse_args(argv)
    script_dir = os.path.dirname(os.path.abspath(__file__))
    report_path = args.report or os.path.join(script_dir, REPORT_NAME)

    explicit_projects, explicit_roots, filters = [], [], []
    for target in args.targets:
        if os.path.isfile(target) and target.lower().endswith(".csproj"):
            explicit_projects.append(os.path.abspath(target))
        elif os.path.isdir(target):
            explicit_roots.append(os.path.abspath(target))
        else:
            filters.append(target.lower())

    if explicit_roots:
        roots = explicit_roots
    elif explicit_projects:
        roots = []
    else:
        roots = locate_roots(script_dir, args.self)

    say("")
    rule("=")
    say("FFF assembly builder   config=%s" % args.configuration)
    rule("=")
    for root in roots:
        say("  scan  %s" % root)

    projects = discover(roots) if roots else OrderedDict()
    for path in explicit_projects:
        proj = parse_project(path)
        proj.mod_root = os.path.dirname(path)
        projects.setdefault(proj.key, proj)

    broken = [p for p in projects.values() if p.parse_error]
    for p in broken:
        say("  ! unreadable project %s (%s)" % (p.path, p.parse_error))
        del projects[p.key]

    if not projects:
        say("")
        say("Nothing to build: no .csproj found.")
        say("Pass a folder or a .csproj explicitly, e.g.")
        say('  python build_dlls.py "D:\\...\\_Sources\\Fortified\\Fortified.csproj"')
        return 2

    link_dependencies(projects)
    ordered, cycle = topo_order(projects)

    if filters:
        keep = {p.key for p in ordered
                if any(f in p.name.lower() or f in p.path.lower() for f in filters)}
        if not keep:
            say("")
            say("No project matched: %s" % ", ".join(filters))
            say("Discovered: %s" % ", ".join(p.name for p in ordered))
            return 2
        # Pull in the dependencies of what matched, or the build order is a lie.
        changed = True
        while changed:
            changed = False
            for proj in ordered:
                if proj.key in keep and not proj.deps <= keep:
                    keep |= (proj.deps & set(projects.keys()))
                    changed = True
        ordered = [p for p in ordered if p.key in keep]

    say("")
    say("Build order (%d project%s):" % (len(ordered), "" if len(ordered) == 1 else "s"))
    for i, proj in enumerate(ordered, 1):
        dep_names = sorted(projects[k].name for k in proj.deps if k in projects)
        suffix = ("  <- %s" % ", ".join(dep_names)) if dep_names else ""
        say("  %d. %-24s %s%s" % (i, proj.name, proj.path, suffix))
    if cycle:
        say("  ! circular reference detected; falling back to path order for: %s"
            % ", ".join(sorted(os.path.basename(k) for k in cycle)))

    if args.list:
        say("")
        for proj in ordered:
            say("  %-24s -> %s" % (proj.name, proj.output_dll or "(unknown output)"))
        return 0

    dotnet = find_dotnet()
    if dotnet is None and not args.dry_run:
        say("")
        say("ERROR: 'dotnet' was not found on PATH.")
        say("Install the .NET SDK (https://dotnet.microsoft.com/download) and re-run.")
        return 2

    # --- guard: RimWorld holding the DLLs -----------------------------------
    if not args.no_process_check and not args.dry_run:
        procs = rimworld_processes()
        locked = [p.output_dll for p in ordered if dll_is_locked(p.output_dll)]
        if procs or locked:
            say("")
            rule("!")
            if procs:
                say("  RimWorld appears to be running: %s" % ", ".join(procs))
            for dll in locked:
                say("  locked (in use): %s" % dll)
            say("  Building now usually fails mid-write and leaves a truncated DLL.")
            say("  Close RimWorld first.")
            rule("!")
            if not confirm("Build anyway?", args.yes):
                say("Aborted.")
                return 2

    # --- build ---------------------------------------------------------------
    started = time.time()
    failed_any = False
    skipped = set()
    for i, proj in enumerate(ordered, 1):
        if proj.key in skipped:
            proj.status = "skipped"
            say("")
            say("[%d/%d] SKIP %s (a dependency failed)" % (i, len(ordered), proj.name))
            continue

        say("")
        say("[%d/%d] %s  (%s)" % (i, len(ordered), proj.name, args.configuration))
        if args.dry_run:
            proj.status = "dry"
            say("    dry-run: would run dotnet build %s -c %s"
                % (proj.path, args.configuration))
            continue

        try:
            run_build(proj, dotnet, args)
        except KeyboardInterrupt:
            say("")
            say("Interrupted by user.")
            return 130

        if proj.status == "ok":
            note = ""
            if proj.output_dll and os.path.isfile(proj.output_dll):
                note = "  (%.0f KB)" % (os.path.getsize(proj.output_dll) / 1024.0)
            say("    OK   %.1fs  %d warning%s%s"
                % (proj.duration, len(proj.warnings),
                   "" if len(proj.warnings) == 1 else "s", note))
        else:
            failed_any = True
            say("    FAIL %.1fs  %d error%s (exit %s)"
                % (proj.duration, len(proj.errors),
                   "" if len(proj.errors) == 1 else "s", proj.exit_code))
            for d in proj.errors[:5]:
                say("      %s  %s: %s" % (d.where(), d.code, d.msg))
            if len(proj.errors) > 5:
                say("      ... %d more, see the report" % (len(proj.errors) - 5))

            downstream = dependents_of(proj, ordered)
            if args.keep_going:
                for dep in downstream:
                    skipped.add(dep.key)
                if downstream:
                    say("    skipping dependents: %s"
                        % ", ".join(d.name for d in downstream))
            else:
                for rest in ordered[i:]:
                    rest.status = "skipped"
                say("    aborting the run (--keep-going to continue with the rest)")
                break

    elapsed = time.time() - started

    # --- report --------------------------------------------------------------
    say("")
    rule("=")
    total_err = sum(len(p.errors) for p in ordered)
    total_warn = sum(len(p.warnings) for p in ordered)
    for proj in ordered:
        say("  %-8s %-24s %6.1fs  %d error(s), %d warning(s)"
            % (proj.status.upper(), proj.name, proj.duration,
               len(proj.errors), len(proj.warnings)))
    say("  total: %d error(s), %d warning(s) in %.1fs"
        % (total_err, total_warn, elapsed))
    rule("=")

    written = write_report(report_path, ordered, args, elapsed,
                           roots or [os.path.dirname(p) for p in explicit_projects],
                           cycle)
    if written:
        say("  report: %s" % written)

    if args.dry_run:
        return 0
    return 1 if failed_any else 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        say("")
        say("Interrupted.")
        sys.exit(130)
