using HarmonyLib;
using SephiriaSkins.Core;
using UnityEngine;
using Object = UnityEngine.Object;
namespace SephiriaSkins.Plugin;
internal sealed class VisualAdapter
{
    private sealed class Output
    {
        public SpriteRenderer Renderer = null!;
        public Sprite? Original;
        public Sprite? Applied;
        public Material? OriginalMaterial;
        public Material? AppliedMaterial;
    }
    private readonly Dictionary<int, Output> outputs = new();
    private readonly Dictionary<int, (GameObject Object, string Key)> particles = new();
    private readonly Dictionary<(AnimationSet, string, string), string> keys = new();
    private readonly HashSet<(AnimationSet, string)> observed = new();
    private readonly List<(SpriteRenderer Renderer, string Key)> staticTargets = new();
    private float nextStaticScan;
    private static readonly System.Reflection.FieldInfo Frame = AccessTools.Field(typeof(Animator2D_Basic), "currentFrameIdx");
    public void Replace(Animator2D_Basic animator, ref Sprite sprite)
    {
        var plugin = Plugin.Instance;
        if (!plugin || !animator.currentSet) return;
        var role = Ownership.Role(animator);
        if (role == null) { Forget(animator.gameObject); return; }
        var state = animator.currentSet.sprites.FirstOrDefault(s => s.state.Equals(animator.CurrentStateName, StringComparison.OrdinalIgnoreCase));
        if (state == null) return;
        var tuple = (animator.currentSet, state.state, role);
        if (!keys.TryGetValue(tuple, out var key))
        {
            key = RuntimeCatalog.AnimationKey(role, animator.currentSet, state); keys[tuple] = key;
            if (observed.Add((animator.currentSet, role))) RuntimeCatalog.Observe(plugin!.Catalog, animator, role);
        }
        var theme = plugin!.Theme;
        if (theme == null) return;
        var bindings = role == "body" ? theme.Pack.Manifest.Body : role == "weapon" ? theme.Pack.Manifest.Weapons : theme.Pack.Manifest.Effects;
        if (!bindings.TryGetValue(key, out var binding)) { Forget(animator.gameObject); return; }
        var idx = (int)Frame.GetValue(animator);
        // AnimationSet is sparse; each keyframe holds until the next one.
        var at = -1;
        for (var i = 0; i < binding.FrameIndices.Length; i++) if (binding.FrameIndices[i] <= idx && (at < 0 || binding.FrameIndices[i] > binding.FrameIndices[at])) at = i;
        if (at < 0 || !sprite) return; // Preserve intentionally blank frames.
        var replacement = theme.Get<Sprite>(binding.Frames[at]);
        IEnumerable<SpriteRenderer> renderers = animator is Animator2D_SpriteRenderer single ? new[] { single.spriteRenderer } :
            animator is Animator2D_MultipleSpriteRenderer multi ? multi.spriteRenderers : Array.Empty<SpriteRenderer>();
        foreach (var renderer in renderers)
        {
            if (!renderer) continue;
            var id = renderer.GetInstanceID();
            if (!outputs.TryGetValue(id, out var output)) outputs[id] = output = new Output { Renderer = renderer };
            output.Original = sprite; output.Applied = replacement;
            if (binding.Material != null)
            {
                if (!output.AppliedMaterial || renderer.sharedMaterial != output.AppliedMaterial) output.OriginalMaterial = renderer.sharedMaterial;
                output.AppliedMaterial = theme.Get<Material>(binding.Material); renderer.sharedMaterial = output.AppliedMaterial;
            }
            else if (output.AppliedMaterial)
            {
                if (renderer.sharedMaterial == output.AppliedMaterial) renderer.sharedMaterial = output.OriginalMaterial;
                output.AppliedMaterial = null;
            }
        }
        sprite = replacement;
        var animatorId = animator.GetInstanceID();
        if (binding.Particle != null)
        {
            if (!particles.TryGetValue(animatorId, out var particle) || particle.Key != key)
            {
                if (particle.Object) Object.Destroy(particle.Object);
                var instance = Object.Instantiate(theme.Get<GameObject>(binding.Particle), animator.transform);
                particles[animatorId] = (instance, key);
            }
        }
        else if (particles.TryGetValue(animatorId, out var particle)) { if (particle.Object) Object.Destroy(particle.Object); particles.Remove(animatorId); }
    }
    private static void Restore(Output o)
    {
        if (!o.Renderer) return;
        if (o.Renderer.sprite == o.Applied) o.Renderer.sprite = o.Original;
        if (o.AppliedMaterial && o.Renderer.sharedMaterial == o.AppliedMaterial) o.Renderer.sharedMaterial = o.OriginalMaterial;
    }
    public void Forget(GameObject root)
    {
        foreach (var pair in outputs.Where(p => !p.Value.Renderer || p.Value.Renderer.transform == root.transform || p.Value.Renderer.transform.IsChildOf(root.transform)).ToArray())
        { Restore(pair.Value); outputs.Remove(pair.Key); }
        foreach (var pair in particles.Where(p => !p.Value.Object || p.Value.Object.transform.IsChildOf(root.transform)).ToArray())
        { if (pair.Value.Object) Object.Destroy(pair.Value.Object); particles.Remove(pair.Key); }
    }
    public void RestoreAll()
    {
        foreach (var o in outputs.Values) Restore(o);
        outputs.Clear();
        foreach (var p in particles.Values) if (p.Object) Object.Destroy(p.Object);
        particles.Clear(); keys.Clear(); staticTargets.Clear(); nextStaticScan = 0;
    }
    public void Refresh()
    {
        foreach (var animator in Object.FindObjectsByType<Animator2D_Basic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!animator || !animator.gameObject.scene.IsValid() || !animator.currentSet || !animator.IsReady) continue;
            var state = animator.currentSet.sprites.FirstOrDefault(s => s.state.Equals(animator.CurrentStateName, StringComparison.OrdinalIgnoreCase));
            if (state == null) continue;
            var idx = (int)Frame.GetValue(animator);
            var frame = state.timeline.OrderBy(f => f.frameIdx).LastOrDefault(f => f.frameIdx <= idx);
            if (frame != null) animator.SetSprite(frame.sprite); // No state changes or event replay.
        }
    }
    public void Prune()
    {
        foreach (var p in outputs.Where(p => !p.Value.Renderer).ToArray()) outputs.Remove(p.Key);
        foreach (var p in particles.Where(p => !p.Value.Object).ToArray()) particles.Remove(p.Key);
    }
    public void ApplyStatic(RuntimeTheme? theme)
    {
        if (theme == null || theme.Pack.Manifest.Visuals.Count == 0) return;
        if (Time.unscaledTime >= nextStaticScan)
        {
            nextStaticScan = Time.unscaledTime + 0.5f; staticTargets.Clear();
            var paths = new RuntimeCatalog.PathIndex();
            foreach (var renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!renderer || !renderer.gameObject.scene.IsValid()) continue;
                var tag = renderer.GetComponentInParent<CosmeticOwner>();
                if (tag && tag.Role == "weapon" && tag.VisualKeys.TryGetValue(renderer.GetInstanceID(), out var detachedKey))
                {
                    if (theme.Pack.Manifest.Visuals.ContainsKey(detachedKey)) staticTargets.Add((renderer, detachedKey));
                    continue;
                }
                var weapon = renderer.GetComponentInParent<WeaponSimple>();
                var modern = renderer.GetComponentInParent<NewWeapon>();
                var fx = renderer.GetComponentInParent<SpriteFx>();
                var bullet = renderer.GetComponentInParent<Bullet>();
                var source = weapon ? weapon.transform : modern ? modern.transform : fx ? fx.transform : bullet ? bullet.transform : null;
                if (!source) continue;
                var relative = paths.PathOf(renderer.transform);
                var parentPath = source!.parent ? paths.PathOf(source.parent) + "/" : "";
                if (relative.StartsWith(parentPath, StringComparison.Ordinal)) relative = relative.Substring(parentPath.Length);
                var key = (weapon || modern ? "weapon" : "effect") + "/" + relative + "/SpriteRenderer";
                if (theme.Pack.Manifest.Visuals.ContainsKey(key)) staticTargets.Add((renderer, key));
            }
        }
        foreach (var target in staticTargets)
        {
            var renderer = target.Renderer; if (!renderer) continue;
            var id = renderer.GetInstanceID();
            if (!Ownership.IsLocal(Ownership.Owner(renderer)))
            {
                if (outputs.TryGetValue(id, out var old)) { Restore(old); outputs.Remove(id); }
                continue;
            }
            if (!theme.Pack.Manifest.Visuals.TryGetValue(target.Key, out var binding)) continue;
            if (!outputs.TryGetValue(id, out var output)) outputs[id] = output = new Output { Renderer = renderer, Original = renderer.sprite };
            if (binding.Sprite != null && renderer.sprite)
            {
                if (renderer.sprite != output.Applied) output.Original = renderer.sprite;
                output.Applied = theme.Get<Sprite>(binding.Sprite); renderer.sprite = output.Applied;
            }
            if (binding.Material != null)
            {
                if (!output.AppliedMaterial || renderer.sharedMaterial != output.AppliedMaterial) output.OriginalMaterial = renderer.sharedMaterial;
                output.AppliedMaterial = theme.Get<Material>(binding.Material); renderer.sharedMaterial = output.AppliedMaterial;
            }
        }
    }
}
