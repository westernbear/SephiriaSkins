"""List every failed or unexecuted native combat trial without declaring a pass.

The strict release verifier remains verify_play_results.py. This inventory audit
also accepts interrupted evidence so unfinished trials cannot disappear from
the reported scope.
"""
import argparse
import json
import re
from collections import Counter
from pathlib import Path

from diagnostic_expectations import expected_trials
from verify_play_results import entries, read, verify_combat_case


def audit(report, baseline=False):
    coverage = entries(report, "weapon coverage")[0]
    expected = expected_trials(coverage)
    cases = [item["value"] for item in report["results"] if item["name"].startswith("combat ")]
    counts = Counter((case["id"], case["mode"]) for case in cases)
    trial_exceptions = {}
    for exception in report.get("exceptions", []):
        match = re.match(r"attack (\d+) (basic|dash|special) ", exception)
        if match:
            trial_exceptions.setdefault((int(match[1]), match[2]), []).append(exception)
    failures = []
    passed = []
    for case in cases:
        exceptions = trial_exceptions.get((case["id"], case["mode"]), [])
        if exceptions:
            failures.append({"id": case["id"], "mode": case["mode"],
                             "reason": "game exception recorded during this native trial", "exceptions": exceptions})
            continue
        try:
            delta = verify_combat_case(case, require_themed=not baseline)
            passed.append({"id": case["id"], "mode": case["mode"], "fireTimingDifferenceMs": round(delta*1000, 3)})
        except (ValueError, KeyError, TypeError) as error:
            failures.append({"id": case["id"], "mode": case["mode"], "reason": str(error)})
    def modes(trials):
        return [{"id": weapon, "mode": mode} for weapon, mode in sorted(trials)]
    restored = report.get("restored")
    return {"packId": report.get("packId"), "runId": report.get("runId"),
            "comparison": "native/native" if baseline else "native/theme",
            "pluginSha256": report.get("pluginSha256"), "coverage": coverage,
            "expectedTrials": len(expected), "recordedTrials": len(cases),
            "matchingTrials": passed, "failedTrials": failures,
            "unexecutedTrials": modes(expected - counts.keys()),
            "unexpectedTrials": modes(counts.keys() - expected),
            "duplicateTrials": modes(key for key, count in counts.items() if count != 1),
            "exceptions": report.get("exceptions", []), "hooks": report.get("hooks", []),
            "cleanupRecorded": restored is not None,
            "cleanup": restored,
            "scope": "Recorded basic/dash/special input trials only; this audit does not grant release approval or verify missing attack variants."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    parser.add_argument("--output")
    parser.add_argument("--baseline", action="store_true", help="Audit native/native trials; this does not turn failed baseline comparisons into passes")
    args = parser.parse_args()
    result = audit(read(args.report), args.baseline)
    if args.output:
        destination = Path(args.output)
        if destination.exists():
            raise ValueError("Audit output already exists; preserve the earlier evidence.")
        destination.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("expectedTrials", "recordedTrials", "failedTrials", "cleanupRecorded")} |
                     {"matchingTrials": len(result["matchingTrials"]), "unexecutedTrials": len(result["unexecutedTrials"])},
                     ensure_ascii=False, indent=2))
