using System.Collections;
using SephiriaSkins.Core;
using UnityEngine;

namespace SephiriaSkins.Plugin;

// Explicit opt-in switching fixture: keep the live weapon instance while a
// different theme commits, then inspect both that instance and fresh equips.
internal static class CycleProbe
{
    public static IEnumerator Run(Plugin plugin, Func<PackEntry, IEnumerator> apply, Action restore,
        string root, Action<string, object> record)
    {
        var available = PackDiscovery.Scan(Path.Combine(root, "Skins"), plugin.Catalog);
        var entries = plugin.Diagnostics.CyclePackIds.Select(id => available.SingleOrDefault(p => p.Error == null && p.Pack?.Manifest.Id == id)
            ?? throw new InvalidOperationException("Cycle pack unavailable or rejected: " + id)).ToArray();
        var uiKeys = entries.SelectMany(e => e.Pack!.Manifest.Ui.Keys).Distinct().ToArray();
        foreach (var entry in entries) plugin.Diagnostics.RecordManifest(entry);
        var nativeWeapons = DiagnosticWeapons.Playable();
        var originals = new Dictionary<int, object>();
        restore(); plugin.SetGameUi(false); plugin.SetPixelArt(true);
        var player = Ownership.Local!;
        var controller = player.GetComponent<WeaponControllerSimple>();
        foreach (var weapon in nativeWeapons)
        {
            player.CancelCurrentAction(); player.DespawnAllBullet(); controller.EquipWeapon(false, weapon.id);
            yield return new WaitForSecondsRealtime(.6f);
            originals[weapon.id] = VisualProbe.Snapshot();
        }
        yield return Reclaim();
        var originalUi = VisualProbe.UiSnapshot(uiKeys);
        var before = Counts();
        record("cycle inventory", new { packIds = plugin.Diagnostics.CyclePackIds, rounds = 2,
            manifests = entries.Select(e => e.Pack!.Manifest).ToArray(), coverage = DiagnosticWeapons.Coverage(), originals, originalUi });
        for (var round = 0; round < 2; round++)
        foreach (var entry in entries)
        {
            var previousId = plugin.Theme?.Pack.Manifest.Id;
            plugin.SetGameUi(false);
            yield return apply(entry); yield return new WaitForSecondsRealtime(.4f);
            var reusedWeapon = controller.currentWeapon.entityId;
            var reused = VisualProbe.Snapshot();
            var uiOffSnapshot = VisualProbe.UiSnapshot(uiKeys);
            var uiOff = originalUi == uiOffSnapshot;
            var fresh = new List<object>();
            foreach (var weapon in nativeWeapons)
            {
                player.CancelCurrentAction(); player.DespawnAllBullet(); controller.EquipWeapon(false, weapon.id);
                yield return new WaitForSecondsRealtime(.6f);
                if (controller.currentWeapon?.entityId != weapon.id) throw new InvalidOperationException("Cycle native equip failed: " + weapon.id);
                fresh.Add(new { weapon.id, renderers = VisualProbe.Snapshot() });
            }
            var pixelSprite = player.TopdownActor.bodyRenderer.sprite;
            plugin.SetPixelArt(false);
            var smoothSprite = player.TopdownActor.bodyRenderer.sprite;
            var sameBounds = pixelSprite && smoothSprite && Vector3.Distance(pixelSprite.bounds.size, smoothSprite.bounds.size) < .00001f;
            var sampling = pixelSprite && smoothSprite && pixelSprite.texture.filterMode == FilterMode.Point && smoothSprite.texture.filterMode == FilterMode.Bilinear;
            yield return new WaitForSecondsRealtime(.2f);
            plugin.SetGameUi(true);
            var uiRoundTrip = RuntimeProbe.CheckUiRoundTrip(plugin);
            plugin.SetGameUi(false);
            yield return apply(entry); yield return new WaitForSecondsRealtime(.3f);
            var uiOffAfterReloadSnapshot = VisualProbe.UiSnapshot(uiKeys);
            record("cycle switch", new { round, skin = entry.Pack!.Manifest.Id, previousId, applied = plugin.Theme?.Pack.Manifest.Id == entry.Pack.Manifest.Id,
                reusedWeapon, reused, fresh, uiOff, uiOffAfterReload = !plugin.GameUi && originalUi == uiOffAfterReloadSnapshot,
                uiOffSnapshot, uiOffAfterReloadSnapshot,
                reloadRenderers = VisualProbe.Snapshot(), reloadWeapon = controller.currentWeapon.entityId,
                sameBounds, sampling, uiRoundTrip, audioChannels = plugin.Audio.ActiveReplacements });
            plugin.SetPixelArt(true);
        }
        restore(); yield return new WaitForSecondsRealtime(3); yield return Reclaim();
        record("cycle restoration", new { themeNull = plugin.Theme == null, audioChannels = plugin.Audio.ActiveReplacements,
            uiOriginal = originalUi == VisualProbe.UiSnapshot(uiKeys), uiRestoredSnapshot = VisualProbe.UiSnapshot(uiKeys), renderers = VisualProbe.Snapshot(),
            weapon = controller.currentWeapon.entityId, before, after = Counts() });
    }

    private static IEnumerator Reclaim() { yield return null; yield return null; yield return Resources.UnloadUnusedAssets(); GC.Collect(); }
    private static object Counts() => new
    {
        textures = Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t),
        sprites = Resources.FindObjectsOfTypeAll<Sprite>().Count(t => t),
        fonts = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().Count(t => t),
        materials = Resources.FindObjectsOfTypeAll<Material>().Count(t => t),
        bundles = AssetBundle.GetAllLoadedAssetBundles().Count()
    };
}
