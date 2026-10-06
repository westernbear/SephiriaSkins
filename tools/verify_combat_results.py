"""Verify a completed combat-only game run, retaining its declared inventory scope."""
import argparse
import json
from pathlib import Path

from verify_play_results import clean_play, entries, read, require, verify_combat


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    parser.add_argument("--baseline", action="store_true", help="Native/native comparison with skin Harmony hooks disabled")
    args = parser.parse_args()
    report = read(args.report)
    run = read(Path(args.report).with_name("run.json"))
    require("--skins-playtest-combat-only" in run["arguments"], "combat-only launch evidence")
    require(("--skins-probe-baseline" in run["arguments"]) == args.baseline, "matching native baseline launch evidence")
    require(report["packId"] == run["packId"] and report["runId"] == run["runId"] and
            report["pluginSha256"] == run["pluginSha256"], "matching combat run")
    clean_play(report)
    result = verify_combat(report, require_themed=not args.baseline)
    result.update(packId=report["packId"], comparison="native/native" if args.baseline else "native/theme",
                  weaponScope=entries(report, "weapon coverage")[0]["scope"])
    print(json.dumps(result, ensure_ascii=False, indent=2))
