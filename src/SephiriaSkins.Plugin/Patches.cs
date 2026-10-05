using System.Reflection;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;
namespace SephiriaSkins.Plugin;

[HarmonyPatch]
internal static class SpriteOutputPatch
{
    static IEnumerable<MethodBase> TargetMethods() => typeof(Animator2D_Basic).Assembly.GetTypes()
        .Where(t => typeof(Animator2D_Basic).IsAssignableFrom(t) && !t.IsAbstract)
        .Select(t => t.GetMethod("SetSprite", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .Where(m => m != null).Cast<MethodBase>();
    static void Prefix(Animator2D_Basic __instance, ref Sprite sprite)
    {
        try { Plugin.Instance?.Visuals.Replace(__instance, ref sprite); }
        catch (Exception e) { Plugin.Instance?.ReportHookError(e); }
    }
}
[HarmonyPatch]
internal static class OwnerScopePatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var roots = new[] { typeof(WeaponSimple), typeof(Charm_Basic), typeof(CharacterDash), typeof(NewWeaponFireData) };
        var types = new[] { typeof(CombatBehaviour), typeof(UnitAvatar), typeof(PlayerAvatar), typeof(Bullet), typeof(Animator2D_Basic) }
            .Concat(typeof(PlayerAvatar).Assembly.GetTypes().Where(t => roots.Any(root => root.IsAssignableFrom(t)))).Distinct();
        foreach (var type in types)
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.ReturnType != typeof(void)) continue;
                var n = method.Name;
                if (n == "Update" || n.Contains("PlaySound") || n.Contains("CreateSwingFx") || n.Contains("SwingFx") || n.Contains("DieClientside") || n.Contains("HitFeedback") ||
                    n.Contains("ParryFx") || n.Contains("AdditionalLifeFx") || n.Contains("SuperArmorBreakFx") || n.Contains("BloodFestivalHealFx") ||
                    n == "ShowEmoji" || n == "StartStun" || n == "UpdateCostumeOutfit" || n.Contains("RevivePatient") ||
                    ((n.Contains("Create") || n.Contains("Spawn")) && n.Contains("Fx")) ||
                    (type == typeof(Bullet) && (n.Contains("Attack") || n.Contains("Collision") || n == "DestroySelf"))) yield return method;
            }
    }
    static void Prefix(object __instance, object[] __args, out UnitAvatar? __state)
    {
        UnitAvatar? owner = __instance is Component c ? Ownership.Owner(c) : null;
        if (__instance is Animator2D_Basic animator && Ownership.Role(animator) == "body") owner = Ownership.Local;
        if (__instance is NewWeaponFireData) owner = __args.OfType<UnitAvatar>().FirstOrDefault() ?? __args.OfType<Transform>().Select(Ownership.Owner).FirstOrDefault(o => o);
        __state = OwnerContext.Push(owner);
    }
    static Exception? Finalizer(UnitAvatar? __state, Exception? __exception) { OwnerContext.Pop(__state); return __exception; }
}
[HarmonyPatch(typeof(SpriteFx), nameof(SpriteFx.OnSpawn))]
internal static class FxSpawnPatch
{
    static void Prefix(SpriteFx __instance)
    {
        Plugin.Instance?.Visuals.Forget(__instance.gameObject);
        var tag = __instance.GetComponent<CosmeticOwner>() ?? __instance.gameObject.AddComponent<CosmeticOwner>();
        tag.Owner = OwnerContext.Current;
    }
}
[HarmonyPatch(typeof(WeaponSimple), "Initialize")]
internal static class DetachedWeaponPatch
{
    static void Prefix(WeaponSimple __instance, WeaponControllerSimple newValue)
    {
        try { DetachedWeaponTargets.Bind(__instance, newValue); }
        catch (Exception e) { Plugin.Instance?.ReportHookError(e); }
    }
}
[HarmonyPatch(typeof(SpriteFx), nameof(SpriteFx.OnDespawn))]
internal static class FxDespawnPatch
{
    static void Prefix(SpriteFx __instance)
    {
        Plugin.Instance?.Visuals.Forget(__instance.gameObject);
        var tag = __instance.GetComponent<CosmeticOwner>(); if (tag) tag.Owner = null;
    }
}
[HarmonyPatch(typeof(Bullet), nameof(Bullet.OnDespawn))]
internal static class BulletDespawnPatch
{
    static void Prefix(Bullet __instance) => Plugin.Instance?.Visuals.Forget(__instance.gameObject);
}
[HarmonyPatch(typeof(RuntimeManager), nameof(RuntimeManager.CreateInstance), new[] { typeof(GUID) })]
internal static class AudioCreatePatch
{
    static void Postfix(GUID guid, EventInstance __result)
    { try { Plugin.Instance?.Audio.Register(__result, guid); } catch (Exception e) { Plugin.Instance?.ReportHookError(e); } }
}
[HarmonyPatch]
internal static class AudioAttachPatch
{
    static IEnumerable<MethodBase> TargetMethods() => typeof(RuntimeManager).GetMethods().Where(m => m.Name == "AttachInstanceToGameObject");
    static void Postfix(object[] __args)
    {
        if (__args.Length < 2 || !(__args[0] is EventInstance instance)) return;
        var target = __args[1] is GameObject go ? go.transform : __args[1] as Transform;
        if (target) Plugin.Instance?.Audio.Attach(instance, target);
    }
}
[HarmonyPatch(typeof(EventInstance), nameof(EventInstance.start))]
internal static class AudioStartPatch
{
    static void Postfix(EventInstance __instance, RESULT __result)
    { if (__result == RESULT.OK) try { Plugin.Instance?.Audio.Start(__instance); } catch (Exception e) { Plugin.Instance?.ReportHookError(e); } }
}
[HarmonyPatch(typeof(EventInstance), nameof(EventInstance.stop))]
internal static class AudioStopPatch
{
    static void Postfix(EventInstance __instance, STOP_MODE mode) => Plugin.Instance?.Audio.Stop(__instance, mode);
}
[HarmonyPatch(typeof(EventInstance), nameof(EventInstance.setVolume))]
internal static class AudioVolumePatch
{
    static void Prefix(EventInstance __instance, ref float volume) => Plugin.Instance?.Audio.Volume(__instance, ref volume);
}
[HarmonyPatch(typeof(EventInstance), nameof(EventInstance.setPaused))]
internal static class PauseInstancePatch
{
    static void Postfix(EventInstance __instance, bool paused) => Plugin.Instance?.Audio.PauseInstance(__instance, paused);
}
[HarmonyPatch(typeof(EventInstance), nameof(EventInstance.setPitch))]
internal static class AudioPitchPatch
{
    static void Postfix(EventInstance __instance, float pitch) => Plugin.Instance?.Audio.Pitch(__instance, pitch);
}
[HarmonyPatch(typeof(SoundManager), nameof(SoundManager.PauseAll))]
internal static class PauseAudioPatch { static void Postfix() => Plugin.Instance?.Audio.Pause(true); }
[HarmonyPatch(typeof(SoundManager), nameof(SoundManager.UnPauseAll))]
internal static class ResumeAudioPatch { static void Postfix() => Plugin.Instance?.Audio.Pause(false); }

[HarmonyPatch(typeof(PlayerInputController), "get_BlockAvatarInput")]
internal static class SelectorInputPatch
{
    static void Postfix(ref bool __result) => __result |= Plugin.Instance?.SelectorOpen == true;
}
