import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_combat_results import audit
from verify_play_results import verify_combat, verify_combat_case


class CombatAuditTests(unittest.TestCase):
    def report(self):
        trial = {"events": ["basic:0"], "projectiles": ['{"damage":20}'], "guarded": False,
                 "hits": [], "unitHits": [], "mpCosts": [], "radius": .4, "radiusAfter": .4,
                 "canMove": True, "themedFrames": 8, "fireSeconds": [.1], "frameMilliseconds": 16}
        case = {"id": 19, "mode": "basic", "original": copy.deepcopy(trial), "themed": copy.deepcopy(trial)}
        case["original"]["themedFrames"] = 0
        coverage = {"modes": ["basic", "dash", "special"], "weapons": [
            {"id": 19, "skippedModes": {"special": "native game exception, reproduced with skin hooks disabled"}},
            {"id": 100, "skippedModes": {}}]}
        return {"results": [{"name": "weapon coverage", "value": coverage},
                            {"name": "combat 19 basic", "value": case}], "restored": None}

    def test_interrupted_audit_keeps_unexecuted_trials_and_native_skip_scope(self):
        report = self.report()
        result = audit(report)
        self.assertEqual(result["expectedTrials"], 5)
        self.assertEqual(result["unexecutedTrials"], [
            {"id": 19, "mode": "dash"}, {"id": 100, "mode": "basic"},
            {"id": 100, "mode": "dash"}, {"id": 100, "mode": "special"}])
        self.assertFalse(result["cleanupRecorded"])
        with self.assertRaisesRegex(ValueError, "enumerated weapon trials"):
            verify_combat(report)

    def test_audit_reports_damage_difference_and_duplicate_evidence(self):
        report = self.report()
        report["results"][-1]["value"]["themed"]["projectiles"] = ['{"damage":21}']
        report["results"].append(copy.deepcopy(report["results"][-1]))
        result = audit(report)
        self.assertEqual(len(result["failedTrials"]), 2)
        self.assertEqual(result["matchingTrials"], [])
        self.assertEqual(result["duplicateTrials"], [{"id": 19, "mode": "basic"}])
        self.assertIn("damage/shape/colliders", result["failedTrials"][0]["reason"])

    def test_native_baseline_needs_original_frames_and_still_checks_damage(self):
        case = self.report()["results"][-1]["value"]
        with self.assertRaisesRegex(ValueError, "native baseline body frames"):
            verify_combat_case(case, require_themed=False)
        case["themed"]["themedFrames"] = 0
        verify_combat_case(case, require_themed=False)
        with self.assertRaisesRegex(ValueError, "themed body frames"):
            verify_combat_case(case)
        case["themed"]["projectiles"] = ['{"damage":21}']
        with self.assertRaisesRegex(ValueError, "damage/shape/colliders"):
            verify_combat_case(case, require_themed=False)

    def test_identical_pre_exception_payloads_are_never_counted_as_matching_trials(self):
        report = self.report()
        report["exceptions"] = ["attack 19 basic original: native NullReferenceException"]
        result = audit(report)
        self.assertEqual(result["matchingTrials"], [])
        self.assertEqual(result["failedTrials"][0]["exceptions"], report["exceptions"])

    def test_incomplete_status_lifetime_cannot_pass_even_with_matching_partial_damage(self):
        case = self.report()["results"][-1]["value"]
        case["original"]["targetStatusComplete"] = True
        case["themed"]["targetStatusComplete"] = False
        with self.assertRaisesRegex(ValueError, "status lifetime completed"):
            verify_combat_case(case)
        case["themed"]["targetStatusComplete"] = True
        verify_combat_case(case)


if __name__ == "__main__":
    unittest.main()
