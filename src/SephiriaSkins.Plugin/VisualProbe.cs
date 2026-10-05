using System.Collections;
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
        var output = Path.Combine(root, "Export", "visual"); Directory.CreateDirectory(output);
        var entry = PackDiscovery.Scan(Path.Combine(root, "Skins"), plugin.Catalog).Single(p => p.Pack?.Manifest.Id == "fan.hachiware" && p.Error == null);
        var results = new List<object>();
        plugin.SetPixelArt(true); plugin.SetGameUi(true);
        var dummy = CombatManager.Instance.AllCreatures.FirstOrDefault(u => u && u.faction == "Dummy");
        if (dummy) Ownership.Local!.ReqSetPosition(dummy!.transform.position - Vector3.right * 1.5f, true);
        yield return new WaitForSecondsRealtime(.5f);
        foreach (var weapon in WeaponDatabase.GetDefaultWeapons().Where(w => w && w.mainWeaponPrefab && w.mainWeaponPrefab.GetComponent<WeaponSimple>())
            .GroupBy(w => w.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType).Select(g => g.OrderBy(w => w.id).First()))
        {
            plugin.SetPixelArt(true);
            restore(); Ownership.Local!.GetComponent<WeaponControllerSimple>().EquipWeapon(false, weapon.id);
            yield return new WaitForSecondsRealtime(.8f);
            results.Add(new { weapon.id, original = Snapshot() });
            yield return apply(entry); yield return new WaitForSecondsRealtime(.8f);
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
                var player = Ownership.Local!; player.CancelCurrentAction(); player.Networkmp = player.MaxMp; player.CurrentDashModule.RestoreDashCount(99);
                if (dummy) player.ReqSetPosition(dummy!.transform.position - Vector3.right * 1.5f, true);
                yield return new WaitForSecondsRealtime(.25f);
                if (mode == "dash") { player.CurrentDashModule.StartDash((Vector2)player.transform.position + Vector2.right * 3); yield return null; }
                if (mode == "special") player.SubAttackButtonDown(Vector2.right); else player.AttackButtonDown(Vector2.right);
                if (mode == "special")
                {
                    yield return new WaitForSecondsRealtime(1.5f);
                    var type = weapon.mainWeaponPrefab.GetComponent<WeaponSimple>().weaponType;
                    if (type == EWeaponType.SwordAndShield || type == EWeaponType.Katana) player.AttackButtonDown(Vector2.right);
                }
                foreach (var frame in new[] { 0, 1, 2 })
                {
                    yield return new WaitForSecondsRealtime(.065f);
                    results.Add(new { weapon.id, mode, frame, pixel, renderers = Snapshot() });
                    RuntimeProbe.Capture(Path.Combine(output, $"attack-{weapon.id}-{mode}-{frame}-{(pixel ? "pixel" : "smooth")}.png"), Debug.Log);
                }
                player.AttackButtonUp(); player.SubAttackButtonUp(); yield return new WaitForSecondsRealtime(1.5f);
            }
            }
        }
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
        RuntimeProbe.Capture(Path.Combine(output, "selector-pixel-on-ui-off.png"), Debug.Log);
        pixelButton.onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
        var smoothSprite = Ownership.Local.TopdownActor.bodyRenderer.sprite;
        RuntimeProbe.Capture(Path.Combine(output, "selector-pixel-off-ui-off.png"), Debug.Log);
        uiButton.onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
        var appliedUi = UiSnapshot();
        RuntimeProbe.Capture(Path.Combine(output, "selector-pixel-off-ui-on.png"), Debug.Log);
        uiButton.onClick.Invoke(); yield return new WaitForSecondsRealtime(.3f);
        results.Add(new { toggles = new { pixelButtonWorked = !plugin.PixelArt, uiButtonWorked = !plugin.GameUi,
            smallerPixelTexture = pixelSprite.texture.height < smoothSprite.texture.height, point = pixelSprite.texture.filterMode == FilterMode.Point,
            smooth = smoothSprite.texture.filterMode == FilterMode.Bilinear, sameBounds = Vector3.Distance(pixelSprite.bounds.size, smoothSprite.bounds.size) < .00001f,
            persistedPixel = System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(plugin.Config.ConfigFilePath), @"PixelArt\s*=\s*false", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            persistedUi = System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(plugin.Config.ConfigFilePath), @"GameUi\s*=\s*false", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            uiChanged = originalUi != appliedUi, uiRestored = originalUi == UiSnapshot(), nativeButtons = pixelButton.image.sprite && uiButton.image.sprite,
            labels = new[] { pixelButton.GetComponentInChildren<TMPro.TMP_Text>().text, uiButton.GetComponentInChildren<TMPro.TMP_Text>().text } } });
        selector(false); plugin.SetPixelArt(true); plugin.SetGameUi(true);
        File.WriteAllText(Path.Combine(output, "renderers.json"), Json.Write(results));
    }
    private static string UiSnapshot() => Json.Write(Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(g => g && !g.GetComponentInParent<SkinSelectorMarker>() && Plugin.Instance!.Theme!.Pack.Manifest.Ui.ContainsKey(RuntimeCatalog.UiKey(g))).OrderBy(g => g.GetInstanceID())
        .Select(g => new { id = g.GetInstanceID(), sprite = (g as Image)?.sprite?.GetInstanceID(), font = (g as TMPro.TMP_Text)?.font?.GetInstanceID() }).ToArray());
    private static object Snapshot() => Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(r => r && (Ownership.IsLocal(Ownership.Owner(r)) || (Ownership.Local!.GetComponentInChildren<PlayerAvatarCostume>()?.waterReflectionObject && r.transform.IsChildOf(Ownership.Local.GetComponentInChildren<PlayerAvatarCostume>().waterReflectionObject))))
        .Select(r => new { path = RuntimeCatalog.PathOf(r.transform), r.enabled, active = r.gameObject.activeInHierarchy, shader = r.sharedMaterial?.shader?.name,
            sprite = r.sprite?.name, rect = r.sprite ? r.sprite.rect.ToString() : "", ppu = r.sprite?.pixelsPerUnit, pivot = r.sprite?.pivot.ToString(),
            textureSize = r.sprite ? new[] { r.sprite.texture.width, r.sprite.texture.height } : null,
            fullTexture = r.sprite && r.sprite.rect.x == 0 && r.sprite.rect.y == 0,
            position = r.transform.position.ToString(), scale = r.transform.lossyScale.ToString(), sorting = r.sortingOrder, layer = r.sortingLayerName }).ToArray();
}
