using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SephiriaSkins.Core;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SephiriaSkins.Plugin;

[BepInPlugin(Id, "Sephiria Skins", "0.2.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "dev.sephiria.skins";
    internal static Plugin? Instance;
    internal readonly VisualAdapter Visuals = new();
    internal readonly UiAdapter Ui = new();
    internal readonly AudioAdapter Audio = new();
    internal RuntimeTheme? Theme;
    private readonly ThemeTransaction<RuntimeTheme> transaction = new();
    internal AssetCatalog Catalog = new();
    internal DiagnosticRun Diagnostics = null!;
    private Harmony? harmony;
    private ConfigEntry<string> selected = null!;
    private ConfigEntry<Key> shortcut = null!;
    private ConfigEntry<bool> pixelArt = null!, gameUi = null!;
    internal bool PixelArt => pixelArt.Value;
    internal bool GameUi => gameUi.Value;
    internal string SelectedId => selected.Value;
    internal void SetPixelArt(bool value)
    {
        if (loading || pixelArt.Value == value) return;
        pixelArt.Value = value; Config.Save(); Visuals.RestoreAll(); Visuals.Refresh(); Visuals.ApplyStatic(Theme);
    }
    internal void SetGameUi(bool value)
    {
        if (loading || gameUi.Value == value) return;
        gameUi.Value = value; Config.Save(); Ui.Restore(); ApplyUi();
    }
    internal void ApplyUi(bool includeInactive = true) { if (GameUi) Ui.Apply(Theme, includeInactive); }
    private List<PackEntry> packs = new();
    private string root = "", status = "", unsupported = "";
    private bool loading, open, quitting;
    private byte[]? diagnosticConfigSnapshot;
    private float nextTick;
    private readonly NativeSelector selector = new();
    private readonly HashSet<string> errors = new();
    private CursorLockMode priorLock;
    private bool priorCursor;
    internal bool SelectorOpen => open;
    private void Awake()
    {
        Instance = this;
        root = Path.GetDirectoryName(Info.Location);
        if (Environment.GetCommandLineArgs().Any(a => a == "--skins-probe" || a == "--skins-playtest") && File.Exists(Config.ConfigFilePath))
            diagnosticConfigSnapshot = File.ReadAllBytes(Config.ConfigFilePath);
        selected = Config.Bind("Skin", "Selected", "", "Pack ID. Empty uses the original game appearance.");
        shortcut = Config.Bind("Keys", "Selector", Key.F6, "Open/close skin selector.");
        pixelArt = Config.Bind("Appearance", "PixelArt", true, "Pixel rendering for skin body, weapons and effects.");
        gameUi = Config.Bind("Appearance", "GameUi", true, "Apply the skin to game UI. Off restores native UI.");
        var catalogResource = GetType().Assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("catalog-1.0.33.json"));
        if (catalogResource != null)
        {
            using var stream = GetType().Assembly.GetManifestResourceStream(catalogResource);
            using var reader = new StreamReader(stream!); Catalog = Json.Read<AssetCatalog>(reader.ReadToEnd());
        }
        var hash = Keys.Hash(File.ReadAllBytes(typeof(PlayerAvatar).Assembly.Location));
        if (Catalog.AssemblySha256.Length == 0 || hash != Catalog.AssemblySha256 || Application.unityVersion != Catalog.UnityVersion)
            unsupported = "게임 DLL 또는 Unity 버전이 카탈로그와 다릅니다. 카탈로그를 다시 생성하세요.";
        var baselineProbe = Environment.GetCommandLineArgs().Contains("--skins-probe-baseline");
        if (unsupported.Length == 0 && !baselineProbe)
        {
            harmony = new Harmony(Id);
            try { harmony.PatchAll(GetType().Assembly); }
            catch (Exception e) { harmony.UnpatchSelf(); unsupported = "게임 연결 실패: " + e.Message; Logger.LogError(e); }
        }
        ReloadList();
        Logger.LogInfo("SephiriaSkins ready; catalog=" + Catalog.Id + "; animations=" + Catalog.Animations.Count + "; ui=" + Catalog.Ui.Count + "; audio=" + Catalog.Audio.Count + "; " + unsupported);
        var diagnosticRun = Environment.GetCommandLineArgs().Any(a => a == "--skins-probe" || a == "--skins-playtest");
        if (diagnosticRun)
        {
            try { Diagnostics = new DiagnosticRun(root, Environment.GetCommandLineArgs()); }
            catch (Exception e) { Logger.LogError("Diagnostic arguments rejected: " + e.Message); return; }
            Logger.LogInfo("SKINS_DIAGNOSTIC_OUTPUT " + Diagnostics.DirectoryPath);
        }
        if (selected.Value.Length > 0 && unsupported.Length == 0 && !baselineProbe &&
            (!diagnosticRun || Environment.GetCommandLineArgs().Contains("--skins-playtest-persisted")))
        {
            var entry = packs.FirstOrDefault(p => p.Pack?.Manifest.Id == selected.Value && p.Error == null);
            if (entry != null) StartCoroutine(Apply(entry));
            else status = "저장된 팩을 불러오지 못했습니다. 원본을 사용합니다.";
        }
        if (Environment.GetCommandLineArgs().Contains("--skins-probe")) { Application.runInBackground = true; StartCoroutine(Probe()); }
        if (Environment.GetCommandLineArgs().Contains("--skins-playtest"))
        {
            var priorSelection = selected.Value;
            var priorPixel = PixelArt; var priorUi = GameUi;
            Application.runInBackground = true;
            StartCoroutine(PlayProbe.Run(this, Apply, RestoreOriginal, SetOpen, () => errors.ToArray(), root,
                () => { selected.Value = priorSelection; pixelArt.Value = priorPixel; gameUi.Value = priorUi; Config.Save(); RestoreDiagnosticConfig(); }));
        }
    }
    private void ReloadList()
    {
        packs = PackDiscovery.Scan(Path.Combine(root, "Skins"), Catalog);
        foreach (var p in packs.Where(p => p.Error != null)) Logger.LogWarning(p.Source + ": " + p.Error);
    }
    private IEnumerator Apply(PackEntry entry)
    {
        if (loading || unsupported.Length > 0 || entry.Error != null || entry.Pack == null) yield break;
        loading = true; status = "팩 검사 및 리소스 로딩 중…";
        try
        {
            PackSnapshot? snapshot = null;
            try { snapshot = PackReader.Read(entry.Source, Catalog); }
            catch (Exception e) { status = "불러오기 실패: " + e.Message; Logger.LogWarning(e); }
            RuntimeTheme? next = null;
            string? error = null;
            if (snapshot != null) yield return RuntimeTheme.Prepare(snapshot, Path.Combine(root, ".cache"), (theme, message) => { next = theme; error = message; });
            if (next != null)
            {
                var committed = transaction.TryPrepare(() => next, (previous, candidate) =>
                {
                    try
                    {
                        Visuals.RestoreAll(); Ui.Restore(); Audio.Restore(); Theme = candidate;
                        Visuals.Refresh(); ApplyUi(); Audio.ThemeChanged();
                    }
                    catch
                    {
                        Visuals.RestoreAll(); Ui.Restore(); Audio.Restore(); Theme = previous;
                        Visuals.Refresh(); ApplyUi(); Audio.ThemeChanged(); throw;
                    }
                }, out error);
                if (committed) { selected.Value = next.Pack.Manifest.Id; Config.Save(); status = "적용됨: " + next.Pack.Manifest.Name; Logger.LogInfo(status); }
                else { status = "적용 실패, 이전 팩 유지: " + error; Logger.LogWarning(status); }
            }
            else if (snapshot != null) { status = "불러오기 실패, 현재 팩 유지: " + error; Logger.LogWarning(status); }
        }
        finally { loading = false; }
    }
    private void RestoreOriginal()
    {
        if (loading) return;
        Visuals.RestoreAll(); Ui.Restore(); Audio.Restore();
        transaction.Restore((_, _) => Theme = null);
        selected.Value = ""; Config.Save(); status = "게임 원본으로 복원됨";
    }
    private void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;
        if (open)
        {
            if (!selector.Open(packs, () => Theme, entry => StartCoroutine(Apply(entry)), RestoreOriginal, ReloadSelected,
                () => { RuntimeCatalog.Export(Catalog, Path.Combine(root, "Export")); Logger.LogInfo("Template saved: " + Path.Combine(root, "Export")); }, () => SetOpen(false),
                () => PixelArt, () => SetPixelArt(!PixelArt), () => GameUi, () => SetGameUi(!GameUi)))
            { open = false; return; }
            priorLock = Cursor.lockState; priorCursor = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (Ownership.Local) Ownership.Local!.localDataStorage.Stop();
            if (Ownership.Local) { Ownership.Local!.AttackButtonUp(); Ownership.Local.SubAttackButtonUp(); }
        }
        else { selector.Close(); Cursor.lockState = priorLock; Cursor.visible = priorCursor; }
    }
    private void ReloadSelected()
    {
        ReloadList();
        var entry = packs.FirstOrDefault(p => p.Pack?.Manifest.Id == selected.Value && p.Error == null);
        if (entry != null) StartCoroutine(Apply(entry));
        else if (Theme != null) status = "현재 팩 재검사 실패: 기존 정상 테마 유지";
    }
    private void Update()
    {
        if (Keyboard.current != null && shortcut.Value != Key.None && Keyboard.current[shortcut.Value].wasPressedThisFrame) SetOpen(!open);
        if (open && ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (UIInputModule.currentModule && UIInputModule.currentModule.cancel && UIInputModule.currentModule.cancel.action.WasPressedThisFrame()))) SetOpen(false);
        if (Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + 0.2f;
        try { selector.Update(packs, loading, status, unsupported.Length > 0); ApplyUi(false); Audio.Tick(); Visuals.Prune(); }
        catch (Exception e) { ReportHookError(e); }
    }
    private void LateUpdate()
    {
        try { Visuals.ApplyStatic(Theme); }
        catch (Exception e) { ReportHookError(e); }
    }
    internal void ReportHookError(Exception e)
    {
        if (errors.Add(e.GetType().Name + e.Message)) Logger.LogError(e);
    }
    private void RestoreDiagnosticConfig()
    {
        if (diagnosticConfigSnapshot != null) File.WriteAllBytes(Config.ConfigFilePath, diagnosticConfigSnapshot);
    }
    internal bool DiagnosticConfigRestored => diagnosticConfigSnapshot == null || File.Exists(Config.ConfigFilePath) &&
        diagnosticConfigSnapshot.SequenceEqual(File.ReadAllBytes(Config.ConfigFilePath));
    private IEnumerator Probe()
    {
        var priorSelection = selected.Value;
        var priorPixel = PixelArt; var priorUi = GameUi;
        SetPixelArt(true); SetGameUi(true);
        var priorBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var pad = InputSystem.AddDevice<Gamepad>("Skins output-probe gamepad");
        DiagnosticSession? session = null;
        var exceptions = new List<string>();
        void OnLog(string message, string stack, LogType type) { if (type == LogType.Exception) exceptions.Add(message + "\n" + stack); }
        Application.logMessageReceived += OnLog;
        var pending = new Stack<IEnumerator>(); pending.Push(ProbeSteps(value => session = value));
        while (pending.Count > 0)
        {
            if (File.Exists(Diagnostics.File("cancel.request"))) { exceptions.Add("diagnostic cancelled by request"); break; }
            object? value = null; bool next;
            try { next = pending.Peek().MoveNext(); if (next) value = pending.Peek().Current; }
            catch (Exception e) { exceptions.Add(e.ToString()); break; }
            if (!next) { (pending.Pop() as IDisposable)?.Dispose(); continue; }
            if (value is IEnumerator nested) pending.Push(nested); else yield return value;
        }
        while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
        SetOpen(false); RestoreOriginal(); RuntimeProbe.LeaveLobby();
        if (Mirror.NetworkServer.active) Mirror.NetworkManager.singleton.StopHost();
        yield return new WaitForSecondsRealtime(3);
        var profileAbsent = session == null || (!SaveData.Exists(session.Profile) && !SaveData.Exists(session.Profile + "TMP"));
        session?.Dispose(); InputSystem.RemoveDevice(pad); InputSystem.settings.backgroundBehavior = priorBackground;
        selected.Value = priorSelection; pixelArt.Value = priorPixel; gameUi.Value = priorUi; Config.Save(); RestoreDiagnosticConfig();
        File.WriteAllText(Diagnostics.File("probe-cleanup.json"), Json.Write(new { packId = Diagnostics.PackId, runId = Diagnostics.RunId, exceptions, profileAbsent, themeNull = Theme == null, audioReplacements = Audio.ActiveReplacements, errors = errors.ToArray(),
            configBytesRestored = DiagnosticConfigRestored, fontLifetime = new { FontLifetime.RemovedFonts, FontLifetime.RemovedAtlases, FontLifetime.FaceResets } }));
        Application.logMessageReceived -= OnLog;
        if (Environment.GetCommandLineArgs().Contains("--skins-probe-exit")) Application.Quit();
    }
    // Combat A/B trials reuse a fully prepared theme. Reload/disposal have their
    // own fixtures; decoding every resource for each swing needlessly dominates
    // a full weapon inventory run. Only opt-in diagnostics can call these.
    internal RuntimeTheme SuspendDiagnosticTheme()
    {
        if (Diagnostics == null || Theme == null) throw new InvalidOperationException("No diagnostic theme to suspend.");
        var current = Theme;
        Visuals.RestoreAll(); Ui.Restore(); Audio.Restore(); Theme = null;
        return current;
    }
    internal void ResumeDiagnosticTheme(RuntimeTheme current)
    {
        if (Diagnostics == null || Theme != null) throw new InvalidOperationException("Invalid diagnostic theme resume.");
        Theme = current; Visuals.Refresh(); Visuals.ApplyStatic(Theme); ApplyUi(); Audio.ThemeChanged();
    }
    private IEnumerator ProbeSteps(Action<DiagnosticSession> isolated)
    {
        yield return new WaitForSecondsRealtime(25);
        var args = Environment.GetCommandLineArgs();
        var results = new List<object>();
        var zipResults = new List<object>();
        object? lobby = null;
        if (args.Contains("--skins-probe-host"))
        {
            if (!SteamManager.Initialized) throw new InvalidOperationException("Start and sign in to Steam, then restart the probe.");
            if (Mirror.NetworkServer.active || Mirror.NetworkClient.active) throw new InvalidOperationException("Probe requires a fresh title session.");
            isolated(new DiagnosticSession("SKINS_OUTPUT_PROBE_" + Guid.NewGuid().ToString("N")));
            EOSLobbyManager.StartHostWhenReady();
            var deadline = Time.realtimeSinceStartup + 45;
            while (!Ownership.Local && Time.realtimeSinceStartup < deadline) yield return null;
            Logger.LogInfo("SKINS_HOST_PROBE server=" + Mirror.NetworkServer.active + " client=" + Mirror.NetworkClient.active + " local=" + (bool)Ownership.Local);
            if (Ownership.Local) yield return new WaitForSecondsRealtime(8);
            if (Ownership.Local && args.Contains("--skins-probe-lobby"))
            {
                yield return RuntimeProbe.CreatePrivateLobby(result => { lobby = result; Logger.LogInfo("SKINS_LOBBY_PROBE " + Json.Write(result)); });
            }
        }
        if (args.Contains("--skins-probe-packs") && !args.Contains("--skins-probe-baseline"))
        {
            ReloadList();
            var diagnosticPacks = Diagnostics.ExplicitPack ? new[] { Diagnostics.Select(packs) } : packs.Where(p => p.Error == null && p.Pack != null).ToArray();
            foreach (var entry in diagnosticPacks)
            {
                Diagnostics.RecordManifest(entry);
                var local = Ownership.Local;
                var radius = local ? local!.TopdownRigidbody.MovementCollider.radius : 0;
                yield return Apply(entry);
                yield return new WaitForSecondsRealtime(1);
                var bodySprite = local && local!.TopdownActor.bodyRenderer ? local.TopdownActor.bodyRenderer.sprite : null;
                var frameCheck = RuntimeProbe.CheckBodyFrames(this);
                var pooledEffect = RuntimeProbe.CheckFxReuse(this);
                var beforeFailure = Theme;
                yield return Apply(new PackEntry { Source = Diagnostics.File("missing-probe-pack.zip"), Pack = entry.Pack });
                var failedReloadPreserved = Theme == beforeFailure && Theme?.AssetCount > 0;
                var negativeReloads = new List<object>();
                foreach (var negative in RuntimeProbe.NegativePacks(this, entry))
                {
                    var acceptedSnapshot = false;
                    try { PackReader.Read(negative.Source, Catalog); acceptedSnapshot = true; } catch (InvalidDataException) { }
                    var validTheme = Theme;
                    yield return Apply(new PackEntry { Source = negative.Source, Pack = entry.Pack });
                    negativeReloads.Add(new { kind = negative.Label, acceptedSnapshot, decodeFixture = negative.DecodeFixture,
                        preserved = ReferenceEquals(validTheme, Theme) && Theme?.AssetCount > 0 });
                }
                yield return Apply(entry);
                var repeatedReload = Theme?.Pack.Manifest.Id == entry.Pack!.Manifest.Id;
                RestoreOriginal(); yield return Apply(entry);
                var immediateReapply = Theme?.Pack.Manifest.Id == entry.Pack.Manifest.Id;
                var skinFontIds = (Dictionary<int, int>)AccessTools.Field(typeof(RuntimeTheme), "skinFonts").GetValue(null);
                var fallbacksExcludeSkinFonts = Theme?.Assets.Values.OfType<TMPro.TMP_FontAsset>().All(f => f.fallbackFontAssetTable.All(fallback => fallback && !skinFontIds.ContainsKey(fallback.GetInstanceID()))) ?? false;
                var uiRoundTrip = RuntimeProbe.CheckUiRoundTrip(this);
                var audioSettings = Audio.ProbeSettings();
                object? musicStop = null;
                yield return RuntimeProbe.CheckMusicStop(this, result => musicStop = result);
                object? audioEvents = null;
                yield return RuntimeProbe.CheckAudioEvents(this, value => audioEvents = value);
                SetOpen(true); yield return null; yield return null;
                RuntimeProbe.Capture(Diagnostics.File("selector-" + entry.Pack!.Manifest.Id + ".png"), Logger.LogInfo);
                var selector = RuntimeProbe.CheckSelector(this, SetOpen);
                bodySprite = local?.TopdownActor?.bodyRenderer?.sprite;
                var result = new
                {
                    skin = entry.Pack!.Manifest.Id,
                    applied = Theme?.Pack.Manifest.Id == entry.Pack.Manifest.Id,
                    assets = Theme?.AssetCount ?? 0,
                    audioReplacements = Audio.ActiveReplacements,
                    decodedAudioFormats = Theme?.Pack.Manifest.Resources.Values.Where(r => r.Kind == "audio").Select(r => Path.GetExtension(r.File)).Distinct().ToArray(),
                    local = (bool)local,
                    sprite = bodySprite ? bodySprite!.name : "",
                    collisionRadiusUnchanged = !local || radius == local!.TopdownRigidbody.MovementCollider.radius,
                    frames = frameCheck,
                    pooledEffect,
                    audioSettings,
                    musicStop,
                    audioEvents,
                    failedReloadPreserved,
                    negativeReloads,
                    repeatedReload,
                    immediateReapply,
                    fallbacksExcludeSkinFonts,
                    uiRoundTrip,
                    selector,
                    fonts = Theme?.Assets.Values.OfType<TMPro.TMP_FontAsset>().Select(f => new { name = f.name, fallbacks = f.fallbackFontAssetTable.Count, koreanFallback = f.HasCharacter('가', true) }).ToArray()
                };
                results.Add(result); Logger.LogInfo("SKINS_PACK_PROBE " + Json.Write(result));
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    RuntimeProbe.Capture(Diagnostics.File("probe-" + entry.Pack.Manifest.Id + ".png"), Logger.LogInfo);
                RestoreOriginal();
                var restoredSprite = local?.TopdownActor?.bodyRenderer?.sprite;
                Logger.LogInfo("SKINS_RESTORE_PROBE " + Json.Write(new
                {
                    skin = entry.Pack!.Manifest.Id,
                    themeNull = Theme == null,
                    audioReplacements = Audio.ActiveReplacements,
                    originalBody = !restoredSprite || !restoredSprite!.name.StartsWith(entry.Pack!.Manifest.Id + ":", StringComparison.Ordinal)
                }));
                yield return new WaitForSecondsRealtime(0.2f);
                if (args.Contains("--skins-probe-zip"))
                {
                    var archive = Diagnostics.File("probe-" + entry.Pack.Manifest.Id + ".zip");
                    if (Directory.Exists(entry.Source)) System.IO.Compression.ZipFile.CreateFromDirectory(entry.Source, archive);
                    else File.Copy(entry.Source, archive);
                    yield return Apply(new PackEntry { Source = archive, Pack = entry.Pack });
                    var duplicates = Diagnostics.File("duplicate-ids-" + entry.Pack.Manifest.Id); Directory.CreateDirectory(duplicates);
                    File.Copy(archive, Path.Combine(duplicates, "one.zip")); File.Copy(archive, Path.Combine(duplicates, "two.zip"));
                    var excluded = PackDiscovery.Scan(duplicates, Catalog);
                    zipResults.Add(new { skin = entry.Pack!.Manifest.Id, applied = Theme?.Pack.Manifest.Id == entry.Pack.Manifest.Id, assets = Theme?.AssetCount ?? 0,
                        duplicateIdsExcludeBoth = excluded.Count == 2 && excluded.All(p => p.Error?.StartsWith("Duplicate skin ID:", StringComparison.Ordinal) == true) });
                    RestoreOriginal();
                }
            }
        }
        foreach (var a in Resources.FindObjectsOfTypeAll<Animator2D_Basic>())
            if (a && a.currentSet && Ownership.Role(a) is string role) RuntimeCatalog.Observe(Catalog, a, role);
        RuntimeCatalog.Export(Catalog, Diagnostics.DirectoryPath);
        File.WriteAllText(Diagnostics.File("probe-results.json"), Json.Write(new { packId = Diagnostics.PackId, runId = Diagnostics.RunId, packs = results, zip = zipResults, lobby, errors = errors.ToArray(), server = Mirror.NetworkServer.active, client = Mirror.NetworkClient.active, local = (bool)Ownership.Local }));
        Logger.LogInfo("SKINS_PROBE animations=" + Catalog.Animations.Count + " ui=" + Catalog.Ui.Count + " audio=" + Catalog.Audio.Count + " unsupported=" + unsupported);
    }
    private void OnApplicationQuit() { quitting = true; Cleanup(); }
    private void OnDestroy() { if (!quitting) Cleanup(); }
    private void Cleanup()
    {
        SetOpen(false); StopAllCoroutines(); Visuals.RestoreAll(); Ui.Restore(); Audio.Restore();
        transaction.Restore((_, _) => Theme = null); selector.Dispose();
        harmony?.UnpatchSelf(); Instance = null;
    }
}
