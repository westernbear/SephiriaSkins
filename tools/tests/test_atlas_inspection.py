import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inspect_sprite_atlas import inspect


class AtlasInspectionTests(unittest.TestCase):
    def test_effect_phases_retain_common_cell_extent_and_center(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "effects.png"
            image = Image.new("RGBA", (192, 64))
            draw = ImageDraw.Draw(image)
            draw.rectangle((44, 28, 52, 36), fill="white")
            draw.rectangle((124, 18, 162, 46), fill="white")
            image.save(path)
            before = path.read_bytes()
            result = inspect(path, 2, 1, "fx", anchor="center", rect_mode="cell")
            first, peak = result["resources"].values()
            self.assertEqual(first["rect"], [0, 0, 96, 64])
            self.assertEqual(peak["rect"], [96, 0, 96, 64])
            self.assertEqual(first["pivot"], peak["pivot"])
            self.assertEqual(first["pivot"], [.5, .5])
            self.assertEqual(first["pixelsPerUnit"], peak["pixelsPerUnit"])
            self.assertEqual(path.read_bytes(), before)

    def artwork(self, path):
        # Synthetic metadata fixture only, never used as character artwork.
        image = Image.new("RGBA", (160, 100))
        ink = ImageDraw.Draw(image)
        ink.rectangle((30, 20, 60, 60), fill="white")
        ink.rectangle((55, 35, 85, 50), fill="white")  # Connected tail crosses the nominal grid.
        ink.rectangle((110, 20, 140, 60), fill="white")
        image.save(path)
        return image

    def test_component_rectangles_preserve_tail_source_bytes_and_feet_anchor(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "body.png"
            self.artwork(path)
            before = hashlib.sha256(path.read_bytes()).hexdigest()
            with self.assertRaisesRegex(ValueError, "cell edge"):
                inspect(path, 2, 1, "body")
            result = inspect(path, 2, 1, "body", world_height=1, layout="components")
            first = result["resources"]["body.0.0"]
            second = result["resources"]["body.0.1"]
            self.assertEqual(first["rect"], [24, 33, 68, 53])
            self.assertAlmostEqual(first["pivot"][1], 6/53)
            self.assertEqual(first["pixelsPerUnit"], second["pixelsPerUnit"])
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), before)

    def test_detached_low_alpha_ink_is_rejected_instead_of_silently_clipped(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "body.png"
            image = self.artwork(path)
            image.putpixel((95, 80), (255, 255, 255, 12))
            image.save(path)
            with self.assertRaisesRegex(ValueError, "Alpha outside authored body rectangles"):
                inspect(path, 2, 1, "body", layout="components")

    def test_grid_rejects_empty_cells_and_opaque_backgrounds(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "body.png"
            image = Image.new("RGBA", (64, 32))
            ImageDraw.Draw(image).rectangle((8, 8, 22, 22), fill="white")
            image.save(path)
            with self.assertRaisesRegex(ValueError, "Empty cell"):
                inspect(path, 2, 1, "body")
            Image.new("RGBA", (64, 32), "white").save(path)
            with self.assertRaisesRegex(ValueError, "transparent background"):
                inspect(path, 2, 1, "body")


if __name__ == "__main__":
    unittest.main()
