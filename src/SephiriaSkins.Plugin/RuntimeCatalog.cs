using SephiriaSkins.Core;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace SephiriaSkins.Plugin;
internal static class RuntimeCatalog
{
    public static string Clean(string name) => name.Replace("(Clone)", "").Trim();
    public static string PathOf(Transform target)
    {
        var parts = new List<string>();
        for (var t = target; t; t = t.parent)
        {
            var name = Clean(t.name);
            if (t.parent)
            {
                var index = 0;
                for (var i = 0; i < t.GetSiblingIndex(); i++) if (Clean(t.parent.GetChild(i).name) == name) index++;
                if (index > 0) name += "[" + index + "]";
            }
            parts.Add(name);
        }
        parts.Reverse(); return string.Join("/", parts);
    }
    public static string UiKey(Component c) => "ui/" + PathOf(c.transform) + "/" + c.GetType().Name;
    public static string AnimationKey(string role, AnimationSet set, AnimationSet.StateInfo state) => Keys.Animation(role, set.name, state.state, state.fps, state.repeat, state.timeline.Select(f => f.frameIdx), state.timeline.Select(f => f.sprite ? f.sprite.name : ""));
    public static void Observe(AssetCatalog catalog, Animator2D_Basic animator, string role)
    {
        if (!animator.currentSet) return;
        foreach (var state in animator.currentSet.sprites)
        {
            var key = AnimationKey(role, animator.currentSet, state);
            catalog.Animations[key] = new CatalogAnimation
            {
                Role = role,
                SetName = animator.currentSet.name,
                State = state.state,
                Fps = state.fps,
                Repeat = state.repeat,
                ReferencePath = PathOf(animator.transform),
                FrameIndices = state.timeline.Select(f => f.frameIdx).ToArray(),
                SpriteNames = state.timeline.Select(f => f.sprite ? f.sprite.name : "").ToArray(),
                Events = state.frameEvents.SelectMany(f => f.events.Select(e => f.frame + ":" + e.componentName + "." + e.methodName)).ToArray()
            };
        }
    }
    public static void ObserveUi(AssetCatalog catalog)
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<Graphic>())
        {
            if (!component || !component.gameObject.scene.IsValid() || component.GetComponentInParent<SkinSelectorMarker>()) continue;
            var key = UiKey(component);
            catalog.Ui[key] = new CatalogUi { Role = component.GetComponentInParent<Canvas>()?.name ?? "UI", ReferencePath = PathOf(component.transform), Component = component.GetType().Name };
        }
    }
    public static void Export(AssetCatalog catalog, string directory)
    {
        ObserveUi(catalog); ObserveAudio(catalog); Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory, "catalog.json"), Json.Write(catalog));
        var template = new SkinManifest { Id = "my.skin", Version = "1.0.0", Name = "My skin", Author = "Author", CompatibleCatalogs = new[] { catalog.Id } };
        File.WriteAllText(System.IO.Path.Combine(directory, "skin.json"), Json.Write(template));
        File.WriteAllText(System.IO.Path.Combine(directory, "animations.json"), Json.Write(catalog.Animations));
    }
    public static void ObserveAudio(AssetCatalog catalog)
    {
        if (!SoundManager.Instance) return;
        if (FMODUnity.RuntimeManager.StudioSystem.getBankList(out var banks) != FMOD.RESULT.OK) return;
        foreach (var bank in banks)
        {
            if (bank.getEventList(out var events) != FMOD.RESULT.OK) continue;
            foreach (var ev in events)
            {
                if (ev.getID(out var guid) != FMOD.RESULT.OK) continue;
                ev.getPath(out var path);
                var key = "guid:" + guid;
                var channel = path != null && path.IndexOf("BGM", StringComparison.OrdinalIgnoreCase) >= 0 ? "music" : path != null && path.IndexOf("ambience", StringComparison.OrdinalIgnoreCase) >= 0 ? "ambience" : "sfx";
                catalog.Audio[key] = new CatalogAudio { Path = path ?? key, Channel = channel, ReferencePath = "FMOD bank" };
            }
        }
    }
}
public sealed class SkinSelectorMarker : MonoBehaviour { }
