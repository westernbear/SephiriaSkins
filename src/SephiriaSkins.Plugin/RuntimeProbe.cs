using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
namespace SephiriaSkins.Plugin;

// Opt-in diagnostic fixture. It renders cosmetic outputs without playing frame events.
internal static class RuntimeProbe
{
    private static object? lobby;
    public static IEnumerator CreatePrivateLobby(Action<object> done)
    {
        string? error = null;
        try
        {
            var panel = UIManager.Instance.GetElement<UI_MultiplayerPanel>();
            if (!panel) throw new InvalidOperationException("Multiplayer panel unavailable.");
            lobby = AccessTools.Field(typeof(UI_MultiplayerPanel), "networkLobby").GetValue(panel);
            lobby ??= Resources.FindObjectsOfTypeAll<MonoBehaviour>().FirstOrDefault(m => m && m.gameObject.scene.IsValid() && m.GetType().FullName == "HeathenEngineering.SteamworksIntegration.LobbyManager");
            if (lobby == null) throw new InvalidOperationException("Steam lobby manager unavailable.");
            panel.OnCreateButton(); panel.privacyBox.ChangeValue(2);
            panel.lobbyNameInput.text = "Sephiria Skins verification";
            var arguments = lobby.GetType().GetField("createArguments").GetValue(lobby);
            var type = arguments.GetType().GetField("type");
            type.SetValue(arguments, Enum.Parse(type.FieldType, "k_ELobbyTypePrivate"));
            arguments.GetType().GetField("slots").SetValue(arguments, 2);
            lobby.GetType().GetMethod("Create", Type.EmptyTypes).Invoke(lobby, null);
        }
        catch (Exception e) { error = e.GetBaseException().Message; }
        if (error == null)
        {
            var deadline = Time.realtimeSinceStartup + 20;
            while (!(bool)lobby!.GetType().GetProperty("HasLobby").GetValue(lobby) && Time.realtimeSinceStartup < deadline) yield return null;
        }
        var created = error == null && (bool)lobby!.GetType().GetProperty("HasLobby").GetValue(lobby);
        done(new
        {
            created,
            members = created ? (int)lobby!.GetType().GetProperty("MemberCount").GetValue(lobby) : 0,
            privacy = created ? lobby!.GetType().GetProperty("Type").GetValue(lobby).ToString() : "",
            error
        });
    }
    public static void LeaveLobby()
    {
        if (lobby != null && (bool)lobby.GetType().GetProperty("HasLobby").GetValue(lobby)) lobby.GetType().GetMethod("Leave", Type.EmptyTypes).Invoke(lobby, null);
        lobby = null;
    }
    public static object CheckBodyFrames(Plugin plugin)
    {
        var costume = Ownership.Local?.GetComponentInChildren<PlayerAvatarCostume>();
        if (!costume || plugin.Theme == null) return new { checkedFrames = 0, failures = 0, states = 0 };
        var root = new GameObject("SephiriaSkinsProbe"); root.SetActive(false); root.transform.SetParent(costume!.transform, false);
        var renderer = root.AddComponent<SpriteRenderer>();
        var animator = root.AddComponent<Animator2D_SpriteRenderer>(); animator.spriteRenderer = renderer; animator.enabled = false;
        var reflectedObject = new GameObject("Reflection", typeof(SpriteRenderer)); reflectedObject.transform.SetParent(root.transform, false);
        var reflected = reflectedObject.GetComponent<SpriteRenderer>(); reflected.sortingOrder = -7; reflected.flipX = true;
        var multi = root.AddComponent<Animator2D_MultipleSpriteRenderer>(); multi.spriteRenderers = new List<SpriteRenderer> { renderer, reflected }; multi.enabled = false;
        var stateField = AccessTools.Field(typeof(Animator2D_Basic), "currentState");
        var frameField = AccessTools.Field(typeof(Animator2D_Basic), "currentFrameIdx");
        var checkedFrames = 0; var failures = 0; var states = 0; var timelineUnchanged = true; var materialsChecked = 0; var particlesChecked = 0;
        var priorAnimations = new Dictionary<string, Core.CatalogAnimation>(plugin.Catalog.Animations);
        try
        {
            foreach (var set in Resources.FindObjectsOfTypeAll<AnimationSet>().Where(s => s))
                foreach (var state in set.sprites)
                {
                    var key = RuntimeCatalog.AnimationKey("body", set, state);
                    if (!plugin.Theme.Pack.Manifest.Body.TryGetValue(key, out var binding)) continue;
                    states++; animator.currentSet = set; stateField.SetValue(animator, state);
                    multi.currentSet = set; stateField.SetValue(multi, state);
                    var events = string.Join("\n", state.frameEvents.SelectMany(f => f.events.Select(e => f.frame + ":" + e.componentName + "." + e.methodName)));
                    for (var i = 0; i < state.timeline.Count; i++)
                    {
                        var frame = state.timeline[i]; frameField.SetValue(animator, frame.frameIdx);
                        animator.SetSprite(frame.sprite); checkedFrames++;
                        var expected = frame.sprite ? plugin.Theme.Get<Sprite>(binding.Frames[i]) : null;
                        if (renderer.sprite != expected) failures++;
                        frameField.SetValue(multi, frame.frameIdx); multi.SetSprite(frame.sprite);
                        if (renderer.sprite != expected || reflected.sprite != expected || reflected.sortingOrder != -7 || !reflected.flipX) failures++;
                        if (frame.sprite && binding.Material != null)
                        {
                            materialsChecked++;
                            if (renderer.sharedMaterial != plugin.Theme.Get<Material>(binding.Material) || reflected.sharedMaterial != plugin.Theme.Get<Material>(binding.Material)) failures++;
                        }
                        if (frame.sprite && binding.Particle != null)
                        {
                            particlesChecked++;
                            if (!root.GetComponentsInChildren<ParticleSystem>(true).Any()) failures++;
                        }
                    }
                    timelineUnchanged &= key == RuntimeCatalog.AnimationKey("body", set, state) && events == string.Join("\n", state.frameEvents.SelectMany(f => f.events.Select(e => f.frame + ":" + e.componentName + "." + e.methodName)));
                }
        }
        finally
        {
            plugin.Visuals.Forget(root); Object.Destroy(root);
            plugin.Catalog.Animations = priorAnimations;
        }
        return new { checkedFrames, multipleRendererFrames = checkedFrames, materialsChecked, particlesChecked, failures, states, timelineUnchanged };
    }
    public static object CheckSelector(Plugin plugin, Action<bool> setOpen)
    {
        setOpen(true);
        var input = PlayerInputController.Instance;
        var blocked = input && input!.BlockAvatarInput;
        var selector = AccessTools.Field(typeof(Plugin), "selector").GetValue(plugin);
        AccessTools.Method(typeof(NativeSelector), "ShowDetails").Invoke(selector, null);
        var detailsOpened = Resources.FindObjectsOfTypeAll<SkinSelectorMarker>().Count(m => m && m.GetComponent<UI_MessageBox_YesNo>()?.IsOpened == true) == 2;
        setOpen(false);
        var detailsClosed = !Resources.FindObjectsOfTypeAll<SkinSelectorMarker>().Any(m => m && m.GetComponent<UI_MessageBox_YesNo>()?.IsOpened == true);
        return new { avatarInputBlocked = (bool)blocked, closed = !plugin.SelectorOpen, native = Resources.FindObjectsOfTypeAll<SkinSelectorMarker>().Any(m => m), detailsOpened, detailsClosed };
    }
    public static object CheckUiRoundTrip(Plugin plugin)
    {
        var theme = plugin.Theme;
        if (theme == null || theme.Pack.Manifest.Ui.Count == 0) return new { available = false };
        var graphics = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Graphic>().Where(g => g && g.gameObject.scene.IsValid() &&
            !g.GetComponentInParent<SkinSelectorMarker>() && theme.Pack.Manifest.Ui.ContainsKey(RuntimeCatalog.UiKey(g))).ToArray();
        string Snapshot(UnityEngine.UI.Graphic g) => Core.Json.Write(new
        {
            sprite = (g as UnityEngine.UI.Image)?.sprite?.GetInstanceID(), texture = (g as UnityEngine.UI.RawImage)?.texture?.GetInstanceID(),
            font = (g as TMPro.TMP_Text)?.font?.GetInstanceID(), fontMaterial = (g as TMPro.TMP_Text)?.fontSharedMaterial?.GetInstanceID(),
            material = g is TMPro.TMP_Text ? 0 : g.material?.GetInstanceID(),
            imageType = (g as UnityEngine.UI.Image)?.type,
            color = new[] { g.color.r, g.color.g, g.color.b, g.color.a },
            position = new[] { g.rectTransform.anchoredPosition.x, g.rectTransform.anchoredPosition.y },
            size = new[] { g.rectTransform.sizeDelta.x, g.rectTransform.sizeDelta.y },
            fontSize = (g as TMPro.TMP_Text)?.fontSize, text = (g as TMPro.TMP_Text)?.text
        });
        plugin.Ui.Restore();
        var original = graphics.ToDictionary(g => g.GetInstanceID(), Snapshot);
        var texts = graphics.OfType<TMPro.TMP_Text>().ToDictionary(t => t.GetInstanceID(), t => t.text);
        plugin.Ui.Apply(theme);
        var changed = graphics.Count(g => Snapshot(g) != original[g.GetInstanceID()]);
        var textPreserved = graphics.OfType<TMPro.TMP_Text>().All(t => t.text == texts[t.GetInstanceID()]);
        plugin.Ui.Restore();
        var failures = graphics.Where(g => Snapshot(g) != original[g.GetInstanceID()]).Select(RuntimeCatalog.UiKey).ToArray();
        plugin.Ui.Apply(theme);
        return new { available = true, checkedGraphics = graphics.Length, changed, textPreserved, restored = failures.Length == 0, failures = failures.Take(20).ToArray() };
    }
    public static object CheckFxReuse(Plugin plugin)
    {
        if (plugin.Theme == null || !Ownership.Local) return new { available = false };
        var prefab = Resources.FindObjectsOfTypeAll<SpriteFx>().FirstOrDefault(f => f && !f.gameObject.scene.IsValid() && f.animator2D && f.animator2D.currentSet &&
            f.animator2D.currentSet.sprites.Any(s => plugin.Theme.Pack.Manifest.Effects.ContainsKey(RuntimeCatalog.AnimationKey("effect", f.animator2D.currentSet, s))) &&
            f.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is SpriteRenderer || c is Animator2D_Basic || c is SpriteFx));
        if (!prefab) return new { available = false };
        var fixture = Object.Instantiate(prefab!.gameObject); var fx = fixture.GetComponent<SpriteFx>();
        var prior = OwnerContext.Push(Ownership.Local);
        var localReplacement = false; var cleared = false; var original = false;
        try
        {
            fx.OnSpawn();
            var renderer = fx.animator2D.GetComponent<SpriteRenderer>() ?? fx.animator2D.GetComponentInChildren<SpriteRenderer>();
            localReplacement = renderer && renderer.sprite && renderer.sprite.name.StartsWith(plugin.Theme.Pack.Manifest.Id + ":", StringComparison.Ordinal);
            fx.OnDespawn(); cleared = !fx.GetComponent<CosmeticOwner>().Owner;
            OwnerContext.Pop(null); fx.OnSpawn();
            original = renderer && renderer.sprite && !renderer.sprite.name.StartsWith(plugin.Theme.Pack.Manifest.Id + ":", StringComparison.Ordinal);
            fx.OnDespawn();
        }
        finally { OwnerContext.Pop(prior); plugin.Visuals.Forget(fixture); Object.Destroy(fixture); }
        return new { available = true, localReplacement, ownerCleared = cleared, recycledOriginal = original };
    }
    public static IEnumerator CheckMusicStop(Plugin plugin, Action<object> done)
    {
        var key = plugin.Theme?.Pack.Manifest.Audio.FirstOrDefault(p => p.Value.Channel == "music").Key;
        if (key == null) { done(new { available = false }); yield break; }
        var before = plugin.Audio.ActiveReplacements;
        var instance = FMODUnity.RuntimeManager.CreateInstance(plugin.Catalog.Audio[key].Path);
        try
        {
            instance.start();
            var started = plugin.Audio.ActiveReplacements == before + 1;
            yield return new WaitForSecondsRealtime(.25f);
            instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            instance.getVolume(out var volume);
            var originalMutedDuringStop = Mathf.Abs(volume) < .0001f;
            yield return new WaitForSecondsRealtime(.4f); plugin.Audio.Tick();
            done(new { available = true, started, originalMutedDuringStop, replacementStopped = plugin.Audio.ActiveReplacements == before });
        }
        finally { instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); instance.release(); }
    }
    public static void Capture(string path, Action<object> log)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D? pixels = null;
        var canvases = new List<(UnityEngine.Canvas Canvas, RenderMode Mode, Camera Camera, float Distance)>();
        try
        {
            target.Create();
            var cameras = Camera.allCameras.Where(c => c && c.enabled).OrderBy(c => c.depth).ToArray();
            var main = cameras.LastOrDefault();
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>().Where(c => c && c.gameObject.activeInHierarchy && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay))
            {
                canvases.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance));
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = main; canvas.planeDistance = 1;
            }
            Canvas.ForceUpdateCanvases();
            foreach (var camera in cameras)
            {
                var request = new RenderPipeline.StandardRequest { destination = camera.targetTexture ? camera.targetTexture : target };
                if (GraphicsSettings.currentRenderPipeline && RenderPipeline.SupportsRenderRequest(camera, request)) RenderPipeline.SubmitRenderRequest(camera, request);
                else
                {
                    var old = camera.targetTexture; camera.targetTexture = request.destination;
                    try { camera.Render(); } finally { camera.targetTexture = old; }
                }
            }
            RenderTexture.active = target;
            pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(pixels));
            log("SKINS_RENDER_PROBE cameras=" + string.Join(",", cameras.Select(c => c.name)) + "; path=" + path);
        }
        catch (Exception e) { log("SKINS_RENDER_PROBE failed: " + e.Message); }
        finally
        {
            foreach (var item in canvases) if (item.Canvas) { item.Canvas.renderMode = item.Mode; item.Canvas.worldCamera = item.Camera; item.Canvas.planeDistance = item.Distance; }
            RenderTexture.active = previous; if (pixels) Object.Destroy(pixels); target.Release(); Object.Destroy(target);
        }
    }
}
