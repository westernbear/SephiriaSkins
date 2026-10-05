using HarmonyLib;

namespace SephiriaSkins.Plugin;

// Opt-in diagnostics use fresh memory data, never a user's save slot or cloud data.
internal sealed class DiagnosticSession : IDisposable
{
    private static readonly HashSet<string> profiles = new();
    private readonly Harmony guard;
    private readonly SaveData? originalSave = SaveManager.Current, originalRun = SaveManager.CurrentRun;
    private readonly string originalProfile = SaveManager.Binded;
    private readonly SaveData originalOptions = OptionsBinding.Instance.Options, originalDeviceOptions = OptionsBinding.Instance.DeviceOptions;
    public string Profile { get; }

    public DiagnosticSession(string profile)
    {
        Profile = profile;
        profiles.Add(profile);
        guard = new Harmony("dev.sephiria.skins.diagnostic." + profile);
        guard.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save)), prefix: new HarmonyMethod(typeof(DiagnosticSession), nameof(CanSave)));
        SetOptions(originalOptions.Copy(), originalDeviceOptions.Copy());
        OptionsBinding.Instance.Options.enableSave = OptionsBinding.Instance.Options.enableCloudSave = false;
        OptionsBinding.Instance.DeviceOptions.enableSave = OptionsBinding.Instance.DeviceOptions.enableCloudSave = false;
        SaveManager.Binded = profile;
        var save = NewData(profile);
        save.SetString("PlayerName", "Skins Test"); save.SetBool("DestinySwitch_PrologueClear", true);
        AccessTools.Field(typeof(SaveManager), "current").SetValue(null, save);
        RestartRun();
    }
    public void RestartRun() => AccessTools.Field(typeof(SaveManager), "currentRun").SetValue(null, NewData(Profile + "TMP"));
    private static SaveData NewData(string name)
    {
        var data = new SaveData(true) { enableSave = false, enableCloudSave = false };
        data.CreateNew(name); return data;
    }
    private static bool CanSave() => !profiles.Contains(SaveManager.Binded);
    private static void SetOptions(SaveData options, SaveData deviceOptions)
    {
        AccessTools.Property(typeof(OptionsBinding), nameof(OptionsBinding.Options)).SetValue(OptionsBinding.Instance, options);
        AccessTools.Property(typeof(OptionsBinding), nameof(OptionsBinding.DeviceOptions)).SetValue(OptionsBinding.Instance, deviceOptions);
    }
    public void Dispose()
    {
        SetOptions(originalOptions, originalDeviceOptions);
        SaveManager.Binded = originalProfile;
        AccessTools.Field(typeof(SaveManager), "current").SetValue(null, originalSave);
        AccessTools.Field(typeof(SaveManager), "currentRun").SetValue(null, originalRun);
        profiles.Remove(Profile); guard.UnpatchSelf();
    }
}
