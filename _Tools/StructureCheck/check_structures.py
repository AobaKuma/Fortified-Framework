#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
FFF structure-layout overlap checker (Fortified Feature Framework).

Primary purpose: catch POWER TRANSMITTER OVERLAPS in FFF_StructureDef /
StructureLayoutDef element layouts before they reach map generation.

Two transmitters (PowerConduit, HiddenConduit, Battery, generators, ...) may never
occupy the same cell. When they do, RimWorld logs at generation time:

    Tried to register trasmitter Battery43189 at (95, 0, 94), but there is already
    a power net here. There can't be two transmitters on the same cell.

...which names the spawned Thing, not the layout that placed it. This script points
at the exact def and cell instead.

It reports two severities:

  ERROR  two power transmitters on one cell.
         Always a bug: red log spam, and one of the two silently fails to connect.

  WARN   two edifices on one cell (wall vs. a 3x1 building, razorwire vs. a 2x2
         wreck, ...). Silent at runtime because GenSpawn's WipeMode.VanishOrMoveAside
         deletes the loser, but it leaves holes in walls and missing props.

The single most common cause of both: for multi-cell things `pos` is the CENTRE
cell, not the lower-left corner. A 3x1 building at x=9 covers x=8..10.

Usage:
    python check_structures.py [mod_root ...]

With no arguments it scans this mod root plus every sibling mod folder next to it,
so a framework checkout validates the content mods that depend on it.

Exit code is 0 even when problems are found (so the .bat can open the report);
check the report, or pass --strict to exit 1 on any ERROR.
"""

import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

REPORT_NAME = "StructureOverlaps.txt"

# Directories that never hold Defs but do hold thousands of files. Pruned during
# the walk (not merely skipped afterwards) — a source tree's obj/ and bin/ folders
# alone can dominate the whole run time.
PRUNE_DIRS = {
    "_source", "_sources", "obj", "bin", ".git", ".vs", ".idea", "migrationbackup",
    "unityproject", "textures", "sounds", "about", "packages", "node_modules",
    "publicizedassemblies", "assemblies", "news",
}


def walk_xml(root):
    """Yield .xml paths under root, pruning heavy non-Def directories."""
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d.lower() not in PRUNE_DIRS]
        for fn in filenames:
            if fn.lower().endswith(".xml"):
                yield os.path.join(dirpath, fn)

STRUCTURE_TAGS = (
    "Fortified.Structures.FFF_StructureDef",
    "FFF_StructureDef",
)

# ---------------------------------------------------------------------------
# Vanilla fallback table: defs that live in Core/DLCs, not in any mod folder.
# size is (x, z); "transmits" mirrors ThingDef.EverTransmitsPower.
# ---------------------------------------------------------------------------
VANILLA = {
    # transmitters
    "PowerConduit":             ((1, 1), True,  False),
    "HiddenConduit":            ((1, 1), True,  False),
    "WaterproofConduit":        ((1, 1), True,  False),
    "Battery":                  ((1, 2), True,  True),
    "SolarGenerator":           ((3, 3), True,  True),
    "WindTurbine":              ((2, 5), True,  True),
    "WoodFiredGenerator":       ((2, 2), True,  True),
    "ChemfuelPoweredGenerator": ((2, 2), True,  True),
    "GeothermalGenerator":      ((6, 6), True,  True),
    "PowerSwitch":              ((1, 1), True,  True),
    # common edifices used in ruin layouts
    "Wall":                     ((1, 1), False, True),
    "Door":                     ((1, 1), False, True),
    "Autodoor":                 ((1, 1), False, True),
    "Fence":                    ((1, 1), False, True),
    "FenceGate":                ((1, 1), False, True),
    "Sandbags":                 ((1, 1), False, True),
    "Barricade":                ((1, 1), False, True),
    "AncientRazorWire":         ((1, 1), False, True),
    "AncientBarrel":            ((1, 1), False, True),
    "AncientLargeCrate":        ((2, 2), False, True),
    "AncientMilitaryCrate":     ((2, 2), False, True),
    "AncientHermeticCrate":     ((2, 2), False, True),
    "AncientMachine":           ((1, 1), False, True),
    "AncientRustedTruck":       ((3, 6), False, True),
    "TrapIED_HighExplosive":    ((1, 1), False, False),
    # craters are terrain-like props, not edifices
    "CraterSmall":              ((1, 1), False, False),
    "CraterMedium":             ((2, 2), False, False),
    "CraterLarge":              ((3, 3), False, False),
    "ChunkSlagSteel":           ((1, 1), False, False),
}


# ---------------------------------------------------------------------------
# ThingDef table
# ---------------------------------------------------------------------------
def collect_thingdefs(roots):
    """Return {defName: (size, transmits, is_edifice)} from every ThingDef found."""
    raw = {}      # defName -> (size|None, transmits|None, edifice|None, parentName|None)
    by_name = {}  # Name= attribute -> same tuple, for inheritance

    for root in roots:
        for path in walk_xml(root):
            try:
                text = open(path, encoding="utf-8-sig", errors="replace").read()
            except OSError:
                continue
            if "<ThingDef" not in text:
                continue
            for blk in re.findall(r"<ThingDef\b[^>]*>.*?</ThingDef>", text, re.S):
                head = re.match(r"<ThingDef\b[^>]*>", blk).group(0)
                name = re.search(r'\bName\s*=\s*"([^"]+)"', head)
                parent = re.search(r'\bParentName\s*=\s*"([^"]+)"', head)
                dn = re.search(r"<defName>\s*([^<\s]+)\s*</defName>", blk)

                sz = re.search(r"<size>\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)\s*</size>", blk)
                size = (int(sz.group(1)), int(sz.group(2))) if sz else None

                transmits = None
                if re.search(r"CompProperties_Battery", blk):
                    transmits = True
                elif re.search(r"CompProperties_Power\b", blk):
                    m = re.search(r"<transmitsPower>\s*(\w+)\s*</transmitsPower>", blk)
                    transmits = bool(m) and m.group(1).lower() == "true"

                ed = re.search(r"<isEdifice>\s*(\w+)\s*</isEdifice>", blk)
                edifice = None
                if ed:
                    edifice = ed.group(1).lower() == "true"
                elif "<building>" in blk:
                    edifice = True  # has a building block, no explicit override

                rec = (size, transmits, edifice, parent.group(1) if parent else None)
                if dn:
                    raw[dn.group(1)] = rec
                if name:
                    by_name[name.group(1)] = rec

    def resolve(rec, depth=0):
        size, transmits, edifice, parent = rec
        while parent and depth < 12:
            prec = by_name.get(parent)
            if not prec:
                break
            psize, ptrans, pedif, pparent = prec
            if size is None:
                size = psize
            if transmits is None:
                transmits = ptrans
            if edifice is None:
                edifice = pedif
            parent, depth = pparent, depth + 1
        return (size or (1, 1), bool(transmits), True if edifice is None else edifice)

    table = {dn: resolve(rec) for dn, rec in raw.items()}
    for dn, (size, transmits, edifice) in VANILLA.items():
        table.setdefault(dn, (size, transmits, edifice))
    return table


# ---------------------------------------------------------------------------
# Geometry — mirrors Verse.GenAdj.OccupiedRect
# ---------------------------------------------------------------------------
def occupied_cells(pos, rot, size):
    sx, sz = (size[1], size[0]) if rot in (1, 3) else size
    x0 = pos[0] - (sx - 1) // 2
    z0 = pos[1] - (sz - 1) // 2
    return [(x, z) for x in range(x0, x0 + sx) for z in range(z0, z0 + sz)]


def ints(text):
    return [int(v) for v in re.findall(r"-?\d+", text or "")]


def element_placements(el):
    """Yield (defName, (x, z), rot) for the static thing elements."""
    cls = el.get("Class", "")
    dn = (el.findtext("def") or "").strip()
    if not dn or "Terrain" in cls or "Roof" in cls or "Pawn" in cls:
        return
    rot = ints(el.findtext("rot") or "0")
    rot = rot[0] % 4 if rot else 0

    if "ThingRect" in cls:
        p, s = ints(el.findtext("pos")), ints(el.findtext("size"))
        if len(p) < 3 or len(s) < 2:
            return
        for x in range(p[0], p[0] + s[0]):
            for z in range(p[2], p[2] + s[1]):
                yield dn, (x, z), rot
    elif "ThingScatter" in cls:
        pl = el.find("posList")
        if pl is None:
            return
        for li in pl:
            p = ints(li.text)
            if len(p) >= 3:
                yield dn, (p[0], p[2]), rot
    elif cls.endswith("FFF_Element_Thing"):
        p = ints(el.findtext("pos"))
        if len(p) >= 3:
            yield dn, (p[0], p[2]), rot


# ---------------------------------------------------------------------------
def check_structures(roots, table):
    errors, warns, unknown, parse_errors = [], [], set(), []

    for root in roots:
        for path in walk_xml(root):
            try:
                text = open(path, encoding="utf-8-sig", errors="replace").read()
            except OSError as exc:
                parse_errors.append(f"{path}: {exc}")
                continue
            if not any(t in text for t in STRUCTURE_TAGS):
                continue
            try:
                tree = ET.fromstring(text)
            except ET.ParseError as exc:
                parse_errors.append(f"{path}: {exc}")
                continue

            rel = os.path.relpath(path, os.path.dirname(root))
            for d in tree:
                if d.tag not in STRUCTURE_TAGS:
                    continue
                name = d.findtext("defName") or "<no defName>"
                els = d.find("elements")
                if els is None:
                    continue

                trans_cells = defaultdict(list)
                edif_cells = defaultdict(list)
                for el in els:
                    for dn, pos, rot in element_placements(el):
                        rec = table.get(dn)
                        if rec is None:
                            unknown.add(dn)
                            continue
                        size, transmits, edifice = rec
                        for c in occupied_cells(pos, rot, size):
                            if transmits:
                                trans_cells[c].append(dn)
                            # Conduits are isEdifice=false, so they legitimately share a
                            # cell with a wall or turret; only count real edifices here.
                            if edifice:
                                edif_cells[c].append(dn)

                for c, defs in sorted(trans_cells.items()):
                    if len(defs) > 1:
                        errors.append(f"{name}  cell {c}  ->  {' + '.join(defs)}\n"
                                      f"        {rel}")
                for c, defs in sorted(edif_cells.items()):
                    if len(defs) > 1:
                        warns.append(f"{name}  cell {c}  ->  {' + '.join(defs)}\n"
                                     f"        {rel}")

    return errors, warns, sorted(unknown), parse_errors


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    strict = "--strict" in sys.argv

    here = os.path.dirname(os.path.abspath(__file__))
    mod_root = os.path.abspath(os.path.join(here, "..", ".."))
    if args:
        roots = [os.path.abspath(a) for a in args]
    else:
        mods_dir = os.path.dirname(mod_root)
        roots = [mod_root]
        for entry in sorted(os.listdir(mods_dir)):
            p = os.path.join(mods_dir, entry)
            if os.path.isdir(p) and p != mod_root and not entry.startswith("."):
                roots.append(p)

    table = collect_thingdefs(roots)
    errors, warns, unknown, parse_errors = check_structures(roots, table)

    lines = []
    lines.append("FFF structure-layout overlap report")
    lines.append("=" * 78)
    lines.append("")
    lines.append("Scanned roots:")
    for r in roots:
        lines.append(f"  {r}")
    lines.append(f"\nThingDefs resolved: {len(table)}")
    lines.append("")
    lines.append(f"ERROR  two power transmitters on one cell : {len(errors)}")
    lines.append(f"WARN   two edifices on one cell           : {len(warns)}")
    lines.append("")

    lines.append("-" * 78)
    lines.append("ERRORS - power transmitters sharing a cell")
    lines.append("PowerNetGrid will log 'there can't be two transmitters on the same cell'.")
    lines.append("-" * 78)
    lines.extend(errors or ["  (none)"])
    lines.append("")

    lines.append("-" * 78)
    lines.append("WARNINGS - edifices sharing a cell")
    lines.append("Silent at runtime: GenSpawn's WipeMode deletes the loser, leaving holes.")
    lines.append("Remember multi-cell things use their CENTRE cell as pos.")
    lines.append("Caveat: footprints for Core/DLC defs come from the hand-maintained VANILLA")
    lines.append("table in this script, so a wrong entry there can produce a false warning.")
    lines.append("-" * 78)
    lines.extend(warns or ["  (none)"])
    lines.append("")

    if unknown:
        lines.append("-" * 78)
        lines.append("UNRESOLVED defs (not found in any scanned mod, not in the vanilla table)")
        lines.append("Their footprint was assumed unknown and skipped - add them to VANILLA")
        lines.append("in this script if they matter.")
        lines.append("-" * 78)
        lines.extend(f"  {u}" for u in unknown)
        lines.append("")

    if parse_errors:
        lines.append("-" * 78)
        lines.append("PARSE ERRORS")
        lines.append("-" * 78)
        lines.extend(f"  {p}" for p in parse_errors)
        lines.append("")

    report = "\n".join(lines)
    out = os.path.join(here, REPORT_NAME)
    with open(out, "w", encoding="utf-8") as fh:
        fh.write(report)

    print(report)
    print(f"\nReport written to {out}")

    if strict and errors:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
