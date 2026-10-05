using UnityEngine;
namespace SephiriaSkins.Plugin;
internal static class Ownership
{
    public static PlayerAvatar? Local => CombatManager.Instance ? CombatManager.Instance.CurrentPlayer : null;
    public static bool IsLocal(UnitAvatar? avatar) => avatar && Local && avatar == Local;
    public static UnitAvatar? Owner(Component? component) => Owner(component, 0);
    private static UnitAvatar? Owner(Component? component, int depth)
    {
        if (!component || depth > 16) return null;
        var cosmetic = component!.GetComponentInParent<CosmeticOwner>();
        if (cosmetic && cosmetic.Role == "weapon") return cosmetic.Owner;
        var avatar = component!.GetComponentInParent<UnitAvatar>();
        if (avatar) return avatar;
        var charm = component.GetComponentInParent<Charm_Basic>();
        if (charm) return charm.NetworkAvatar;
        var weapon = component.GetComponentInParent<WeaponSimple>();
        if (weapon) return weapon.Networkowner ? weapon.Networkowner.unitAvatar : null;
        var modern = component.GetComponentInParent<NewWeapon>();
        if (modern) return modern.Owner ? modern.Owner.UnitAvatar : null;
        var bullet = component.GetComponentInParent<Bullet>();
        if (bullet) return bullet.NetworkOwner;
        var fx = component.GetComponentInParent<SpriteFx>();
        if (fx && fx.FollowParent && fx.FollowParent != fx.transform) return Owner(fx.FollowParent, depth + 1);
        var tag = component.GetComponentInParent<CosmeticOwner>();
        return tag ? tag.Owner : null;
    }
    public static string? Role(Animator2D_Basic animator)
    {
        var local = Local;
        if (!local) return null;
        var tag = animator.GetComponentInParent<CosmeticOwner>();
        if (tag && tag.Role == "weapon") return IsLocal(tag.Owner) ? "weapon" : null;
        var costume = local!.GetComponentInChildren<PlayerAvatarCostume>();
        if (costume && (animator.transform == costume.transform || animator.transform.IsChildOf(costume.transform) ||
            (costume.waterReflectionObject && (animator.transform == costume.waterReflectionObject || animator.transform.IsChildOf(costume.waterReflectionObject))))) return "body";
        if (!IsLocal(Owner(animator))) return null;
        if (animator.GetComponentInParent<WeaponSimple>() || animator.GetComponentInParent<NewWeapon>()) return "weapon";
        return "effect";
    }
}
public sealed class CosmeticOwner : MonoBehaviour
{
    public UnitAvatar? Owner;
    public string Role = "";
    public readonly Dictionary<int, string> VisualKeys = new();
    private void OnDisable() { if (Role != "weapon") Owner = null; Plugin.Instance?.Visuals.Forget(gameObject); }
}
internal static class DetachedWeaponTargets
{
    public static void Bind(WeaponSimple weapon, WeaponControllerSimple? controller)
    {
        var owner = controller ? controller!.unitAvatar : null;
        var roots = new[] { weapon.mainWeapon, weapon.subWeapon,
            weapon.mainWeaponBody && weapon.mainWeaponBody.weaponStencilRenderer ? weapon.mainWeaponBody.weaponStencilRenderer.transform : null,
            weapon.subWeaponBody && weapon.subWeaponBody.weaponStencilRenderer ? weapon.subWeaponBody.weaponStencilRenderer.transform : null };
        foreach (var root in roots)
        {
            if (!root) continue;
            var tag = root!.GetComponent<CosmeticOwner>() ?? root.gameObject.AddComponent<CosmeticOwner>();
            tag.Owner = owner; tag.Role = "weapon";
            if (!owner) Plugin.Instance?.Visuals.Forget(root.gameObject);
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var id = renderer.GetInstanceID();
                if (tag.VisualKeys.ContainsKey(id) || !renderer.transform.IsChildOf(weapon.transform)) continue;
                var path = RuntimeCatalog.PathOf(renderer.transform);
                var prefix = weapon.transform.parent ? RuntimeCatalog.PathOf(weapon.transform.parent) + "/" : "";
                if (prefix.Length > 0 && path.StartsWith(prefix, StringComparison.Ordinal)) path = path.Substring(prefix.Length);
                tag.VisualKeys[id] = "weapon/" + path + "/SpriteRenderer";
            }
        }
    }
}
internal static class OwnerContext
{
    [ThreadStatic] public static UnitAvatar? Current;
    public static UnitAvatar? Push(UnitAvatar? owner) { var old = Current; Current = owner; return old; }
    public static void Pop(UnitAvatar? old) => Current = old;
}
