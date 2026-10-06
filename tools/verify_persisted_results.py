"""Verify a fresh process loaded a prepared selection and both saved switches."""
import argparse
import json
from pathlib import Path
from verify_play_results import clean_play, entries, require


def verify(report, pixel, game_ui):
    clean_play(report)
    startup = entries(report, "persisted startup")[0]
    require(startup["selected"] == startup["theme"] == report["packId"], "persisted selection")
    require(startup["pixel"] == pixel and startup["gameUi"] == game_ui, "persisted switches")
    require(startup["bodyThemed"] and startup["filter"] == ("Point" if pixel else "Bilinear"), "persisted body sampling")
    if game_ui:
        ui = startup["uiRoundTrip"]
        require(ui["available"] and ui["changed"] > 0 and ui["textPreserved"] and ui["restored"] and not ui["failures"], "persisted themed UI")
    else:
        require(startup["uiOriginal"], "persisted original UI")
    return {"packId": report["packId"], "pixel": pixel, "gameUi": game_ui, "freshProcessLoaded": True}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    parser.add_argument("--pixel", choices=("on", "off"), required=True)
    parser.add_argument("--ui", choices=("on", "off"), required=True)
    args = parser.parse_args()
    report = json.loads(Path(args.report).read_text(encoding="utf-8-sig"))
    print(json.dumps(verify(report, args.pixel == "on", args.ui == "on"), indent=2))
