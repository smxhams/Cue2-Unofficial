#!/usr/bin/env python3
"""Extract user-facing UI strings from Cue2 scenes for translation catalogs.

Usage:
  python3 tools/i18n/extract_ui_strings.py
  python3 tools/i18n/extract_ui_strings.py --merge translations/cue2.csv

Default scan roots:
  src/UI/Shell, src/UI/Windows, src/UI/Settings, src/UI/Inspectors

Output is CSV rows (keys,en) ready to paste or merge into translations/cue2.csv.
English source text is used as the message key (Godot-friendly).

Automation tips for new languages / bulk fill:
  1. Run this extractor and merge into cue2.csv (preserves existing mi/es/…).
  2. Translate empty cells with a spreadsheet, Weblate, or an LLM (review Māori/native speakers).
  3. Restart Cue2 — LocalizationService reloads the CSV at startup.
  4. UI that uses UiLocalizer.LocalizeTree / Tr(English) picks up new rows automatically.
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
from pathlib import Path

PROPS = (
    "text",
    "tooltip_text",
    "placeholder_text",
    "ok_button_text",
    "cancel_button_text",
    "title",
)
PROP_RE = re.compile(r"^\s*(" + "|".join(PROPS) + r')\s*=\s*"(.*)"\s*$')
PROP_OPEN_RE = re.compile(r"^\s*(" + "|".join(PROPS) + r')\s*=\s*"(.*)$')

SKIP_EXACT = {"", "0", "100%", "--", "Cue2 ", " ", "X:", "Y:", "BG", "ID:", "#1"}


def should_keep(s: str) -> bool:
    s = s.strip()
    if not s or s in SKIP_EXACT:
        return False
    if len(s) <= 1:
        return False
    if re.fullmatch(r"[\d\s.%/\-:]+", s) and "{" not in s:
        return False
    return True


def normalize(s: str) -> str:
    return (
        s.replace("\\n", "\n")
        .replace('\\"', '"')
        .replace("\\t", "\t")
        .strip("\n")
    )


def extract_from_tscn(path: Path) -> set[str]:
    found: set[str] = set()
    lines = path.read_text(encoding="utf-8").splitlines()
    i = 0
    while i < len(lines):
        line = lines[i]
        m = PROP_RE.match(line)
        if m:
            s = normalize(m.group(2))
            if should_keep(s):
                found.add(s)
            i += 1
            continue
        m2 = PROP_OPEN_RE.match(line)
        if m2 and line.count('"') < 2:
            buf = [m2.group(2)]
            i += 1
            while i < len(lines):
                if lines[i].rstrip().endswith('"'):
                    buf.append(lines[i].rstrip()[:-1])
                    break
                buf.append(lines[i])
                i += 1
            s = normalize("\n".join(buf))
            if should_keep(s):
                found.add(s)
        i += 1
    return found


def load_existing(csv_path: Path) -> dict[str, dict[str, str]]:
    if not csv_path.exists():
        return {}
    with csv_path.open(encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        rows = {}
        for row in reader:
            key = row.get("keys") or row.get("key")
            if key:
                rows[key] = row
        return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--merge",
        type=Path,
        help="Merge extracted English keys into this CSV (preserves other locale columns).",
    )
    parser.add_argument(
        "--roots",
        nargs="*",
        default=[
            "src/UI/Shell",
            "src/UI/Windows",
            "src/UI/Settings",
            "src/UI/Inspectors",
        ],
        help="Directories or .tscn files to scan",
    )
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]

    strings: set[str] = set()
    for rel in args.roots:
        p = root / rel
        if p.is_file() and p.suffix == ".tscn":
            strings |= extract_from_tscn(p)
        elif p.is_dir():
            for tscn in p.rglob("*.tscn"):
                strings |= extract_from_tscn(tscn)

    if args.merge:
        existing = load_existing(args.merge if args.merge.is_absolute() else root / args.merge)
        # Preserve header locale columns
        locales = ["en"]
        if existing:
            sample = next(iter(existing.values()))
            locales = [c for c in sample.keys() if c not in ("keys", "key")]
            if "en" not in locales:
                locales = ["en"] + locales

        for s in strings:
            if s not in existing:
                existing[s] = {"keys": s, "en": s, **{loc: s if loc == "en" else s for loc in locales}}
                existing[s]["keys"] = s
                existing[s]["en"] = s
                for loc in locales:
                    if loc != "en" and loc not in existing[s]:
                        existing[s][loc] = s  # untranslated placeholder = English

        out_path = args.merge if args.merge.is_absolute() else root / args.merge
        fieldnames = ["keys"] + locales
        with out_path.open("w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, fieldnames=fieldnames, lineterminator="\n")
            w.writeheader()
            for key in sorted(existing.keys(), key=lambda k: k.lower()):
                row = existing[key]
                out = {"keys": key}
                for loc in locales:
                    out[loc] = row.get(loc, row.get("en", key))
                w.writerow(out)
        print(f"Merged {len(strings)} extracted strings into {out_path} ({len(existing)} total keys)")
    else:
        w = csv.writer(sys.stdout, lineterminator="\n")
        w.writerow(["keys", "en"])
        for s in sorted(strings, key=str.lower):
            w.writerow([s, s])
        print(f"# {len(strings)} strings", file=sys.stderr)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
