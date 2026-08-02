using System;
using System.Linq;
using Cue2.Base.Classes.Connections;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Godot;
using Godot.Collections;

namespace Cue2.Base.Classes;

/// <summary>
/// How component defaults resolve audio output (standalone audio or video embedded audio).
/// </summary>
public enum ComponentAudioOutputDefaultMode
{
    /// <summary>Use Default Patch when present, otherwise the first available patch.</summary>
    Preferred = 0,
    /// <summary>Use a specific patch by id.</summary>
    Patch = 1,
    /// <summary>Use a named direct output device.</summary>
    Direct = 2,
    /// <summary>No audio output assigned.</summary>
    None = 3
}

/// <summary>
/// How component defaults resolve a video target layer.
/// </summary>
public enum ComponentTargetLayerDefaultMode
{
    /// <summary>Use the first layer in <see cref="DisplaysManager.Layers"/> when any exist.</summary>
    FirstAvailable = 0,
    /// <summary>Use a specific layer by id.</summary>
    Layer = 1,
    /// <summary>No output (TargetLayerId = -1).</summary>
    None = 2
}

/// <summary>
/// Manages storing of settings data
/// Child of GlobalData
/// </summary>
public partial class Settings : Node
{
    private GlobalSignals _globalSignals;
    private GlobalData _globalData;
    private AudioDevices _audioDevices;
    private static Dictionary<int, AudioOutputPatch> _audioOutputPatches = new Dictionary<int, AudioOutputPatch>();
    private DisplaysManager _displaysManager;

    /// <summary>System default UI scale (1.0 = 100%).</summary>
    public const float DefaultUiScale = 1.0f;

    /// <summary>System default Go button scale (1.0 = base).</summary>
    public const float DefaultGoScale = 1.0f;

    /// <summary>System default waveform peak bin count.</summary>
    public const int DefaultWaveformResolution = 4096;

    /// <summary>System default stop fade-out duration in seconds.</summary>
    public const float DefaultStopFadeDuration = 2.5f;

    /// <summary>Default for copying used media into the show folder (Audio/Video/Images).</summary>
    public const bool DefaultMediaBackupEnabled = true;

    /// <summary>Default for multi-edit of shell properties when multiple cues are selected.</summary>
    public const bool DefaultMultiEditEnabled = true;

    /// <summary>Default for selecting a newly created cue after Add / drop.</summary>
    public const bool DefaultSelectNewCues = true;

    /// <summary>Default show mode (false = edit mode; true = live show / cue edits locked).</summary>
    public const bool DefaultShowMode = false;

    /// <summary>Default for drawing audio waveforms inside Timeline Inspector cue bars.</summary>
    public const bool DefaultShowTimelineWaveforms = true;

    /// <summary>Default solid colour behind video layers on all output windows.</summary>
    public static readonly Color DefaultOutputBackgroundColor = Colors.Black;

    /// <summary>Default decode/present quality mode for live video outputs.</summary>
    public static readonly VideoQualityMode DefaultVideoQualityMode = VideoQualityMode.Balanced;

    /// <summary>Default inspector video preview quality.</summary>
    public static readonly VideoPreviewQuality DefaultVideoPreviewQuality = VideoPreviewQuality.Full;

    /// <summary>Default output window vsync / frame-pacing mode.</summary>
    public static readonly OutputVSyncMode DefaultOutputVSyncMode = OutputVSyncMode.PreferVSync;

    /// <summary>Default audio latency / buffer preset.</summary>
    public static readonly AudioLatencyMode DefaultAudioLatencyMode = AudioLatencyMode.Balanced;

    /// <summary>Default de-click ramp after audio start/seek (milliseconds).</summary>
    public const int DefaultAudioDeclickMs = 8;

    /// <summary>Minimum allowed de-click ramp (milliseconds).</summary>
    public const int MinAudioDeclickMs = 0;

    /// <summary>Maximum allowed de-click ramp (milliseconds).</summary>
    public const int MaxAudioDeclickMs = 50;

    /// <summary>Default session master volume (linear 1.0 = 0 dB / 100%).</summary>
    public const float DefaultAudioMasterVolume = 1f;

    // ── Cue shell defaults (system factory values) ─────────────────────────

    /// <summary>System default pre-wait in seconds for newly created cues.</summary>
    public const double SystemDefaultCuePreWait = 0.0;

    /// <summary>System default post-wait in seconds for newly created cues.</summary>
    public const double SystemDefaultCuePostWait = 0.0;

    /// <summary>System default continue mode for newly created cues.</summary>
    public const FollowType SystemDefaultCueFollow = FollowType.None;

    /// <summary>System default shell colour for newly created cues.</summary>
    public static readonly Color SystemDefaultCueColor = new Color(0f, 0f, 0f, 1.0f);

    /// <summary>System default armed state for newly created cues.</summary>
    public const bool SystemDefaultCueArmed = true;

    /// <summary>System default skip-if-disarmed for newly created cues.</summary>
    public const bool SystemDefaultCueSkipIfDisarmed = false;

    // ── Audio component defaults (system factory values) ───────────────────

    /// <summary>System default linear volume for new audio components (1.0 = 0 dB).</summary>
    public const double SystemDefaultAudioVolume = 1.0;

    /// <summary>System default stereo pan for new audio components (0 = center).</summary>
    public const float SystemDefaultAudioPan = 0f;

    /// <summary>System default loop for new audio components.</summary>
    public const bool SystemDefaultAudioLoop = false;

    /// <summary>System default play count for new audio components.</summary>
    public const int SystemDefaultAudioPlayCount = 1;

    /// <summary>System default fade-in seconds for new audio components.</summary>
    public const double SystemDefaultAudioFadeIn = 0.0;

    /// <summary>System default fade-out seconds for new audio components.</summary>
    public const double SystemDefaultAudioFadeOut = 0.0;

    /// <summary>System default audio output mode for new audio components.</summary>
    public const ComponentAudioOutputDefaultMode SystemDefaultAudioOutputMode =
        ComponentAudioOutputDefaultMode.Preferred;

    // ── Video component defaults (system factory values) ───────────────────

    /// <summary>System default TextureRect expand mode for new video components.</summary>
    public const TextureRect.ExpandModeEnum SystemDefaultVideoExpandMode =
        TextureRect.ExpandModeEnum.IgnoreSize;

    /// <summary>System default TextureRect stretch mode for new video components.</summary>
    public const TextureRect.StretchModeEnum SystemDefaultVideoStretchMode =
        TextureRect.StretchModeEnum.KeepAspectCentered;

    /// <summary>System default opacity for new video components (0–1).</summary>
    public const float SystemDefaultVideoOpacity = 1f;

    /// <summary>System default loop for new video components.</summary>
    public const bool SystemDefaultVideoLoop = false;

    /// <summary>System default play count for new video components.</summary>
    public const int SystemDefaultVideoPlayCount = 1;

    /// <summary>System default use-embedded-audio for new video components.</summary>
    public const bool SystemDefaultVideoUseAudio = true;

    /// <summary>System default linear audio volume for new video components.</summary>
    public const float SystemDefaultVideoAudioVolume = 1f;

    /// <summary>System default stereo pan for new video components.</summary>
    public const float SystemDefaultVideoPan = 0f;

    /// <summary>System default fade-in seconds for new video components.</summary>
    public const double SystemDefaultVideoFadeIn = 0.0;

    /// <summary>System default fade-out seconds for new video components.</summary>
    public const double SystemDefaultVideoFadeOut = 0.0;

    /// <summary>System default still-image hold duration (0 = until stopped).</summary>
    public const double SystemDefaultVideoImageDuration = 0.0;

    /// <summary>System default embedded-audio output mode for new video components.</summary>
    public const ComponentAudioOutputDefaultMode SystemDefaultVideoOutputMode =
        ComponentAudioOutputDefaultMode.Preferred;

    /// <summary>System default target-layer mode for new video components.</summary>
    public const ComponentTargetLayerDefaultMode SystemDefaultVideoTargetLayerMode =
        ComponentTargetLayerDefaultMode.FirstAvailable;

    // ── Text component defaults (system factory values) ────────────────────

    /// <summary>System default hold duration for new text components (0 = until stopped).</summary>
    public const double SystemDefaultTextDuration = 0.0;

    /// <summary>System default opacity for new text components (0–1).</summary>
    public const float SystemDefaultTextOpacity = 1f;

    /// <summary>System default BBCode flag for new text components.</summary>
    public const bool SystemDefaultTextUseBbcode = false;

    /// <summary>System default font size for new text components.</summary>
    public const int SystemDefaultTextFontSize = 48;

    /// <summary>System default system font family name (empty = theme default).</summary>
    public const string SystemDefaultTextFontName = "";

    /// <summary>System default font colour for new text components.</summary>
    public static readonly Color SystemDefaultTextFontColor = Colors.White;

    /// <summary>System default horizontal alignment for new text components.</summary>
    public const HorizontalAlignment SystemDefaultTextHAlign = HorizontalAlignment.Center;

    /// <summary>System default vertical alignment for new text components.</summary>
    public const VerticalAlignment SystemDefaultTextVAlign = VerticalAlignment.Center;

    /// <summary>System default autowrap for new text components.</summary>
    public const bool SystemDefaultTextAutowrap = true;

    /// <summary>System default margins (px) for new text components.</summary>
    public const int SystemDefaultTextMargins = 16;

    /// <summary>System default outline size for new text components.</summary>
    public const int SystemDefaultTextOutlineSize = 0;

    /// <summary>System default outline colour for new text components.</summary>
    public static readonly Color SystemDefaultTextOutlineColor = Colors.Black;

    /// <summary>System default background-enabled for new text components.</summary>
    public const bool SystemDefaultTextBackgroundEnabled = false;

    /// <summary>System default background colour for new text components.</summary>
    public static readonly Color SystemDefaultTextBackgroundColor = new Color(0f, 0f, 0f, 0.55f);

    /// <summary>System default fade-in seconds for new text components.</summary>
    public const double SystemDefaultTextFadeIn = 0.0;

    /// <summary>System default fade-out seconds for new text components.</summary>
    public const double SystemDefaultTextFadeOut = 0.0;

    /// <summary>System default target-layer mode for new text components.</summary>
    public const ComponentTargetLayerDefaultMode SystemDefaultTextTargetLayerMode =
        ComponentTargetLayerDefaultMode.FirstAvailable;

    public float UiScale = DefaultUiScale;
    public float GoScale = DefaultGoScale;
    public int WaveformResolution = DefaultWaveformResolution;

    /// <summary>
    /// Global stop fade-out duration in seconds (first Stop fades; second Stop hard-cuts).
    /// 0 = immediate stop. Persisted with the session.
    /// </summary>
    public float StopFadeDuration = DefaultStopFadeDuration;

    /// <summary>
    /// When true, used media files are copied into the show folder (Audio/Video/Images)
    /// so the show can be moved between machines with relative media paths.
    /// Persisted with the showfile.
    /// </summary>
    public bool MediaBackupEnabled = DefaultMediaBackupEnabled;

    /// <summary>
    /// When true and multiple cues are selected, the Shell Inspector enters multi-edit mode
    /// (blank fields; edits apply to every selected cue). When false, only the last-focused
    /// cue is shown/edited. Persisted with the showfile.
    /// </summary>
    public bool MultiEditEnabled = DefaultMultiEditEnabled;

    /// <summary>
    /// When true, newly created cues (Add Cue, media drop) become the selection/focus.
    /// When false, selection is left unchanged. Persisted with the showfile.
    /// </summary>
    public bool SelectNewCues = DefaultSelectNewCues;

    /// <summary>
    /// When true, the UI is in Show Mode: cue/cuelist editing is locked (inspectors hidden,
    /// shell inline edits disabled, structural cue ops blocked). When false (default), Edit Mode.
    /// Persisted with the showfile. Not tracked by undo/redo.
    /// </summary>
    public bool ShowMode = DefaultShowMode;

    /// <summary>
    /// True when cue and cuelist document edits must be blocked (Show Mode active).
    /// </summary>
    public bool IsCueEditingLocked => ShowMode;

    /// <summary>
    /// When true, the Timeline Inspector draws available audio waveforms inside cue bars.
    /// When false, bars show solid colour only. Persisted with the showfile.
    /// </summary>
    public bool ShowTimelineWaveforms = DefaultShowTimelineWaveforms;

    /// <summary>
    /// Solid colour drawn behind all layers on every video output window.
    /// Persisted with the showfile. Does not affect the inspector video previewer.
    /// </summary>
    public Color OutputBackgroundColor = DefaultOutputBackgroundColor;

    /// <summary>
    /// Soft decode/present quality for live video outputs (prefetch ring, lateness drop).
    /// Persisted with the showfile.
    /// </summary>
    public VideoQualityMode VideoQualityMode = DefaultVideoQualityMode;

    /// <summary>
    /// Inspector video preview resolution scale (never affects house outputs).
    /// Persisted with the showfile.
    /// </summary>
    public VideoPreviewQuality VideoPreviewQuality = DefaultVideoPreviewQuality;

    /// <summary>
    /// Vsync / frame-pacing preference for video output windows.
    /// Persisted with the showfile.
    /// </summary>
    public OutputVSyncMode OutputVSyncMode = DefaultOutputVSyncMode;

    /// <summary>
    /// Soft audio fill/prefetch latency mode for standalone and embedded audio.
    /// Persisted with the showfile.
    /// </summary>
    public AudioLatencyMode AudioLatencyMode = DefaultAudioLatencyMode;

    /// <summary>
    /// Raised-cosine de-click ramp after audio start/seek, in milliseconds (0 = off).
    /// Persisted with the showfile.
    /// </summary>
    public int AudioDeclickMs = DefaultAudioDeclickMs;

    /// <summary>
    /// Session master volume applied to all cue audio (linear 0–1). Persisted with the showfile.
    /// Runtime mute is separate (see <see cref="AudioDevices.SessionMasterMuted"/>).
    /// </summary>
    public float AudioMasterVolume = DefaultAudioMasterVolume;

    /// <summary>
    /// Resolved present tuning for the current <see cref="VideoQualityMode"/>.
    /// </summary>
    public VideoPresentTuning GetVideoPresentTuning() =>
        VideoPresentTuning.ForMode(VideoQualityMode);

    /// <summary>
    /// Resolved audio fill/prefetch/declick tuning for the current latency mode and declick ms.
    /// </summary>
    public AudioPresentTuning GetAudioPresentTuning() =>
        AudioPresentTuning.ForMode(AudioLatencyMode, AudioDeclickMs);

    // ── Cue shell defaults (show-scoped; applied to newly created cues) ─────

    /// <summary>Default pre-wait (seconds) applied when a new cue is created.</summary>
    public double CueDefaultPreWait = SystemDefaultCuePreWait;

    /// <summary>Default post-wait (seconds) applied when a new cue is created.</summary>
    public double CueDefaultPostWait = SystemDefaultCuePostWait;

    /// <summary>Default continue mode applied when a new cue is created.</summary>
    public FollowType CueDefaultFollow = SystemDefaultCueFollow;

    /// <summary>Default shell colour applied when a new cue is created.</summary>
    public Color CueDefaultColor = SystemDefaultCueColor;

    /// <summary>Default armed state applied when a new cue is created.</summary>
    public bool CueDefaultArmed = SystemDefaultCueArmed;

    /// <summary>Default skip-if-disarmed applied when a new cue is created.</summary>
    public bool CueDefaultSkipIfDisarmed = SystemDefaultCueSkipIfDisarmed;

    // ── Audio component defaults (show-scoped; applied on new AudioComponent) ─

    /// <summary>Default linear volume (0–1) for new audio components.</summary>
    public double AudioDefaultVolume = SystemDefaultAudioVolume;

    /// <summary>Default stereo pan (−1…+1) for new audio components.</summary>
    public float AudioDefaultPan = SystemDefaultAudioPan;

    /// <summary>Default loop flag for new audio components.</summary>
    public bool AudioDefaultLoop = SystemDefaultAudioLoop;

    /// <summary>Default play count for new audio components.</summary>
    public int AudioDefaultPlayCount = SystemDefaultAudioPlayCount;

    /// <summary>Default fade-in duration (seconds) for new audio components.</summary>
    public double AudioDefaultFadeIn = SystemDefaultAudioFadeIn;

    /// <summary>Default fade-out duration (seconds) for new audio components.</summary>
    public double AudioDefaultFadeOut = SystemDefaultAudioFadeOut;

    /// <summary>Default audio output mode for new audio components.</summary>
    public ComponentAudioOutputDefaultMode AudioDefaultOutputMode = SystemDefaultAudioOutputMode;

    /// <summary>Default patch id when <see cref="AudioDefaultOutputMode"/> is <see cref="ComponentAudioOutputDefaultMode.Patch"/>.</summary>
    public int AudioDefaultPatchId = -1;

    /// <summary>Default direct device name when mode is <see cref="ComponentAudioOutputDefaultMode.Direct"/>.</summary>
    public string AudioDefaultDirectOutput = string.Empty;

    // ── Video component defaults (show-scoped; applied on new VideoComponent) ─

    /// <summary>Default TextureRect expand mode for new video components.</summary>
    public TextureRect.ExpandModeEnum VideoDefaultExpandMode = SystemDefaultVideoExpandMode;

    /// <summary>Default TextureRect stretch mode for new video components.</summary>
    public TextureRect.StretchModeEnum VideoDefaultStretchMode = SystemDefaultVideoStretchMode;

    /// <summary>Default opacity (0–1) for new video components.</summary>
    public float VideoDefaultOpacity = SystemDefaultVideoOpacity;

    /// <summary>Default loop flag for new video components.</summary>
    public bool VideoDefaultLoop = SystemDefaultVideoLoop;

    /// <summary>Default play count for new video components.</summary>
    public int VideoDefaultPlayCount = SystemDefaultVideoPlayCount;

    /// <summary>Default use-embedded-audio for new video (non-image) components.</summary>
    public bool VideoDefaultUseAudio = SystemDefaultVideoUseAudio;

    /// <summary>Default linear audio volume for new video components.</summary>
    public float VideoDefaultAudioVolume = SystemDefaultVideoAudioVolume;

    /// <summary>Default stereo pan for new video components.</summary>
    public float VideoDefaultPan = SystemDefaultVideoPan;

    /// <summary>Default fade-in duration (seconds) for new video components.</summary>
    public double VideoDefaultFadeIn = SystemDefaultVideoFadeIn;

    /// <summary>Default fade-out duration (seconds) for new video components.</summary>
    public double VideoDefaultFadeOut = SystemDefaultVideoFadeOut;

    /// <summary>Default still-image hold duration in seconds (0 = until stopped).</summary>
    public double VideoDefaultImageDuration = SystemDefaultVideoImageDuration;

    /// <summary>Default embedded-audio output mode for new video components.</summary>
    public ComponentAudioOutputDefaultMode VideoDefaultOutputMode = SystemDefaultVideoOutputMode;

    /// <summary>Default patch id when <see cref="VideoDefaultOutputMode"/> is Patch.</summary>
    public int VideoDefaultPatchId = -1;

    /// <summary>Default direct device name when video audio mode is Direct.</summary>
    public string VideoDefaultDirectOutput = string.Empty;

    /// <summary>Default target-layer mode for new video components.</summary>
    public ComponentTargetLayerDefaultMode VideoDefaultTargetLayerMode =
        SystemDefaultVideoTargetLayerMode;

    /// <summary>Default layer id when <see cref="VideoDefaultTargetLayerMode"/> is Layer.</summary>
    public int VideoDefaultTargetLayerId = -1;

    // ── Text component defaults (show-scoped; applied on new TextComponent) ──

    /// <summary>Default hold duration (seconds) for new text components (0 = until stopped).</summary>
    public double TextDefaultDuration = SystemDefaultTextDuration;

    /// <summary>Default opacity (0–1) for new text components.</summary>
    public float TextDefaultOpacity = SystemDefaultTextOpacity;

    /// <summary>Default BBCode interpretation for new text components.</summary>
    public bool TextDefaultUseBbcode = SystemDefaultTextUseBbcode;

    /// <summary>Default font size (px) for new text components.</summary>
    public int TextDefaultFontSize = SystemDefaultTextFontSize;

    /// <summary>Default system font family name for new text components (empty = theme).</summary>
    public string TextDefaultFontName = SystemDefaultTextFontName;

    /// <summary>Default font colour for new text components.</summary>
    public Color TextDefaultFontColor = SystemDefaultTextFontColor;

    /// <summary>Default horizontal alignment for new text components.</summary>
    public HorizontalAlignment TextDefaultHAlign = SystemDefaultTextHAlign;

    /// <summary>Default vertical alignment for new text components.</summary>
    public VerticalAlignment TextDefaultVAlign = SystemDefaultTextVAlign;

    /// <summary>Default autowrap for new text components.</summary>
    public bool TextDefaultAutowrap = SystemDefaultTextAutowrap;

    /// <summary>Default margins (px) for new text components.</summary>
    public int TextDefaultMargins = SystemDefaultTextMargins;

    /// <summary>Default outline size (px) for new text components.</summary>
    public int TextDefaultOutlineSize = SystemDefaultTextOutlineSize;

    /// <summary>Default outline colour for new text components.</summary>
    public Color TextDefaultOutlineColor = SystemDefaultTextOutlineColor;

    /// <summary>Default background panel enabled for new text components.</summary>
    public bool TextDefaultBackgroundEnabled = SystemDefaultTextBackgroundEnabled;

    /// <summary>Default background panel colour for new text components.</summary>
    public Color TextDefaultBackgroundColor = SystemDefaultTextBackgroundColor;

    /// <summary>Default fade-in duration (seconds) for new text components.</summary>
    public double TextDefaultFadeIn = SystemDefaultTextFadeIn;

    /// <summary>Default fade-out duration (seconds) for new text components.</summary>
    public double TextDefaultFadeOut = SystemDefaultTextFadeOut;

    /// <summary>Default target-layer mode for new text components.</summary>
    public ComponentTargetLayerDefaultMode TextDefaultTargetLayerMode =
        SystemDefaultTextTargetLayerMode;

    /// <summary>Default layer id when <see cref="TextDefaultTargetLayerMode"/> is Layer.</summary>
    public int TextDefaultTargetLayerId = -1;

    public bool VerbosePrint = true;
    
    
    // Cuelight settings
    public Color CueLightIdleColour = new Color(0f, 0f, 0.1f, 1f);
    public Color CueLightGoColour = new Color(0f, 1f, 0f, 1f);
    public Color CueLightStandbyColour = new Color(1f, 0.4f, 0f, 1f);
    public Color CueLightCountInColour = new Color(1f, 0f, 0f, 1f);
    public byte CueLightBrightness = 50;
    
    
    /// <summary>Factory name for the audio output patch created on new sessions.</summary>
    public const string DefaultAudioPatchName = "Default Patch";

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        _displaysManager = GetNode<DisplaysManager>("/root/DisplaysManager");

        // First launch / empty show: ensure a Default Patch exists (same as DisplaysManager defaults).
        // Deferred so AudioDevices autoload _Ready and SDL device enumeration are fully available.
        CallDeferred(nameof(EnsureDefaultAudioPatch));
    }

    public override void _ExitTree()
    {
        // Ensure all AudioOutputPatch GodotObjects (which are never added to the scene tree)
        // are explicitly Freed on shutdown to prevent leaks. They are owned by this static
        // collection and the audio patch settings UI.
        foreach (var patch in _audioOutputPatches.Values.ToList())
        {
            if (patch != null && GodotObject.IsInstanceValid(patch))
            {
                patch.Free();
            }
        }
        _audioOutputPatches.Clear();
    }
    
    public Dictionary<int, AudioOutputPatch> GetAudioOutputPatches() => _audioOutputPatches;
    
    
    public void UpdatePatch(AudioOutputPatch patch)
    {
        _audioOutputPatches[patch.Id] = patch;
        GD.Print($"Settings:UpdatePatch - Updated patch with id: {patch.Id} and name: {patch.Name}");
    }

    public void DeletePatch(int patchId)
    {
        _audioOutputPatches[patchId].Free();
        _audioOutputPatches.Remove(patchId);
        // Shell ✕ / media health depends on patch existence
        GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
    } 
    
    public AudioOutputPatch CreateNewPatch()
    {
        var newPatch = new AudioOutputPatch();
        _audioOutputPatches.Add(newPatch.Id, newPatch);
        return newPatch;
    }

    /// <summary>
    /// Creates the session "Default Patch", optionally routing the current system default playback device.
    /// </summary>
    /// <returns>The newly created patch.</returns>
    /// <remarks>
    /// Stereo patch channels Left/Right are 1:1 mapped to device outputs 0/1 when available.
    /// On mono devices both channels route to output 0. If the system default cannot be resolved
    /// or opened, the patch is still created empty so the user can assign a device later.
    /// </remarks>
    public AudioOutputPatch CreateDefaultAudioPatch()
    {
        var patch = new AudioOutputPatch(DefaultAudioPatchName);
        _audioOutputPatches.Add(patch.Id, patch);

        TryRouteSystemDefaultDevice(patch);

        GD.Print($"Settings:CreateDefaultAudioPatch - Created '{patch.Name}' (id={patch.Id}), " +
                 $"devices={patch.OutputDevices.Count}");
        return patch;
    }

    /// <summary>
    /// Ensures at least one audio output patch exists by creating <see cref="DefaultAudioPatchName"/> when empty.
    /// </summary>
    /// <remarks>
    /// Used on first launch and after resets. Does nothing when patches already exist (loaded show, etc.).
    /// </remarks>
    public void EnsureDefaultAudioPatch()
    {
        if (_audioOutputPatches.Count > 0)
            return;

        CreateDefaultAudioPatch();
        GD.Print("Settings:EnsureDefaultAudioPatch - No patches present; created Default Patch.");
    }

    /// <summary>
    /// Returns the preferred audio output patch for newly created media cues.
    /// </summary>
    /// <returns>
    /// Patch named <see cref="DefaultAudioPatchName"/> when present; otherwise the lowest-id patch;
    /// or <c>null</c> when no patches exist.
    /// </returns>
    public AudioOutputPatch GetPreferredAudioOutputPatch()
    {
        if (_audioOutputPatches.Count == 0)
            return null;

        foreach (var patch in _audioOutputPatches.Values)
        {
            if (patch != null && GodotObject.IsInstanceValid(patch) &&
                string.Equals(patch.Name, DefaultAudioPatchName, StringComparison.Ordinal))
            {
                return patch;
            }
        }

        return _audioOutputPatches
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .FirstOrDefault(p => p != null && GodotObject.IsInstanceValid(p));
    }

    /// <summary>
    /// Opens the system default playback device (if any) and wires it into <paramref name="patch"/>
    /// with a simple stereo default route.
    /// </summary>
    private void TryRouteSystemDefaultDevice(AudioOutputPatch patch)
    {
        if (patch == null || _audioDevices == null)
            return;

        string deviceName = _audioDevices.GetSystemDefaultPlaybackDeviceName();
        if (string.IsNullOrEmpty(deviceName))
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                "Default Patch created without a system output device (none detected).", 1);
            return;
        }

        // Prefer a name that appears in the current playback enumeration so open-by-name succeeds.
        var available = _audioDevices.GetAvailableAudioDeviceNames();
        if (available != null && available.Count > 0 &&
            !available.Contains(deviceName, StringComparer.Ordinal))
        {
            // Case-insensitive match (Windows device names can vary slightly).
            var match = available.FirstOrDefault(n =>
                string.Equals(n, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                deviceName = match;
            else
            {
                GD.Print($"Settings:TryRouteSystemDefaultDevice - Default name '{deviceName}' " +
                         "not in available list; attempting open anyway.");
            }
        }

        var device = _audioDevices.OpenAudioDevice(deviceName, out string error);
        if (device == null)
        {
            _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
                $"Default Patch: could not open system output '{deviceName}': {error}", 1);
            return;
        }

        int outputCount = Math.Max(1, device.Channels);
        patch.AddDeviceOutputs(deviceName, outputCount);

        // Default stereo channels (constructor order: Left=0, Right=1).
        var channelIds = patch.Channels.Keys.OrderBy(k => k).ToList();
        if (channelIds.Count > 0 && outputCount > 0)
            patch.SetRouting(deviceName, 0, channelIds[0], true);

        if (channelIds.Count > 1)
        {
            // Stereo device: Right → output 1. Mono: fold Right onto output 0.
            int rightOut = outputCount > 1 ? 1 : 0;
            patch.SetRouting(deviceName, rightOut, channelIds[1], true);
        }

        _globalSignals?.EmitSignal(nameof(GlobalSignals.Log),
            $"Default Patch routed to system output: {deviceName}", 0);
    }
    
    public AudioOutputPatch GetPatch(int patchId) => _audioOutputPatches[patchId];

    public void AddPatch(AudioOutputPatch patch)
    {
        if (_audioOutputPatches.ContainsKey(patch.Id))
        {
            GD.PrintErr($"Settings:AddPatch - Patch ID already exists: {patch.Id}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
                $"Settings:AddPatch - Failed to add patch due to duplicate ID: {patch.Id}", 2);
            return;
        }
        _audioOutputPatches.Add(patch.Id, patch);
        GD.Print($"Settings:AddPatch - Added patch with ID: {patch.Id} and name: {patch.Name}");
        
        // Double check audio devices in added patch are opened. 
        foreach (var device in patch.OutputDevices)
        {
            _audioDevices.OpenAudioDevice(device.Key, out var _);
        }
        // Note: callers that bulk-add patches (load/history) should recheck once afterward;
        // single interactive deletes recheck in DeletePatch.
    }

    private void PrintPatches()
    {
        foreach (var patch in _audioOutputPatches)
        {
            foreach (var channels in patch.Value.Channels)
            {
                GD.Print($"Settings:PrintPatches - ID: {patch.Key} Name: {patch.Value.Name} Channel: {channels.Key} Name: {channels.Value}");
            }
        }
    }
    
    
    // Save and loads
    /// <summary>
    /// Resets all show/session settings to factory defaults (New Session / before Open).
    /// </summary>
    /// <remarks>
    /// Clears audio patches then seeds a <see cref="DefaultAudioPatchName"/> (system playback
    /// device when available). Also resets displays (canvas/layers/screens), cue lights,
    /// OSC listen/connections, and general scalars. Does <b>not</b> reset Input Map — that
    /// lives in user preferences. Emits scale/display signals so live UI can resync.
    /// When used as the wipe step before Open, <see cref="LoadSettings"/> replaces the seeded
    /// Default Patch with the showfile's patch table.
    /// </remarks>
    public void ResetSettings()
    {
        // Audio output patches
        foreach (var patch in _audioOutputPatches.Values.ToList())
        {
            if (patch != null && GodotObject.IsInstanceValid(patch))
                patch.Free();
        }
        _audioOutputPatches.Clear();

        // Seed a Default Patch (system playback device when available) so new cues can play out.
        CreateDefaultAudioPatch();

        // General show scalars
        UiScale = DefaultUiScale;
        GoScale = DefaultGoScale;
        WaveformResolution = DefaultWaveformResolution;
        StopFadeDuration = DefaultStopFadeDuration;
        MediaBackupEnabled = DefaultMediaBackupEnabled;
        MultiEditEnabled = DefaultMultiEditEnabled;
        SelectNewCues = DefaultSelectNewCues;
        ShowMode = DefaultShowMode;
        ShowTimelineWaveforms = DefaultShowTimelineWaveforms;
        OutputBackgroundColor = DefaultOutputBackgroundColor;
        VideoQualityMode = DefaultVideoQualityMode;
        VideoPreviewQuality = DefaultVideoPreviewQuality;
        OutputVSyncMode = DefaultOutputVSyncMode;
        AudioLatencyMode = DefaultAudioLatencyMode;
        AudioDeclickMs = DefaultAudioDeclickMs;
        AudioMasterVolume = DefaultAudioMasterVolume;
        VerbosePrint = true;

        // Operator runtime video controls should never carry across New Session.
        _displaysManager?.ClearRuntimeOutputControls();
        _displaysManager?.ApplyOutputBackgroundColor(OutputBackgroundColor);
        _displaysManager?.ApplyOutputVSyncPreference();
        _audioDevices?.SyncSessionMasterFromSettings();

        // Cue shell defaults for newly created cues
        ResetCueDefaultsToSystem();
        ResetAudioDefaultsToSystem();
        ResetVideoDefaultsToSystem();
        ResetTextDefaultsToSystem();

        // Cue light appearance defaults
        CueLightIdleColour = new Color(0f, 0f, 0.1f, 1f);
        CueLightGoColour = new Color(0f, 1f, 0f, 1f);
        CueLightStandbyColour = new Color(1f, 0.4f, 0f, 1f);
        CueLightCountInColour = new Color(1f, 0f, 0f, 1f);
        CueLightBrightness = 50;

        // Input Map is user-scoped (UserDataManager) — leave live bindings alone on New Session.

        // Displays / canvas editor model
        _displaysManager?.ResetToDefaults();

        // Cue lights registry
        _globalData?.CueLightManager?.Reset();

        // OSC
        GetNodeOrNull<OscListen>("/root/OscListen")?.ResetToDefaults();
        GetNodeOrNull<OscConnections>("/root/OscConnections")?.ClearAll();

        // MIDI session inputs
        GetNodeOrNull<MidiManager>("/root/MidiManager")?.ResetToDefaults();

        // Notify live UI (settings general, GO scale, show mode, etc.)
        _globalSignals?.EmitSignal(nameof(GlobalSignals.UiScaleChanged), UiScale);
        _globalSignals?.EmitSignal(nameof(GlobalSignals.GoScaleChanged), GoScale);
        NotifyShowModeChanged();

        GD.Print("Settings:ResetSettings - Show settings restored to defaults.");
    }

    /// <summary>
    /// Broadcasts the current <see cref="ShowMode"/> so title bar, inspectors, and shell bars resync.
    /// Does not push undo history.
    /// </summary>
    public void NotifyShowModeChanged()
    {
        _globalSignals?.EmitSignal(nameof(GlobalSignals.ShowModeChanged), ShowMode);
    }

    /// <summary>
    /// Sets <see cref="ShowMode"/> and notifies listeners. No-ops when the value is unchanged
    /// (unless <paramref name="forceNotify"/> is true). Not tracked by undo/redo.
    /// </summary>
    /// <param name="enabled">True for Show Mode; false for Edit Mode.</param>
    /// <param name="forceNotify">When true, always emit even if the value did not change.</param>
    public void SetShowMode(bool enabled, bool forceNotify = false)
    {
        if (ShowMode == enabled && !forceNotify)
            return;
        ShowMode = enabled;
        NotifyShowModeChanged();
        GD.Print($"Settings:SetShowMode - ShowMode={ShowMode}");
    }

    public Dictionary GetData()
    {
        var saveTable = new Dictionary();
        var patchTable = new Dictionary();

        foreach (var patch in _audioOutputPatches)
        {
            patchTable.Add(patch.Key, patch.Value.GetData());
        }

        var devices = _audioDevices.GetOpenAudioDevicesNames();

        saveTable.Add("AudioPatch", patchTable);
        saveTable.Add("AudioDevices", devices);
        saveTable.Add("Displays", _displaysManager.GetData());
        saveTable.Add("CueLights", _globalData.CueLightManager.GetData());
        
        saveTable.Add("UiScale", UiScale);
        saveTable.Add("GoScale", GoScale);
        saveTable.Add("WaveformResolution", WaveformResolution);
        saveTable.Add("StopFadeDuration", StopFadeDuration);
        saveTable.Add("MediaBackupEnabled", MediaBackupEnabled);
        saveTable.Add("MultiEditEnabled", MultiEditEnabled);
        saveTable.Add("SelectNewCues", SelectNewCues);
        saveTable.Add("ShowMode", ShowMode);
        saveTable.Add("ShowTimelineWaveforms", ShowTimelineWaveforms);
        saveTable.Add("OutputBackgroundColor", OutputBackgroundColor.ToHtml(true));
        saveTable.Add("VideoQualityMode", (int)VideoQualityMode);
        saveTable.Add("VideoPreviewQuality", (int)VideoPreviewQuality);
        saveTable.Add("OutputVSyncMode", (int)OutputVSyncMode);
        saveTable.Add("AudioLatencyMode", (int)AudioLatencyMode);
        saveTable.Add("AudioDeclickMs", AudioDeclickMs);
        saveTable.Add("AudioMasterVolume", AudioMasterVolume);

        // Cue shell defaults (show-scoped)
        saveTable.Add("CueDefaults", CaptureCueDefaultsDict());
        saveTable.Add("AudioDefaults", CaptureAudioDefaultsDict());
        saveTable.Add("VideoDefaults", CaptureVideoDefaultsDict());
        saveTable.Add("TextDefaults", CaptureTextDefaultsDict());
        
        // Cuelights
        saveTable.Add("CueLightIdleColour", CueLightIdleColour.ToHtml());
        saveTable.Add("CueLightGoColour", CueLightGoColour.ToHtml());
        saveTable.Add("CueLightStandbyColour", CueLightStandbyColour.ToHtml());
        saveTable.Add("CueLightCountInColour", CueLightCountInColour.ToHtml());
        saveTable.Add("CueLightBrightness", CueLightBrightness);
        
        // Osc Listen
        var oscListen = GetNodeOrNull<OscListen>("/root/OscListen");
        if (oscListen != null)
        {
            saveTable.Add("OscListen", oscListen.GetData());
            // Project-action OSC bindings (Go, Save, …) — show-scoped, separate undo slice.
            saveTable.Add("OscInputMap", oscListen.GetInputMapBindingsData());
        }
        
        // Osc Connections
        saveTable.Add("OscConnections", GetNode<OscConnections>("/root/OscConnections").GetData());

        // MIDI session (enabled + session input device list)
        var midi = GetNodeOrNull<MidiManager>("/root/MidiManager");
        if (midi != null)
        {
            saveTable.Add("Midi", midi.GetData());
            // Project-action MIDI bindings (Go, Save, …) — show-scoped, separate undo slice.
            saveTable.Add("MidiInputMap", midi.GetInputMapBindingsData());
        }

        // Keyboard Input Map is stored in user:// via UserDataManager (not in the showfile).
        
        return saveTable;
    }

    public void LoadSettings(Dictionary settingsData)
    {
        GD.Print($"Settings:LoadSettings - Loading Settings");

        if (settingsData.TryGetValue("AudioDevices", out var devices))
        {
            GD.Print($"Settings:LoadSettings - Loading AudioDevices");
            var deviceArray = (Array<string>)devices;
            foreach (var device in deviceArray)
            {
                _audioDevices.OpenAudioDevice(device, out var _);
            }
        }

        if (settingsData.TryGetValue("AudioPatch", out var patchs))
        {
            GD.Print($"Settings:LoadSettings - Loading AudioPatches");
            // Replace any session-seeded Default Patch from ResetSettings so open/load is authoritative.
            foreach (var existing in _audioOutputPatches.Values.ToList())
            {
                if (existing != null && GodotObject.IsInstanceValid(existing))
                    existing.Free();
            }
            _audioOutputPatches.Clear();

            foreach (var patch in (Dictionary)patchs)
            {
                var patchAsDict = patch.Value.AsGodotDictionary();
                var patchObj = AudioOutputPatch.FromData(patchAsDict);
                if (patchObj != null)
                    AddPatch(patchObj);
                else
                    GD.PrintErr("Settings:LoadSettings - Failed to deserialize an audio output patch.");
            }

            // Older showfiles with an empty patch table still get a usable Default Patch.
            EnsureDefaultAudioPatch();
        }
        
        if (settingsData.TryGetValue("Displays", out var displays))
        {
            GD.Print($"Settings:LoadSettings - Loading Displays");
            var displaysAsDict = displays.AsGodotDictionary();
            _displaysManager.LoadFromData(displaysAsDict);
        }

        if (settingsData.TryGetValue("CueLights", out var cueLights))
        {
            GD.Print($"Settings:LoadSettings - Loading CueLights");
            var cueLightsAsDict = cueLights.AsGodotDictionary();
            _globalData.CueLightManager.LoadData(cueLightsAsDict);
        }
        
        UiScale = settingsData.TryGetValue("UiScale", out var value) ? (float)value : UiScale;
        _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), UiScale);
        GoScale = settingsData.TryGetValue("GoScale", out value) ? (float)value : GoScale;
        _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), GoScale);
        WaveformResolution = settingsData.TryGetValue("WaveformResolution", out value) ? (int)value : WaveformResolution;
        StopFadeDuration = settingsData.TryGetValue("StopFadeDuration", out value) ? (float)value : StopFadeDuration;
        // Default true for older shows that predate this setting
        MediaBackupEnabled = settingsData.TryGetValue("MediaBackupEnabled", out value)
            ? value.AsBool()
            : DefaultMediaBackupEnabled;
        MultiEditEnabled = settingsData.TryGetValue("MultiEditEnabled", out value)
            ? value.AsBool()
            : DefaultMultiEditEnabled;
        // Default true for older shows that predate this setting
        SelectNewCues = settingsData.TryGetValue("SelectNewCues", out value)
            ? value.AsBool()
            : DefaultSelectNewCues;
        // Default false (edit mode) for older shows that predate this setting
        ShowMode = settingsData.TryGetValue("ShowMode", out value)
            ? value.AsBool()
            : DefaultShowMode;
        ShowTimelineWaveforms = settingsData.TryGetValue("ShowTimelineWaveforms", out value)
            ? value.AsBool()
            : DefaultShowTimelineWaveforms;
        OutputBackgroundColor = settingsData.TryGetValue("OutputBackgroundColor", out value)
            ? Color.FromString(value.AsString(), DefaultOutputBackgroundColor)
            : DefaultOutputBackgroundColor;
        VideoQualityMode = settingsData.TryGetValue("VideoQualityMode", out value)
            ? ClampEnum(value.AsInt32(), VideoQualityMode.PreferQuality, VideoQualityMode.PreferPerformance, DefaultVideoQualityMode)
            : DefaultVideoQualityMode;
        VideoPreviewQuality = settingsData.TryGetValue("VideoPreviewQuality", out value)
            ? ClampEnum(value.AsInt32(), VideoPreviewQuality.Full, VideoPreviewQuality.Quarter, DefaultVideoPreviewQuality)
            : DefaultVideoPreviewQuality;
        OutputVSyncMode = settingsData.TryGetValue("OutputVSyncMode", out value)
            ? ClampEnum(value.AsInt32(), OutputVSyncMode.PreferVSync, OutputVSyncMode.LowLatency, DefaultOutputVSyncMode)
            : DefaultOutputVSyncMode;
        AudioLatencyMode = settingsData.TryGetValue("AudioLatencyMode", out value)
            ? ClampEnum(value.AsInt32(), AudioLatencyMode.PreferLowLatency, AudioLatencyMode.PreferStability, DefaultAudioLatencyMode)
            : DefaultAudioLatencyMode;
        AudioDeclickMs = settingsData.TryGetValue("AudioDeclickMs", out value)
            ? Math.Clamp(value.AsInt32(), MinAudioDeclickMs, MaxAudioDeclickMs)
            : DefaultAudioDeclickMs;
        AudioMasterVolume = settingsData.TryGetValue("AudioMasterVolume", out value)
            ? Math.Clamp(value.AsSingle(), 0f, 1f)
            : DefaultAudioMasterVolume;

        // Loading a showfile should not keep the previous show's emergency disable/blackout.
        _displaysManager?.ClearRuntimeOutputControls();
        _displaysManager?.ApplyOutputBackgroundColor(OutputBackgroundColor);
        _displaysManager?.ApplyOutputVSyncPreference();
        _audioDevices?.SyncSessionMasterFromSettings();

        // Cue shell defaults (older shows without this key keep system defaults)
        if (settingsData.TryGetValue("CueDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyCueDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetCueDefaultsToSystem();

        if (settingsData.TryGetValue("AudioDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyAudioDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetAudioDefaultsToSystem();

        if (settingsData.TryGetValue("VideoDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyVideoDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetVideoDefaultsToSystem();

        if (settingsData.TryGetValue("TextDefaults", out value) && value.VariantType == Variant.Type.Dictionary)
            ApplyTextDefaultsFromDict(value.AsGodotDictionary());
        else
            ResetTextDefaultsToSystem();
        
        CueLightIdleColour = settingsData.TryGetValue("CueLightIdleColour", out value) ? Color.FromString(value.AsString(), CueLightIdleColour) : CueLightIdleColour;
        CueLightGoColour = settingsData.TryGetValue("CueLightGoColour", out value) ? Color.FromString(value.AsString(), CueLightGoColour) : CueLightGoColour;
        CueLightStandbyColour = settingsData.TryGetValue("CueLightStandbyColour", out value) ? Color.FromString(value.AsString(), CueLightStandbyColour) : CueLightStandbyColour;
        CueLightCountInColour = settingsData.TryGetValue("CueLightCountInColour", out value) ? Color.FromString(value.AsString(), CueLightCountInColour) : CueLightCountInColour;
        CueLightBrightness = settingsData.TryGetValue("CueLightBrightness", out value) ? (byte)value : CueLightBrightness;
        
        // Osc Listen
        if (settingsData.TryGetValue("OscListen", out var oscListen))
        {
            GD.Print($"Settings:LoadSettings - Loading OscListen");
            var oscListenAsDict = oscListen.AsGodotDictionary();
            GetNodeOrNull<OscListen>("/root/OscListen")?.LoadFromData(oscListenAsDict);
        }

        if (settingsData.TryGetValue("OscInputMap", out var oscInputMap) &&
            oscInputMap.VariantType == Variant.Type.Dictionary)
        {
            GD.Print("Settings:LoadSettings - Loading OscInputMap");
            GetNodeOrNull<OscListen>("/root/OscListen")
                ?.LoadInputMapBindingsData(oscInputMap.AsGodotDictionary());
        }
        
        // Osc Connections
        if (settingsData.TryGetValue("OscConnections", out var oscConnections))
        {
            GD.Print($"Settings:LoadSettings - Loading OscConnections");
            var oscConnectionsAsDict = oscConnections.AsGodotDictionary();
            GetNode<OscConnections>("/root/OscConnections").LoadFromData(oscConnectionsAsDict);
        }

        if (settingsData.TryGetValue("Midi", out var midiData) && midiData.VariantType == Variant.Type.Dictionary)
        {
            GD.Print("Settings:LoadSettings - Loading Midi");
            GetNodeOrNull<MidiManager>("/root/MidiManager")?.LoadFromData(midiData.AsGodotDictionary());
        }

        if (settingsData.TryGetValue("MidiInputMap", out var midiInputMap) &&
            midiInputMap.VariantType == Variant.Type.Dictionary)
        {
            GD.Print("Settings:LoadSettings - Loading MidiInputMap");
            GetNodeOrNull<MidiManager>("/root/MidiManager")
                ?.LoadInputMapBindingsData(midiInputMap.AsGodotDictionary());
        }

        // Legacy showfiles may still contain "InputMap" — ignore; bindings are user preferences.
        if (settingsData.ContainsKey("InputMap"))
        {
            GD.Print("Settings:LoadSettings - Ignoring showfile InputMap (now stored in user preferences).");
        }

        // Sync show/edit mode UI after load (always notify so chrome matches the loaded value).
        NotifyShowModeChanged();
    }

    /// <summary>
    /// Applies a partial settings dictionary for scoped undo/redo without a full session reset.
    /// Only keys present in <paramref name="settingsData"/> are touched (e.g. StopFadeDuration alone
    /// will not rebuild displays).
    /// </summary>
    /// <param name="settingsData">Subset of <see cref="GetData"/> keys to restore.</param>
    public void ApplyPartialFromHistory(Dictionary settingsData)
    {
        if (settingsData == null || settingsData.Count == 0) return;

        GD.Print($"Settings:ApplyPartialFromHistory - Applying {settingsData.Count} key(s)");

        // Open devices first so patch UI / playback can resolve hardware after restore.
        // We intentionally do not close devices absent from the snapshot — another patch or
        // direct-output cue may still need them, and closing mid-session is disruptive.
        if (TryGetSettingsValue(settingsData, "AudioDevices", out var devices))
        {
            var deviceArray = devices.AsGodotArray();
            foreach (var device in deviceArray)
            {
                string deviceName = device.AsString();
                if (string.IsNullOrEmpty(deviceName)) continue;
                _audioDevices.OpenAudioDevice(deviceName, out var _);
            }
        }

        // Replace the entire patch table when AudioPatch is present. Old GodotObjects are freed
        // so no UI or cue may keep a dangling reference — callers must rebuild matrix UIs and
        // RelinkCueComponents after restore (HistoryManager does both).
        if (TryGetSettingsValue(settingsData, "AudioPatch", out var patchs)
            && patchs.VariantType == Variant.Type.Dictionary)
        {
            foreach (var patch in _audioOutputPatches.Values.ToList())
            {
                if (patch != null && GodotObject.IsInstanceValid(patch))
                    patch.Free();
            }
            _audioOutputPatches.Clear();

            var patchDict = patchs.AsGodotDictionary();
            foreach (var patchKey in patchDict.Keys)
            {
                var patchAsDict = patchDict[patchKey].AsGodotDictionary();
                var patchObj = AudioOutputPatch.FromData(patchAsDict);
                if (patchObj != null)
                    AddPatch(patchObj);
                else
                    GD.PrintErr($"Settings:ApplyPartialFromHistory - Failed to restore patch key '{patchKey}'");
            }

            GD.Print($"Settings:ApplyPartialFromHistory - Restored {_audioOutputPatches.Count} audio output patch(es)");
            GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.RecheckAllQuiet();
        }

        if (TryGetSettingsValue(settingsData, "Displays", out var displays)
            && displays.VariantType == Variant.Type.Dictionary)
        {
            _displaysManager.LoadFromData(displays.AsGodotDictionary());
            // DisplaysChanged is emitted by LoadFromData; MediaHealthService also listens there.
        }

        if (settingsData.TryGetValue("CueLights", out var cueLights))
        {
            var cueLightsAsDict = cueLights.AsGodotDictionary();
            _globalData.CueLightManager.LoadData(cueLightsAsDict);
        }

        if (TryGetSettingsValue(settingsData, "UiScale", out var value))
        {
            UiScale = value.AsSingle();
            _globalSignals.EmitSignal(nameof(GlobalSignals.UiScaleChanged), UiScale);
        }
        if (TryGetSettingsValue(settingsData, "GoScale", out value))
        {
            GoScale = value.AsSingle();
            _globalSignals.EmitSignal(nameof(GlobalSignals.GoScaleChanged), GoScale);
        }
        if (TryGetSettingsValue(settingsData, "WaveformResolution", out value))
            WaveformResolution = value.AsInt32();
        if (TryGetSettingsValue(settingsData, "StopFadeDuration", out value))
            StopFadeDuration = value.AsSingle();
        if (TryGetSettingsValue(settingsData, "MediaBackupEnabled", out value))
            MediaBackupEnabled = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "MultiEditEnabled", out value))
            MultiEditEnabled = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "SelectNewCues", out value))
            SelectNewCues = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "ShowMode", out value))
        {
            ShowMode = ReadBoolVariant(value);
            NotifyShowModeChanged();
        }
        if (TryGetSettingsValue(settingsData, "ShowTimelineWaveforms", out value))
            ShowTimelineWaveforms = ReadBoolVariant(value);
        if (TryGetSettingsValue(settingsData, "OutputBackgroundColor", out value))
        {
            OutputBackgroundColor = Color.FromString(value.AsString(), DefaultOutputBackgroundColor);
            _displaysManager?.ApplyOutputBackgroundColor(OutputBackgroundColor);
        }
        if (TryGetSettingsValue(settingsData, "VideoQualityMode", out value))
            VideoQualityMode = ClampEnum(value.AsInt32(), VideoQualityMode.PreferQuality, VideoQualityMode.PreferPerformance, DefaultVideoQualityMode);
        if (TryGetSettingsValue(settingsData, "VideoPreviewQuality", out value))
            VideoPreviewQuality = ClampEnum(value.AsInt32(), VideoPreviewQuality.Full, VideoPreviewQuality.Quarter, DefaultVideoPreviewQuality);
        if (TryGetSettingsValue(settingsData, "OutputVSyncMode", out value))
        {
            OutputVSyncMode = ClampEnum(value.AsInt32(), OutputVSyncMode.PreferVSync, OutputVSyncMode.LowLatency, DefaultOutputVSyncMode);
            _displaysManager?.ApplyOutputVSyncPreference();
        }
        if (TryGetSettingsValue(settingsData, "AudioLatencyMode", out value))
            AudioLatencyMode = ClampEnum(value.AsInt32(), AudioLatencyMode.PreferLowLatency, AudioLatencyMode.PreferStability, DefaultAudioLatencyMode);
        if (TryGetSettingsValue(settingsData, "AudioDeclickMs", out value))
            AudioDeclickMs = Math.Clamp(value.AsInt32(), MinAudioDeclickMs, MaxAudioDeclickMs);
        if (TryGetSettingsValue(settingsData, "AudioMasterVolume", out value))
        {
            AudioMasterVolume = Math.Clamp(value.AsSingle(), 0f, 1f);
            _audioDevices?.SetSessionMasterVolume(AudioMasterVolume);
        }

        if (TryGetSettingsValue(settingsData, "CueDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyCueDefaultsFromDict(value.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "AudioDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyAudioDefaultsFromDict(value.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "VideoDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyVideoDefaultsFromDict(value.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "TextDefaults", out value)
            && value.VariantType == Variant.Type.Dictionary)
        {
            ApplyTextDefaultsFromDict(value.AsGodotDictionary());
        }

        if (settingsData.TryGetValue("CueLightIdleColour", out value))
            CueLightIdleColour = Color.FromString(value.AsString(), CueLightIdleColour);
        if (settingsData.TryGetValue("CueLightGoColour", out value))
            CueLightGoColour = Color.FromString(value.AsString(), CueLightGoColour);
        if (settingsData.TryGetValue("CueLightStandbyColour", out value))
            CueLightStandbyColour = Color.FromString(value.AsString(), CueLightStandbyColour);
        if (settingsData.TryGetValue("CueLightCountInColour", out value))
            CueLightCountInColour = Color.FromString(value.AsString(), CueLightCountInColour);
        if (settingsData.TryGetValue("CueLightBrightness", out value))
            CueLightBrightness = (byte)value;

        if (settingsData.TryGetValue("OscListen", out var oscListen))
        {
            var oscListenAsDict = oscListen.AsGodotDictionary();
            GetNodeOrNull<OscListen>("/root/OscListen")?.LoadFromData(oscListenAsDict);
        }

        if (settingsData.TryGetValue("OscConnections", out var oscConnections))
        {
            var oscConnectionsAsDict = oscConnections.AsGodotDictionary();
            GetNodeOrNull<OscConnections>("/root/OscConnections")?.LoadFromData(oscConnectionsAsDict);
        }

        if (TryGetSettingsValue(settingsData, "OscInputMap", out var oscInputMapSlice)
            && oscInputMapSlice.VariantType == Variant.Type.Dictionary)
        {
            GetNodeOrNull<OscListen>("/root/OscListen")
                ?.LoadInputMapBindingsData(oscInputMapSlice.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "Midi", out var midiSlice)
            && midiSlice.VariantType == Variant.Type.Dictionary)
        {
            GetNodeOrNull<MidiManager>("/root/MidiManager")?.LoadFromData(midiSlice.AsGodotDictionary());
        }

        if (TryGetSettingsValue(settingsData, "MidiInputMap", out var midiInputMapSlice)
            && midiInputMapSlice.VariantType == Variant.Type.Dictionary)
        {
            GetNodeOrNull<MidiManager>("/root/MidiManager")
                ?.LoadInputMapBindingsData(midiInputMapSlice.AsGodotDictionary());
        }

        // InputMap is stored in user prefs (not the showfile) but still participates in
        // session undo/redo via HistoryManager with the "InputMap" slice key.
        if (TryGetSettingsValue(settingsData, "InputMap", out var inputMapData) && _globalData != null)
        {
            _globalData.ApplyInputBindings(inputMapData.AsGodotDictionary());
            // Keep user:// bindings aligned with the restored live map.
            _globalData.UserDataManager?.PersistLiveInputMap();
        }
    }

    /// <summary>
    /// Captures a settings subset for undo/redo. Scalar general-settings keys are read directly
    /// (no full GetData) so history does not depend on displays/OSC/etc. serialization.
    /// When <paramref name="keys"/> is null or empty, returns a full <see cref="GetData"/> snapshot.
    /// </summary>
    /// <param name="keys">Optional key filter (e.g. "StopFadeDuration", "InputMap", "AudioPatch").</param>
    /// <returns>Dictionary suitable for history storage (caller should deep-clone if needed).</returns>
    public Dictionary CaptureHistorySlice(params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            return GetData();

        var slice = new Dictionary();
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key)) continue;
            if (TryCaptureScalarHistoryKey(key, out var value))
                slice[key] = value;
            else if (key == "InputMap" && _globalData != null)
            {
                // Live InputMap snapshot for undo (persisted to user prefs, not the showfile).
                slice[key] = _globalData.GetCustomInputBindings();
            }
            else if (key == "AudioPatch")
            {
                var patchTable = new Dictionary();
                foreach (var patch in _audioOutputPatches)
                    patchTable.Add(patch.Key, patch.Value.GetData());
                slice[key] = patchTable;
            }
            else if (key == "AudioDevices")
            {
                slice[key] = _audioDevices != null
                    ? _audioDevices.GetOpenAudioDevicesNames()
                    : new Array<string>();
            }
            else if (key == "Displays")
            {
                // Canvas size + screens + target layers — avoid full GetData (patches, OSC, …).
                slice[key] = _displaysManager != null
                    ? _displaysManager.GetData()
                    : new Dictionary();
            }
            else if (key == "CueDefaults")
            {
                slice[key] = CaptureCueDefaultsDict();
            }
            else if (key == "AudioDefaults")
            {
                slice[key] = CaptureAudioDefaultsDict();
            }
            else if (key == "VideoDefaults")
            {
                slice[key] = CaptureVideoDefaultsDict();
            }
            else if (key == "TextDefaults")
            {
                slice[key] = CaptureTextDefaultsDict();
            }
            else if (key == "Midi")
            {
                var midi = GetNodeOrNull<MidiManager>("/root/MidiManager");
                slice[key] = midi != null ? midi.GetData() : new Dictionary();
            }
            else if (key == "MidiInputMap")
            {
                var midi = GetNodeOrNull<MidiManager>("/root/MidiManager");
                slice[key] = midi != null ? midi.GetInputMapBindingsData() : new Dictionary();
            }
            else if (key == "OscListen")
            {
                var osc = GetNodeOrNull<OscListen>("/root/OscListen");
                slice[key] = osc != null ? osc.GetData() : new Dictionary();
            }
            else if (key == "OscInputMap")
            {
                var osc = GetNodeOrNull<OscListen>("/root/OscListen");
                slice[key] = osc != null ? osc.GetInputMapBindingsData() : new Dictionary();
            }
            else if (key == "OscConnections")
            {
                var oscConn = GetNodeOrNull<OscConnections>("/root/OscConnections");
                slice[key] = oscConn != null ? oscConn.GetData() : new Dictionary();
            }
            else
            {
                // Fallback for other complex keys (CueLights, …)
                var full = GetData();
                if (full.ContainsKey(key))
                    slice[key] = full[key];
            }
        }
        return slice;
    }

    /// <summary>
    /// Reads known general-settings scalars without building a full session settings dump.
    /// </summary>
    private bool TryCaptureScalarHistoryKey(string key, out Variant value)
    {
        switch (key)
        {
            case "UiScale":
                value = UiScale;
                return true;
            case "GoScale":
                value = GoScale;
                return true;
            case "WaveformResolution":
                value = WaveformResolution;
                return true;
            case "StopFadeDuration":
                value = StopFadeDuration;
                return true;
            case "MediaBackupEnabled":
                // Store as int for stable JSON round-trip across Godot versions.
                value = MediaBackupEnabled ? 1 : 0;
                return true;
            case "MultiEditEnabled":
                value = MultiEditEnabled ? 1 : 0;
                return true;
            case "SelectNewCues":
                value = SelectNewCues ? 1 : 0;
                return true;
            case "ShowMode":
                value = ShowMode ? 1 : 0;
                return true;
            case "ShowTimelineWaveforms":
                value = ShowTimelineWaveforms ? 1 : 0;
                return true;
            case "OutputBackgroundColor":
                value = OutputBackgroundColor.ToHtml(true);
                return true;
            case "VideoQualityMode":
                value = (int)VideoQualityMode;
                return true;
            case "VideoPreviewQuality":
                value = (int)VideoPreviewQuality;
                return true;
            case "OutputVSyncMode":
                value = (int)OutputVSyncMode;
                return true;
            case "AudioLatencyMode":
                value = (int)AudioLatencyMode;
                return true;
            case "AudioDeclickMs":
                value = AudioDeclickMs;
                return true;
            case "AudioMasterVolume":
                value = AudioMasterVolume;
                return true;
            default:
                value = default;
                return false;
        }
    }

    /// <summary>
    /// Clamps a raw int into an enum range; returns <paramref name="fallback"/> if out of range.
    /// </summary>
    private static TEnum ClampEnum<TEnum>(int raw, TEnum min, TEnum max, TEnum fallback)
        where TEnum : struct, Enum
    {
        int minI = Convert.ToInt32(min);
        int maxI = Convert.ToInt32(max);
        if (raw < minI || raw > maxI)
            return fallback;
        return (TEnum)Enum.ToObject(typeof(TEnum), raw);
    }

    /// <summary>
    /// Resets all cue shell defaults to system factory values.
    /// </summary>
    public void ResetCueDefaultsToSystem()
    {
        CueDefaultPreWait = SystemDefaultCuePreWait;
        CueDefaultPostWait = SystemDefaultCuePostWait;
        CueDefaultFollow = SystemDefaultCueFollow;
        CueDefaultColor = SystemDefaultCueColor;
        CueDefaultArmed = SystemDefaultCueArmed;
        CueDefaultSkipIfDisarmed = SystemDefaultCueSkipIfDisarmed;
    }

    /// <summary>
    /// Applies the show's cue shell defaults to a newly constructed cue.
    /// Does not change id, name, number, parent/children, or components.
    /// </summary>
    /// <param name="cue">Cue instance to configure (typically just constructed).</param>
    public void ApplyShellDefaults(Cue cue)
    {
        if (cue == null) return;
        cue.PreWait = CueDefaultPreWait;
        cue.PostWait = CueDefaultPostWait;
        cue.Follow = CueDefaultFollow;
        cue.Color = CueDefaultColor;
        cue.Armed = CueDefaultArmed;
        cue.SkipIfDisarmed = CueDefaultSkipIfDisarmed;
    }

    /// <summary>
    /// Serializes the cue shell defaults block for showfile / history.
    /// </summary>
    public Dictionary CaptureCueDefaultsDict()
    {
        return new Dictionary
        {
            ["PreWait"] = CueDefaultPreWait,
            ["PostWait"] = CueDefaultPostWait,
            ["Follow"] = (int)CueDefaultFollow,
            ["Color"] = CueDefaultColor.ToHtml(true),
            ["Armed"] = CueDefaultArmed ? 1 : 0,
            ["SkipIfDisarmed"] = CueDefaultSkipIfDisarmed ? 1 : 0
        };
    }

    /// <summary>
    /// Loads cue shell defaults from a dictionary (showfile or history slice).
    /// Missing keys keep their current values.
    /// </summary>
    /// <param name="data">Dictionary with PreWait, PostWait, Follow, Color, Armed, SkipIfDisarmed.</param>
    public void ApplyCueDefaultsFromDict(Dictionary data)
    {
        if (data == null) return;

        if (TryGetSettingsValue(data, "PreWait", out var v))
            CueDefaultPreWait = v.AsDouble();
        if (TryGetSettingsValue(data, "PostWait", out v))
            CueDefaultPostWait = v.AsDouble();
        if (TryGetSettingsValue(data, "Follow", out v))
        {
            int followInt = v.AsInt32();
            if (followInt is >= 0 and <= 2)
                CueDefaultFollow = (FollowType)followInt;
        }
        if (TryGetSettingsValue(data, "Color", out v))
            CueDefaultColor = Color.FromString(v.AsString(), CueDefaultColor);
        if (TryGetSettingsValue(data, "Armed", out v))
            CueDefaultArmed = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "SkipIfDisarmed", out v))
            CueDefaultSkipIfDisarmed = ReadBoolVariant(v);
    }

    /// <summary>
    /// Returns true when every cue shell default matches the system factory value.
    /// </summary>
    public bool AreCueDefaultsAtSystem()
    {
        return Mathf.IsEqualApprox((float)CueDefaultPreWait, (float)SystemDefaultCuePreWait)
               && Mathf.IsEqualApprox((float)CueDefaultPostWait, (float)SystemDefaultCuePostWait)
               && CueDefaultFollow == SystemDefaultCueFollow
               && CueDefaultColor.IsEqualApprox(SystemDefaultCueColor)
               && CueDefaultArmed == SystemDefaultCueArmed
               && CueDefaultSkipIfDisarmed == SystemDefaultCueSkipIfDisarmed;
    }

    // ── Audio component defaults ───────────────────────────────────────────

    /// <summary>
    /// Resets all audio component defaults to system factory values.
    /// </summary>
    public void ResetAudioDefaultsToSystem()
    {
        AudioDefaultVolume = SystemDefaultAudioVolume;
        AudioDefaultPan = SystemDefaultAudioPan;
        AudioDefaultLoop = SystemDefaultAudioLoop;
        AudioDefaultPlayCount = SystemDefaultAudioPlayCount;
        AudioDefaultFadeIn = SystemDefaultAudioFadeIn;
        AudioDefaultFadeOut = SystemDefaultAudioFadeOut;
        AudioDefaultOutputMode = SystemDefaultAudioOutputMode;
        AudioDefaultPatchId = -1;
        AudioDefaultDirectOutput = string.Empty;
    }

    /// <summary>
    /// Applies show audio defaults to a newly constructed audio component.
    /// Does not change file path, routing matrix, metadata, or in/out times.
    /// Includes default audio output assignment.
    /// </summary>
    /// <param name="comp">Audio component to configure.</param>
    public void ApplyAudioDefaults(AudioComponent comp)
    {
        if (comp == null) return;
        comp.Volume = Math.Clamp(AudioDefaultVolume, 0.0, 1.0);
        comp.Pan = AudioDefaultPan;
        comp.Loop = AudioDefaultLoop;
        comp.PlayCount = Math.Max(1, AudioDefaultPlayCount);
        comp.FadeInDuration = Math.Max(0.0, AudioDefaultFadeIn);
        comp.FadeOutDuration = Math.Max(0.0, AudioDefaultFadeOut);
        ApplyResolvedAudioOutput(
            AudioDefaultOutputMode,
            AudioDefaultPatchId,
            AudioDefaultDirectOutput,
            out var patch,
            out int patchId,
            out string direct);
        comp.Patch = patch;
        comp.PatchId = patchId;
        comp.DirectOutput = direct;
    }

    /// <summary>
    /// Serializes audio component defaults for showfile / history.
    /// </summary>
    public Dictionary CaptureAudioDefaultsDict()
    {
        return new Dictionary
        {
            ["Volume"] = AudioDefaultVolume,
            ["Pan"] = AudioDefaultPan,
            ["Loop"] = AudioDefaultLoop ? 1 : 0,
            ["PlayCount"] = AudioDefaultPlayCount,
            ["FadeIn"] = AudioDefaultFadeIn,
            ["FadeOut"] = AudioDefaultFadeOut,
            ["OutputMode"] = (int)AudioDefaultOutputMode,
            ["PatchId"] = AudioDefaultPatchId,
            ["DirectOutput"] = AudioDefaultDirectOutput ?? string.Empty
        };
    }

    /// <summary>
    /// Loads audio component defaults from a dictionary. Missing keys keep current values.
    /// </summary>
    /// <param name="data">Dictionary with Volume, Pan, Loop, PlayCount, FadeIn, FadeOut, output fields.</param>
    public void ApplyAudioDefaultsFromDict(Dictionary data)
    {
        if (data == null) return;

        if (TryGetSettingsValue(data, "Volume", out var v))
            AudioDefaultVolume = Math.Clamp(v.AsDouble(), 0.0, 1.0);
        if (TryGetSettingsValue(data, "Pan", out v))
            AudioDefaultPan = Mathf.Clamp(v.AsSingle(), -1f, 1f);
        if (TryGetSettingsValue(data, "Loop", out v))
            AudioDefaultLoop = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "PlayCount", out v))
            AudioDefaultPlayCount = Math.Max(1, v.AsInt32());
        if (TryGetSettingsValue(data, "FadeIn", out v))
            AudioDefaultFadeIn = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "FadeOut", out v))
            AudioDefaultFadeOut = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "OutputMode", out v))
            AudioDefaultOutputMode = ParseAudioOutputMode(v.AsInt32());
        if (TryGetSettingsValue(data, "PatchId", out v))
            AudioDefaultPatchId = v.AsInt32();
        if (TryGetSettingsValue(data, "DirectOutput", out v))
            AudioDefaultDirectOutput = v.AsString() ?? string.Empty;
    }

    // ── Video component defaults ───────────────────────────────────────────

    /// <summary>
    /// Resets all video component defaults to system factory values.
    /// </summary>
    public void ResetVideoDefaultsToSystem()
    {
        VideoDefaultExpandMode = SystemDefaultVideoExpandMode;
        VideoDefaultStretchMode = SystemDefaultVideoStretchMode;
        VideoDefaultOpacity = SystemDefaultVideoOpacity;
        VideoDefaultLoop = SystemDefaultVideoLoop;
        VideoDefaultPlayCount = SystemDefaultVideoPlayCount;
        VideoDefaultUseAudio = SystemDefaultVideoUseAudio;
        VideoDefaultAudioVolume = SystemDefaultVideoAudioVolume;
        VideoDefaultPan = SystemDefaultVideoPan;
        VideoDefaultFadeIn = SystemDefaultVideoFadeIn;
        VideoDefaultFadeOut = SystemDefaultVideoFadeOut;
        VideoDefaultImageDuration = SystemDefaultVideoImageDuration;
        VideoDefaultOutputMode = SystemDefaultVideoOutputMode;
        VideoDefaultPatchId = -1;
        VideoDefaultDirectOutput = string.Empty;
        VideoDefaultTargetLayerMode = SystemDefaultVideoTargetLayerMode;
        VideoDefaultTargetLayerId = -1;
    }

    /// <summary>
    /// Applies show video defaults to a newly constructed video component.
    /// Does not change file path, routing matrix, or metadata.
    /// Includes default target layer and embedded-audio output.
    /// Still-image path still forces UseAudio/HasAudio off after this call.
    /// </summary>
    /// <param name="comp">Video component to configure.</param>
    public void ApplyVideoDefaults(VideoComponent comp)
    {
        if (comp == null) return;
        comp.TextureExpandMode = VideoDefaultExpandMode;
        comp.TextureStretchMode = VideoDefaultStretchMode;
        comp.Opacity = Mathf.Clamp(VideoDefaultOpacity, 0f, 1f);
        comp.Loop = VideoDefaultLoop;
        comp.PlayCount = Math.Max(1, VideoDefaultPlayCount);
        comp.UseAudio = VideoDefaultUseAudio;
        comp.AudioVolume = Mathf.Clamp(VideoDefaultAudioVolume, 0f, 1f);
        comp.Volume = comp.AudioVolume;
        comp.Pan = VideoDefaultPan;
        comp.FadeInDuration = Math.Max(0.0, VideoDefaultFadeIn);
        comp.FadeOutDuration = Math.Max(0.0, VideoDefaultFadeOut);
        comp.TargetLayerId = ResolveTargetLayerId(
            VideoDefaultTargetLayerMode, VideoDefaultTargetLayerId);
        ApplyResolvedAudioOutput(
            VideoDefaultOutputMode,
            VideoDefaultPatchId,
            VideoDefaultDirectOutput,
            out var patch,
            out int patchId,
            out string direct);
        comp.Patch = patch;
        comp.PatchId = patchId;
        comp.DirectOutput = direct;
        if (comp.IsImage)
        {
            double hold = Math.Max(0.0, VideoDefaultImageDuration);
            comp.Duration = hold;
            comp.TotalDuration = hold <= 0 ? -1.0 : hold * (comp.Loop ? 1 : Math.Max(1, comp.PlayCount));
            if (comp.Loop)
                comp.TotalDuration = -1.0;
            // Images never use embedded audio.
            comp.UseAudio = false;
            comp.HasAudio = false;
        }
    }

    /// <summary>
    /// Serializes video component defaults for showfile / history.
    /// </summary>
    public Dictionary CaptureVideoDefaultsDict()
    {
        return new Dictionary
        {
            ["ExpandMode"] = (int)VideoDefaultExpandMode,
            ["StretchMode"] = (int)VideoDefaultStretchMode,
            ["Opacity"] = VideoDefaultOpacity,
            ["Loop"] = VideoDefaultLoop ? 1 : 0,
            ["PlayCount"] = VideoDefaultPlayCount,
            ["UseAudio"] = VideoDefaultUseAudio ? 1 : 0,
            ["AudioVolume"] = VideoDefaultAudioVolume,
            ["Pan"] = VideoDefaultPan,
            ["FadeIn"] = VideoDefaultFadeIn,
            ["FadeOut"] = VideoDefaultFadeOut,
            ["ImageDuration"] = VideoDefaultImageDuration,
            ["OutputMode"] = (int)VideoDefaultOutputMode,
            ["PatchId"] = VideoDefaultPatchId,
            ["DirectOutput"] = VideoDefaultDirectOutput ?? string.Empty,
            ["TargetLayerMode"] = (int)VideoDefaultTargetLayerMode,
            ["TargetLayerId"] = VideoDefaultTargetLayerId
        };
    }

    /// <summary>
    /// Loads video component defaults from a dictionary. Missing keys keep current values.
    /// </summary>
    /// <param name="data">Dictionary of video default fields.</param>
    public void ApplyVideoDefaultsFromDict(Dictionary data)
    {
        if (data == null) return;

        if (TryGetSettingsValue(data, "ExpandMode", out var v)
            && TryParseEnum(v, out TextureRect.ExpandModeEnum expand))
        {
            VideoDefaultExpandMode = expand;
        }
        if (TryGetSettingsValue(data, "StretchMode", out v)
            && TryParseEnum(v, out TextureRect.StretchModeEnum stretch))
        {
            VideoDefaultStretchMode = stretch;
        }
        if (TryGetSettingsValue(data, "Opacity", out v))
            VideoDefaultOpacity = VideoComponent.ParseOpacity(v);
        if (TryGetSettingsValue(data, "Loop", out v))
            VideoDefaultLoop = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "PlayCount", out v))
            VideoDefaultPlayCount = Math.Max(1, v.AsInt32());
        if (TryGetSettingsValue(data, "UseAudio", out v))
            VideoDefaultUseAudio = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "AudioVolume", out v))
            VideoDefaultAudioVolume = Mathf.Clamp(v.AsSingle(), 0f, 1f);
        if (TryGetSettingsValue(data, "Pan", out v))
            VideoDefaultPan = Mathf.Clamp(v.AsSingle(), -1f, 1f);
        if (TryGetSettingsValue(data, "FadeIn", out v))
            VideoDefaultFadeIn = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "FadeOut", out v))
            VideoDefaultFadeOut = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "ImageDuration", out v))
            VideoDefaultImageDuration = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "OutputMode", out v))
            VideoDefaultOutputMode = ParseAudioOutputMode(v.AsInt32());
        if (TryGetSettingsValue(data, "PatchId", out v))
            VideoDefaultPatchId = v.AsInt32();
        if (TryGetSettingsValue(data, "DirectOutput", out v))
            VideoDefaultDirectOutput = v.AsString() ?? string.Empty;
        if (TryGetSettingsValue(data, "TargetLayerMode", out v))
            VideoDefaultTargetLayerMode = ParseTargetLayerMode(v.AsInt32());
        if (TryGetSettingsValue(data, "TargetLayerId", out v))
            VideoDefaultTargetLayerId = v.AsInt32();
    }

    // ── Text component defaults ────────────────────────────────────────────

    /// <summary>
    /// Resets all text component defaults to system factory values.
    /// </summary>
    public void ResetTextDefaultsToSystem()
    {
        TextDefaultDuration = SystemDefaultTextDuration;
        TextDefaultOpacity = SystemDefaultTextOpacity;
        TextDefaultUseBbcode = SystemDefaultTextUseBbcode;
        TextDefaultFontSize = SystemDefaultTextFontSize;
        TextDefaultFontName = SystemDefaultTextFontName;
        TextDefaultFontColor = SystemDefaultTextFontColor;
        TextDefaultHAlign = SystemDefaultTextHAlign;
        TextDefaultVAlign = SystemDefaultTextVAlign;
        TextDefaultAutowrap = SystemDefaultTextAutowrap;
        TextDefaultMargins = SystemDefaultTextMargins;
        TextDefaultOutlineSize = SystemDefaultTextOutlineSize;
        TextDefaultOutlineColor = SystemDefaultTextOutlineColor;
        TextDefaultBackgroundEnabled = SystemDefaultTextBackgroundEnabled;
        TextDefaultBackgroundColor = SystemDefaultTextBackgroundColor;
        TextDefaultFadeIn = SystemDefaultTextFadeIn;
        TextDefaultFadeOut = SystemDefaultTextFadeOut;
        TextDefaultTargetLayerMode = SystemDefaultTextTargetLayerMode;
        TextDefaultTargetLayerId = -1;
    }

    /// <summary>
    /// Applies show text defaults to a newly constructed text component.
    /// Does not change content. Includes default target layer assignment.
    /// </summary>
    /// <param name="comp">Text component to configure.</param>
    public void ApplyTextDefaults(TextComponent comp)
    {
        if (comp == null) return;
        comp.Duration = Math.Max(0.0, TextDefaultDuration);
        comp.Opacity = Mathf.Clamp(TextDefaultOpacity, 0f, 1f);
        comp.UseBbcode = TextDefaultUseBbcode;
        comp.FontSize = Math.Max(1, TextDefaultFontSize);
        comp.FontName = TextDefaultFontName ?? string.Empty;
        comp.FontColor = TextDefaultFontColor;
        comp.HorizontalAlignment = TextDefaultHAlign;
        comp.VerticalAlignment = TextDefaultVAlign;
        comp.Autowrap = TextDefaultAutowrap;
        comp.Margins = Math.Max(0, TextDefaultMargins);
        comp.OutlineSize = Math.Max(0, TextDefaultOutlineSize);
        comp.OutlineColor = TextDefaultOutlineColor;
        comp.BackgroundEnabled = TextDefaultBackgroundEnabled;
        comp.BackgroundColor = TextDefaultBackgroundColor;
        comp.FadeInDuration = Math.Max(0.0, TextDefaultFadeIn);
        comp.FadeOutDuration = Math.Max(0.0, TextDefaultFadeOut);
        comp.TargetLayerId = ResolveTargetLayerId(
            TextDefaultTargetLayerMode, TextDefaultTargetLayerId);
        comp.RecalculateDuration();
    }

    /// <summary>
    /// Serializes text component defaults for showfile / history.
    /// </summary>
    public Dictionary CaptureTextDefaultsDict()
    {
        return new Dictionary
        {
            ["Duration"] = TextDefaultDuration,
            ["Opacity"] = TextDefaultOpacity,
            ["UseBbcode"] = TextDefaultUseBbcode ? 1 : 0,
            ["FontSize"] = TextDefaultFontSize,
            ["FontName"] = TextDefaultFontName ?? string.Empty,
            ["FontColor"] = TextDefaultFontColor.ToHtml(true),
            ["HAlign"] = (int)TextDefaultHAlign,
            ["VAlign"] = (int)TextDefaultVAlign,
            ["Autowrap"] = TextDefaultAutowrap ? 1 : 0,
            ["Margins"] = TextDefaultMargins,
            ["OutlineSize"] = TextDefaultOutlineSize,
            ["OutlineColor"] = TextDefaultOutlineColor.ToHtml(true),
            ["BackgroundEnabled"] = TextDefaultBackgroundEnabled ? 1 : 0,
            ["BackgroundColor"] = TextDefaultBackgroundColor.ToHtml(true),
            ["FadeIn"] = TextDefaultFadeIn,
            ["FadeOut"] = TextDefaultFadeOut,
            ["TargetLayerMode"] = (int)TextDefaultTargetLayerMode,
            ["TargetLayerId"] = TextDefaultTargetLayerId
        };
    }

    /// <summary>
    /// Loads text component defaults from a dictionary. Missing keys keep current values.
    /// </summary>
    /// <param name="data">Dictionary of text default fields.</param>
    public void ApplyTextDefaultsFromDict(Dictionary data)
    {
        if (data == null) return;

        if (TryGetSettingsValue(data, "Duration", out var v))
            TextDefaultDuration = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "Opacity", out v))
            TextDefaultOpacity = VideoComponent.ParseOpacity(v);
        if (TryGetSettingsValue(data, "UseBbcode", out v))
            TextDefaultUseBbcode = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "FontSize", out v))
            TextDefaultFontSize = Math.Max(1, v.AsInt32());
        if (TryGetSettingsValue(data, "FontName", out v))
            TextDefaultFontName = v.AsString() ?? string.Empty;
        if (TryGetSettingsValue(data, "FontColor", out v))
            TextDefaultFontColor = Color.FromString(v.AsString(), TextDefaultFontColor);
        if (TryGetSettingsValue(data, "HAlign", out v)
            && TryParseEnum(v, out HorizontalAlignment hAlign))
        {
            TextDefaultHAlign = hAlign;
        }
        if (TryGetSettingsValue(data, "VAlign", out v)
            && TryParseEnum(v, out VerticalAlignment vAlign))
        {
            TextDefaultVAlign = vAlign;
        }
        if (TryGetSettingsValue(data, "Autowrap", out v))
            TextDefaultAutowrap = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "Margins", out v))
            TextDefaultMargins = Math.Max(0, v.AsInt32());
        if (TryGetSettingsValue(data, "OutlineSize", out v))
            TextDefaultOutlineSize = Math.Max(0, v.AsInt32());
        if (TryGetSettingsValue(data, "OutlineColor", out v))
            TextDefaultOutlineColor = Color.FromString(v.AsString(), TextDefaultOutlineColor);
        if (TryGetSettingsValue(data, "BackgroundEnabled", out v))
            TextDefaultBackgroundEnabled = ReadBoolVariant(v);
        if (TryGetSettingsValue(data, "BackgroundColor", out v))
            TextDefaultBackgroundColor = Color.FromString(v.AsString(), TextDefaultBackgroundColor);
        if (TryGetSettingsValue(data, "FadeIn", out v))
            TextDefaultFadeIn = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "FadeOut", out v))
            TextDefaultFadeOut = Math.Max(0.0, v.AsDouble());
        if (TryGetSettingsValue(data, "TargetLayerMode", out v))
            TextDefaultTargetLayerMode = ParseTargetLayerMode(v.AsInt32());
        if (TryGetSettingsValue(data, "TargetLayerId", out v))
            TextDefaultTargetLayerId = v.AsInt32();
    }

    // ── Shared output / layer resolution for component defaults ────────────

    /// <summary>
    /// Resolves an audio output assignment from defaults mode + patch id / direct name.
    /// Preferred falls back to <see cref="GetPreferredAudioOutputPatch"/>; missing patch → none.
    /// </summary>
    public void ApplyResolvedAudioOutput(
        ComponentAudioOutputDefaultMode mode,
        int patchId,
        string directOutput,
        out AudioOutputPatch patch,
        out int resolvedPatchId,
        out string resolvedDirect)
    {
        patch = null;
        resolvedPatchId = -1;
        resolvedDirect = null;

        switch (mode)
        {
            case ComponentAudioOutputDefaultMode.None:
                return;

            case ComponentAudioOutputDefaultMode.Patch:
                if (patchId >= 0
                    && _audioOutputPatches.TryGetValue(patchId, out var byId)
                    && byId != null
                    && GodotObject.IsInstanceValid(byId))
                {
                    patch = byId;
                    resolvedPatchId = byId.Id;
                }
                return;

            case ComponentAudioOutputDefaultMode.Direct:
                if (!string.IsNullOrEmpty(directOutput))
                    resolvedDirect = directOutput;
                return;

            case ComponentAudioOutputDefaultMode.Preferred:
            default:
                var preferred = GetPreferredAudioOutputPatch();
                if (preferred != null)
                {
                    patch = preferred;
                    resolvedPatchId = preferred.Id;
                }
                return;
        }
    }

    /// <summary>
    /// Resolves a target layer id from defaults mode + stored layer id.
    /// FirstAvailable uses the first entry in <see cref="DisplaysManager.Layers"/>; missing layer → -1.
    /// </summary>
    /// <param name="mode">Default mode.</param>
    /// <param name="layerId">Stored layer id when mode is Layer.</param>
    /// <returns>Resolved layer id, or -1 for no output.</returns>
    public static int ResolveTargetLayerId(ComponentTargetLayerDefaultMode mode, int layerId)
    {
        switch (mode)
        {
            case ComponentTargetLayerDefaultMode.None:
                return -1;

            case ComponentTargetLayerDefaultMode.Layer:
                if (layerId >= 0 && DisplaysManager.GetLayerById(layerId) != null)
                    return layerId;
                // Missing layer: fall back to first available when possible.
                if (DisplaysManager.Layers != null && DisplaysManager.Layers.Count > 0)
                    return DisplaysManager.Layers[0].LayerId;
                return -1;

            case ComponentTargetLayerDefaultMode.FirstAvailable:
            default:
                if (DisplaysManager.Layers != null && DisplaysManager.Layers.Count > 0)
                    return DisplaysManager.Layers[0].LayerId;
                return -1;
        }
    }

    private static ComponentAudioOutputDefaultMode ParseAudioOutputMode(int value)
    {
        return TryParseEnum(value, out ComponentAudioOutputDefaultMode mode)
            ? mode
            : ComponentAudioOutputDefaultMode.Preferred;
    }

    private static ComponentTargetLayerDefaultMode ParseTargetLayerMode(int value)
    {
        return TryParseEnum(value, out ComponentTargetLayerDefaultMode mode)
            ? mode
            : ComponentTargetLayerDefaultMode.FirstAvailable;
    }

    /// <summary>
    /// Parses a numeric Variant into an enum, handling Godot long-backed enums
    /// (e.g. <see cref="TextureRect.ExpandModeEnum"/>) where <see cref="Enum.IsDefined"/>
    /// rejects a plain <see cref="int"/>.
    /// </summary>
    private static bool TryParseEnum<TEnum>(Variant value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        try
        {
            long raw = value.VariantType switch
            {
                Variant.Type.Int => value.AsInt64(),
                Variant.Type.Float => (long)value.AsDouble(),
                Variant.Type.String => long.TryParse(value.AsString(), out long p) ? p : long.MinValue,
                _ => value.AsInt64()
            };
            if (raw == long.MinValue && value.VariantType == Variant.Type.String)
                return false;
            return TryParseEnum(raw, out result);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses an integer into an enum using the enum's actual underlying type
    /// (int vs long) so <see cref="Enum.IsDefined"/> does not throw.
    /// </summary>
    private static bool TryParseEnum<TEnum>(long value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        try
        {
            Type enumType = typeof(TEnum);
            Type underlying = Enum.GetUnderlyingType(enumType);
            object boxed = Convert.ChangeType(value, underlying);
            if (!Enum.IsDefined(enumType, boxed))
                return false;
            result = (TEnum)Enum.ToObject(enumType, boxed);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a 32-bit int into an enum (convenience for non-Variant call sites).
    /// </summary>
    private static bool TryParseEnum<TEnum>(int value, out TEnum result) where TEnum : struct, Enum
        => TryParseEnum((long)value, out result);

    /// <summary>
    /// TryGetValue that tolerates string / StringName keys after JSON history clones.
    /// </summary>
    private static bool TryGetSettingsValue(Dictionary data, string key, out Variant value)
    {
        value = default;
        if (data == null || string.IsNullOrEmpty(key)) return false;
        if (data.TryGetValue(key, out value)) return true;

        foreach (var k in data.Keys)
        {
            if (k.AsString() == key)
            {
                value = data[k];
                return true;
            }
        }
        return false;
    }

    private static bool ReadBoolVariant(Variant value)
    {
        return value.VariantType switch
        {
            Variant.Type.Bool => value.AsBool(),
            Variant.Type.Int => value.AsInt32() != 0,
            Variant.Type.Float => !Mathf.IsZeroApprox(value.AsSingle()),
            Variant.Type.String => value.AsString() is "1" or "true" or "True",
            _ => value.AsBool()
        };
    }
    
}