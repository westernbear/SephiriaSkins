using FMOD;
using FMOD.Studio;
using FMODUnity;
using SephiriaSkins.Core;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;
namespace SephiriaSkins.Plugin;
internal sealed class AudioAdapter
{
    private sealed class Playing
    {
        public EventInstance Instance;
        public string Key = "";
        public UnitAvatar? Owner;
        public Channel Channel;
        public bool HasChannel, Suppressed, Started, IsPaused, Stopping;
        public AudioBinding? Binding;
        public float OriginalVolume = 1;
    }
    private readonly Dictionary<IntPtr, Playing> events = new();
    [ThreadStatic] public static bool InternalWrite;
    public bool Paused;
    public int ActiveReplacements => events.Values.Count(e => e.HasChannel);
    public static string EventKey(GUID guid)
    {
        if (RuntimeManager.StudioSystem.getEventByID(guid, out var description) == RESULT.OK && description.getPath(out var path) == RESULT.OK) return path;
        return "guid:" + guid;
    }
    public void Register(EventInstance instance, GUID guid)
    {
        if (!instance.isValid()) return;
        var key = "guid:" + guid;
        if (events.TryGetValue(instance.handle, out var old)) StopReplacement(old);
        events[instance.handle] = new Playing { Instance = instance, Key = key, Owner = OwnerContext.Current };
        var plugin = Plugin.Instance;
        if (plugin && !plugin!.Catalog.Audio.ContainsKey(key)) plugin.Catalog.Audio[key] = new CatalogAudio { Path = EventKey(guid), ReferencePath = OwnerContext.Current ? RuntimeCatalog.PathOf(OwnerContext.Current!.transform) : "client" };
    }
    public void Attach(EventInstance instance, Transform target)
    {
        if (events.TryGetValue(instance.handle, out var entry)) entry.Owner = Ownership.Owner(target);
    }
    public void Start(EventInstance instance)
    {
        if (!events.TryGetValue(instance.handle, out var entry)) return;
        entry.Started = true; entry.Stopping = false; StopReplacement(entry);
        var theme = Plugin.Instance?.Theme;
        if (theme == null || !theme.Pack.Manifest.Audio.TryGetValue(entry.Key, out var binding) ||
            (binding.Scope == "local" && !Ownership.IsLocal(entry.Owner))) return;
        if (!theme.Sounds.TryGetValue(binding.Resource, out var sound)) throw new InvalidDataException("Use loose WAV/OGG for FMOD audio: " + binding.Resource);
        var result = RuntimeManager.CoreSystem.playSound(sound, default, true, out var channel);
        if (result != RESULT.OK) throw new InvalidDataException("FMOD playback failed: " + result);
        entry.Binding = binding; entry.Channel = channel; entry.HasChannel = true;
        entry.Instance.getVolume(out entry.OriginalVolume);
        entry.Instance.getPitch(out var pitch); channel.setPitch(pitch);
        channel.setMode(binding.Loop ? MODE.LOOP_NORMAL : MODE.LOOP_OFF);
        if (binding.Channel == "music" && channel.getDSPClock(out _, out var clock) == RESULT.OK && RuntimeManager.CoreSystem.getSoftwareFormat(out var sampleRate, out _, out _) == RESULT.OK)
        { channel.addFadePoint(clock, 0); channel.addFadePoint(clock + (ulong)(sampleRate / 5), 1); }
        SetOriginalVolume(entry, 0); entry.Suppressed = true;
        UpdateVolume(entry); channel.setPaused(Paused || entry.IsPaused);
    }
    private static void SetOriginalVolume(Playing entry, float volume)
    {
        InternalWrite = true;
        try { if (entry.Instance.isValid()) entry.Instance.setVolume(volume); }
        finally { InternalWrite = false; }
    }
    public void Volume(EventInstance instance, ref float volume)
    {
        if (!InternalWrite && events.TryGetValue(instance.handle, out var entry) && entry.Suppressed)
        { entry.OriginalVolume = volume; volume = 0; }
    }
    public void Stop(EventInstance instance, STOP_MODE mode)
    {
        if (!events.TryGetValue(instance.handle, out var entry)) return;
        if (mode == STOP_MODE.ALLOWFADEOUT && entry.HasChannel && entry.Channel.getDSPClock(out _, out var clock) == RESULT.OK &&
            RuntimeManager.CoreSystem.getSoftwareFormat(out var sampleRate, out _, out _) == RESULT.OK)
        {
            entry.Stopping = true;
            entry.Channel.removeFadePoints(0,ulong.MaxValue);
            entry.Channel.addFadePoint(clock,1); entry.Channel.addFadePoint(clock + (ulong)(sampleRate / 5),0);
            entry.Channel.setDelay(0,clock + (ulong)(sampleRate / 5),true);
            return;
        }
        StopReplacement(entry); entry.Started = false;
    }
    private static void UpdateVolume(Playing entry)
    {
        if (!entry.HasChannel || entry.Binding == null) return;
        var manager = SoundManager.Instance;
        var master = manager ? manager.GetMasterVolume() : 1;
        var channel = manager ? entry.Binding.Channel == "music" ? manager.GetBGMVolume() : entry.Binding.Channel == "ambience" ? manager.GetAmbienceVolume() : manager.GetFXVolume() : 1;
        entry.Channel.setVolume(Mathf.Clamp01(master * channel * entry.Binding.Volume * entry.OriginalVolume));
    }
    public void Tick()
    {
        foreach (var pair in events.ToArray())
        {
            var entry = pair.Value;
            if (entry.HasChannel)
            {
                if (entry.Binding!.Scope == "local" && !Ownership.IsLocal(entry.Owner)) { StopReplacement(entry); continue; }
                UpdateVolume(entry);
                if (entry.Binding.Loop && !entry.Instance.isValid()) StopReplacement(entry);
                else if (entry.Channel.isPlaying(out var playing) != RESULT.OK || !playing) StopReplacement(entry, false);
            }
            if (!entry.Instance.isValid() && !entry.HasChannel) { events.Remove(pair.Key); continue; }
            if (entry.Started && entry.Instance.isValid() && entry.Instance.getPlaybackState(out var state) == RESULT.OK && state == PLAYBACK_STATE.STOPPED)
            { StopReplacement(entry); events.Remove(pair.Key); }
        }
    }
    private static void StopReplacement(Playing entry, bool restore = true)
    {
        if (restore && entry.Suppressed) { SetOriginalVolume(entry, entry.OriginalVolume); entry.Suppressed = false; }
        if (entry.HasChannel) entry.Channel.stop();
        entry.HasChannel = false; entry.Binding = null;
    }
    public void ThemeChanged()
    {
        foreach (var entry in events.Values) StopReplacement(entry);
        foreach (var entry in events.Values.ToArray())
            if (entry.Started && !entry.Stopping && entry.Instance.isValid() && entry.Instance.getPlaybackState(out var state) == RESULT.OK && state != PLAYBACK_STATE.STOPPED && state != PLAYBACK_STATE.STOPPING &&
                Plugin.Instance?.Theme?.Pack.Manifest.Audio.TryGetValue(entry.Key, out var binding) == true && binding.Loop)
                Start(entry.Instance);
    }
    public void Pause(bool paused)
    {
        Paused = paused;
        foreach (var entry in events.Values) if (entry.HasChannel) entry.Channel.setPaused(paused || entry.IsPaused);
    }
    public void PauseInstance(EventInstance instance, bool paused)
    {
        if (!events.TryGetValue(instance.handle, out var entry)) return;
        entry.IsPaused = paused; if (entry.HasChannel) entry.Channel.setPaused(paused || Paused);
    }
    public void Pitch(EventInstance instance, float pitch)
    {
        if (events.TryGetValue(instance.handle, out var entry) && entry.HasChannel) entry.Channel.setPitch(pitch);
    }
    public void Restore() { foreach (var entry in events.Values) StopReplacement(entry); }
    internal object ProbeSettings()
    {
        var entry = events.Values.FirstOrDefault(e => e.HasChannel);
        if (entry == null || !SoundManager.Instance) return new { available = false };
        var master = SoundManager.Instance.GetMasterVolume();
        var priorPause = Paused;
        entry.Instance.getPitch(out var priorPitch);
        var priorInstancePause = entry.IsPaused;
        var mute = false; var pause = false; var pitch = false; var suppressed = false;
        try
        {
            SoundManager.Instance.SetMasterVolume(0); UpdateVolume(entry);
            entry.Channel.getVolume(out var volume); mute = Mathf.Abs(volume) < .0001f;
            Pause(true); entry.Channel.getPaused(out var paused); pause = paused;
            entry.Instance.setPitch(1.25f); entry.Channel.getPitch(out var rate); pitch = Mathf.Abs(rate - 1.25f) < .0001f;
            entry.Instance.getVolume(out var originalVolume); suppressed = Mathf.Abs(originalVolume) < .0001f;
        }
        finally
        {
            SoundManager.Instance.SetMasterVolume(master); UpdateVolume(entry);
            entry.Instance.setPitch(priorPitch); entry.IsPaused = priorInstancePause; Pause(priorPause);
        }
        return new { available = true, masterMute = mute, pause, pitchTracking = pitch, originalSuppressed = suppressed };
    }
}
