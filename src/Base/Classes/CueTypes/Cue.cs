using System;
using Godot;
using System.Collections.Generic;
using System.Linq;
using Cue2.Shared;
using Godot.Collections;
using Array = Godot.Collections.Array;

namespace Cue2.Base.Classes.CueTypes;


/// <summary>
/// How a cue chains to the next cue at the same nesting level (cue sequences).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="None"/> — do not auto-start the next cue.</item>
/// <item><see cref="Continue"/> — auto-continue: after this cue's pre-wait ends (content phase starts),
/// the next sibling is armed and waits this cue's <see cref="Cue.PostWait"/> before starting
/// (post-wait 0 = next starts when content phase starts).</item>
/// <item><see cref="Follow"/> — auto-follow: when this cue's content completes, the next sibling is armed
/// and waits this cue's <see cref="Cue.PostWait"/> before starting (post-wait 0 = immediate).</item>
/// </list>
/// The next cue is always the next entry at the same nested level (root order or parent <see cref="Cue.ChildCues"/>).
/// Armed next cues appear in the active list with a continue/follow lead-in timer, then their own pre-wait/content.
/// </remarks>
public enum FollowType
{
    /// <summary>Do not continue — next cue is not started automatically.</summary>
    None = 0,

    /// <summary>Auto-continue — next sibling armed after pre-wait; then post-wait lead-in on that cue.</summary>
    Continue = 1,

    /// <summary>Auto-follow — next sibling armed after content completes; then post-wait lead-in on that cue.</summary>
    Follow = 2
}

public class Cue : ICue
{
    private static int _nextId = 0;
    public int Id { get; set; }

    /// <summary>
    /// Resets the static cue-id allocator (call when starting a new empty session).
    /// </summary>
    public static void ResetIdAllocator()
    {
        _nextId = 0;
    }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            NameChanged?.Invoke(value);
        }
    }

    private string _cueNum;
    public string CueNum
    {
        get => _cueNum;
        set
        {
            _cueNum = value;
            CueNumChanged?.Invoke(value);
        }
    }
    
    public ShellBar ShellBar { get; set; }

    public int ParentId = -1;

    public List<int> ChildCues = new List<int>(); // list of child cue ID's
    
    private double _preWait;
    private double _duration;
    private double _totalDuration;
    private double _postWait;

    public double PreWait
    {
        get => _preWait;
        set
        {
            if (Math.Abs(_preWait - value) < 1e-9) return;
            _preWait = value;
            PreWaitChanged?.Invoke(_preWait);
        }
    }

    /// <summary>Duration of cue contents excluding pre/post wait (includes child cues).</summary>
    public double Duration
    {
        get => _duration;
        set
        {
            if (Math.Abs(_duration - value) < 1e-9) return;
            _duration = value;
            DurationChanged?.Invoke(_duration);
        }
    }

    public double TotalDuration
    {
        get => _totalDuration;
        set
        {
            if (Math.Abs(_totalDuration - value) < 1e-9) return;
            _totalDuration = value;
            TotalDurationChanged?.Invoke(_totalDuration);
        }
    }

    public double PostWait
    {
        get => _postWait;
        set
        {
            if (Math.Abs(_postWait - value) < 1e-9) return;
            _postWait = value;
            PostWaitChanged?.Invoke(_postWait);
        }
    }

    private Color _color;
    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            ColorChanged?.Invoke(value);
        }
    }
    private FollowType _follow = FollowType.None;

    /// <summary>
    /// Continue / follow mode for cue sequences (see <see cref="FollowType"/>).
    /// </summary>
    /// <value>One of <see cref="FollowType.None"/>, <see cref="FollowType.Continue"/>, or <see cref="FollowType.Follow"/>.</value>
    public FollowType Follow
    {
        get => _follow;
        set
        {
            if (_follow == value) return;
            _follow = value;
            FollowChanged?.Invoke(_follow);
        }
    }
    
    /// <summary>
    /// Stored value if it's children are expanded to view.
    /// </summary>
    public bool Expanded { get; set; } = false;

    private bool _armed = true;

    /// <summary>
    /// Whether this cue is armed for playback. When false, GO does not play content
    /// and advances the playhead to the next cue instead.
    /// </summary>
    /// <value>Default is <c>true</c> (armed).</value>
    public bool Armed
    {
        get => _armed;
        set
        {
            if (_armed == value) return;
            _armed = value;
            ArmedChanged?.Invoke(_armed);
        }
    }

    private bool _skipIfDisarmed;

    /// <summary>
    /// When true and <see cref="Armed"/> is false, advancing the playhead after a prior GO
    /// bypasses this cue and selects the next eligible cue instead of standing on it.
    /// </summary>
    /// <value>Default is <c>false</c> (disarmed cues still receive playhead selection).</value>
    public bool SkipIfDisarmed
    {
        get => _skipIfDisarmed;
        set
        {
            if (_skipIfDisarmed == value) return;
            _skipIfDisarmed = value;
            SkipIfDisarmedChanged?.Invoke(_skipIfDisarmed);
        }
    }

    /// <summary>
    /// True when this cue should be bypassed when advancing the playhead after GO
    /// (disarmed with <see cref="SkipIfDisarmed"/>).
    /// </summary>
    public bool ShouldSkipOnPlayhead => !Armed && SkipIfDisarmed;
    
    // Events
    public event Action<string> NameChanged;
    public event Action<string> CueNumChanged;
    public event Action<double> PreWaitChanged;
    public event Action<double> DurationChanged;
    public event Action<double> TotalDurationChanged;
    public event Action<double> PostWaitChanged;
    public event Action<Color> ColorChanged;
    public event Action<FollowType> FollowChanged;
    public event Action<bool> ArmedChanged;
    public event Action<bool> SkipIfDisarmedChanged;

    
    
    public List<ICueComponent> Components = new List<ICueComponent>();
    
    public Cue() // // Default constructor for base cue
    {
        Id = _nextId++;
        _name = "New cue number " + Id.ToString();
        _cueNum = Id.ToString();
        Color = new Color(0f, 0f, 0f, 1.0f);
    }
    
    

    public Cue(Dictionary data) // Load from saved data - Using full namespace
    {
        if (!data.ContainsKey("Id"))
        {
            GD.PrintErr("Cue:Constructor - Missing 'Id' key in data.");
            return;
        }
        Id = data["Id"].AsInt32();
        if (Id >= _nextId) _nextId = Id + 1;
        Name = data.ContainsKey("Name") ? (string)data["Name"] : "Unnamed Cue";
        _cueNum = data.ContainsKey("CueNum") ? (string)data["CueNum"] : Id.ToString();
        ParentId = data.ContainsKey("ParentId") ? (int)data["ParentId"] : -1;
        if (data.ContainsKey("ChildCues"))
        {
            var childArray = data["ChildCues"].AsGodotArray();
            foreach (var childInt in childArray)
            {
                ChildCues.Add(childInt.AsInt32());
            }
        }
        PreWait = data.ContainsKey("PreWait") ? (double)data["PreWait"] : 0.0;
        Duration = data.ContainsKey("Duration") ? (double)data["Duration"] : 0.0;
        TotalDuration = data.ContainsKey("TotalDuration") ? (double)data["TotalDuration"] : 0.0;
        PostWait = data.ContainsKey("PostWait") ? (double)data["PostWait"] : 0.0;
        _follow = data.ContainsKey("Follow") ? (FollowType)(int)data["Follow"] : FollowType.None;
        Expanded = data.TryGetValue("Expanded", out var expVal) ? expVal.AsBool() : false;
        Color = data.TryGetValue("Color", out var value) ? Color.FromString(value.AsString(), Color) : Color;
        // Missing keys (legacy saves) default to armed / not skip.
        _armed = data.TryGetValue("Armed", out var armedVal) ? armedVal.AsBool() : true;
        _skipIfDisarmed = data.TryGetValue("SkipIfDisarmed", out var skipVal) && skipVal.AsBool();

        
        if (data.ContainsKey("Components"))
        {
            var compData = data["Components"].AsGodotArray();
            foreach (var compVar in compData)
            {
                if (compVar.VariantType != Variant.Type.Dictionary)
                {
                    GD.PrintErr("Cue:Constructor - Component data is not a dictionary.");
                    continue;
                }
                var compHash = compVar.AsGodotDictionary();
                if (!compHash.ContainsKey("Type"))
                {
                    GD.PrintErr("Cue:Constructor - Missing 'Type' in component data.");
                    continue;
                }
                string type = (string)compHash["Type"];
                ICueComponent comp = type switch
                {
                    "Audio" => new AudioComponent(),
                    "Video" => new VideoComponent(),
                    "Network" => new NetworkComponent(),
                    "CueLight" => new CueLightComponent(),
                    "OscComponent" => new OscComponent(),
                    "Control" => new ControlComponent(),
                    _ => null
                };
                if (comp != null)
                {
                    try
                    {
                        comp.LoadFromData(compHash);
                        Components.Add(comp);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"Cue:Constructor - Error loading component '{type}': {ex.Message}");
                    }
                }
                else
                {
                    GD.PrintErr($"Cue:Constructor - Unknown component type '{type}'.");
                }
            }
        }
    }
    
    // Methods to add components dynamically
    public AudioComponent AddAudioComponent(string audioFile, AudioOutputPatch patch = null)
    {
        if (Components.FirstOrDefault(c => c.Type == "Audio") is AudioComponent existing)
        {
            GD.Print($"Cue:AddAudioComponent - Audio component already exists in cue {Id}. Returning existing.");
            return existing;
        }
        var audioComp = new AudioComponent { AudioFile = audioFile, Patch = patch };
        Components.Add(audioComp);
        return audioComp;
    }
    
    public AudioComponent GetAudioComponent()
    {
        return Components.FirstOrDefault(c => c.Type == "Audio", defaultValue:null) as AudioComponent;
    }
    
    public VideoComponent GetVideoComponent()
    {
        return Components.FirstOrDefault(c => c.Type == "Video", defaultValue:null) as VideoComponent;
    }

    public VideoComponent AddVideoComponent(string videoFile)
    {
        if (Components.FirstOrDefault(c => c.Type == "Video") is VideoComponent existing)
        {
            GD.Print($"Cue:AddVideoComponent - Video component already exists in cue {Id}. Returning existing.");
            return existing;
        }
        var videoComp = new VideoComponent { VideoFile = videoFile };
        // Prefer a real layer when one exists so new media cues are playable;
        // user can still choose "No Output" (-1) in the inspector.
        if (DisplaysManager.Layers != null && DisplaysManager.Layers.Count > 0)
            videoComp.TargetLayerId = DisplaysManager.Layers[0].LayerId;
        //videoComp.ExtractAudioIfPresent(videoFile, globalSignals);
        Components.Add(videoComp);
        return videoComp;
    }

    public void AddNetworkComponent(/* params */)
    {
        var netComp = new NetworkComponent { /* init */ };
        Components.Add(netComp);
    }

    /// <summary>
    /// Adds a ICueComponent to this cue
    /// </summary>
    /// <param name="component"></param>
    public void AddICueComponent(ICueComponent component)
    {
        Components.Add(component);
    }

    public void RemoveICueComponent(ICueComponent component)
    {
        Components.Remove(component);
    }
    
    public CueLightComponent[] GetCueLightComponents()
    {
        return Components.OfType<CueLightComponent>().ToArray();
    }

    public OscComponent[] GetOscComponents()
    {
        return Components.OfType<OscComponent>().ToArray();
    }

    /// <summary>
    /// Returns all control components on this cue.
    /// </summary>
    /// <returns>Array of <see cref="ControlComponent"/> instances (may be empty).</returns>
    public ControlComponent[] GetControlComponents()
    {
        return Components.OfType<ControlComponent>().ToArray();
    }

    public double CalculateTotalDuration()
    {
        var contentsDuration = 0.0;
        foreach (var component in Components)
        {
            if (component.Type == "Audio")
            {
                if (((AudioComponent)component).Loop == true)
                {
                    contentsDuration = -1;
                    break;
                }
                ((AudioComponent)component).RecalculateDuration();
                var componentDuration = ((AudioComponent)component).TotalDuration;
                if (contentsDuration < componentDuration) contentsDuration = componentDuration;
            }
            else if (component.Type == "Video")
            {
                var video = (VideoComponent)component;
                if (video.Loop)
                {
                    contentsDuration = -1;
                    break;
                }
                video.RecalculateDuration();
                var componentDuration = video.TotalDuration;
                if (contentsDuration < componentDuration) contentsDuration = componentDuration;
            }
        }

        // If loop
        if (contentsDuration == -1)
        {
            Duration = -1;
            TotalDuration = -1;
            return TotalDuration;
        }
        
        var childDuration = DurationOfChildren();
        if (childDuration == -1)
        {
            Duration = -1;
            TotalDuration = -1;
            return TotalDuration;
        }
        if (childDuration > contentsDuration) contentsDuration = childDuration;
        Duration = contentsDuration;
        TotalDuration = PreWait + contentsDuration + PostWait;
        return TotalDuration;
    }

    private double DurationOfChildren()
    {
        var longestDuration = 0.0;
        foreach (var childId in ChildCues)
        {
            var childCue = CueList.FetchCueFromId(childId);
            if (childCue != null)
            {
                var childDuration = childCue.CalculateTotalDuration();
                if (childDuration == -1) return childDuration; // Break if loop found

                if (childDuration > longestDuration) longestDuration = childDuration;
            }
        }
        return longestDuration;
    }
    
    public void AddChildCue(int childId)
    {
        ChildCues.Add(childId);
    }

    public void RemoveChildCue(int childId)
    {
        ChildCues.Remove(childId);
    }

    public void SetParent(int parentId)
    {
        ParentId = parentId;
    }
    
    
    

    public Dictionary GetData()
    {
        var dict = new Dictionary();
        dict.Add("Id", Id.ToString());
        dict.Add("Name", Name);
        dict.Add("CueNum", CueNum);
        dict.Add("ParentId", ParentId.ToString());
        dict.Add("ChildCues", new Array<int>(ChildCues));
        dict.Add("PreWait", PreWait);
        dict.Add("Duration", Duration);
        dict.Add("TotalDuration", TotalDuration);
        dict.Add("PostWait", PostWait);
        dict.Add("Follow", (int)Follow);
        dict.Add("Expanded", Expanded);
        dict.Add("Color", Color.ToHtml());
        dict.Add("Armed", Armed);
        dict.Add("SkipIfDisarmed", SkipIfDisarmed);

        var compData = new Array();
        foreach (var comp in Components)
        {
            var compDict = comp.GetData();
            compDict.Add("Type", comp.Type);
            compData.Add(compDict);
        }
        dict.Add("Components", compData);

        return dict;
    }

    /// <summary>
    /// Applies serialized cue data onto this instance in place (identity preserved).
    /// Used by scoped undo/redo so a single cue can be restored without rebuilding the list.
    /// </summary>
    /// <param name="data">Dictionary previously produced by <see cref="GetData"/>.</param>
    /// <remarks>
    /// Does not free or recreate <see cref="ShellBar"/>. Hierarchy fields (ParentId, ChildCues)
    /// are applied from data; structural list rebuilds should use full cuelist history instead.
    /// </remarks>
    public void ApplyFromData(Dictionary data)
    {
        if (data == null) return;

        // Identity: keep existing Id; only advance static counter if needed for consistency.
        if (data.ContainsKey("Id"))
        {
            int loadedId = data["Id"].AsInt32();
            if (loadedId != Id)
                GD.PrintErr($"Cue:ApplyFromData - Id mismatch (live={Id}, data={loadedId}); keeping live Id.");
        }

        Name = data.ContainsKey("Name") ? (string)data["Name"] : Name;
        CueNum = data.ContainsKey("CueNum") ? (string)data["CueNum"] : CueNum;
        ParentId = data.ContainsKey("ParentId") ? data["ParentId"].AsInt32() : ParentId;

        ChildCues.Clear();
        if (data.ContainsKey("ChildCues"))
        {
            var childArray = data["ChildCues"].AsGodotArray();
            foreach (var childInt in childArray)
                ChildCues.Add(childInt.AsInt32());
        }

        PreWait = data.ContainsKey("PreWait") ? (double)data["PreWait"] : PreWait;
        Duration = data.ContainsKey("Duration") ? (double)data["Duration"] : Duration;
        TotalDuration = data.ContainsKey("TotalDuration") ? (double)data["TotalDuration"] : TotalDuration;
        PostWait = data.ContainsKey("PostWait") ? (double)data["PostWait"] : PostWait;
        // Assign via field then notify once so UI gets a single FollowChanged after full apply.
        var loadedFollow = data.ContainsKey("Follow") ? (FollowType)(int)data["Follow"] : _follow;
        Expanded = data.TryGetValue("Expanded", out var expVal) ? expVal.AsBool() : Expanded;
        Color = data.TryGetValue("Color", out var colorVal)
            ? Color.FromString(colorVal.AsString(), Color)
            : Color;
        // Assign via properties so ShellBar / inspector listeners refresh after undo.
        Armed = data.TryGetValue("Armed", out var armedVal) ? armedVal.AsBool() : Armed;
        SkipIfDisarmed = data.TryGetValue("SkipIfDisarmed", out var skipVal)
            ? skipVal.AsBool()
            : SkipIfDisarmed;

        Components.Clear();
        if (data.ContainsKey("Components"))
        {
            var compData = data["Components"].AsGodotArray();
            foreach (var compVar in compData)
            {
                if (compVar.VariantType != Variant.Type.Dictionary)
                {
                    GD.PrintErr("Cue:ApplyFromData - Component data is not a dictionary.");
                    continue;
                }
                var compHash = compVar.AsGodotDictionary();
                if (!compHash.ContainsKey("Type"))
                {
                    GD.PrintErr("Cue:ApplyFromData - Missing 'Type' in component data.");
                    continue;
                }
                string type = (string)compHash["Type"];
                ICueComponent comp = type switch
                {
                    "Audio" => new AudioComponent(),
                    "Video" => new VideoComponent(),
                    "Network" => new NetworkComponent(),
                    "CueLight" => new CueLightComponent(),
                    "OscComponent" => new OscComponent(),
                    "Control" => new ControlComponent(),
                    _ => null
                };
                if (comp == null) continue;
                try
                {
                    comp.LoadFromData(compHash);
                    Components.Add(comp);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"Cue:ApplyFromData - Failed to load component '{type}': {ex.Message}");
                }
            }
        }

        _follow = loadedFollow;
        FollowChanged?.Invoke(_follow);
        ShellBar?.RelationshipChanged();
    }

    /// <summary>
    /// Returns the next cue at the same nesting level (next root sibling or next entry in the parent's
    /// <see cref="ChildCues"/> list). Used by auto-continue / auto-follow sequences.
    /// </summary>
    /// <returns>The next sibling cue, or null if this is the last at its level.</returns>
    public Cue GetNextSiblingCue()
    {
        if (ParentId >= 0)
        {
            var parent = CueList.FetchCueFromId(ParentId);
            if (parent == null) return null;
            int idx = parent.ChildCues.IndexOf(Id);
            if (idx < 0 || idx + 1 >= parent.ChildCues.Count) return null;
            return CueList.FetchCueFromId(parent.ChildCues[idx + 1]);
        }

        // Root level: prefer shell container order (matches visual list).
        if (ShellBar != null && GodotObject.IsInstanceValid(ShellBar))
        {
            var parentNode = ShellBar.GetParent();
            if (parentNode != null)
            {
                int i = ShellBar.GetIndex();
                for (int j = i + 1; j < parentNode.GetChildCount(); j++)
                {
                    if (parentNode.GetChild(j) is ShellBar nextShell && nextShell.CueId >= 0)
                    {
                        var next = CueList.FetchCueFromId(nextShell.CueId);
                        if (next != null) return next;
                    }
                }
                return null;
            }
        }

        // Fallback without shells: first top-level cue after this id in CueIndex is unreliable;
        // walk any root cues that share ParentId == -1 is order-undefined. Return null.
        return null;
    }

    /// <summary>
    /// Walks auto-continue / auto-follow links from this cue and returns the last cue that will play
    /// as part of the sequence (the first cue with <see cref="FollowType.None"/>, or the last reachable).
    /// </summary>
    /// <returns>The terminal cue of the sequence starting at this cue (always at least this cue).</returns>
    public Cue GetSequenceEndCue()
    {
        var current = this;
        var guard = 0;
        while (current.Follow != FollowType.None && guard++ < 10000)
        {
            var next = current.GetNextSiblingCue();
            if (next == null) break;
            current = next;
        }
        return current;
    }

    /// <summary>
    /// Returns the first cue after this cue's continue/follow sequence (playhead target after GO),
    /// or null if there is no cue after the sequence at this nesting level.
    /// </summary>
    public Cue GetCueAfterSequence()
    {
        return GetSequenceEndCue().GetNextSiblingCue();
    }

    /// <summary>
    /// Walks forward from <paramref name="start"/> (inclusive) past cues that should be bypassed
    /// when advancing the playhead (<see cref="ShouldSkipOnPlayhead"/>).
    /// </summary>
    /// <param name="start">First candidate cue, or null.</param>
    /// <returns>The first cue that should receive the playhead, or null if none remain.</returns>
    public static Cue ResolvePlayheadTarget(Cue start)
    {
        var current = start;
        var guard = 0;
        while (current != null && current.ShouldSkipOnPlayhead && guard++ < 10000)
            current = current.GetNextSiblingCue();
        return current;
    }
    
}
