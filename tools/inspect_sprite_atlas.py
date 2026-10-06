"""Inspect authored PNG cells and write sprite rectangles without altering the artwork.

The artist supplies the grid and anchors. Empty cells, ink touching a cell edge,
and opaque backgrounds fail rather than silently clipping a pose or its tail.
"""
import argparse
import json
from pathlib import Path
from PIL import Image


def component_boxes(alpha, columns, rows):
    width, height = alpha.size
    ink = bytearray(255 if value >= 128 else 0 for value in alpha.tobytes())
    major = {}
    minor = []
    for start in range(len(ink)):
        if not ink[start]:
            continue
        ink[start] = 0
        stack = [start]
        count, x0, y0, x1, y1 = 0, width, height, 0, 0
        while stack:
            pixel = stack.pop()
            x, y = pixel % width, pixel // width
            count += 1
            x0, y0, x1, y1 = min(x0, x), min(y0, y), max(x1, x+1), max(y1, y+1)
            for neighbour in ((pixel-1 if x else -1), (pixel+1 if x+1 < width else -1),
                              (pixel-width if y else -1), (pixel+width if y+1 < height else -1)):
                if neighbour >= 0 and ink[neighbour]:
                    ink[neighbour] = 0
                    stack.append(neighbour)
        if count < 500:
            minor.append((count, (x0, y0, x1, y1)))
            continue
        cell = (min(rows-1, int((y0+y1)/2/height*rows)), min(columns-1, int((x0+x1)/2/width*columns)))
        if cell in major:
            raise ValueError(f"Multiple connected silhouettes assigned to cell {cell}")
        major[cell] = (x0, y0, x1, y1)
    if len(major) != columns*rows:
        raise ValueError(f"Expected {columns*rows} connected silhouettes, found {len(major)}")
    # Unattached artwork outside the body rectangles is an error. Components
    # inside a silhouette's rectangle can be facial details; record for review.
    details = []
    for count, box in minor:
        owners = [cell for cell, bounds in major.items() if bounds[0] <= box[0] and bounds[1] <= box[1]
                  and bounds[2] >= box[2] and bounds[3] >= box[3]]
        if len(owners) != 1:
            raise ValueError(f"Detached ink outside a silhouette: {box}, {count} pixels")
        details.append({"cell": list(owners[0]), "rect": list(box), "pixels": count})
    return major, details


def inspect(path, columns, rows, prefix, world_height=1.0, anchor="feet", padding=6, layout="grid", rect_mode="ink"):
    if columns < 1 or rows < 1 or padding < 1 or world_height <= 0:
        raise ValueError("Positive grid, padding and world height required")
    if rect_mode not in ("ink", "cell") or rect_mode == "cell" and (layout != "grid" or anchor != "center"):
        raise ValueError("Full cell rectangles require grid layout and a center anchor")
    with Image.open(path) as image:
        if image.format != "PNG" or "A" not in image.getbands():
            raise ValueError("Authored PNG must contain alpha")
        alpha = image.getchannel("A")
        if alpha.getextrema()[0] > 0:
            raise ValueError("PNG has no fully transparent background")
        width, height = image.size
        detected, details = component_boxes(alpha, columns, rows) if layout == "components" else ({}, [])
        cells = []
        for row in range(rows):
            for col in range(columns):
                left, right = round(col*width/columns), round((col+1)*width/columns)
                top, bottom = round(row*height/rows), round((row+1)*height/rows)
                box = detected.get((row, col))
                if box:
                    x0, y0, x1, y1 = box
                    if min(x0, y0, width-x1, height-y1) < padding:
                        raise ValueError(f"Silhouette touches atlas edge {row},{col}")
                    for other, bounds in detected.items():
                        if other != (row, col) and x0-padding < bounds[2] and x1+padding > bounds[0] and y0-padding < bounds[3] and y1+padding > bounds[1]:
                            raise ValueError(f"Neighbour enters sprite padding {row},{col}")
                    x0, y0, x1, y1 = x0-padding, y0-padding, x1+padding, y1+padding
                    cells.append((row, col, left, right, x0, y0, x1, y1, box[3]-box[1]))
                    continue
                ink = alpha.crop((left, top, right, bottom))
                box = ink.getbbox()
                if not box:
                    raise ValueError(f"Empty cell {row},{col}")
                if min(box[0], box[1], right-left-box[2], bottom-top-box[3]) < padding:
                    raise ValueError(f"Ink too close to cell edge {row},{col}; redraw with wider gutters")
                x0, y0, x1, y1 = (left, top, right, bottom) if rect_mode == "cell" else (left+box[0]-padding, top+box[1]-padding, left+box[2]+padding, top+box[3]+padding)
                cells.append((row, col, left, right, x0, y0, x1, y1, box[3]-box[1]))
        if layout == "components":
            remaining = bytearray(alpha.tobytes())
            for _, _, _, _, x0, y0, x1, y1, _ in cells:
                empty = bytes(x1-x0)
                for y in range(y0, y1):
                    remaining[y*width+x0:y*width+x1] = empty
            outside = next((pixel for pixel, value in enumerate(remaining) if value), None)
            if outside is not None:
                raise ValueError(f"Alpha outside authored body rectangles: {outside % width},{outside // width}; redraw detached ink or widen authored gutters")
        # Standing front idle is the explicit scale reference; other poses keep
        # this same PPU rather than changing body size as their bounds change.
        ppu = (round(height/rows) if rect_mode == "cell" else cells[0][-1]) / world_height
        resources = {}
        for row, col, left, right, x0, y0, x1, y1, ink_height in cells:
            pivot_x = ((left+right)/2-x0)/(x1-x0)
            if not 0 <= pivot_x <= 1:
                raise ValueError(f"Authored anchor outside pose {row},{col}")
            resources[f"{prefix}.{row}.{col}"] = {
                "file": Path(path).name, "kind": "sprite", "rect": [x0, height-y1, x1-x0, y1-y0],
                "pivot": [pivot_x, padding/(y1-y0) if anchor == "feet" else .5], "pixelsPerUnit": ppu}
        return {"source": Path(path).name, "size": [width, height], "grid": [columns, rows],
                "layout": layout, "rectMode": rect_mode, "padding": padding, "interiorDetailsForReview": details, "resources": resources}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("png")
    parser.add_argument("output", help="New inspection JSON; source PNG is never edited")
    parser.add_argument("--columns", type=int, required=True)
    parser.add_argument("--rows", type=int, required=True)
    parser.add_argument("--prefix", default="body")
    parser.add_argument("--world-height", type=float, default=1.0)
    parser.add_argument("--anchor", choices=("feet", "center"), default="feet")
    parser.add_argument("--layout", choices=("grid", "components"), default="grid",
                        help="Components is for connected body silhouettes only; it does not edit artwork")
    parser.add_argument("--rect-mode", choices=("ink", "cell"), default="ink",
                        help="Cell preserves the common center and phase extent of an effect sequence")
    args = parser.parse_args()
    output = Path(args.output)
    if output.exists():
        parser.error("Inspection output must be new")
    output.write_text(json.dumps(inspect(args.png, args.columns, args.rows, args.prefix,
                                        args.world_height, args.anchor, layout=args.layout, rect_mode=args.rect_mode), indent=2), encoding="utf-8")
