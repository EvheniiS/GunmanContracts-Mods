"""Compact FrameProbe CSV and optionally filter MelonLoader text with Jev.

Only selected warning/error lines are sent to TypeSafe, and only with --jev.
The raw logs stay untouched. No third-party Python package is needed.
"""

import argparse
import csv
import json
import os
import re
import statistics
import sys
import urllib.error
import urllib.request
from collections import deque
from pathlib import Path

API = "https://api.typesafe.ai/v1/systemone"
PERF = re.compile(r"\b(perf|frame.?time|fps|gpu|cpu|render|draw calls?|reproject|stutter|spike|instanc)\b", re.I)
ISSUE = re.compile(r"\b(warn(?:ing)?|error|exception|fail(?:ed|ure)?|timeout)\b", re.I)


def number(row, key):
    try:
        return float(row[key]) if row[key] else None
    except (KeyError, ValueError):
        return None


def compare(current, baseline):
    fields = ("frame_p95_ms", "main_p95_ms", "render_p95_ms", "gpu_p95_ms", "draw_calls_p95")
    scenes = sorted({row.get("scene") for row in current} & {row.get("scene") for row in baseline})
    comparisons = []
    for scene in scenes:
        current_rows = [row for row in current if row.get("scene") == scene]
        baseline_rows = [row for row in baseline if row.get("scene") == scene]
        item = {"scene": scene, "current_windows": len(current_rows), "baseline_windows": len(baseline_rows)}
        for field in fields:
            current_values = [value for row in current_rows if (value := number(row, field)) is not None]
            baseline_values = [value for row in baseline_rows if (value := number(row, field)) is not None]
            if current_values and baseline_values:
                item[field + "_delta"] = round(statistics.median(current_values) - statistics.median(baseline_values), 3)
        comparisons.append(item)
    return comparisons


def jev_batch(lines, key):
    questions = {
        str(i): {
            "type": "noul",
            "instructions": (
                f"Could game or mod log line `lines.{i}` plausibly explain poor frame time, "
                "a rendering/CPU bottleneck, or invalidate a performance comparison? "
                "Judge relevance only; do not infer that it actually caused a slowdown."
            ),
            "criteria": {
                "true": "A performance cause or measurement confounder is plausible from this line.",
                "false": "Routine or unrelated warning/error with no apparent performance link."
            },
        }
        for i in range(len(lines))
    }
    payload = json.dumps({"model": "jev-latest", "state": {"lines": {str(i): line for i, line in enumerate(lines)}}, "questions": questions}).encode()
    request = urllib.request.Request(API, payload, {
        "Authorization": "Bearer " + key,
        "Content-Type": "application/json",
    })
    with urllib.request.urlopen(request, timeout=30) as response:
        data = json.load(response)
    return [data["answers"][str(i)]["noul"] for i in range(len(lines))], data.get("usage", {})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("csv", type=Path, help="FrameProbe session CSV")
    parser.add_argument("--baseline", type=Path, help="Baseline CSV for same-scene comparison")
    parser.add_argument("--log", type=Path, help="MelonLoader log to filter")
    parser.add_argument("--jev", action="store_true", help="Send selected warning/error lines to TypeSafe Jev")
    parser.add_argument("--hz", type=int, default=120, help="Headset refresh rate for the frame budget (default: 120)")
    parser.add_argument("--out", type=Path, help="Write compact JSON report here, instead of stdout")
    args = parser.parse_args()
    if args.hz <= 0:
        parser.error("--hz must be positive")
    if args.jev and not args.log:
        parser.error("--jev requires --log")
    key = os.environ.get("TYPESAFE_API_KEY", "")
    if args.jev and not key:
        parser.error("TYPESAFE_API_KEY is not present in this process environment")

    with args.csv.open(newline="", encoding="utf-8-sig") as source:
        rows = list(csv.DictReader(source))
    budget = 1000 / args.hz
    windows = []
    for row in rows:
        main_p95 = number(row, "main_p95_ms")
        gpu_p95 = number(row, "gpu_p95_ms")
        windows.append({
            "utc": row.get("utc"), "scene": row.get("scene"), "samples": row.get("samples"),
            "frame_p95_ms": number(row, "frame_p95_ms"),
            "main_p95_ms": main_p95, "render_p95_ms": number(row, "render_p95_ms"),
            "gpu_p95_ms": gpu_p95, "gpu_missing": row.get("gpu_missing"),
            "main_over_budget": main_p95 is not None and main_p95 > budget,
            "gpu_over_budget": gpu_p95 is not None and gpu_p95 > budget,
        })
    report = {"frame_budget_ms": round(budget, 3), "refresh_hz": args.hz,
              "windows": windows, "notes": ["Timing values are observations, not per-mod attribution."]}
    if args.baseline:
        with args.baseline.open(newline="", encoding="utf-8-sig") as source:
            baseline_rows = list(csv.DictReader(source))
        report["comparison"] = compare(rows, baseline_rows)
        report["notes"].append("Comparison deltas are current minus baseline, using the median of each scene's window p95 values. Match headset and game settings before interpreting them.")

    if args.log:
        obvious = deque(maxlen=80)
        ambiguous = deque(maxlen=40)
        with args.log.open(encoding="utf-8", errors="replace") as source:
            for line_no, line in enumerate(source, 1):
                line = line.strip()[:600]
                if PERF.search(line):
                    obvious.append({"line": line_no, "text": line, "reason": "performance keyword"})
                elif ISSUE.search(line):
                    ambiguous.append({"line": line_no, "text": line})
        kept = list(obvious)
        if args.jev:
            tokens = {"input_tokens": 0, "output_tokens": 0}
            jev_failed = False
            for offset in range(0, len(ambiguous), 10):
                batch = list(ambiguous)[offset:offset + 10]
                try:
                    scores, usage = jev_batch([item["text"] for item in batch], key)
                except (urllib.error.URLError, KeyError, ValueError) as error:
                    report["jev_error"] = str(error)
                    kept.extend({**item, "reason": "unclassified; Jev request failed"} for item in list(ambiguous)[offset:])
                    jev_failed = True
                    break
                for item, score in zip(batch, scores):
                    if score >= 0.5:
                        kept.append({**item, "reason": "Jev possible relevance", "probability": round(score, 3)})
                for token_key in tokens:
                    tokens[token_key] += usage.get(token_key, 0)
            report["jev_usage"] = tokens
            report["jev_candidates"] = len(ambiguous)
            report["jev_filter_complete"] = not jev_failed
        else:
            kept.extend({**item, "reason": "unclassified warning/error"} for item in ambiguous)
        report["log_lines"] = sorted(kept, key=lambda item: item["line"])

    rendered = json.dumps(report, indent=2, ensure_ascii=False)
    if args.out:
        args.out.write_text(rendered + "\n", encoding="utf-8")
        print(args.out)
    else:
        print(rendered)


if __name__ == "__main__":
    try:
        main()
    except (OSError, csv.Error) as error:
        print(f"FrameProbe: {error}", file=sys.stderr)
        sys.exit(1)
