"""Synthetic alpha-cleanup fixtures, never production character artwork."""
import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from prepare_sprite_atlas import clean_cell, prepare


class SpritePreparationTests(unittest.TestCase):
    def body(self):
        image = Image.new("RGBA", (128, 128))
        draw = ImageDraw.Draw(image)
        draw.rectangle((40, 30, 85, 90), fill=(250, 250, 250, 255), outline=(30, 20, 40, 255), width=3)
        draw.rectangle((17, 15, 23, 22), fill=(248, 248, 248, 200))
        image.putpixel((1, 1), (10, 90, 240, 1))
        return image

    def test_body_preserves_closed_white_interior_and_rgb_removes_exterior_residue(self):
        original = self.body()
        result, record = clean_cell(original, "body")
        self.assertEqual(result.getpixel((60, 60)), original.getpixel((60, 60)))
        self.assertEqual(result.getpixel((20, 18))[3], 0)
        self.assertEqual(result.getpixel((1, 1))[3], 0)
        for channel in "RGB":
            self.assertEqual(result.getchannel(channel).tobytes(), original.getchannel(channel).tobytes())
        self.assertTrue(record["rgbUnchanged"])

    def test_open_outline_cannot_silently_erase_white_body(self):
        image = self.body()
        ImageDraw.Draw(image).rectangle((58, 28, 65, 35), fill=(250, 250, 250, 255))
        with self.assertRaises(ValueError):
            clean_cell(image, "body")

    def test_effect_keeps_luminous_ink_and_outline_without_blue_background(self):
        image = Image.new("RGBA", (128, 128), (50, 90, 180, 100))
        draw = ImageDraw.Draw(image)
        draw.rectangle((47, 47, 81, 81), fill=(240, 240, 250, 180), outline=(20, 30, 60, 255), width=3)
        result, _ = clean_cell(image, "effects")
        self.assertEqual(result.getpixel((60, 60)), image.getpixel((60, 60)))
        self.assertEqual(result.getpixel((47, 60)), image.getpixel((47, 60)))
        self.assertEqual(result.getpixel((15, 15))[3], 0)

    def test_new_atlas_preserves_source_and_selected_pixels_without_resampling(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory)/"original.png", Path(directory)/"prepared.png"
            original = self.body()
            original.save(source)
            prior_hash = hashlib.sha256(source.read_bytes()).hexdigest()
            report = prepare(source, output, 1, 1, "body")
            self.assertEqual(report["sourceSha256"], prior_hash)
            self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), prior_hash)
            cell = report["cells"][0]
            left, top, _, _ = cell["sourceInk"]
            x, y = cell["destination"]
            with Image.open(output) as prepared:
                self.assertEqual(prepared.getpixel((x+60-left, y+60-top)), original.getpixel((60, 60)))
            with self.assertRaisesRegex(ValueError, "must be new"):
                prepare(source, output, 1, 1, "body")

    def test_source_edge_art_is_rejected_instead_of_inventing_clipped_parts(self):
        image = Image.new("RGBA", (128, 128))
        ImageDraw.Draw(image).rectangle((0, 30, 80, 90), fill=(30, 20, 40, 255))
        with self.assertRaisesRegex(ValueError, "cell edge"):
            clean_cell(image, "ui")

    def test_reviewed_unequal_rows_preserve_visible_pixels_and_clear_hidden_rgb(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory)/"original.png", Path(directory)/"prepared.png"
            original = Image.new("RGBA", (128, 200), (12, 25, 85, 0))
            draw = ImageDraw.Draw(original)
            draw.rectangle((40, 20, 85, 50), fill=(120, 40, 80, 255))
            draw.rectangle((40, 95, 85, 140), fill=(70, 110, 190, 255))
            draw.rectangle((55, 105, 65, 115), fill=(12, 25, 85, 0))
            original.save(source)
            report = prepare(source, output, 1, 2, "ui", row_cuts=[0, 75, 200])
            self.assertEqual(report["sourceRowCuts"], [0, 75, 200])
            with Image.open(output) as prepared:
                self.assertTrue(all(pixel == (0, 0, 0, 0) for pixel in prepared.getdata() if pixel[3] == 0))
                for index, point in enumerate(((45, 25), (45, 100))):
                    cell = report["cells"][index]
                    left, top, _, _ = cell["sourceInk"]
                    x, y = cell["destination"]
                    actual = prepared.getpixel((x+point[0]-left, y+point[1]-cell["sourceCell"][1]-top))
                    self.assertEqual(actual, original.getpixel(point))
            with self.assertRaisesRegex(ValueError, "cover the source"):
                prepare(source, Path(directory)/"invalid.png", 1, 2, "ui", row_cuts=[0, 80, 199])

    def test_row_specific_columns_preserve_panels_that_cross_a_nominal_grid(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory)/"original.png"
            image = Image.new("RGBA", (256, 200))
            draw = ImageDraw.Draw(image)
            for box in ((20, 15, 130, 80), (175, 15, 240, 80), (20, 115, 90, 180), (110, 115, 235, 180)):
                draw.rectangle(box, fill=(120, 40, 80, 255))
            image.save(source)
            with self.assertRaisesRegex(ValueError, "cell edge"):
                prepare(source, Path(directory)/"nominal.png", 2, 2, "ui")
            report = prepare(source, Path(directory)/"reviewed.png", 2, 2, "ui",
                             columns_by_row=[[0, 150, 256], [0, 100, 256]])
            self.assertEqual(report["cells"][0]["sourceCell"], [0, 0, 150, 100])
            self.assertEqual(report["cells"][2]["sourceCell"], [0, 100, 100, 200])
            self.assertTrue(all(cell["rgbUnchanged"] for cell in report["cells"]))


if __name__ == "__main__":
    unittest.main()
