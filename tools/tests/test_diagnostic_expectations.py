import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from diagnostic_expectations import expected_trials, verify_body_renderers, verify_weapon_renderers
from verify_play_results import native_quick_draw, native_transformed_attack, native_movement_dash, native_crossbow_preparation, native_charged_bow
from verify_visual_results import verify_presentation


class DiagnosticExpectationsTests(unittest.TestCase):
    def test_toggle_comparison_requires_the_same_authored_frame_and_world_geometry(self):
        comparison = {"resource": "body.0.0", "pixelFrame": "fan.test:body.0.0", "smoothFrame": "fan.test:body.0.0",
                      "sampledSmoothFrame": "fan.test:body.0.1", "pixelBounds": [1, 1.1], "smoothBounds": [1, 1.1],
                      "pixelPivot": [.5, .04], "smoothPivot": [.5, .04]}
        verify_presentation(comparison, "fan.test")
        for alteration in ({"smoothFrame": "fan.test:body.0.1"}, {"smoothBounds": [.9, 1.1]}, {"smoothPivot": [.4, .04]}):
            with self.assertRaises(ValueError):
                verify_presentation({**comparison, **alteration}, "fan.test")

    def test_bow_needs_recorded_native_charge_phase_and_real_basic_arrow(self):
        trial = {"nativeClass": "WeaponSimple_Bow", "bowRelease": {"ratio": 1, "phase": 1},
                 "projectiles": ['{"damageId":"Weapon_BasicAttack","kind":"Bullet"}']}
        self.assertTrue(native_charged_bow(trial))
        for alteration in ({"bowRelease": {"ratio": .9, "phase": 1}},
                           {"bowRelease": {"ratio": 1, "phase": 0}},
                           {"projectiles": []}, {"nativeClass": "WeaponSimple_Crossbow"},
                           {"projectiles": ['{"damageId":"Weapon_SpecialAttack","kind":"Bullet"}']}):
            self.assertFalse(native_charged_bow({**trial, **alteration}))

    def test_crossbow_secondary_needs_native_state_change_and_actual_shot(self):
        before = {"nativeClass": "WeaponSimple_Crossbow", "crossbowSpecial": "FastReload", "ammo": 2, "magazineCapacity": 5}
        after = {**before, "ammo": 5}
        trial = {"events": ["basic:0"], "secondaryBefore": before, "secondary": after, "mpCosts": [10]}
        self.assertTrue(native_crossbow_preparation(trial))
        self.assertFalse(native_crossbow_preparation({**trial, "events": []}))
        self.assertFalse(native_crossbow_preparation({**trial, "secondaryBefore": after}))
        self.assertFalse(native_crossbow_preparation({**trial, "mpCosts": []}))
        for action, field, initial, final in (("IceBuff", "iceBuff", 0, 1), ("AmmoCompression", "compressedAmmo", False, True)):
            prepared = {**trial, "secondaryBefore": {"nativeClass": "WeaponSimple_Crossbow", field: initial},
                        "secondary": {"nativeClass": "WeaponSimple_Crossbow", "crossbowSpecial": action, field: final}}
            self.assertTrue(native_crossbow_preparation(prepared))
            self.assertFalse(native_crossbow_preparation({**prepared, "secondaryBefore": prepared["secondary"]}))

    def test_non_damaging_dash_needs_native_null_firedata_and_no_hit(self):
        trial = {"nativeClass": "WeaponSimple_Dagger", "nativeDashData": None, "events": ["dash"], "projectiles": [], "hits": [], "unitHits": []}
        self.assertTrue(native_movement_dash(trial))
        self.assertFalse(native_movement_dash({**trial, "nativeDashData": "missed projectile"}))
        self.assertFalse(native_movement_dash({**trial, "hits": ["damage"]}))
        self.assertFalse(native_movement_dash({k:v for k,v in trial.items() if k != "nativeDashData"}))

    def test_secondary_basic_event_needs_proven_native_transformation(self):
        trial = {"events": ["basic:3"], "secondary": {"nativeClass": "WeaponSimple_GreatSword", "transforms": True, "transformed": True}}
        self.assertTrue(native_transformed_attack(trial))
        self.assertFalse(native_transformed_attack({**trial, "secondary": {**trial["secondary"], "transformed": False}}))
        self.assertFalse(native_transformed_attack({"events": ["basic:3"]}))

    def test_secondary_basic_event_needs_proven_native_sheath_preparation(self):
        trial = {"events": ["basic:2"], "secondary": {"nativeClass": "WeaponSimple_Katana",
            "sheathAction": "Sheath", "useQuickDraw": True, "bladeSheathed": True,
            "sheathEnabled": True, "animatorGuard": True}}
        self.assertTrue(native_quick_draw(trial))
        for key in ("useQuickDraw", "bladeSheathed", "sheathEnabled", "animatorGuard"):
            altered = {**trial, "secondary": {**trial["secondary"], key: False}}
            self.assertFalse(native_quick_draw(altered))
        self.assertFalse(native_quick_draw({"events": ["basic:2"]}))

    def test_upgraded_ids_are_required_and_skips_have_reasons(self):
        coverage = {"modes": ["basic", "dash", "special"], "weapons": [
            {"id": 913, "skipReason": None}, {"id": 914, "skipReason": "no native prefab"}]}
        self.assertEqual(expected_trials(coverage), {(913, m) for m in coverage["modes"]})
        coverage["weapons"][1]["skipReason"] = ""
        with self.assertRaises(ValueError):
            expected_trials(coverage)

    def test_excluding_a_native_fault_requires_a_reason_and_only_omits_that_mode(self):
        coverage = {"modes": ["basic", "dash", "special"], "weapons": [
            {"id": 19, "skippedModes": {"special": "native NullReferenceException reproduced without theme hooks"}}]}
        self.assertEqual(expected_trials(coverage), {(19, "basic"), (19, "dash")})
        coverage["weapons"][0]["skippedModes"]["special"] = ""
        with self.assertRaisesRegex(ValueError, "skip reasons"):
            expected_trials(coverage)

    def renderer(self, path, key, sprite):
        return {"path": path, "visualKey": key, "sprite": sprite, "active": True, "shader": "native"}

    def test_weaponless_pack_retains_native_weapon_and_detects_previous_pack_leak(self):
        native = self.renderer("weapon/body", "weapon/body/SpriteRenderer", "original")
        manifest = {"id": "fan.test", "visuals": {}, "weapons": {}}
        verify_weapon_renderers(manifest, [native], [dict(native)])
        leaked = dict(native, sprite="fan.previous:weapon")
        with self.assertRaisesRegex(ValueError, "native weapon retained"):
            verify_weapon_renderers(manifest, [native], [leaked])

    def test_arbitrary_manifest_sprite_hide_and_mirror_are_enforced(self):
        body = self.renderer("body", "weapon/body", "fan.test:custom.sword")
        addon = self.renderer("addon", "weapon/addon", None)
        mask = dict(self.renderer("mask", "weapon/mask", body["sprite"]), mirrorSource="body")
        manifest = {"id": "fan.test", "weapons": {"animation": {}}, "visuals": {
            "weapon/body": {"sprite": "custom.sword"}, "weapon/addon": {"hide": True}}}
        original = [dict(body, sprite="original"), dict(addon, sprite="decoration"), dict(mask, sprite="original")]
        self.assertEqual(verify_weapon_renderers(manifest, original, [body, addon, mask]), 2)
        with self.assertRaisesRegex(ValueError, "aligned mask"):
            verify_weapon_renderers(manifest, original, [body, addon, dict(mask, sprite="old")])
        with self.assertRaisesRegex(ValueError, "hidden decoration"):
            verify_weapon_renderers(manifest, original, [body, dict(addon, sprite="duplicate"), mask])

    def test_body_masks_and_reflections_keep_native_transform_and_frame(self):
        manifest = {"id": "fan.test", "body": {"body/exact": {"frames": ["idle", "blink"]}}}
        original = [dict(self.renderer(path, None, "original"), fullTexture=True, scale=scale, sorting=0, layer="native")
                    for path, scale in (("Body", "(1,1,1)"), ("Body/StencilSolid", "(1,1,1)"), ("WaterReflection", "(1,-1,1)"))]
        # Actual paths contain their native avatar parent.
        for renderer in original:
            renderer["path"] = "Avatar/" + renderer["path"]
        themed = [dict(renderer, sprite="fan.test:idle") for renderer in original]
        self.assertEqual(verify_body_renderers(manifest, original, themed)["nativeReflections"], 1)
        with self.assertRaisesRegex(ValueError, "alignment"):
            verify_body_renderers(manifest, original, [*themed[:2], dict(themed[2], sprite="fan.test:blink")])
        with self.assertRaisesRegex(ValueError, "native body scale"):
            verify_body_renderers(manifest, original, [*themed[:2], dict(themed[2], scale="(1,1,1)")])


if __name__ == "__main__":
    unittest.main()
