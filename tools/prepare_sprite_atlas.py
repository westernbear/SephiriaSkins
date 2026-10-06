"""Optional, explicitly authorized alpha cleanup of already generated artwork.

Never paints or recolors a character. Selected RGB pixels are copied verbatim;
only background alpha, disconnected residue and atlas placement are changed.
Keep the generated original, inspect the result, and distribute both the source
hash and this report in provenance. Do not use without the user's explicit approval.
"""
import argparse
import hashlib
import json
from collections import deque
from pathlib import Path

from PIL import Image, ImageFilter


def largest_component(mask, width, height):
    pending = bytearray(mask)
    best = []
    for start in range(len(pending)):
        if not pending[start]:
            continue
        pending[start] = 0
        queue = [start]
        found = []
        while queue:
            at = queue.pop()
            found.append(at)
            x, y = at % width, at // width
            for other in (at-1 if x else -1, at+1 if x+1 < width else -1,
                          at-width if y else -1, at+width if y+1 < height else -1):
                if other >= 0 and pending[other]:
                    pending[other] = 0
                    queue.append(other)
        if len(found) > len(best):
            best = found
    result = bytearray(width*height)
    for at in best:
        result[at] = 255
    return result, len(best)


def exterior_white(rgba):
    """Find exterior residue using the existing closed dark artwork outlines."""
    width, height = rgba.size
    pixels = list(rgba.getdata())
    outside = bytearray(width*height)
    queue = deque()
    # Closing one-pixel gaps is a mask operation only. No RGB ink is painted.
    # A closed original outline protects the white and blue body interior while
    # all colors of exterior cutout residue are removed, rather than only white.
    outline = Image.frombytes("L", rgba.size, bytes(255 if p[3] >= 64 and max(p[:3]) <= 130 else 0 for p in pixels))
    barrier = outline.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3)).tobytes()

    def traversable(at):
        return not barrier[at]

    for y in range(height):
        for x in range(width):
            if x in (0, width-1) or y in (0, height-1):
                at = y*width+x
                if traversable(at) and not outside[at]:
                    outside[at] = 1
                    queue.append(at)
    while queue:
        at = queue.popleft()
        x, y = at % width, at // width
        for other in (at-1 if x else -1, at+1 if x+1 < width else -1,
                      at-width if y else -1, at+width if y+1 < height else -1):
            if other >= 0 and not outside[other] and traversable(other):
                outside[other] = 1
                queue.append(other)
    return outside


def clean_cell(source, mode, effect_luma=208, effect_radius=7):
    width, height = source.size
    pixels = list(source.getdata())
    alpha = source.getchannel("A")
    _, original_count = largest_component(bytearray(255 if p[3] >= 64 else 0 for p in pixels), width, height)
    if mode == "effects":
        seeds = bytes(255 if p[3] >= 16 and sum(p[:3])/3 >= effect_luma else 0 for p in pixels)
        mask = Image.frombytes("L", source.size, seeds).filter(ImageFilter.MaxFilter(effect_radius*2+1))
        allowed = mask.tobytes()
    else:
        exterior = exterior_white(source) if mode == "body" else bytearray(width*height)
        foreground = bytearray(255 if p[3] >= 64 and not exterior[i] else 0 for i, p in enumerate(pixels))
        core, count = largest_component(foreground, width, height)
        if count < 500:
            raise ValueError("No complete connected artwork in cell")
        if mode == "body" and count < original_count * .65:
            raise ValueError("Outline did not protect the original body interior; redraw this pose")
        mask = Image.frombytes("L", source.size, bytes(core)).filter(ImageFilter.MaxFilter(3))
        allowed = bytes(255 if value and not exterior[i] else 0 for i, value in enumerate(mask.tobytes()))
    cleaned_alpha = bytes(p[3] if allowed[i] and p[3] >= 2 else 0 for i, p in enumerate(pixels))
    result = source.copy()
    result.putalpha(Image.frombytes("L", source.size, cleaned_alpha))
    if not result.getchannel("A").getbbox():
        raise ValueError("Cleanup produced an empty sprite")
    assert result.getchannel("R").tobytes() == source.getchannel("R").tobytes()
    assert result.getchannel("G").tobytes() == source.getchannel("G").tobytes()
    assert result.getchannel("B").tobytes() == source.getchannel("B").tobytes()
    bounds = result.getchannel("A").getbbox()
    if min(bounds[0], bounds[1], width-bounds[2], height-bounds[3]) < 2:
        raise ValueError("Authored artwork touches the source cell edge; do not invent clipped parts")
    return result, {"sourceVisiblePixels": sum(v > 0 for v in alpha.tobytes()),
                    "retainedVisiblePixels": sum(v > 0 for v in cleaned_alpha), "rgbUnchanged": True}


def prepare(source, output, columns, rows, mode, cell_size=256, effect_luma=208, effect_radius=7, row_cuts=None, column_cuts=None, columns_by_row=None):
    source, output = Path(source), Path(output)
    report_path = output.with_suffix(".cleanup.json")
    if output.exists() or report_path.exists():
        raise ValueError("Output PNG and report must be new")
    if min(columns, rows, cell_size) <= 0 or not 0 < effect_luma < 256 or not 0 <= effect_radius <= 12:
        raise ValueError("Invalid cleanup dimensions or thresholds")
    with Image.open(source) as raw:
        if raw.format != "PNG" or raw.mode != "RGBA":
            raise ValueError("Source must be the generated RGBA PNG")
        image = raw.copy()
    row_cuts = row_cuts or [round(n*image.height/rows) for n in range(rows+1)]
    column_cuts = column_cuts or [round(n*image.width/columns) for n in range(columns+1)]
    for cuts, count, extent in ((row_cuts, rows, image.height), (column_cuts, columns, image.width)):
        if len(cuts) != count+1 or cuts[0] != 0 or cuts[-1] != extent or any(a >= b for a,b in zip(cuts,cuts[1:])):
            raise ValueError("Authored cuts must cover the source exactly in increasing order")
    if columns_by_row is not None:
        if len(columns_by_row) != rows or any(len(cuts) != columns+1 or cuts[0] != 0 or cuts[-1] != image.width or
                any(a >= b for a,b in zip(cuts,cuts[1:])) for cuts in columns_by_row):
            raise ValueError("Reviewed row columns must cover each source row exactly in increasing order")
    canvas = Image.new("RGBA", (columns*cell_size, rows*cell_size))
    records = []
    for row in range(rows):
        for col in range(columns):
            row_columns = columns_by_row[row] if columns_by_row is not None else column_cuts
            box = (row_columns[col], row_cuts[row], row_columns[col+1], row_cuts[row+1])
            original = image.crop(box)
            try:
                cleaned, record = clean_cell(original, mode, effect_luma, effect_radius)
            except ValueError as error:
                raise ValueError(f"Source cell {row},{col}: {error}") from error
            bounds = cleaned.getchannel("A").getbbox()
            ink = cleaned.crop(bounds)
            # Removed background has no visible artwork. Clear only its hidden
            # RGB so PNG previews and texture interpolation cannot reveal it;
            # every retained visible RGBA pixel remains byte-identical.
            ink = Image.frombytes("RGBA", ink.size, b"".join(bytes(p) if p[3] else bytes(4) for p in ink.getdata()))
            if ink.width > cell_size-24 or ink.height > cell_size-24:
                raise ValueError("Artwork cannot fit with padding without scaling; choose a larger output cell")
            # No interpolation: the generated RGB artwork stays unchanged.
            x = col*cell_size+(cell_size-ink.width)//2
            y = row*cell_size+(210-ink.height if mode == "body" else (cell_size-ink.height)//2)
            if y < row*cell_size+12 or y+ink.height > (row+1)*cell_size-12:
                raise ValueError("Whole unscaled pose cannot fit the authored feet baseline")
            canvas.paste(ink, (x, y))
            records.append(record | {"row": row, "column": col, "sourceCell": list(box), "sourceInk": list(bounds), "destination": [x,y]})
    canvas.save(output)
    report = {"source": source.name, "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
              "output": output.name, "outputSha256": hashlib.sha256(output.read_bytes()).hexdigest(),
              "mode": mode, "grid": [columns,rows], "cellSize": cell_size,
              "sourceRowCuts": row_cuts, "sourceColumnCuts": column_cuts,
              "sourceColumnCutsByRow": columns_by_row,
              "effectSeedLuma": effect_luma if mode == "effects" else None,
              "effectOutlineRadius": effect_radius if mode == "effects" else None,
              "transparentRgbCleared": True,
              "method": "Background alpha isolation, zero RGB only for removed transparent pixels, and unscaled cell placement; retained artwork RGBA copied verbatim",
              "cells": records}
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("output")
    parser.add_argument("--columns", type=int, required=True)
    parser.add_argument("--rows", type=int, required=True)
    parser.add_argument("--mode", choices=("body","ui","effects"), required=True)
    parser.add_argument("--cell-size", type=int, default=256)
    parser.add_argument("--effect-seed-luma", type=float, default=208)
    parser.add_argument("--effect-outline-radius", type=int, default=7)
    parser.add_argument("--row-cuts", help="Reviewed source row boundaries, comma-separated; avoids clipping an irregular generated layout")
    parser.add_argument("--column-cuts", help="Reviewed source column boundaries, comma-separated")
    parser.add_argument("--row-columns-file", help="JSON array of reviewed column boundaries per row; supports irregular generated layouts without clipping")
    parser.add_argument("--confirm-alpha-cleanup", action="store_true", help="Use only after explicit user authorization; the flag is not authorization")
    args = parser.parse_args()
    if not args.confirm_alpha_cleanup:
        parser.error("Explicit permission for scripted image cleanup is required")
    report = prepare(args.source,args.output,args.columns,args.rows,args.mode,args.cell_size,args.effect_seed_luma,args.effect_outline_radius,
                     [int(n) for n in args.row_cuts.split(',')] if args.row_cuts else None,
                     [int(n) for n in args.column_cuts.split(',')] if args.column_cuts else None,
                     json.loads(Path(args.row_columns_file).read_text(encoding='utf-8-sig')) if args.row_columns_file else None)
    print(json.dumps({key: report[key] for key in ("output","outputSha256","mode","grid","method")}, indent=2))
