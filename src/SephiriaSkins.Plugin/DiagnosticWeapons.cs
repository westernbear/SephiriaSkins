namespace SephiriaSkins.Plugin;

internal static class DiagnosticWeapons
{
    public static WeaponEntity[] All() => WeaponDatabase.GetAll().Where(w => w).OrderBy(w => w.id).ToArray();
    public static string? SkipReason(WeaponEntity weapon)
    {
        if (!weapon.enabled) return "disabled in installed game";
        if (weapon.activeState != WeaponEntity.EActiveState.Active) return "not active in installed game: " + weapon.activeState;
        if (!weapon.mainWeaponPrefab) return "no main weapon prefab";
        var native = weapon.mainWeaponPrefab.GetComponent<WeaponSimple>();
        if (!native) return "native WeaponControllerSimple cannot equip this prefab";
        if (native is WeaponSimple_Staff)
            return "legacy StaffMagic uses a SpriteFx RPC path outside the diagnostic's native projectile/event observers; combat equivalence unverified";
        if (native is WeaponSimple_Golem)
            return "native attacks are delegated to equipped golem charms; the ordinary avatar fixture has no golem parts, so this conditional loadout is unverified";
        // The base class's two input methods are empty. Some legacy database
        // entries still instantiate it; a silent input is not an attack pass.
        var nativeType = native.GetType();
        if (nativeType.GetMethod(nameof(WeaponSimple.AttackButtonDown), new[] { typeof(int) })?.DeclaringType == typeof(WeaponSimple) &&
            nativeType.GetMethod(nameof(WeaponSimple.SubAttackButtonDown), Type.EmptyTypes)?.DeclaringType == typeof(WeaponSimple))
            return "native primary and secondary input methods have no implementation";
        return null;
    }
    private static bool Selected(WeaponEntity weapon)
    {
        var run = Plugin.Instance!.Diagnostics;
        return run.WeaponScope == "all" || run.WeaponScope == "defaults" && weapon.isDefaultWeapon || run.WeaponIds?.Contains(weapon.id) == true;
    }
    public static WeaponEntity[] Playable()
    {
        var all = All();
        var requested = Plugin.Instance!.Diagnostics.WeaponIds;
        if (requested != null && requested.Any(id => !all.Any(w => w.id == id))) throw new InvalidOperationException("Requested weapon absent from installed database.");
        foreach (var trial in Plugin.Instance.Diagnostics.SkippedTrials.Keys)
            if (!all.Any(w => w.id == int.Parse(trial.Split(':')[0]) && Selected(w))) throw new InvalidOperationException("Skipped trial is outside the selected native weapon inventory: " + trial);
        return all.Where(w => SkipReason(w) == null && Selected(w)).ToArray();
    }
    private static string? ModeSkipReason(WeaponEntity weapon, string mode)
    {
        if (Plugin.Instance!.Diagnostics.SkippedTrials.TryGetValue(weapon.id + ":" + mode, out var reason)) return reason;
        var native = weapon.mainWeaponPrefab?.GetComponent<WeaponSimple>();
        if (!native) return null; // Whole-weapon exclusions already describe this case.
        if (mode == "dash" && native is WeaponSimple_Bow && native.dashAttacks.Length == 0)
            return "legacy native bow has no dash fire data; hold/release fires an ordinary basic arrow after the dash, so a separate dash attack is unexecuted";
        if (mode == "special" && native is WeaponSimple_Dagger dagger && (dagger.critFury || dagger.evadeFury))
            return "native secondary requires fury earned from critical hits or evasion; the fresh zero-fury fixture does not exercise this conditional variant";
        var method = mode == "special" ? native.GetType().GetMethod(nameof(WeaponSimple.SubAttackButtonDown), Type.EmptyTypes) :
            native.GetType().GetMethod(nameof(WeaponSimple.AttackButtonDown), new[] { typeof(int) });
        return method?.DeclaringType == typeof(WeaponSimple) ? "native input method is the empty WeaponSimple base implementation" : null;
    }
    public static bool ShouldRun(WeaponEntity weapon, string mode) => ModeSkipReason(weapon, mode) == null;
    private static object[] DeclaredFireVariants(WeaponEntity weapon)
    {
        var native = weapon.mainWeaponPrefab?.GetComponent<WeaponSimple>();
        if (!native) return Array.Empty<object>();
        var declarations = new List<object>();
        var sources = new UnityEngine.Component[] { native! }.Concat(native!.addons.Where(a => a).Cast<UnityEngine.Component>()).Distinct();
        foreach (var source in sources)
        foreach (var field in source.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var value = field.GetValue(source);
            var data = value is NewWeaponFireData single ? new[] { single } : value as NewWeaponFireData[];
            if (data == null) continue;
            for (var index = 0; index < data.Length; index++)
            {
                var fire = data[index];
                var prefab = (fire as NewWeaponFireData_Bullet)?.bulletPrefab ?? (fire as NewWeaponFireData_SpecialProjectile)?.projectilePrefab;
                declarations.Add(new { source = source.GetType().Name + ":" + source.name, field = field.Name, index,
                    fireData = fire ? fire.name : null, kind = fire ? fire.GetType().Name : null,
                    bulletPrefab = prefab ? prefab.name : null,
                    movementModules = prefab ? prefab!.GetComponentsInChildren<BulletMoveModule>(true).Select(m => m.GetType().Name).Distinct().ToArray() : Array.Empty<string>(),
                    scope = "native prefab declaration only; actual execution is recorded separately in combat results" });
            }
        }
        return declarations.ToArray();
    }
    public static object Coverage() => new
    {
        modes = new[] { "basic", "dash", "special" },
        scope = Plugin.Instance!.Diagnostics.WeaponScope,
        weapons = All().Select(w => new
        {
            w.id, w.name, w.isDefaultWeapon, w.enhanceFromId,
            type = w.mainWeaponPrefab?.GetComponent<WeaponSimple>()?.weaponType.ToString(),
            nativeClass = w.mainWeaponPrefab?.GetComponent<WeaponSimple>()?.GetType().Name,
            declaredFireVariants = DeclaredFireVariants(w),
            selected = Selected(w),
            skipReason = SkipReason(w),
            skippedModes = new[] { "basic", "dash", "special" }.Where(mode => !ShouldRun(w, mode))
                .ToDictionary(mode => mode, mode => ModeSkipReason(w, mode)!)
        }).ToArray(),
        untested = new[] { "additional combo stages", "variable charge durations", "mine detonation after target movement", "long-lived effects beyond the trial window" }
    };
}
