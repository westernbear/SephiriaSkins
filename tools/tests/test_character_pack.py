import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_character_pack import build, compose


class CharacterPackTests(unittest.TestCase):
    def recipe(self):
        return {"manifest": {"id": "test.character", "version": "1.0.0", "name": "Test", "author": "Test"},
                "resources": {"pose.a": {"file": "pose.png", "kind": "sprite"},
                              "pose.b": {"file": "pose.png", "kind": "sprite"}},
                "bodyStates": {"MOVE": ["pose.a", "pose.b"]}}

    def catalog(self):
        return {"id": "test.catalog", "animations": {"body/exact": {
            "role": "body", "setName": "Body", "state": "MOVE", "frameIndices": [0, 3, 1]}},
            "visuals": {"weapon/addon": {"role": "weapon"}}, "ui": {}, "audio": {}}

    def test_unsorted_sparse_frame_indices_and_array_order_are_preserved(self):
        manifest, _ = compose(self.recipe(), self.catalog())
        self.assertEqual(manifest["body"]["body/exact"], {
            "frameIndices": [0, 3, 1], "frames": ["pose.a", "pose.a", "pose.b"]})
        self.assertEqual(manifest["weapons"], {})

    def test_single_pass_pose_phases_reach_the_last_authored_pose_without_restarting(self):
        recipe = self.recipe()
        recipe["resources"].update({"pose.c": {"file": "pose.png", "kind": "sprite"}})
        recipe["bodyStates"]["MOVE"] = {"poses": ["pose.a", "pose.b", "pose.c"], "phase": "once"}
        catalog = self.catalog()
        catalog["animations"]["body/exact"]["frameIndices"] = [0, 8, 2, 5, 4]
        manifest, _ = compose(recipe, catalog)
        binding = manifest["body"]["body/exact"]
        self.assertEqual(binding["frameIndices"], [0, 8, 2, 5, 4])
        self.assertEqual(binding["frames"], ["pose.a", "pose.c", "pose.a", "pose.b", "pose.b"])

    def test_missing_pose_and_weaponless_hidden_visual_are_rejected(self):
        recipe = self.recipe()
        recipe["bodyStates"] = {}
        with self.assertRaises(ValueError):
            compose(recipe, self.catalog())
        recipe = self.recipe()
        recipe["visuals"] = {"weapon/addon": {"hide": True}}
        with self.assertRaisesRegex(ValueError, "Weaponless"):
            compose(recipe, self.catalog())
        recipe = self.recipe()
        recipe["effectAnimations"] = {"body/exact": ["pose.a"]}
        with self.assertRaisesRegex(ValueError, "Wrong animation role"):
            compose(recipe, self.catalog())

    def test_build_refuses_overwrite_and_clipped_body(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            recipe = root / "recipe.json"
            recipe.write_text(json.dumps(self.recipe()), encoding="utf-8")
            catalog = root / "catalog.json"
            catalog.write_text(json.dumps(self.catalog()), encoding="utf-8")
            for record in ("PROVENANCE.md", "prompts.txt"):
                (root / record).write_text("Synthetic test fixture", encoding="utf-8")
            image = Image.new("RGBA", (16, 16))
            ImageDraw.Draw(image).rectangle((3, 3, 12, 12), fill="white")
            image.save(root / "pose.png")
            output = root / "new-pack"
            build(recipe, catalog, output)
            with self.assertRaisesRegex(ValueError, "never overwritten"):
                build(recipe, catalog, output)
            with self.assertRaisesRegex(ValueError, "ID already exists"):
                build(recipe, catalog, root / "duplicate")
            alternate = self.recipe()
            alternate["manifest"]["id"] = "test.clipped"
            recipe.write_text(json.dumps(alternate), encoding="utf-8")
            image.putpixel((0, 8), (255, 255, 255, 255))
            image.save(root / "pose.png")
            with self.assertRaisesRegex(ValueError, "touches cell edge"):
                build(recipe, catalog, root / "clipped")
            self.assertFalse((root / "clipped").exists())


if __name__ == "__main__":
    unittest.main()
