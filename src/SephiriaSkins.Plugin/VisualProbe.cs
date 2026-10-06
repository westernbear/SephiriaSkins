using System.Collections;
using HarmonyLib;
using SephiriaSkins.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace SephiriaSkins.Plugin;

// Opt-in composition evidence: inspect actual native renderers, not just frame counts.
internal static class VisualProbe
{
    public static IEnumerator Inspect(Plugin plugin, Func<PackEntry, IEnumerator> apply, Action restore, Action<bool> selector, string root)
    {
        var output = plugin.Diagnostics.File("visual"); Directory.CreateDirectory(output);
        var entry = plugin.Diagnostics.Select(PackDiscovery.Scan(Path.Combine(root, "Skins"), plugin.Catalog));
        var results = new List<object>();
        results.Add(new { packId = plugin.Diagnostics.PackId, runId = plugin.Diagnostics.RunId, manifest = entry.Pack!.Manifest, coverage = DiagnosticWeapons.Coverage() });
        plugin.SetPixelArt(true); plugin.SetGameUi(true);
        selector(true); yield return null; yield return null;
        RuntimeProbe.Capture(Path.Combine(output, "selector-pixel-on-ui-on.png"), Debug.Log);
        selector(false);
        var dummy = CombatManager.Instance.AllCreatures.FirstOrDefault(u => u && u.faction == "Dummy");
        if (dummy) Ownership.Local!.ReqSetPosition(dummy!.transform.position - Vector3.right * 1.5f, true);
        yield return new WaitForSecondsRealtime(.5f);
        foreach (var weapon in DiagnosticWeapons.Playable())
        {
            var priorMaxMp = Ownership.Local!.maxMp;
            Ownership.Local.NetworkmaxMp = Math.Max(priorMaxMp, 1000);
            // A previous dash can leave the avatar on top of the native dummy,
            // obscuring the idle body/weapon evidence with its wooden target.
            Ownership.Local.CancelCurrentAction(); Ownership.Local.DespawnAllBullet();
            if (dummy) Ownership.Local.ReqSetPosition(dummy!.transform.position - Vector3.right * 2, true);
            plugin.SetPixelArt(true);
            var visualTheme = plugin.SuspendDiagnosticTheme();
            Ownership.Local!.GetComponent<WeaponControllerSimple>().EquipWeapon(false, weapon.id);
            yield return new WaitForSecondsRealtime(.8f);
            if (Ownership.Local!.GetComponent<WeaponControllerSimple>().currentWeapon?.entityId != weapon.id)
                throw new InvalidOperationException("Native visual equip failed for weapon " + weapon.id);
            results.Add(new { weapon.id, original = Snapshot() });
            plugin.ResumeDiagnosticTheme(visualTheme); yield return new WaitForSecondsRealtime(.8f);
            results.Add(new { weapon.id, themed = Snapshot() });
            RuntimeProbe.Capture(Path.Combine(output, "weapon-" + weapon.id + ".png"), Debug.Log);
            foreach (var direction in new[] { Vector2.up, Vector2.down })
            {
                InputSystem.QueueDeltaStateEvent(Gamepad.current.rightStick, direction);
                Ownership.Local!.ForceAimToPosition((Vector2)Ownership.Local.transform.position + direction * 3);
                yield return new WaitForSecondsRealtime(.35f);
                var facing = direction == Vector2.up ? "back" : "front";
                results.Add(new { weapon.id, facing, renderers = Snapshot() });
                RuntimeProbe.Capture(Path.Combine(output, $"weapon-{weapon.id}-{facing}.png"), Debug.Log);
            }
            InputSystem.QueueDeltaStateEvent(Gamepad.current.rightStick, Vector2.right);
            foreach (var pixel in new[] { true, false })
            {
            plugin.SetPixelArt(pixel);
            foreach (var mode in new[] { "basic", "dash", "special" })
            {
                if (!DiagnosticWeapons.ShouldRun(weapon, mode)) continue;
                var player = Ownership.Local!; player.CancelCurrentAction(); player.Networkmp = player.MaxMp; player.CurrentDashModule.RestoreDashCount(99);
                var priorMoney = player.Money;
                if (player.GetComponent<WeaponControllerSimple>().currentWeapon is WeaponSimple_GreatSword { moneyWhirlwind: true }) player.NetworkcurrentMoney = Math.Max(priorMoney, 10000);
                if (dummy) player.ReqSetPosition(dummy!.transform.position - Vector3.right * 1.5f, true);
                yield return new WaitForSecondsRealtime(.25f);
                var controller = player.GetComponent<WeaponControllerSimple>();
                var events = new List<string>(); var mpCosts = new List<int>(); var nativeFires = 0; var specialFires = 0;
                void Basic(int id) => events.Add("basic:" + id);
                void Dash() => events.Add("dash");
                void Special(int id) => events.Add("special:" + id);
                void SpecialSwing(int id) { if (controller.currentWeapon.weaponType == EWeaponType.Katana) events.Add("special-swing:" + id); }
                void NativeFire(ProjectileBase projectile)
                {
                    nativeFires++;
                    var damageId = (projectile as MeleeCollision)?.damageId ?? (projectile as Bullet)?.damageId;
                    if (damageId == "Weapon_SpecialAttack") specialFires++;
                    if (controller.currentWeapon.weaponType == EWeaponType.Golem) events.Add("golem-fire:" + RuntimeCatalog.Clean(projectile.name));
                }
                void Mp(int amount) => mpCosts.Add(amount);
                controller.OnBeginAttackAnimation += Basic; controller.OnBeginDashAttackAnimation += Dash;
                controller.OnBeginSpecialAttackAnimation += Special; controller.OnSpecialAttackSwing += SpecialSwing;
                player.OnMpUsedServerside += Mp;
                PlayProbe.NativeFireObserved += NativeFire;
                if (mode == "dash") { player.CurrentDashModule.StartDash((Vector2)player.transform.position + Vector2.right * 3); yield return null; }
                var modernKatana = controller.currentWeapon is WeaponSimple_Katana_New;
                if (mode == "special" && modernKatana)
                {
                    player.AttackButtonDown(Vector2.right); yield return new WaitForSecondsRealtime(.12f); player.AttackButtonUp();
                }
                if (mode == "special") player.SubAttackButtonDown(Vector2.right); else player.AttackButtonDown(Vector2.right);
                var guarded = player.isGuardEnabled;
                object? secondary = null;
                if (mode == "special")
                {
                    if (controller.currentWeapon is WeaponSimple_GreatSword chargingSword)
                    {
                        var chargeDeadline = Time.realtimeSinceStartup + 10;
                        while (!(bool)AccessTools.Field(typeof(WeaponSimple_GreatSword), "sweepRequest").GetValue(chargingSword) &&
                               !chargingSword.isTransformed && Time.realtimeSinceStartup < chargeDeadline) yield return null;
                    }
                    else yield return new WaitForSecondsRealtime(1.5f);
                    guarded = player.isGuardEnabled;
                    secondary = PlayProbe.SecondaryState(controller);
                    var type = weapon.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType;
                    if (type == EWeaponType.SwordAndShield || type == EWeaponType.Katana && !modernKatana) player.AttackButtonDown(Vector2.right);
                    if (controller.currentWeapon is WeaponSimple_GreatSword sword)
                    {
                        player.SubAttackButtonUp();
                        if (sword.specialAttackToTransform && sword.isTransformed)
                        { yield return new WaitForSecondsRealtime(.2f); secondary = PlayProbe.SecondaryState(controller); player.AttackButtonDown(Vector2.right); }
                    }
                }
                // Synchronize capture to an actual projectile, not the start
                // of a charging pose. The native guard-only case is retained.
                var fireDeadline = Time.realtimeSinceStartup + 2;
                while ((mode == "special" && modernKatana ? specialFires == 0 : nativeFires == 0) && Time.realtimeSinceStartup < fireDeadline) yield return null;
                if (mode != "special") player.AttackButtonUp();
                foreach (var frame in new[] { 0, 1, 2 })
                {
                    yield return new WaitForSecondsRealtime(frame == 0 ? .015f : frame == 1 ? .065f : .15f);
                    results.Add(new { weapon.id, mode, frame, pixel, nativeFires, specialFires, renderers = Snapshot() });
                    RuntimeProbe.Capture(Path.Combine(output, $"attack-{weapon.id}-{mode}-{frame}-{(pixel ? "pixel" : "smooth")}.png"), Debug.Log);
                }
                player.AttackButtonUp(); player.SubAttackButtonUp(); yield return new WaitForSecondsRealtime(1.5f);
                controller.OnBeginAttackAnimation -= Basic; controller.OnBeginDashAttackAnimation -= Dash;
                controller.OnBeginSpecialAttackAnimation -= Special; controller.OnSpecialAttackSwing -= SpecialSwing;
                player.OnMpUsedServerside -= Mp;
                PlayProbe.NativeFireObserved -= NativeFire;
                results.Add(new { visualAction = true, weapon.id, mode, pixel, events = events.ToArray(), guarded, secondary, nativeFires, specialFires, modernKatana, mpCosts = mpCosts.ToArray(), preparedMaxMp = player.MaxMp });
                player.NetworkcurrentMoney = priorMoney;
            }
            }
            Ownership.Local.NetworkmaxMp = priorMaxMp;
            Ownership.Local.Networkmp = Math.Min(Ownership.Local.mp, Ownership.Local.MaxMp);
        }
        yield return WaterProbe.Inspect(plugin, output, value => results.Add(value));
        plugin.SetPixelArt(true);
        var local = Ownership.Local!; var oldGameOver = local.dieIsGameOver; local.dieIsGameOver = NestedBoolean.False;
        local.Die(0, null); yield return new WaitForSecondsRealtime(.6f);
        local.Revive(local.MaxHp);
        for (var frame = 0; frame < 3; frame++)
        {
            yield return new WaitForSecondsRealtime(.1f);
            results.Add(new { reviveFrame = frame, renderers = Snapshot() });
            RuntimeProbe.Capture(Path.Combine(output, "revive-" + frame + ".png"), Debug.Log);
        }
        local.dieIsGameOver = oldGameOver;
        yield return new WaitForSecondsRealtime(2);
        plugin.SetGameUi(false); yield return new WaitForSecondsRealtime(.3f);
        var originalUi = UiSnapshot();
        yield return apply(entry); yield return new WaitForSecondsRealtime(.3f);
        results.Add(new { uiOffSurvivesReload = originalUi == UiSnapshot(), themeRemains = plugin.Theme != null });
        selector(true); yield return null; yield return null;
        var window = Object.FindObjectsByType<SkinSelectorMarker>(FindObjectsSortMode.None).Single(m => m.GetComponent<UI_MessageBox_YesNo>()?.IsOpened == true);
        var pixelButton = window.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TMPro.TMP_Text>()?.text.StartsWith("도트 감성:") == true);
        var uiButton = window.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<TMPro.TMP_Text>()?.text.StartsWith("게임 UI:") == true);
        var pixelSprite = Ownership.Local!.TopdownActor.bodyRenderer.sprite;
        var spritePrefix = plugin.Theme!.Pack.Manifest.Id + ":";
        if (!pixelSprite || !pixelSprite.name.StartsWith(spritePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Pixel toggle requires an actual themed body frame");
        var presentationFrame = pixelSprite.name.Substring(spritePrefix.Length);
        RuntimeProbe.Capture(Path.Combine(output, "selector-pixel-on-ui-off.png"), Debug.Log);
        pixelButton.onClick.Invoke();
        // The native idle/blink timeline continues during UI waits. Compare
        // both presentations of the same authored frame, rather than the
        // differently sized pose rendered after the next .3 seconds.
        var matchingSmoothSprite = plugin.Theme.VisualSprite(presentationFrame);
        yield return new WaitForSecondsRealtime(.3f);
        var smoothSprite = Ownership.Local.TopdownActor.bodyRenderer.sprite;
        RuntimeProbe.Capture(Path.Combine(output, "selector-pixel-off-ui-off.png"), Debug.Log);
        uiButton.onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
        var appliedUi = UiSnapshot();
        RuntimeProbe.Capture(Path.Combine(output, "selector-pixel-off-ui-on.png"), Debug.Log);
        uiButton.onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
        results.Add(new { toggles = new { pixelButtonWorked = !plugin.PixelArt, uiButtonWorked = !plugin.GameUi,
            smallerPixelTexture = pixelSprite.texture.height < smoothSprite.texture.height, point = pixelSprite.texture.filterMode == FilterMode.Point,
            smooth = smoothSprite.texture.filterMode == FilterMode.Bilinear, sameBounds = Vector3.Distance(pixelSprite.bounds.size, matchingSmoothSprite.bounds.size) < .00001f,
            persistedPixel = System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(plugin.Config.ConfigFilePath), @"PixelArt\s*=\s*false", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            persistedUi = System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(plugin.Config.ConfigFilePath), @"GameUi\s*=\s*false", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            uiChanged = originalUi != appliedUi, uiRestored = originalUi == UiSnapshot(), nativeButtons = pixelButton.image.sprite && uiButton.image.sprite,
            labels = new[] { pixelButton.GetComponentInChildren<TMPro.TMP_Text>().text, uiButton.GetComponentInChildren<TMPro.TMP_Text>().text } } });
        results.Add(new { presentationComparison = new { resource = presentationFrame, pixelFrame = pixelSprite.name,
            smoothFrame = matchingSmoothSprite.name, sampledSmoothFrame = smoothSprite.name,
            pixelBounds = new[] { pixelSprite.bounds.size.x, pixelSprite.bounds.size.y },
            smoothBounds = new[] { matchingSmoothSprite.bounds.size.x, matchingSmoothSprite.bounds.size.y },
            pixelPivot = new[] { pixelSprite.pivot.x / pixelSprite.rect.width, pixelSprite.pivot.y / pixelSprite.rect.height },
            smoothPivot = new[] { matchingSmoothSprite.pivot.x / matchingSmoothSprite.rect.width, matchingSmoothSprite.pivot.y / matchingSmoothSprite.rect.height } } });
        selector(false); plugin.SetPixelArt(true); plugin.SetGameUi(true);
        File.WriteAllText(Path.Combine(output, "renderers.json"), Json.Write(results));
    }
    internal static string UiSnapshot(IEnumerable<string>? keys = null)
    {
        var identities = (keys ?? Plugin.Instance!.Theme!.Pack.Manifest.Ui.Keys).ToHashSet(StringComparer.Ordinal);
        return Json.Write(Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(g => g && !g.GetComponentInParent<SkinSelectorMarker>() && identities.Contains(RuntimeCatalog.UiKey(g))).OrderBy(g => g.GetInstanceID())
        .Select(g => new { id = g.GetInstanceID(), sprite = (g as Image)?.sprite?.GetInstanceID(), texture = (g as RawImage)?.texture?.GetInstanceID(),
            font = (g as TMPro.TMP_Text)?.font?.GetInstanceID(), fontMaterial = UiFontMaterialId(g as TMPro.TMP_Text),
            material = g is TMPro.TMP_Text ? 0 : g.material?.GetInstanceID(), imageType = (g as Image)?.type,
            color = new[] { g.color.r, g.color.g, g.color.b, g.color.a }, position = g.rectTransform.anchoredPosition.ToString(),
            size = g.rectTransform.sizeDelta.ToString(), fontSize = (g as TMPro.TMP_Text)?.fontSize, text = (g as TMPro.TMP_Text)?.text }).ToArray());
    }
    private static int? UiFontMaterialId(TMPro.TMP_Text? text)
    {
        if (!text) return null;
        // An inactive native TMP label may not have initialized its shared
        // material yet. Assigning/restoring a font initializes that font's
        // native default; compare that equivalent state while retaining IDs
        // for every explicit custom or replacement material.
        var material = text!.fontSharedMaterial;
        if (!material && text.font) material = text.font.material;
        return material ? material.GetInstanceID() : null;
    }
    internal static object Snapshot() => Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(r => r && (Ownership.IsLocal(Ownership.Owner(r)) || (Ownership.Local!.GetComponentInChildren<PlayerAvatarCostume>()?.waterReflectionObject && r.transform.IsChildOf(Ownership.Local.GetComponentInChildren<PlayerAvatarCostume>().waterReflectionObject))))
        .Select(r => new { path = RuntimeCatalog.PathOf(r.transform), visualKey = VisualKey(r), mirrorSource = r.GetComponent<CosmeticOwner>()?.MirrorSource ? RuntimeCatalog.PathOf(r.GetComponent<CosmeticOwner>().MirrorSource!.transform) : null,
            r.enabled, active = r.gameObject.activeInHierarchy, shader = r.sharedMaterial?.shader?.name,
            sprite = r.sprite?.name, rect = r.sprite ? r.sprite.rect.ToString() : "", ppu = r.sprite?.pixelsPerUnit, pivot = r.sprite?.pivot.ToString(),
            textureSize = r.sprite ? new[] { r.sprite.texture.width, r.sprite.texture.height } : null,
            fullTexture = r.sprite && r.sprite.rect.x == 0 && r.sprite.rect.y == 0,
            position = r.transform.position.ToString(), scale = r.transform.lossyScale.ToString(), sorting = r.sortingOrder, layer = r.sortingLayerName }).ToArray();
    private static string? VisualKey(SpriteRenderer renderer)
    {
        var tag = renderer.GetComponentInParent<CosmeticOwner>();
        if (tag && tag.VisualKeys.TryGetValue(renderer.GetInstanceID(), out var key)) return key;
        var weapon = renderer.GetComponentInParent<WeaponSimple>();
        var modern = renderer.GetComponentInParent<NewWeapon>();
        var fx = renderer.GetComponentInParent<SpriteFx>();
        var bullet = renderer.GetComponentInParent<Bullet>();
        var source = weapon ? weapon.transform : modern ? modern.transform : fx ? fx.transform : bullet ? bullet.transform : null;
        if (!source) return null;
        var relative = RuntimeCatalog.PathOf(renderer.transform);
        var parentPath = source!.parent ? RuntimeCatalog.PathOf(source.parent) + "/" : "";
        if (relative.StartsWith(parentPath, StringComparison.Ordinal)) relative = relative.Substring(parentPath.Length);
        return (weapon || modern ? "weapon" : "effect") + "/" + relative + "/SpriteRenderer";
    }
}
