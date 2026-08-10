#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
steam_mod_comment_analyzer.py
=============================

抓取單一 Steam Workshop 模組的「全部留言」，做規則式分析後產生一份可互動的 HTML 報告。

依賴：無（純 Python 3.8+ 標準函式庫）。

用法
----
    python steam_mod_comment_analyzer.py 569264526
    python steam_mod_comment_analyzer.py "https://steamcommunity.com/sharedfiles/filedetails/?id=569264526"
    python steam_mod_comment_analyzer.py 569264526 --out report.html --dump-json --dump-csv
    python steam_mod_comment_analyzer.py 569264526 --limit 500 --delay 1.5
    python steam_mod_comment_analyzer.py 569264526 --llm          # 需要環境變數 ANTHROPIC_API_KEY

原理
----
1. `ISteamRemoteStorage/GetPublishedFileDetails/v1/`（官方、免 API key）取得模組 metadata 與作者 SteamID64。
2. POST 到未公開端點
   `https://steamcommunity.com/comment/PublishedFile_Public/render/{creator}/{fileid}/`
   body: `start=<offset>&totalcount=0&count=<n>`，回傳 JSON，內含 `total_count` 與 `comments_html`。
3. 以標準庫 HTMLParser 解析 `comments_html`，抽出每則留言的作者、SteamID、時間戳與內文。
4. 規則式分析：Bug 回報、問答與解法、相容性與依賴、情緒與時間趨勢、重複問題聚類。
5. 輸出單一 HTML 報告（不依賴 CDN，離線可看）。

注意
----
* 步驟 2 的端點未公開，Valve 隨時可能改動。
* 請善待 Steam 伺服器：預設每次請求間隔 1 秒，遇 429/5xx 自動退避重試。
* 作者關閉留言、模組被隱藏或下架時會抓不到內容。
"""

from __future__ import annotations

import argparse
import csv
import gzip
import html as htmllib
import io
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zlib
from collections import Counter, defaultdict
from datetime import datetime, timezone
from html.parser import HTMLParser
from typing import Any, Dict, List, Optional, Tuple

__version__ = "1.0.0"

STEAMID64_BASE = 76561197960265728
API_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"
COMMENT_ENDPOINT = "https://steamcommunity.com/comment/PublishedFile_Public/render/{creator}/{fileid}/"
WORKSHOP_URL = "https://steamcommunity.com/sharedfiles/filedetails/?id={fileid}"

UA = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/124.0 Safari/537.36"
)


# --------------------------------------------------------------------------------------
# HTTP
# --------------------------------------------------------------------------------------
def _decode_body(resp) -> bytes:
    raw = resp.read()
    enc = (resp.headers.get("Content-Encoding") or "").lower()
    if enc == "gzip":
        return gzip.decompress(raw)
    if enc == "deflate":
        try:
            return zlib.decompress(raw)
        except zlib.error:
            return zlib.decompress(raw, -zlib.MAX_WBITS)
    return raw


def http_post_json(url: str, payload: Dict[str, Any], *, retries: int = 4, timeout: int = 30) -> Dict[str, Any]:
    """POST form-urlencoded，回傳解析後的 JSON。含指數退避重試。"""
    data = urllib.parse.urlencode(payload, doseq=True).encode("utf-8")
    headers = {
        "User-Agent": UA,
        "Accept": "application/json, text/javascript, */*; q=0.01",
        "Accept-Language": "en-US,en;q=0.9,zh-TW;q=0.8",
        "Accept-Encoding": "gzip, deflate",
        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
        "X-Requested-With": "XMLHttpRequest",
        "Origin": "https://steamcommunity.com",
    }
    last_err: Optional[Exception] = None
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, data=data, headers=headers, method="POST")
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                body = _decode_body(resp).decode("utf-8", errors="replace")
            return json.loads(body)
        except urllib.error.HTTPError as e:  # 429 / 5xx 退避
            last_err = e
            if e.code in (429, 500, 502, 503, 504) and attempt < retries - 1:
                wait = 2 ** attempt * 2
                eprint(f"  ! HTTP {e.code}，{wait}s 後重試 ({attempt + 1}/{retries - 1})")
                time.sleep(wait)
                continue
            raise
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as e:
            last_err = e
            if attempt < retries - 1:
                wait = 2 ** attempt * 2
                eprint(f"  ! {type(e).__name__}: {e}，{wait}s 後重試")
                time.sleep(wait)
                continue
            raise
    raise RuntimeError(f"請求失敗：{last_err}")


def eprint(*a: Any) -> None:
    print(*a, file=sys.stderr, flush=True)


# --------------------------------------------------------------------------------------
# 留言 HTML 解析（標準庫 HTMLParser）
# --------------------------------------------------------------------------------------
class CommentHTMLParser(HTMLParser):
    """解析 Steam 回傳的 comments_html 片段。

    結構大致為：
        <div class="commentthread_comment ..." id="comment_XXXX">
          <div class="commentthread_comment_avatar ..."><a href=...><img></a></div>
          <div class="commentthread_comment_content">
            <div class="commentthread_comment_author">
              <a class="commentthread_author_link" href=... data-miniprofile="123"><bdi>NAME</bdi></a>
              <span class="commentthread_comment_timestamp" title="..." data-timestamp="...">...</span>
            </div>
            <div class="commentthread_comment_text" id="comment_content_XXXX">內文</div>
          </div>
        </div>
    """

    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.comments: List[Dict[str, Any]] = []
        self._cur: Optional[Dict[str, Any]] = None
        self._depth = 0           # 目前留言 div 的巢狀深度
        self._content_depth = 0   # 內文 div 的巢狀深度（>0 表示正在收集內文）
        self._in_author = False
        self._in_ts = False
        self._text_buf: List[str] = []
        self._author_buf: List[str] = []
        self._ts_buf: List[str] = []

    # -- helpers -----------------------------------------------------------------
    @staticmethod
    def _classes(d: Dict[str, Optional[str]]) -> List[str]:
        return (d.get("class") or "").split()

    def _finish(self) -> None:
        if self._cur is None:
            return
        self._cur["text"] = _tidy(" ".join(self._text_buf))
        if not self._cur.get("author"):
            self._cur["author"] = _tidy(" ".join(self._author_buf)) or "(unknown)"
        if not self._cur.get("ts_text"):
            self._cur["ts_text"] = _tidy(" ".join(self._ts_buf))
        self.comments.append(self._cur)
        self._cur = None
        self._text_buf = []
        self._author_buf = []
        self._ts_buf = []
        self._in_author = False
        self._in_ts = False

    # -- HTMLParser hooks --------------------------------------------------------
    def handle_starttag(self, tag: str, attrs: List[Tuple[str, Optional[str]]]) -> None:
        d = dict(attrs)
        cls = self._classes(d)

        if tag == "div":
            if self._cur is None:
                if "commentthread_comment" in cls and (d.get("id") or "").startswith("comment_"):
                    self._cur = {
                        "comment_id": (d.get("id") or "")[len("comment_"):],
                        "author": "",
                        "author_url": "",
                        "author_steamid": "",
                        "accountid": None,
                        "timestamp": 0,
                        "time_title": "",
                        "ts_text": "",
                        "text": "",
                    }
                    self._depth = 1
                    self._content_depth = 0
                    self._text_buf = []
                    self._author_buf = []
                return
            # 已在留言內
            self._depth += 1
            if self._content_depth:
                self._content_depth += 1
            elif (d.get("id") or "").startswith("comment_content_") or "commentthread_comment_text" in cls:
                self._content_depth = 1
                self._text_buf = []
            return

        if self._cur is None:
            return

        if tag == "a" and "commentthread_author_link" in cls:
            self._cur["author_url"] = d.get("href") or ""
            mp = d.get("data-miniprofile")
            if mp and mp.isdigit():
                self._cur["accountid"] = int(mp)
                self._cur["author_steamid"] = str(STEAMID64_BASE + int(mp))
            m = re.search(r"/profiles/(\d{17})", self._cur["author_url"])
            if m:
                self._cur["author_steamid"] = m.group(1)
            self._in_author = True
            self._author_buf = []
        elif tag == "span" and (any("timestamp" in c for c in cls) or d.get("data-timestamp")):
            # 寬鬆比對：只要 class 含 timestamp 或帶 data-timestamp 屬性就採用
            raw = (d.get("data-timestamp") or "").strip()
            m = re.search(r"\d{9,11}", raw)
            if m:
                self._cur["timestamp"] = int(m.group(0))
            if d.get("title"):
                self._cur["time_title"] = d.get("title") or ""
            self._in_ts = True
            self._ts_buf = []
        elif tag == "br" and self._content_depth:
            self._text_buf.append("\n")
        elif tag == "img" and self._content_depth:
            # Steam 表情符號 / 內嵌圖片
            alt = d.get("alt") or d.get("title") or ""
            if alt:
                self._text_buf.append(alt)

    def handle_endtag(self, tag: str) -> None:
        if self._cur is None:
            return
        if tag == "div":
            if self._content_depth:
                self._content_depth -= 1
            self._depth -= 1
            if self._depth <= 0:
                self._finish()
        elif tag == "a" and self._in_author:
            self._cur["author"] = _tidy(" ".join(self._author_buf))
            self._in_author = False
        elif tag == "span" and self._in_ts:
            self._cur["ts_text"] = _tidy(" ".join(self._ts_buf))
            self._in_ts = False

    def handle_data(self, data: str) -> None:
        if self._cur is None:
            return
        if self._content_depth:
            self._text_buf.append(data)
        elif self._in_author:
            self._author_buf.append(data)
        elif self._in_ts:
            self._ts_buf.append(data)

    def close(self) -> None:  # 收尾未閉合的留言
        super().close()
        if self._cur is not None:
            self._finish()


def _tidy(s: str) -> str:
    s = htmllib.unescape(s)
    s = s.replace("\r", "")
    s = re.sub(r"[ \t ]+", " ", s)
    s = re.sub(r"\n{3,}", "\n\n", s)
    return s.strip()


# -- 時間戳救援 -------------------------------------------------------------------
_MONTHS = {m.lower(): i for i, m in enumerate(
    ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"], 1)}
_MONTHS.update({m.lower(): i for i, m in enumerate(
    ["January", "February", "March", "April", "May", "June", "July", "August",
     "September", "October", "November", "December"], 1)})

# 例："30 June, 2021 @ 2:18:56 pm EST" / "Dec 29, 2025 @ 4:00am" / "May 22 @ 10:25am"
_RE_DMY = re.compile(r"(\d{1,2})\s+([A-Za-z]+),?\s*(\d{4})?")
_RE_MDY = re.compile(r"([A-Za-z]+)\s+(\d{1,2}),?\s*(\d{4})?")
_RE_HM = re.compile(r"(\d{1,2}):(\d{2})(?::(\d{2}))?\s*([ap]m)?", re.IGNORECASE)


def parse_human_time(s: str) -> int:
    """把 Steam 的人類可讀時間字串轉成 UTC epoch。失敗回傳 0。"""
    if not s:
        return 0
    s = s.strip()
    day = mon = year = None
    m = _RE_DMY.search(s)
    if m and m.group(2).lower() in _MONTHS:
        day, mon, year = int(m.group(1)), _MONTHS[m.group(2).lower()], m.group(3)
    else:
        m = _RE_MDY.search(s)
        if m and m.group(1).lower() in _MONTHS:
            mon, day, year = _MONTHS[m.group(1).lower()], int(m.group(2)), m.group(3)
    if day is None or mon is None:
        return 0
    year = int(year) if year else datetime.now().year

    hh = mm = 0
    t = _RE_HM.search(s)
    if t:
        hh, mm = int(t.group(1)), int(t.group(2))
        ap = (t.group(4) or "").lower()
        if ap == "pm" and hh != 12:
            hh += 12
        elif ap == "am" and hh == 12:
            hh = 0
    try:
        return int(datetime(year, mon, day, hh % 24, mm, tzinfo=timezone.utc).timestamp())
    except ValueError:
        return 0


_RE_CID_BLOCK = re.compile(r'id="comment_(\d+)"(.*?)(?=id="comment_\d+"|\Z)', re.S)
_RE_TS_ATTR = re.compile(r'data-timestamp="\s*(\d{9,11})\s*"')


def backfill_timestamps(fragment: str, comments: List[Dict[str, Any]]) -> int:
    """對 timestamp 仍為 0 的留言，依序嘗試三種救援方式。回傳仍失敗的筆數。"""
    # 1) 直接掃原始片段，把 comment id 與其區塊內的 data-timestamp 配對
    by_id: Dict[str, int] = {}
    for blk in _RE_CID_BLOCK.finditer(fragment):
        t = _RE_TS_ATTR.search(blk.group(2))
        if t:
            by_id[blk.group(1)] = int(t.group(1))

    missing = 0
    for c in comments:
        if c.get("timestamp"):
            continue
        cid = c.get("comment_id") or ""
        if cid in by_id:
            c["timestamp"] = by_id[cid]
            continue
        # 2) 用 title 屬性的完整日期
        ts = parse_human_time(c.get("time_title") or "")
        # 3) 用顯示文字（可能省略年份，會以今年推算）
        if not ts:
            ts = parse_human_time(c.get("ts_text") or "")
        c["timestamp"] = ts
        if not ts:
            missing += 1
    return missing


def parse_comments_html(fragment: str) -> List[Dict[str, Any]]:
    p = CommentHTMLParser()
    p.feed(fragment)
    p.close()
    comments = p.comments
    missing = backfill_timestamps(fragment, comments)
    if missing and comments:
        eprint(f"  ! 有 {missing}/{len(comments)} 則留言無法取得時間戳（將以留言順序排序）")
    return comments


# --------------------------------------------------------------------------------------
# 抓取
# --------------------------------------------------------------------------------------
def resolve_file_id(arg: str) -> str:
    arg = arg.strip()
    if arg.isdigit():
        return arg
    m = re.search(r"[?&]id=(\d+)", arg) or re.search(r"/(\d{6,})", arg)
    if m:
        return m.group(1)
    raise SystemExit(f"無法從 '{arg}' 解析出 Workshop 檔案 ID")


def fetch_details(file_id: str) -> Dict[str, Any]:
    """官方 API，免 key。取得 creator / 標題 / 訂閱數等 metadata。"""
    data = http_post_json(API_DETAILS, {"itemcount": 1, "publishedfileids[0]": file_id})
    files = (data.get("response") or {}).get("publishedfiledetails") or []
    if not files:
        raise SystemExit("GetPublishedFileDetails 沒有回傳資料")
    d = files[0]
    if str(d.get("result")) != "1":
        raise SystemExit(f"模組 {file_id} 不存在、已下架或不公開（result={d.get('result')}）")
    return d


def fetch_all_comments(
    creator: str,
    file_id: str,
    *,
    page_size: int = 50,
    delay: float = 1.0,
    limit: Optional[int] = None,
    raw_out: Optional[List[str]] = None,
) -> Tuple[List[Dict[str, Any]], int]:
    url = COMMENT_ENDPOINT.format(creator=creator, fileid=file_id)

    probe = http_post_json(url, {"start": 0, "totalcount": 0, "count": 1})
    if not probe.get("success"):
        raise SystemExit("留言端點回傳 success=false（可能留言已關閉或端點已變更）")
    total = int(probe.get("total_count") or 0)
    if total == 0:
        return [], 0

    target = total if limit is None else min(total, limit)
    eprint(f"→ 共 {total} 則留言，預計抓取 {target} 則")

    out: List[Dict[str, Any]] = []
    seen = set()
    start = 0
    while start < target:
        count = min(page_size, target - start)
        payload = {"start": start, "totalcount": total, "count": count}
        resp = http_post_json(url, payload)
        if not resp.get("success"):
            eprint(f"  ! start={start} 回傳 success=false，停止")
            break
        frag = resp.get("comments_html") or ""
        if raw_out is not None and not raw_out:
            raw_out.append(frag)
        chunk = parse_comments_html(frag)
        new = 0
        for c in chunk:
            if c["comment_id"] and c["comment_id"] in seen:
                continue
            seen.add(c["comment_id"])
            c["order"] = len(out)          # 0 = Steam 回傳的第一則（通常最新）
            out.append(c)
            new += 1
        eprint(f"  · start={start:<6} 解析 {len(chunk):>3} 則（新增 {new}）／累計 {len(out)}")
        if new == 0 and chunk == []:
            eprint("  ! 本頁無資料，停止")
            break
        start += count
        if start < target:
            time.sleep(delay)
    return out, total


# --------------------------------------------------------------------------------------
# 規則式分析
# --------------------------------------------------------------------------------------
KW = {
    "bug": [
        r"\berrors?\b", r"\bcrash(?:e[sd]|ing)?\b", r"\bbugs?\b", r"\bbroken\b", r"\bfreez(?:e|es|ing)\b",
        r"\bexception\b", r"\bnull\s*reference\b", r"\bstack\s*trace\b", r"\bred\s*(?:text|error)\b",
        r"\bdoes\s*(?:n[o']?t|not)\s*work\b", r"\bnot\s*working\b", r"\bwon[''`]?t\s*(?:load|start|work)\b",
        r"\bfail(?:s|ed|ure)?\b", r"\bblack\s*screen\b", r"\binfinite\s*load", r"\bmemory\s*leak\b",
        r"崩[潰溃]", r"閃退", r"闪退", r"當機", r"当机", r"宕机", r"死機", r"卡死", r"卡住",
        r"報錯", r"报错", r"錯誤", r"错误", r"紅字", r"红字", r"無法(?:啟動|載入|使用|運作|开启)",
        r"无法(?:启动|加载|使用|运行)", r"不能用", r"用不了", r"失效", r"壞掉", r"坏了", r"有bug", r"掉幀", r"掉帧",
    ],
    "question": [
        r"\?", r"？", r"\bhow\s+(?:to|do|can|does)\b", r"\bwhy\s+(?:is|does|do|can)\b", r"\bwhat\s+(?:is|does)\b",
        r"\bis\s+(?:this|it)\s+(?:still|compatible)", r"\bcan\s+(?:i|someone|anyone)\b", r"\banyone\s+know\b",
        r"\bany\s+(?:idea|fix|way)\b", r"\bdoes\s+(?:this|it)\s+work\b",
        r"怎[麼么樣样]", r"如何", r"請問", r"请问", r"為什麼", r"为什么", r"哪裡", r"哪里", r"可以嗎", r"可以吗",
        r"有沒有", r"有没有", r"是否", r"求助", r"求解", r"有人知道",
    ],
    "solution": [
        r"\bfixed\b", r"\bsolved\b", r"\bit\s*works?\s*(?:now|for me)\b", r"\bworks?\s*now\b",
        r"\btry\s+(?:to|this|deleting|disabling|reinstalling|verifying)\b", r"\byou\s+(?:need|have)\s+to\b",
        r"\bmake\s+sure\s+(?:to|you)\b", r"\bjust\s+(?:delete|disable|reinstall|unsubscribe|resubscribe)\b",
        r"\bload\s*order\b", r"\bsolution\b", r"\bworkaround\b", r"\bthanks?,?\s*(?:it|that)\s*worked\b",
        r"解決", r"解决", r"修好", r"已修復", r"已修复", r"可以了", r"好了[，,。!！]", r"試試", r"试试",
        r"需要先", r"你要先", r"把.{0,10}(?:改成|換成|删掉|刪掉|關掉|关掉)", r"重新訂閱", r"重新订阅",
        r"重新安裝", r"重新安装", r"驗證檔案", r"验证文件", r"感謝.{0,6}(?:有用|可以)", r"感谢.{0,6}(?:有用|可以)",
    ],
    "compat": [
        r"\bcompatib(?:le|ility)\b", r"\bincompatible\b", r"\bconflicts?\b", r"\bconflicting\b",
        r"\bload\s*order\b", r"\brequire[sd]?\b", r"\bdependenc(?:y|ies)\b", r"\bprerequisite\b",
        r"\bpatch(?:es)?\s+for\b", r"\bworks?\s+with\b", r"\bDLC\b", r"\bupdate[d]?\s+(?:to|for)\b",
        r"相容", r"兼容", r"衝突", r"冲突", r"載入順序", r"加载顺序", r"前置", r"依賴", r"依赖",
        r"需要.{0,6}(?:模組|模组|mod)", r"更新後", r"更新后", r"新版本",
    ],
    "positive": [
        r"\bthanks?\b", r"\bthank\s+you\b", r"\bawesome\b", r"\bamazing\b", r"\bgreat\s+mod\b", r"\bbest\s+mod\b",
        r"\blove\s+(?:this|it)\b", r"\bperfect\b", r"\bexcellent\b", r"\bwell\s+done\b", r"\bgood\s+(?:job|work|mod)\b",
        r"\bworks?\s+(?:great|perfectly|fine)\b", r"\brecommend(?:ed)?\b", r"\bgoat\b", r"\bpog\b",
        r"感謝", r"感谢", r"謝謝", r"谢谢", r"太棒", r"很棒", r"神作", r"好用", r"讚", r"赞", r"必裝", r"必装",
        r"喜歡", r"喜欢", r"厲害", r"厉害", r"辛苦了", r"優秀", r"优秀",
    ],
    "negative": [
        r"\btrash\b", r"\bgarbage\b", r"\buseless\b", r"\bwaste\s+of\b", r"\bterrible\b", r"\bawful\b",
        r"\bdo\s*n[o']?t\s+(?:download|use|subscribe)\b", r"\bunsubscrib(?:e|ed|ing)\b", r"\babandoned\b",
        r"\bdead\s+mod\b", r"\bstill\s+broken\b", r"\bnever\s+works?\b", r"\bfix\s+(?:it|this)\s*!*\b",
        r"垃圾", r"很爛", r"很烂", r"別下載", r"别下载", r"別訂閱", r"别订阅", r"沒用", r"没用",
        r"退訂", r"退订", r"棄坑", r"弃坑", r"沒人維護", r"没人维护", r"作者.{0,4}(?:跑了|不管)", r"修一下",
    ],
    "praise_only_noise": [r"^\s*(?:\+1|nice|ok|good|thx|thanks|cool|\.+|66+|哈+|讚|赞)\s*$"],
}

COMPILED = {k: [re.compile(p, re.IGNORECASE) for p in v] for k, v in KW.items()}

RE_WORKSHOP_LINK = re.compile(r"steamcommunity\.com/(?:sharedfiles|workshop)/filedetails/\?id=(\d+)")
RE_VERSION = re.compile(r"\b(?:v(?:er(?:sion)?)?\.?\s*)?(\d+\.\d+(?:\.\d+)?)\b")
RE_URL = re.compile(r"https?://[^\s<>\"']+")

STOPWORDS = set("""
the a an and or but if then this that these those is are was were be been being do does did doing have has had
i you he she it we they me him her them my your his its our their to of in on at for with from by as not no
so very just too also can could would should will shall may might must im ive dont doesnt cant wont isnt
mod mods game steam workshop please thanks thank hi hello yes yeah nope ok okay lol lmao xd get got make made
still even much many any some more most about because when what which who how why where there here now
""".split())


def _hits(text: str, key: str) -> int:
    return sum(1 for r in COMPILED[key] if r.search(text))


def tokenize(text: str) -> set:
    t = text.lower()
    latin = [w for w in re.findall(r"[a-z][a-z0-9]{2,}", t) if w not in STOPWORDS]
    toks = set(latin)
    for run in re.findall(r"[一-鿿]{2,}", t):
        toks.update(run[i:i + 2] for i in range(len(run) - 1))
    return toks


def classify(comments: List[Dict[str, Any]], creator_id: str) -> None:
    """就地為每則留言加上分類與情緒欄位。"""
    for i, c in enumerate(comments):
        c.setdefault("order", i)
        txt = c.get("text") or ""
        c["len"] = len(txt)
        c["is_author"] = bool(creator_id) and c.get("author_steamid") == str(creator_id)
        c["bug"] = _hits(txt, "bug")
        c["question"] = _hits(txt, "question")
        c["solution"] = _hits(txt, "solution")
        c["compat"] = _hits(txt, "compat")
        pos, neg = _hits(txt, "positive"), _hits(txt, "negative")
        c["pos"], c["neg"] = pos, neg
        c["sentiment"] = "positive" if pos > neg else ("negative" if neg > pos else "neutral")
        c["noise"] = bool(COMPILED["praise_only_noise"][0].match(txt)) or len(txt) < 4
        c["linked_mods"] = sorted(set(RE_WORKSHOP_LINK.findall(txt)))
        c["versions"] = sorted(set(v for v in RE_VERSION.findall(txt) if not v.startswith("0.0")))[:4]

        tags = []
        if c["bug"]:
            tags.append("bug")
        if c["question"]:
            tags.append("question")
        if c["solution"]:
            tags.append("solution")
        if c["compat"] or c["linked_mods"]:
            tags.append("compat")
        if not tags:
            tags.append("other")
        c["tags"] = tags
        c["date"] = (
            datetime.fromtimestamp(c["timestamp"], tz=timezone.utc).strftime("%Y-%m-%d")
            if c["timestamp"] else ""
        )
        c["month"] = c["date"][:7]
        # 沒有時間戳時改用抓取順序（Steam 回傳為新→舊），確保排序邏輯仍可運作
        c["chrono"] = c["timestamp"] if c["timestamp"] else -c["order"]


def _cluster_pass(items: List[Dict[str, Any]], threshold: float, min_shared: int) -> List[Dict[str, Any]]:
    clusters: List[Dict[str, Any]] = []
    for c in items:
        best, best_score = None, 0.0
        for cl in clusters:
            inter = len(c["_tok"] & cl["tok"])
            if inter < min_shared:
                continue
            score = inter / len(c["_tok"] | cl["tok"])
            if score > best_score:
                best, best_score = cl, score
        if best is not None and best_score >= threshold:
            best["members"].append(c)
            best["tok"] |= c["_tok"]
        else:
            clusters.append({"tok": set(c["_tok"]), "members": [c]})
    return [cl for cl in clusters if len(cl["members"]) >= 2]


def cluster_issues(comments: List[Dict[str, Any]], *, max_items: int = 1200) -> List[Dict[str, Any]]:
    """把問題／提問類留言做貪婪 Jaccard 聚類，找出反覆出現的狀況。

    真實留言用詞差異很大，固定門檻常常一組都分不出來，所以改成由嚴到寬遞減，
    取第一個能分出群集的門檻。
    """
    pool = [c for c in comments if (c["bug"] or c["question"]) and not c["noise"]]
    pool = sorted(pool, key=lambda c: -c["chrono"])[:max_items]
    for c in pool:
        c["_tok"] = tokenize(c["text"])
    items = [c for c in pool if len(c["_tok"]) >= 2]
    items.sort(key=lambda c: -len(c["_tok"]))

    clusters: List[Dict[str, Any]] = []
    for threshold, min_shared in ((0.34, 2), (0.26, 2), (0.20, 2), (0.15, 3)):
        clusters = _cluster_pass(items, threshold, min_shared)
        if clusters:
            break

    out = []
    for cl in clusters:
        counter: Counter = Counter()
        for m in cl["members"]:
            counter.update(m["_tok"])
        n_mem = len(cl["members"])
        keywords = [w for w, n in counter.most_common(12) if n >= max(2, n_mem // 3)]
        members = sorted(cl["members"], key=lambda c: -c["chrono"])
        ts = [m["timestamp"] for m in members if m["timestamp"]]
        out.append({
            "count": n_mem,
            "keywords": keywords[:6],
            "bug_ratio": sum(1 for m in members if m["bug"]) / n_mem,
            "first": min(ts) if ts else 0,
            "last": max(ts) if ts else 0,
            "samples": members[:4],
        })
    out.sort(key=lambda x: (-x["count"], -x["last"]))
    for c in comments:
        c.pop("_tok", None)
    return out[:12]


def problem_keywords(comments: List[Dict[str, Any]], top: int = 24) -> List[Tuple[str, int]]:
    """問題類留言的高頻關鍵詞（聚類分不出群時的保底視角）。"""
    probs = [c for c in comments if (c["bug"] or c["question"]) and not c["noise"]]
    others = [c for c in comments if not (c["bug"] or c["question"]) and not c["noise"]]
    pc: Counter = Counter()
    for c in probs:
        pc.update(tokenize(c["text"]))
    oc: Counter = Counter()
    for c in others:
        oc.update(tokenize(c["text"]))
    n_p, n_o = max(len(probs), 1), max(len(others), 1)
    scored = []
    for w, n in pc.items():
        if n < 3 or len(w) < 2:
            continue
        lift = (n / n_p) / ((oc.get(w, 0) / n_o) + 1e-6)
        if lift >= 1.2:
            scored.append((w, n, lift))
    scored.sort(key=lambda x: (-x[1] * min(x[2], 5), -x[1]))
    return [(w, n) for w, n, _ in scored[:top]]


def pair_qa(comments: List[Dict[str, Any]], *, window: int = 6, max_days: int = 60) -> List[Dict[str, Any]]:
    """把問題留言與後續疑似解答的留言配對。

    沒有時間戳時退回以抓取順序推斷先後，不會整段失效。
    """
    ordered = sorted(comments, key=lambda c: c["chrono"])
    pairs = []
    for i, q in enumerate(ordered):
        if not (q["question"] or q["bug"]) or q["noise"]:
            continue
        best = None
        for a in ordered[i + 1:i + 1 + window]:
            if a["comment_id"] == q["comment_id"] or a["noise"]:
                continue
            if q["timestamp"] and a["timestamp"] and a["timestamp"] - q["timestamp"] > max_days * 86400:
                break
            # 回覆必須帶有解法訊號或出自作者；本身還在提問的不算解答
            if a["solution"] == 0 and not a["is_author"]:
                continue
            if a["question"] > a["solution"] and not a["is_author"]:
                continue
            score = a["solution"] * 2 + (3 if a["is_author"] else 0) + min(len(a["text"]) // 120, 2)
            overlap = len(tokenize(q["text"]) & tokenize(a["text"]))
            score += min(overlap, 3)
            if score >= 3 and (best is None or score > best[0]):
                best = (score, a)
        if best:
            pairs.append({"q": q, "a": best[1], "score": best[0]})
    pairs.sort(key=lambda p: (-p["score"], -p["q"]["chrono"]))
    # 同一則解答只保留分數最高的那組配對，避免報告重複
    seen_a: set = set()
    uniq = []
    for p in pairs:
        aid = p["a"]["comment_id"]
        if aid in seen_a:
            continue
        seen_a.add(aid)
        uniq.append(p)
    return uniq[:25]


def monthly_stats(comments: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    buckets: Dict[str, Dict[str, int]] = defaultdict(lambda: {"total": 0, "bug": 0, "pos": 0, "neg": 0})
    for c in comments:
        if not c["month"]:
            continue
        b = buckets[c["month"]]
        b["total"] += 1
        if c["bug"]:
            b["bug"] += 1
        if c["sentiment"] == "positive":
            b["pos"] += 1
        elif c["sentiment"] == "negative":
            b["neg"] += 1
    return [{"month": m, **buckets[m]} for m in sorted(buckets)]


def analyze(comments: List[Dict[str, Any]], details: Dict[str, Any]) -> Dict[str, Any]:
    creator = str(details.get("creator") or "")
    classify(comments, creator)

    real = [c for c in comments if not c["noise"]]
    author_replies = [c for c in comments if c["is_author"]]
    bugs = [c for c in real if c["bug"]]
    questions = [c for c in real if c["question"]]
    solutions = [c for c in real if c["solution"]]
    compat = [c for c in real if c["compat"] or c["linked_mods"]]

    linked = Counter()
    for c in comments:
        linked.update(c["linked_mods"])
    versions = Counter()
    for c in comments:
        versions.update(c["versions"])

    ts = [c["timestamp"] for c in comments if c["timestamp"]]
    top_posters = Counter(c["author"] for c in comments if c["author"])

    return {
        "no_timestamps": len(ts) == 0,
        "ts_coverage": len(ts) / max(len(comments), 1),
        "problem_keywords": problem_keywords(comments),
        "author_reply_list": sorted(author_replies, key=lambda c: -c["chrono"])[:20],
        "counts": {
            "total": len(comments),
            "meaningful": len(real),
            "bug": len(bugs),
            "question": len(questions),
            "solution": len(solutions),
            "compat": len(compat),
            "author_replies": len(author_replies),
            "positive": sum(1 for c in real if c["sentiment"] == "positive"),
            "negative": sum(1 for c in real if c["sentiment"] == "negative"),
            "neutral": sum(1 for c in real if c["sentiment"] == "neutral"),
        },
        "first_ts": min(ts) if ts else 0,
        "last_ts": max(ts) if ts else 0,
        "last_author_reply_ts": max((c["timestamp"] for c in author_replies), default=0),
        "monthly": monthly_stats(comments),
        "clusters": cluster_issues(comments),
        "qa": pair_qa(comments),
        "linked_mods": linked.most_common(15),
        "versions": versions.most_common(12),
        "top_posters": top_posters.most_common(10),
        "top_negative": sorted([c for c in real if c["sentiment"] == "negative"],
                               key=lambda c: (-c["neg"], -c["chrono"]))[:10],
        "top_compat": sorted(compat, key=lambda c: (-(c["compat"] + len(c["linked_mods"])), -c["chrono"]))[:15],
    }


# --------------------------------------------------------------------------------------
# 可選：LLM 摘要
# --------------------------------------------------------------------------------------
def llm_summary(analysis: Dict[str, Any], details: Dict[str, Any], model: str) -> Optional[str]:
    key = os.environ.get("ANTHROPIC_API_KEY")
    if not key:
        eprint("! 未設定 ANTHROPIC_API_KEY，略過 LLM 摘要")
        return None

    lines = [f"模組名稱：{details.get('title', '')}", ""]
    lines.append("== 反覆出現的問題群集 ==")
    for cl in analysis["clusters"][:8]:
        lines.append(f"- ({cl['count']} 則) 關鍵詞: {', '.join(cl['keywords'])}")
        for s in cl["samples"][:2]:
            lines.append(f"    · {s['text'][:220]}")
    lines.append("")
    lines.append("== 問答配對（節錄） ==")
    for p in analysis["qa"][:8]:
        lines.append(f"- Q: {p['q']['text'][:180]}")
        lines.append(f"  A: {p['a']['text'][:220]}{' [作者]' if p['a']['is_author'] else ''}")
    lines.append("")
    lines.append("== 相容性相關留言（節錄） ==")
    for c in analysis["top_compat"][:10]:
        lines.append(f"- {c['text'][:200]}")

    prompt = (
        "以下是某個 Steam Workshop 模組的留言分析節錄。請用繁體中文寫一份給「想安裝這個模組的玩家」看的摘要，"
        "格式為 markdown，包含四段：\n"
        "1. 目前最主要的已知問題（依嚴重度排序，說明症狀）\n"
        "2. 社群已知的解法或繞過方式（具體步驟）\n"
        "3. 相容性／前置需求提醒\n"
        "4. 一句話結論：這個模組現在還值不值得裝\n"
        "只根據下列資料，不要臆測。若資料不足請直說。\n\n" + "\n".join(lines)[:14000]
    )

    payload = json.dumps({
        "model": model,
        "max_tokens": 1600,
        "messages": [{"role": "user", "content": prompt}],
    }).encode("utf-8")
    req = urllib.request.Request(
        "https://api.anthropic.com/v1/messages",
        data=payload,
        headers={
            "content-type": "application/json",
            "x-api-key": key,
            "anthropic-version": "2023-06-01",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            body = json.loads(_decode_body(resp).decode("utf-8"))
        return "".join(b.get("text", "") for b in body.get("content", []))
    except Exception as e:  # noqa: BLE001
        eprint(f"! LLM 摘要失敗：{e}")
        return None


# --------------------------------------------------------------------------------------
# HTML 報告
# --------------------------------------------------------------------------------------
def esc(s: Any) -> str:
    return htmllib.escape(str(s if s is not None else ""), quote=True)


def fmt_ts(ts: int) -> str:
    if not ts:
        return "—"
    return datetime.fromtimestamp(ts, tz=timezone.utc).strftime("%Y-%m-%d")


def md_lite(text: str) -> str:
    """把 LLM 回傳的 markdown 做極簡轉換（標題／清單／粗體）。"""
    out = []
    for line in esc(text).split("\n"):
        s = line.strip()
        if s.startswith("### "):
            out.append(f"<h4>{s[4:]}</h4>")
        elif s.startswith("## "):
            out.append(f"<h3>{s[3:]}</h3>")
        elif s.startswith("# "):
            out.append(f"<h3>{s[2:]}</h3>")
        elif re.match(r"^[-*]\s+", s):
            item = re.sub(r"^[-*]\s+", "", s)
            out.append("<li>" + item + "</li>")
        elif re.match(r"^\d+\.\s+", s):
            item = re.sub(r"^\d+\.\s+", "", s)
            out.append("<li>" + item + "</li>")
        elif s:
            out.append(f"<p>{s}</p>")
    joined = "\n".join(out)
    joined = re.sub(r"(?:<li>.*?</li>\n?)+", lambda m: f"<ul>{m.group(0)}</ul>", joined, flags=re.S)
    joined = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", joined)
    joined = re.sub(r"`([^`]+)`", r"<code>\1</code>", joined)
    return joined


def comment_card(c: Dict[str, Any], file_id: str) -> str:
    badge = '<span class="badge author">作者</span>' if c["is_author"] else ""
    link = f"{WORKSHOP_URL.format(fileid=file_id)}#comment_{esc(c['comment_id'])}"
    return (
        '<div class="cm">'
        f'<div class="cm-h"><a href="{esc(c.get("author_url") or "#")}" target="_blank" rel="noopener">{esc(c["author"])}</a>'
        f'{badge}<span class="cm-d">{esc(c["date"])}</span>'
        f'<a class="permalink" href="{esc(link)}" target="_blank" rel="noopener">↗</a></div>'
        f'<div class="cm-t">{esc(c["text"])}</div>'
        "</div>"
    )


def build_html(details: Dict[str, Any], analysis: Dict[str, Any], comments: List[Dict[str, Any]],
               file_id: str, summary: Optional[str], fetched: int, total: int) -> str:
    cnt = analysis["counts"]
    title = details.get("title") or f"Workshop {file_id}"
    monthly = analysis["monthly"]
    maxm = max([m["total"] for m in monthly], default=1) or 1

    bars = "".join(
        f'<div class="bar" title="{esc(m["month"])}：共 {m["total"]} 則，其中 {m["bug"]} 則含問題回報">'
        f'<div class="stack">'
        f'<div class="seg bug" style="height:{m["bug"] / maxm * 100:.1f}%"></div>'
        f'<div class="seg ok" style="height:{(m["total"] - m["bug"]) / maxm * 100:.1f}%"></div>'
        f'</div><span class="xl">{esc(m["month"][2:])}</span></div>'
        for m in monthly
    ) or ('<p class="muted">這批留言解析不到時間戳，無法畫趨勢圖。'
          '請用 <code>--diagnose</code> 產生原始 HTML 樣本回報問題。</p>')

    tswarn = ""
    if analysis["ts_coverage"] < 0.9:
        tswarn = (f'<div class="health warn">⚠ 只有 {analysis["ts_coverage"] * 100:.0f}% 的留言成功解析出時間戳，'
                  f'時間相關的分析可能不完整。可用 <code>--diagnose</code> 匯出原始 HTML 檢查。</div>')

    def _cl_range(cl: Dict[str, Any]) -> str:
        if not cl["first"] and not cl["last"]:
            return ""
        return f'<span class="muted">{fmt_ts(cl["first"])} → {fmt_ts(cl["last"])}</span>'

    clusters_html = "".join(
        f'<div class="cluster"><div class="ch"><span class="cnum">{cl["count"]} 則</span>'
        f'<span class="kw">{" · ".join(esc(k) for k in cl["keywords"]) or "（無明顯共同關鍵詞）"}</span>'
        + _cl_range(cl) + "</div>"
        + "".join(comment_card(s, file_id) for s in cl["samples"]) + "</div>"
        for cl in analysis["clusters"]
    ) or ('<p class="muted">留言用詞太分散，分不出明確的問題群集 —— 請改看下方的高頻關鍵詞，'
          '或用最下方的留言表格搜尋特定字詞。</p>')

    pk = analysis["problem_keywords"]
    kw_html = ("".join(
        f'<span class="pill kwp" data-w="{esc(w)}">{esc(w)}<b>{n}</b></span>' for w, n in pk
    ) if pk else '<span class="muted">問題類留言太少，無法統計關鍵詞。</span>')

    author_html = "".join(comment_card(c, file_id) for c in analysis["author_reply_list"]) \
        or '<p class="muted">作者沒有在留言區回覆過。</p>'

    qa_html = "".join(
        f'<div class="qa"><div class="q">Q<div>{esc(p["q"]["text"])}'
        f'<span class="cm-d">{esc(p["q"]["author"])} · {esc(p["q"]["date"])}</span></div></div>'
        f'<div class="a">A<div>{esc(p["a"]["text"])}'
        f'<span class="cm-d">{esc(p["a"]["author"])}{" · 作者" if p["a"]["is_author"] else ""} · {esc(p["a"]["date"])}</span></div></div></div>'
        for p in analysis["qa"]
    ) or '<p class="muted">沒有找到明顯的問答配對（多數提問沒有得到帶解法的回覆）。</p>'

    mods_html = "".join(
        f'<li><a href="{WORKSHOP_URL.format(fileid=mid)}" target="_blank" rel="noopener">{esc(mid)}</a>'
        f'<span class="pill">{n} 次提及</span></li>'
        for mid, n in analysis["linked_mods"]
    ) or '<li class="muted">留言中沒有連結到其他 Workshop 項目。</li>'

    ver_html = "".join(f'<span class="pill">{esc(v)} × {n}</span>' for v, n in analysis["versions"]) \
        or '<span class="muted">未偵測到版本號</span>'

    compat_html = "".join(comment_card(c, file_id) for c in analysis["top_compat"]) \
        or '<p class="muted">沒有相容性相關留言。</p>'

    neg_html = "".join(comment_card(c, file_id) for c in analysis["top_negative"]) \
        or '<p class="muted">沒有明顯負面留言。</p>'

    posters_html = "".join(
        f'<li>{esc(a)}<span class="pill">{n}</span></li>' for a, n in analysis["top_posters"]
    )

    summary_html = (
        f'<section><h2>AI 摘要</h2><div class="llm">{md_lite(summary)}</div></section>' if summary else ""
    )

    rows = []
    for c in sorted(comments, key=lambda x: -x["timestamp"]):
        rows.append({
            "d": c["date"],
            "a": c["author"],
            "t": c["text"],
            "g": c["tags"],
            "s": c["sentiment"],
            "au": 1 if c["is_author"] else 0,
            "u": c.get("author_url") or "",
            "id": c["comment_id"],
        })
    data_json = json.dumps(rows, ensure_ascii=False).replace("</", "<\\/")

    pos, neg, neu = cnt["positive"], cnt["negative"], cnt["neutral"]
    stot = max(pos + neg + neu, 1)

    health = []
    if analysis["last_ts"]:
        days = (time.time() - analysis["last_ts"]) / 86400
        health.append(f"最後一則留言在 {days:.0f} 天前")
    if analysis["last_author_reply_ts"]:
        d2 = (time.time() - analysis["last_author_reply_ts"]) / 86400
        health.append(f"作者最後回覆在 {d2:.0f} 天前")
    else:
        health.append("作者從未在留言區回覆")
    bug_rate = cnt["bug"] / max(cnt["meaningful"], 1) * 100
    health.append(f"問題回報佔有效留言 {bug_rate:.0f}%")

    tpl = _TEMPLATE
    repl = {
        "__TITLE__": esc(title),
        "__FILEID__": esc(file_id),
        "__WSURL__": WORKSHOP_URL.format(fileid=file_id),
        "__APPID__": esc(details.get("consumer_app_id", "—")),
        "__CREATOR__": esc(details.get("creator", "—")),
        "__SUBS__": f'{int(details.get("subscriptions") or 0):,}',
        "__FAVS__": f'{int(details.get("favorited") or 0):,}',
        "__VIEWS__": f'{int(details.get("views") or 0):,}',
        "__CREATED__": fmt_ts(int(details.get("time_created") or 0)),
        "__UPDATED__": fmt_ts(int(details.get("time_updated") or 0)),
        "__FETCHED__": f"{fetched:,}",
        "__TOTAL__": f"{total:,}",
        "__C_BUG__": f'{cnt["bug"]:,}',
        "__C_Q__": f'{cnt["question"]:,}',
        "__C_SOL__": f'{cnt["solution"]:,}',
        "__C_COMPAT__": f'{cnt["compat"]:,}',
        "__C_AUTH__": f'{cnt["author_replies"]:,}',
        "__C_MEAN__": f'{cnt["meaningful"]:,}',
        "__POS__": str(pos), "__NEG__": str(neg), "__NEU__": str(neu),
        "__POSP__": f"{pos / stot * 100:.0f}", "__NEGP__": f"{neg / stot * 100:.0f}",
        "__NEUP__": f"{neu / stot * 100:.0f}",
        "__HEALTH__": esc(" ｜ ".join(health)),
        "__TSWARN__": tswarn,
        "__BARS__": bars,
        "__CLUSTERS__": clusters_html,
        "__KEYWORDS__": kw_html,
        "__AUTHORREPLIES__": author_html,
        "__QA__": qa_html,
        "__MODS__": mods_html,
        "__VERS__": ver_html,
        "__COMPAT__": compat_html,
        "__NEG_LIST__": neg_html,
        "__POSTERS__": posters_html,
        "__SUMMARY__": summary_html,
        "__DATA__": data_json,
        "__GEN__": datetime.now().strftime("%Y-%m-%d %H:%M"),
        "__VER__": __version__,
    }
    for k, v in repl.items():
        tpl = tpl.replace(k, v)
    return tpl


_TEMPLATE = r"""<!doctype html>
<html lang="zh-Hant">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>__TITLE__ — 留言分析報告</title>
<style>
:root{--bg:#0f1419;--card:#171d25;--card2:#1d2733;--line:#2a3541;--fg:#c7d5e0;--dim:#8f98a0;
--acc:#66c0f4;--bug:#e05c5c;--ok:#4c9e4c;--warn:#e0a95c;--pos:#5ba85b;--neg:#c9524f}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.65 "Segoe UI","PingFang TC","Microsoft JhengHei",system-ui,sans-serif}
.wrap{max-width:1120px;margin:0 auto;padding:28px 20px 80px}
h1{font-size:26px;margin:0 0 4px}
h2{font-size:19px;margin:0 0 14px;padding-bottom:8px;border-bottom:1px solid var(--line);color:#fff}
h3{font-size:15px;margin:18px 0 8px;color:#fff}
h4{font-size:14px;margin:14px 0 6px;color:#fff}
a{color:var(--acc);text-decoration:none}a:hover{text-decoration:underline}
.muted{color:var(--dim)}
section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:20px;margin:18px 0}
header.hero{background:linear-gradient(135deg,#1b2838,#2a475e);border:1px solid var(--line);border-radius:10px;padding:24px}
.meta{display:flex;flex-wrap:wrap;gap:8px 22px;color:var(--dim);font-size:13px;margin-top:10px}
.kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:12px;margin:18px 0}
.kpi{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px 16px}
.kpi .n{font-size:24px;font-weight:700;color:#fff}
.kpi .l{font-size:12px;color:var(--dim);margin-top:2px}
.kpi.bug .n{color:var(--bug)}.kpi.good .n{color:var(--ok)}.kpi.warn .n{color:var(--warn)}
.health{background:var(--card2);border-left:3px solid var(--acc);padding:10px 14px;border-radius:0 6px 6px 0;font-size:13.5px;margin-bottom:6px}
.chart{display:flex;align-items:flex-end;gap:3px;height:190px;overflow-x:auto;padding-top:10px}
.bar{flex:1 0 22px;display:flex;flex-direction:column;justify-content:flex-end;align-items:center;height:100%}
.stack{width:100%;height:160px;display:flex;flex-direction:column;justify-content:flex-end}
.seg{width:100%}.seg.bug{background:var(--bug)}.seg.ok{background:#3d6e8f}
.xl{font-size:9.5px;color:var(--dim);margin-top:5px;writing-mode:vertical-rl;white-space:nowrap}
.legend{font-size:12px;color:var(--dim);margin-top:10px}
.legend i{display:inline-block;width:10px;height:10px;margin:0 5px 0 14px;border-radius:2px;vertical-align:middle}
.cm{background:var(--card2);border:1px solid var(--line);border-radius:8px;padding:10px 13px;margin:8px 0}
.cm-h{display:flex;align-items:center;gap:9px;font-size:12.5px;margin-bottom:5px}
.cm-d{color:var(--dim)}
.permalink{margin-left:auto;color:var(--dim)}
.cm-t{white-space:pre-wrap;word-break:break-word;font-size:14px}
.badge{font-size:10.5px;padding:1px 7px;border-radius:20px;background:var(--acc);color:#0f1419;font-weight:700}
.cluster{border:1px solid var(--line);border-radius:9px;padding:12px 14px;margin:12px 0;background:#141a21}
.ch{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:6px;font-size:13px}
.cnum{background:var(--bug);color:#fff;padding:2px 10px;border-radius:20px;font-weight:700;font-size:12px}
.kw{color:#fff;font-weight:600}
.qa{border-left:3px solid var(--line);padding-left:14px;margin:16px 0}
.qa .q,.qa .a{display:flex;gap:10px;margin:6px 0;font-size:14px}
.qa .q>:first-child,.qa .a>:first-child{flex:0 0 20px;font-weight:700}
.qa .q>:first-child{color:var(--warn)}.qa .a>:first-child{color:var(--ok)}
.qa .cm-d{display:block;font-size:12px;margin-top:3px}
.pill{display:inline-block;background:var(--card2);border:1px solid var(--line);border-radius:20px;
padding:1px 9px;font-size:12px;margin-left:8px;color:var(--dim)}
.kwbox{display:flex;flex-wrap:wrap;gap:7px}
.kwbox .pill{margin-left:0;cursor:pointer;color:var(--fg);padding:3px 11px}
.kwbox .pill:hover{border-color:var(--acc);color:var(--acc)}
.kwbox .pill b{color:var(--dim);font-weight:400;margin-left:6px;font-size:11px}
.health.warn{border-left-color:var(--warn)}
code{background:#0b0f14;border:1px solid var(--line);border-radius:4px;padding:0 5px;font-size:12.5px}
ul.plain{list-style:none;padding:0;margin:0}
ul.plain li{padding:5px 0;border-bottom:1px solid var(--line)}
.sbar{display:flex;height:26px;border-radius:6px;overflow:hidden;margin:10px 0;font-size:11.5px;color:#fff}
.sbar div{display:flex;align-items:center;justify-content:center}
.sp{background:var(--pos)}.sn{background:var(--neg)}.su{background:#46586a}
.cols{display:grid;grid-template-columns:1fr 1fr;gap:22px}
@media(max-width:760px){.cols{grid-template-columns:1fr}}
.toolbar{display:flex;gap:10px;flex-wrap:wrap;margin-bottom:12px}
input[type=search],select{background:var(--card2);border:1px solid var(--line);color:var(--fg);
border-radius:6px;padding:7px 11px;font-size:14px}
input[type=search]{flex:1;min-width:200px}
table{width:100%;border-collapse:collapse;font-size:13.5px}
th{text-align:left;color:var(--dim);font-weight:600;font-size:12px;border-bottom:1px solid var(--line);padding:7px 8px;cursor:pointer;user-select:none}
td{padding:8px;border-bottom:1px solid var(--line);vertical-align:top}
td.txt{white-space:pre-wrap;word-break:break-word}
.tag{display:inline-block;font-size:10.5px;padding:1px 7px;border-radius:20px;margin-right:4px;border:1px solid var(--line)}
.tag.bug{background:#3a1e1e;color:#f0a0a0}.tag.question{background:#3a301e;color:#f0cf9f}
.tag.solution{background:#1e3a24;color:#a0e0ae}.tag.compat{background:#1e2f3a;color:#9fd0f0}
.tag.other{color:var(--dim)}
.llm{background:var(--card2);border-radius:8px;padding:4px 18px}
.llm ul{padding-left:20px}
footer{color:var(--dim);font-size:12px;text-align:center;margin-top:30px}
.more{color:var(--acc);cursor:pointer;font-size:13px}
</style>
</head>
<body>
<div class="wrap">

<header class="hero">
  <h1>__TITLE__</h1>
  <div><a href="__WSURL__" target="_blank" rel="noopener">Workshop 頁面 ↗</a> <span class="muted">· ID __FILEID__ · AppID __APPID__ · 作者 __CREATOR__</span></div>
  <div class="meta">
    <span>訂閱 __SUBS__</span><span>收藏 __FAVS__</span><span>瀏覽 __VIEWS__</span>
    <span>建立 __CREATED__</span><span>更新 __UPDATED__</span>
    <span>已抓取 __FETCHED__ / __TOTAL__ 則留言</span>
  </div>
</header>

<div class="kpis">
  <div class="kpi bug"><div class="n">__C_BUG__</div><div class="l">問題／Bug 回報</div></div>
  <div class="kpi warn"><div class="n">__C_Q__</div><div class="l">提問</div></div>
  <div class="kpi good"><div class="n">__C_SOL__</div><div class="l">含解法的回覆</div></div>
  <div class="kpi"><div class="n">__C_COMPAT__</div><div class="l">相容性／依賴</div></div>
  <div class="kpi"><div class="n">__C_AUTH__</div><div class="l">作者回覆</div></div>
  <div class="kpi"><div class="n">__C_MEAN__</div><div class="l">有效留言（濾除灌水）</div></div>
</div>

<div class="health">__HEALTH__</div>
__TSWARN__

__SUMMARY__

<section>
  <h2>留言時間趨勢</h2>
  <div class="chart">__BARS__</div>
  <div class="legend"><i style="background:var(--bug)"></i>含問題回報<i style="background:#3d6e8f"></i>其他留言</div>
</section>

<section>
  <h2>反覆出現的問題</h2>
  <p class="muted" style="margin-top:-6px">以關鍵詞重疊度分群，同一群代表多位玩家回報了相似的狀況。</p>
  __CLUSTERS__
  <h3>問題留言的高頻關鍵詞</h3>
  <p class="muted" style="margin-top:-4px;font-size:13px">已扣除一般留言的背景詞頻，數字為出現則數。點一下會帶到下方表格搜尋。</p>
  <div class="kwbox">__KEYWORDS__</div>
</section>

<section>
  <h2>問答與解法</h2>
  <p class="muted" style="margin-top:-6px">系統依時間鄰近度、關鍵詞重疊與是否為作者回覆推測的問答配對，僅供快速掃描。</p>
  __QA__
  <h3>作者的所有回覆</h3>
  <p class="muted" style="margin-top:-4px;font-size:13px">作者本人的發言通常是最可靠的答案來源。</p>
  __AUTHORREPLIES__
</section>

<section>
  <h2>相容性與依賴</h2>
  <div class="cols">
    <div>
      <h3>留言中提及的其他 Workshop 項目</h3>
      <ul class="plain">__MODS__</ul>
      <h3>被提及的版本號</h3>
      <div>__VERS__</div>
    </div>
    <div>
      <h3>最活躍留言者</h3>
      <ul class="plain">__POSTERS__</ul>
    </div>
  </div>
  <h3>相關留言</h3>
  __COMPAT__
</section>

<section>
  <h2>情緒分佈</h2>
  <div class="sbar">
    <div class="sp" style="width:__POSP__%">正面 __POS__</div>
    <div class="su" style="width:__NEUP__%">中性 __NEU__</div>
    <div class="sn" style="width:__NEGP__%">負面 __NEG__</div>
  </div>
  <h3>最強烈的負面留言</h3>
  __NEG_LIST__
</section>

<section>
  <h2>全部留言</h2>
  <div class="toolbar">
    <input type="search" id="q" placeholder="搜尋留言內容或作者…">
    <select id="cat">
      <option value="">全部分類</option>
      <option value="bug">問題／Bug</option>
      <option value="question">提問</option>
      <option value="solution">解法</option>
      <option value="compat">相容性</option>
      <option value="other">其他</option>
    </select>
    <select id="sent">
      <option value="">全部情緒</option>
      <option value="positive">正面</option>
      <option value="neutral">中性</option>
      <option value="negative">負面</option>
    </select>
    <select id="au"><option value="">所有人</option><option value="1">只看作者回覆</option></select>
    <span class="muted" id="stat" style="align-self:center"></span>
  </div>
  <table>
    <thead><tr><th data-k="d">日期</th><th data-k="a">作者</th><th>分類</th><th>內容</th></tr></thead>
    <tbody id="tb"></tbody>
  </table>
  <p style="text-align:center"><span class="more" id="more">載入更多 ▾</span></p>
</section>

<footer>由 steam_mod_comment_analyzer v__VER__ 於 __GEN__ 產生 · 資料來源為 Steam 社群公開留言</footer>
</div>

<script>
const DATA = __DATA__;
const PAGE = 100;
let shown = PAGE, sortK = 'd', sortDir = -1;
const $ = s => document.querySelector(s);
const escHtml = s => s.replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));

function filtered(){
  const q = $('#q').value.trim().toLowerCase();
  const cat = $('#cat').value, sent = $('#sent').value, au = $('#au').value;
  let r = DATA.filter(x =>
    (!q || x.t.toLowerCase().includes(q) || x.a.toLowerCase().includes(q)) &&
    (!cat || x.g.includes(cat)) && (!sent || x.s === sent) && (!au || String(x.au) === au));
  r.sort((a,b) => (a[sortK] > b[sortK] ? 1 : a[sortK] < b[sortK] ? -1 : 0) * sortDir);
  return r;
}
function render(){
  const r = filtered();
  $('#stat').textContent = `符合 ${r.length} 則`;
  $('#tb').innerHTML = r.slice(0, shown).map(x =>
    `<tr><td class="muted" style="white-space:nowrap">${x.d}</td>` +
    `<td style="white-space:nowrap"><a href="${x.u}" target="_blank" rel="noopener">${escHtml(x.a)}</a>` +
    (x.au ? ' <span class="badge">作者</span>' : '') + `</td>` +
    `<td style="white-space:nowrap">${x.g.map(g => `<span class="tag ${g}">${g}</span>`).join('')}</td>` +
    `<td class="txt">${escHtml(x.t)}</td></tr>`).join('');
  $('#more').style.display = r.length > shown ? '' : 'none';
}
['q','cat','sent','au'].forEach(id => $('#'+id).addEventListener('input', () => { shown = PAGE; render(); }));
document.querySelectorAll('.kwbox .pill').forEach(p => p.addEventListener('click', () => {
  $('#q').value = p.dataset.w; $('#cat').value = ''; shown = PAGE; render();
  document.querySelector('#q').scrollIntoView({behavior:'smooth', block:'center'});
}));
$('#more').addEventListener('click', () => { shown += PAGE; render(); });
document.querySelectorAll('th[data-k]').forEach(th => th.addEventListener('click', () => {
  const k = th.dataset.k;
  sortDir = (k === sortK) ? -sortDir : -1; sortK = k; render();
}));
render();
</script>
</body>
</html>
"""


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------
def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(
        description="抓取並分析單一 Steam Workshop 模組的全部留言，產生 HTML 報告。",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="範例：\n  python steam_mod_comment_analyzer.py 569264526 --out mw.html\n",
    )
    ap.add_argument("item", help="Workshop 檔案 ID 或完整網址")
    ap.add_argument("--out", help="輸出的 HTML 檔名（預設 report_<id>.html）")
    ap.add_argument("--limit", type=int, help="最多抓取幾則留言（預設全部）")
    ap.add_argument("--page-size", type=int, default=50, help="每頁留言數，預設 50（Steam 上限約 100）")
    ap.add_argument("--delay", type=float, default=1.0, help="每頁請求間隔秒數，預設 1.0")
    ap.add_argument("--creator", help="手動指定作者 SteamID64（略過 metadata 查詢）")
    ap.add_argument("--cache", default=".", help="快取目錄，預設當前目錄")
    ap.add_argument("--refresh", action="store_true", help="忽略快取，強制重新抓取")
    ap.add_argument("--dump-json", action="store_true", help="另外輸出 <id>.comments.json")
    ap.add_argument("--dump-csv", action="store_true", help="另外輸出 <id>.comments.csv")
    ap.add_argument("--llm", action="store_true", help="呼叫 Claude API 產生主題摘要（需 ANTHROPIC_API_KEY）")
    ap.add_argument("--llm-model", default="claude-sonnet-5", help="LLM 模型名稱")
    ap.add_argument("--from-cache-only", action="store_true", help="只用快取分析，完全不連網")
    ap.add_argument("--diagnose", action="store_true",
                    help="輸出解析健檢報告與原始 HTML 樣本（<id>.raw.html），用於排查解析失敗")
    args = ap.parse_args(argv)

    file_id = resolve_file_id(args.item)
    os.makedirs(args.cache, exist_ok=True)
    cache_path = os.path.join(args.cache, f"{file_id}.cache.json")

    payload: Optional[Dict[str, Any]] = None
    if (not args.refresh) and os.path.exists(cache_path):
        try:
            with open(cache_path, "r", encoding="utf-8") as f:
                payload = json.load(f)
            eprint(f"→ 使用快取 {cache_path}（{len(payload['comments'])} 則，加 --refresh 可重抓）")
        except Exception:  # noqa: BLE001
            payload = None
    if payload is None and args.from_cache_only:
        raise SystemExit("找不到快取，且指定了 --from-cache-only")

    if payload is None:
        details = {"creator": args.creator} if args.creator else fetch_details(file_id)
        creator = str(details.get("creator") or "")
        if not creator:
            raise SystemExit("取不到作者 SteamID64，請用 --creator 指定")
        eprint(f"→ 模組：{details.get('title', '(無標題)')}  作者 {creator}")
        raw_sample: List[str] = []
        comments, total = fetch_all_comments(
            creator, file_id, page_size=args.page_size, delay=args.delay,
            limit=args.limit, raw_out=raw_sample
        )
        payload = {"details": details, "comments": comments, "total": total,
                   "fetched_at": int(time.time()), "version": __version__,
                   "raw_sample": raw_sample[0] if raw_sample else ""}
        with open(cache_path, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False)
        eprint(f"→ 已快取至 {cache_path}")

    details, comments, total = payload["details"], payload["comments"], payload.get("total", len(payload["comments"]))
    if not comments:
        eprint("這個模組沒有任何留言（或留言已關閉）。")
        return 1

    if args.diagnose:
        n = len(comments)
        def pct(k: str) -> str:
            got = sum(1 for c in comments if c.get(k))
            return f"{got}/{n} ({got / n * 100:.0f}%)"
        eprint("\n===== 解析健檢 =====")
        eprint(f"留言總數      : {n}")
        eprint(f"有內文        : {pct('text')}")
        eprint(f"有作者名      : {pct('author')}")
        eprint(f"有 SteamID    : {pct('author_steamid')}")
        eprint(f"有時間戳      : {pct('timestamp')}")
        eprint(f"有時間顯示文字: {pct('ts_text')}")
        eprint(f"有 title 屬性 : {pct('time_title')}")
        bad = [c for c in comments if not c.get("timestamp")]
        for c in bad[:3]:
            eprint(f"  缺時間戳範例: id={c.get('comment_id')} ts_text={c.get('ts_text')!r} "
                   f"title={c.get('time_title')!r}")
        raw = payload.get("raw_sample") or ""
        if raw:
            p = f"{file_id}.raw.html"
            with open(p, "w", encoding="utf-8") as f:
                f.write(raw)
            eprint(f"原始 HTML 樣本已輸出：{os.path.abspath(p)}（約 {len(raw)} 字元）")
        else:
            eprint("快取中沒有原始 HTML 樣本，請加 --refresh --diagnose 重抓一次。")
        eprint("====================\n")

    eprint("→ 分析中…")
    analysis = analyze(comments, details)
    summary = llm_summary(analysis, details, args.llm_model) if args.llm else None

    out = args.out or f"report_{file_id}.html"
    with open(out, "w", encoding="utf-8") as f:
        f.write(build_html(details, analysis, comments, file_id, summary, len(comments), total))
    eprint(f"✓ 報告已輸出：{os.path.abspath(out)}")

    if args.dump_json:
        p = f"{file_id}.comments.json"
        with open(p, "w", encoding="utf-8") as f:
            json.dump(comments, f, ensure_ascii=False, indent=1)
        eprint(f"✓ {p}")
    if args.dump_csv:
        p = f"{file_id}.comments.csv"
        cols = ["comment_id", "date", "timestamp", "author", "author_steamid", "author_url",
                "is_author", "sentiment", "bug", "question", "solution", "compat", "text"]
        with open(p, "w", encoding="utf-8-sig", newline="") as f:
            w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore")
            w.writeheader()
            for c in comments:
                w.writerow(c)
        eprint(f"✓ {p}")

    c = analysis["counts"]
    eprint(f"\n摘要：{c['total']} 則留言 · Bug {c['bug']} · 提問 {c['question']} · "
           f"解法 {c['solution']} · 相容性 {c['compat']} · 作者回覆 {c['author_replies']} · "
           f"正/負 {c['positive']}/{c['negative']}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        eprint("\n已中斷")
        sys.exit(130)
