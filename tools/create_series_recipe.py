"""Connect individually authored Chiikawa-series atlases to reviewed catalog bindings.

Only binding identities are reused from Hachiware. No images, music, fonts or
bundles are copied. The generic compiler retains native frame arrays and timing.
"""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CHARACTERS = {
    "momonga": ("모몽가", [0.70, 0.86, 1, 1], False),
    "chiikawa": ("치이카와", [1, 0.75, 0.83, 1], True),
    "usagi": ("우사기", [1, 0.88, 0.48, 1], True),
    "kurimanju": ("쿠리만쥬", [0.89, 0.76, 0.57, 1], False),
    "rakko": ("랏코", [0.88, 0.89, 0.94, 1], True),
    "shisa": ("시사", [1, 0.77, 0.41, 1], False),
    "furuhonya": ("카니(고서점)", [1, 0.76, 0.86, 1], False),
}


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def poses(row, columns, phase="cycle"):
    return {"poses": [f"body.{row}.{col}" for col in columns], "phase": phase}


def effect_row(binding, animation):
    # This translates the reviewed prior semantic assignments, then splits the
    # former combined impact/guard/revive art into dedicated four-phase rows.
    source = binding["frames"][0]
    if source == "fx.3.3":
        return 5
    if source == "fx.0.3":
        return 3
    row = int(source.split(".")[1])
    return 4 if row == 3 else row


def create(character, source):
    source = Path(source).resolve()
    output = source / "recipe.json"
    if output.exists():
        raise ValueError("Recipe output must be new")
    name, color, has_weapon = CHARACTERS[character]
    native = read(ROOT / "catalog/catalog-1.0.33.json")
    template = read(ROOT / "packs/hachiware/skin.json")
    resources = {}
    for atlas in ("body", "effects", "ui"):
        resources.update(read(source / f"{atlas}-inspection.json")["resources"])
    for key, resource in resources.items():
        if key.startswith("ui.") and key != "ui.1.0":
            w, h = resource["rect"][2:]
            resource["border"] = [min(14, w//4), min(14, h//4)] * 2
    audio = read(source / "audio/audio-provenance.json")
    for entry in audio["files"]:
        resources[Path(entry["file"]).stem] = {"file": "audio/" + entry["file"], "kind": "audio"}
    body = {
        "IDLE": poses(0, [0, 0, 1, 0, 0, 0]), "IDLE_BACK": poses(1, [0, 0, 1, 0, 0, 0]),
        "MOVE": poses(0, [2, 0, 3, 0]), "MOVE_BACK": poses(1, [2, 0, 3, 0]),
        "AIRBORNE": poses(0, [6]), "HITFEEDBACK": poses(0, [6]), "FALL": poses(0, [7]),
        "ATTACK": poses(2, list(range(8)), "once"), "ATTACK_BACK": poses(3, list(range(8)), "once"),
        "GREATSWORDSWING_LOWER": poses(2, [0, 1, 2, 3, 4, 5, 6, 7], "once"),
        "GREATSWORDSWING_SWEEP": poses(2, [0, 1, 3, 4, 5, 6, 7], "once"),
        "GREATSWORDSWING_UPPER": poses(2, [0, 2, 3, 4, 5, 6, 7], "once"),
        "WEAPONSKILL_WHIRLWINDREADY": poses(0, [5]),
        "WEAPONSKILL_WHIRLWIND": poses(2, [2, 3, 4, 5]),
    }
    effects = {key: {"poses": [f"fx.{effect_row(binding, native['animations'][key])}.{n}" for n in range(4)], "phase": "once"}
               for key, binding in template["effects"].items()}
    ui = {}
    for key, binding in template["ui"].items():
        if "sprite" in binding:
            sprite = binding["sprite"]
            index = 4 if sprite.startswith("body.") else int(sprite.split(".")[1])
            ui[key] = {**binding, "sprite": f"ui.{index//4}.{index%4}"}
        elif "color" in binding:
            ui[key] = {"color": color}
    # These exact catalog surfaces were reviewed separately. Inventory backs
    # contain the native slot grid, so tint them instead of erasing that grid.
    for key in (
        "ui/[UI] Panels/CharacterPanel/InentoryParent/Scroll View/Viewport/Content/InventoryZone/Image",
        "ui/[UI] Panels/CharacterPanel/InentoryParent/SkillAndCombo/Skill/Image",
        "ui/[UI] Panels/InventoryPanel/Image", "ui/[UI] Panels/InventoryPanel/Inner/Image",
        "ui/[UI] HUD/HPBar/Image", "ui/[UI] HUD/MPBar/Image", "ui/[UI] HUD/DashBar/Image",
        "ui/[UI] HUD/CurrentQuickSlot/Background/Image",
        "ui/[UI] Panels/ConversationPanel/PortraitZone/AvatarName/TextMeshProUGUI",
    ):
        if key not in native["ui"]:
            raise ValueError(f"Reviewed UI identity missing from this catalog: {key}")
        ui[key] = {"color": color}
    for key, sprite in (
        ("ui/[UI] Panels/OptionPanel/Base/Image", "ui.0.1"),
        ("ui/[UI] Panels/CommonTooltip/Image", "ui.1.2"),
        ("ui/[UI] Panels/FakeTitleLobby/Image/Image", "ui.0.0"),
    ):
        if key not in native["ui"]:
            raise ValueError(f"Reviewed UI identity missing from this catalog: {key}")
        ui[key] = {"sprite": sprite, "imageType": "sliced", "color": [1, 1, 1, 1]}
    bindings = {key: dict(binding) for key, binding in template["audio"].items()}
    # Dedicated new vocal cues at actual native weapon events. Other local
    # events still require runtime ownership; selector clicks remain native.
    for key, entry in native["audio"].items():
        if entry["path"] in ("event:/Scene/attackDagger", "event:/Scene/attackBow", "event:/Scene/attackBash"):
            bindings[key] = {"resource": "attack", "scope": "local", "channel": "sfx", "loop": False, "volume": 0.65}
        if entry["path"] in ("event:/Scene/WeaponCharge", "event:/Scene/crossbow_MinigunLoop",
                             "event:/Scene/SpellTornado_Loop", "event:/Scene/SpellMagicalField_Loop"):
            bindings[key] = {"resource": "charge_loop", "scope": "local", "channel": "sfx", "loop": True, "volume": 0.55}
    recipe = {
        "manifest": {"id": "fan." + character, "version": "1.0.0", "name": name + " 팬아트 테마", "author": "SephiriaSkins",
                     "description": "새로 제작한 본체·초상화·효과·UI·절차적 오디오 테마. " + ("캐릭터 무기로 표시합니다." if has_weapon else "게임 원본 무기를 유지합니다."), "preview": "ui.1.0"},
        "resources": resources, "bodyStates": body, "effectAnimations": effects,
        "ui": ui, "audio": bindings,
        "records": ["PROVENANCE.md", "prompts.txt", "body-inspection.json", "effects-inspection.json", "ui-inspection.json", "audio/audio-provenance.json"],
    }
    # Preserve the actual generation/edit history and any explicitly authorized
    # preparation reports alongside the final resources, when supplied.
    for record in ("generation-records.txt", "generated-drafts.json", "audio-inspection.json"):
        if (source / record).is_file():
            recipe["records"].append(record)
    for atlas in ("body", "effects", "ui", "weapon"):
        inspection = source / f"{atlas}-inspection.json"
        if inspection.is_file():
            report = Path(read(inspection)["source"]).with_suffix(".cleanup.json").as_posix()
            if (source / report).is_file():
                recipe["records"].append(report)
    if has_weapon:
        weapon = read(source / "weapon-inspection.json")["resources"]
        if len(weapon) != 1:
            raise ValueError("Character weapon atlas must contain one complete weapon")
        resources.update(weapon)
        weapon_id = next(iter(weapon))
        recipe["weaponAnimations"] = {}
        for key, binding in template["weapons"].items():
            if binding["frames"][0].startswith("fx."):
                row = effect_row(binding, native["animations"][key])
                rule = {"poses": [f"fx.{row}.{n}" for n in range(4)], "phase": "once"}
            else:
                rule = {"poses": [weapon_id]}
            recipe["weaponAnimations"][key] = rule
        recipe["visuals"] = {key: dict(binding) if binding.get("hide") else {**binding, "sprite": weapon_id}
                             for key, binding in template["visuals"].items()}
        recipe["records"].append("weapon-inspection.json")
    output.write_text(json.dumps(recipe, ensure_ascii=False, indent=2), encoding="utf-8")
    return {"id": recipe["manifest"]["id"], "recipe": str(output), "bodyStates": len(body), "effectAnimations": len(effects), "ui": len(ui), "audio": len(bindings)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("character", choices=CHARACTERS)
    parser.add_argument("assets")
    args = parser.parse_args()
    print(json.dumps(create(args.character, args.assets), ensure_ascii=False, indent=2))
