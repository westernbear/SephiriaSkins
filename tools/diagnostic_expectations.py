"""Shared manifest-driven assertions for newly recorded diagnostic runs."""


def require(value, label):
    if not value:
        raise ValueError(label)


def expected_trials(coverage):
    weapons = coverage["weapons"]
    require(weapons and len({w["id"] for w in weapons}) == len(weapons), "unique weapon inventory")
    require(all(w.get("skipReason") is None or isinstance(w["skipReason"], str)
                and w["skipReason"].strip() for w in weapons), "weapon skip reasons")
    modes = coverage["modes"]
    require(modes and len(set(modes)) == len(modes), "attack modes")
    for weapon in weapons:
        skipped = weapon.get("skippedModes", {})
        require(isinstance(skipped, dict) and set(skipped) <= set(modes) and
                all(isinstance(reason, str) and reason.strip() for reason in skipped.values()), "native trial skip reasons")
    expected = {(w["id"], mode) for w in weapons if w.get("skipReason") is None and w.get("selected", True)
                for mode in modes if mode not in w.get("skippedModes", {})}
    require(expected, "playable weapons")
    return expected


def manifest_sprites(manifest, section):
    return {manifest["id"] + ":" + frame for binding in manifest.get(section, {}).values()
            for frame in binding["frames"]}


def verify_body_renderers(manifest, originals, themed):
    originals = {renderer["path"]: renderer for renderer in originals}
    sprites = manifest_sprites(manifest, "body")
    body = [renderer for renderer in themed if renderer.get("sprite") in sprites]
    require(body, "themed body renderers")
    for renderer in body:
        source = originals.get(renderer["path"])
        require(source, renderer["path"] + " native body renderer")
        require(renderer["fullTexture"], renderer["path"] + " isolated body texture")
        for field in ("shader", "scale", "sorting", "layer"):
            require(renderer[field] == source[field], renderer["path"] + " native body " + field)
    reflections = [renderer for renderer in body if renderer["path"].endswith("/WaterReflection")]
    masks = [renderer for renderer in body if renderer["path"].endswith("/StencilSolid")]
    if reflections or masks:
        require(len({renderer["sprite"] for renderer in body}) == 1, "body/mask/reflection frame alignment")
    return {"bodyRenderers": len(body), "nativeReflections": len(reflections), "bodyMasks": len(masks)}


def verify_weapon_renderers(manifest, originals, themed):
    """Validate declared static bindings, masks, and native weapon retention."""
    originals = {r["path"]: r for r in originals}
    current = {r["path"]: r for r in themed}
    declared = manifest.get("visuals", {})
    checked = 0
    for renderer in themed:
        if not renderer["active"]:
            continue
        binding = declared.get(renderer.get("visualKey"))
        source = originals.get(renderer["path"])
        label = renderer["path"]
        if binding:
            if binding.get("hide"):
                require(renderer["sprite"] is None, label + " hidden decoration")
            elif binding.get("sprite") and source and source["sprite"]:
                require(renderer["sprite"] == manifest["id"] + ":" + binding["sprite"], label + " declared sprite")
            if source and not binding.get("material"):
                require(renderer["shader"] == source["shader"], label + " native material")
            checked += 1
        mirror = renderer.get("mirrorSource")
        if mirror and mirror in current:
            require(renderer["sprite"] == current[mirror]["sprite"], label + " aligned mask")
        if not manifest.get("weapons") and renderer.get("visualKey", "") and renderer["visualKey"].startswith("weapon/") and not binding and source:
            require(renderer["sprite"] == source["sprite"], label + " native weapon retained")
    return checked
