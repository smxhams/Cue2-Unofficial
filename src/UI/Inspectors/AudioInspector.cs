using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Domain.Cuelist;
using Cue2.Domain.Playback;
using Cue2.Domain.Devices;
using Cue2.Domain.ShowSettings;
using Cue2.Domain.Metadata;
using Cue2.Domain.Cues;
using Cue2.Domain.Connections;
using Cue2.Domain.Library;
using Cue2.Domain.Commands;
using Cue2.Services;
using Cue2.Media.Audio;
using Cue2.UI.Utilities;

namespace Cue2.UI.Inspectors;


/// <summary>
/// Inspector UI for managing audio components in cues. Handles file selection, playback settings,
/// and output patching. Supports multi-edit when Settings multi-edit is on and multiple cues are selected.
/// </summary>
/// <remarks>
/// Multi-edit targets are selected cues that have an audio component. Uniform values are shown;
/// mixed values are blank. Waveform and routing matrix reflect the primary (focused) target;
/// scalar edits (volume, pan, loop, times, fades, play count, output, file) apply to all targets.
/// History uses a cuelist snapshot when two or more targets change.
/// </remarks>
public partial class AudioInspector : Control
{

    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private AudioDevices _audioDevices;
    
    private Cue _focusedCue;
    private AudioComponent _focusedAudioComponent;
    private MediaEngine _mediaEngine;

    /// <summary>True when multi-edit setting is on and more than one cue is selected.</summary>
    private bool _isMultiEdit;

    /// <summary>Selected cues that currently have an audio component.</summary>
    private List<(Cue Cue, AudioComponent Component)> _audioTargets = new();

    /// <summary>
    /// True while pushing model → UI (multi sync / restore) so handlers do not re-record.
    /// </summary>
    private bool _isSyncingUi;

    /// <summary>
    /// Bumped on every <see cref="ShellSelected"/> so overlapping async work from rapid multi-select
    /// abandons after awaits.
    /// </summary>
    private int _shellSelectGeneration;
    
    // Ui Nodes
    private Label _infoLabel;
    private HBoxContainer _selectFileContainer;
    private VBoxContainer _inspectorContent;
    private Button _buttonSelectFile;
    private LineEdit _fileUrl;
    private Button _routingCollapseButton;
    private VBoxContainer _routingAccordian;
    private Button _waveformCollapseButton;
    private VBoxContainer _waveformAccordian;
    
    private LineEdit _startTimeInput;
    private LineEdit _endTimeInput;
    private LineEdit _durationValue;
    private LineEdit _fileDurationValue;
    private CheckBox _loopInput;
    private LineEdit _playCountInput;
    private LineEdit _volumeInput;
    private LineEdit _fadeInInput;
    private LineEdit _fadeOutInput;
    private Label _panLabel;
    private HSlider _panSlider;
    private LineEdit _panInput;
    private OptionButton _outputOptionButton;
    private bool _isUpdatingPanUi;
    
    // Routing matrix
    private GridContainer _routingMatrixGrid;
    private VBoxContainer _routingContainer;
    /// <summary>Left-column input labels in the routing matrix (updated when pan changes).</summary>
    private readonly List<Label> _routingInputLabels = new List<Label>();
    
    // Waveform
    private PanelContainer _waveformPanel;
    private WaveformDisplay _waveformDisplay;
    private WaveformPeaks _cachedPeaks;
    private byte[] _cachedPeaksSource;
    private Button _startDragHandle;
    private Button _endDragHandle;
    private HSlider _zoomSlider;
    private HScrollBar _waveformScroll;
    private bool _isDraggingStart;
    private bool _isDraggingEnd;
    private float _viewStartNorm;
    private float _viewSpanNorm = 1f;
    
    private FileDialog _fileDialog;
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
        _audioDevices = GetNode<AudioDevices>("/root/AudioDevices");
        
		
        _globalSignals.ShellFocused += ShellSelected;
        // Media backup rewrites paths while a cue stays selected — refresh URL without re-select
        _globalSignals.SyncShellInspector += RefreshMediaPathDisplay;
        _globalSignals.CueMediaHealthChanged += OnCueMediaHealthChanged;
        
        
        // Ui Node setup
        _infoLabel = GetNode<Label>("%InfoLabel");
        _selectFileContainer = GetNode<HBoxContainer>("%SelectFileContainer");
        _inspectorContent = GetNode<VBoxContainer>("%InspectorContent");
        _buttonSelectFile = GetNode<Button>("%ButtonSelectFile");
        _fileUrl = GetNode<LineEdit>("%FileURL");
        _fileUrlMissingStyle = InspectorMediaUrlStyle.CreateMissingStyle();
        _deleteAudioComponentButton = GetNodeOrNull<Button>("%DeleteAudioComponentButton");
        if (_deleteAudioComponentButton != null)
        {
            _deleteAudioComponentButton.Pressed += OnDeleteAudioComponentPressed;
            _deleteAudioComponentButton.AddThemeColorOverride("font_color", GlobalStyles.Danger);
            try
            {
                _deleteAudioComponentButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
                _deleteAudioComponentButton.ExpandIcon = true;
            }
            catch { /* optional */ }
            _deleteAudioComponentButton.Visible = false;
        }
        
        _routingCollapseButton = GetNode<Button>("%RoutingCollapseButton");
        _routingCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        _routingAccordian = GetNode<VBoxContainer>("%RoutingAccordian");
        _routingContainer = GetNode<VBoxContainer>("%RoutingContainer");
        _routingMatrixGrid = GetNode<GridContainer>("%RoutingMatrixGrid");
        
        _waveformCollapseButton = GetNode<Button>("%WaveformCollapseButton");
        _waveformCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        _waveformAccordian = GetNode<VBoxContainer>("%WaveformAccordian");
        
        _startTimeInput = GetNode<LineEdit>("%StartTimeInput");
        _endTimeInput = GetNode<LineEdit>("%EndTimeInput");
        _durationValue = GetNode<LineEdit>("%DurationValue");
        _fileDurationValue = GetNode<LineEdit>("%FileDurationValue");
        _loopInput = GetNode<CheckBox>("%LoopInput");
        _playCountInput = GetNode<LineEdit>("%PlayCountInput");
        _volumeInput = GetNode<LineEdit>("%VolumeInput");
        _fadeInInput = GetNodeOrNull<LineEdit>("%FadeInInput");
        _fadeOutInput = GetNodeOrNull<LineEdit>("%FadeOutInput");
        _panLabel = GetNodeOrNull<Label>("%PanLabel");
        _panSlider = GetNodeOrNull<HSlider>("%PanSlider");
        _panInput = GetNodeOrNull<LineEdit>("%PanInput");
        _outputOptionButton = GetNode<OptionButton>("%OutputOptionButton");
        
        // Waveform UI setup — peak bars + zoom/scroll
        _waveformPanel = GetNode<PanelContainer>("%WaveformPanel");
        _waveformDisplay = new WaveformDisplay();
        _waveformPanel.AddChild(_waveformDisplay);
        _waveformPanel.MoveChild(_waveformDisplay, 0); // behind handles
        _waveformPanel.Resized += () => { if (_waveformAccordian.Visible) _ = DrawWaveform(); };
        _waveformPanel.GuiInput += OnWaveformPanelGuiInput;

        _startDragHandle = GetNode<Button>("%StartDragHandle");
        _endDragHandle = GetNode<Button>("%EndDragHandle");
        StyleWaveformHandles();
        _startDragHandle.GuiInput += OnStartHandleInput;
        _endDragHandle.GuiInput += OnEndHandleInput;

        _zoomSlider = GetNodeOrNull<HSlider>("%ZoomSlider");
        if (_zoomSlider != null)
        {
            _zoomSlider.MinValue = 1;
            _zoomSlider.MaxValue = 20;
            _zoomSlider.Step = 0.1;
            _zoomSlider.Value = 1;
            _zoomSlider.TooltipText = "Zoom waveform (1× = full file)";
            _zoomSlider.ValueChanged += OnZoomChanged;
        }

        // Scroll bar under zoom (created in code so both inspectors get it)
        _waveformScroll = new HScrollBar
        {
            Name = "WaveformScroll",
            CustomMinimumSize = new Vector2(0, 14),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false,
            TooltipText = "Scroll zoomed waveform"
        };
        _waveformAccordian.AddChild(_waveformScroll);
        _waveformScroll.ValueChanged += OnWaveformScrollChanged; 
        
        
        
        _startTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _startTimeInput);
        _endTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _endTimeInput);
        // Commit on blur as well (Enter is not the only way users finish an edit).
        _startTimeInput.FocusExited += () => TimeFieldSubmitted(_startTimeInput.Text, _startTimeInput);
        _endTimeInput.FocusExited += () => TimeFieldSubmitted(_endTimeInput.Text, _endTimeInput);
        _volumeInput.TextSubmitted += newText => VolumeInputSubmitted(newText, _volumeInput);
        _volumeInput.FocusExited += () => VolumeInputSubmitted(_volumeInput.Text, _volumeInput);
        LineEditDbDragSlider.EnableVolume(_volumeInput);
        if (_panSlider != null)
        {
            _panSlider.MinValue = -100;
            _panSlider.MaxValue = 100;
            _panSlider.Step = 1;
            _panSlider.ValueChanged += OnPanSliderChanged;
            _panSlider.DragEnded += OnPanSliderDragEnded;
        }
        if (_panInput != null)
        {
            _panInput.TextSubmitted += newText => PanInputSubmitted(newText);
            _panInput.FocusExited += () => PanInputSubmitted(_panInput.Text);
        }
        _loopInput.Toggled += OnLoopToggled;
        _playCountInput.TextSubmitted += OnPlayCountSubmitted;
        _playCountInput.FocusExited += () => OnPlayCountSubmitted(_playCountInput.Text);
        if (_fadeInInput != null)
        {
            _fadeInInput.TextSubmitted += text => OnFadeSubmitted(text, isIn: true);
            _fadeInInput.FocusExited += () => OnFadeSubmitted(_fadeInInput.Text, isIn: true);
        }
        if (_fadeOutInput != null)
        {
            _fadeOutInput.TextSubmitted += text => OnFadeSubmitted(text, isIn: false);
            _fadeOutInput.FocusExited += () => OnFadeSubmitted(_fadeOutInput.Text, isIn: false);
        }
        _outputOptionButton.ItemSelected += OutputOptionSelected;

        // Undo/redo and other model restores push this signal; rebind component + refresh UI.
        _globalSignals.SyncShellInspector += OnSyncFromHistory;
        
        UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);
        
        GetNode<Label>("%InfoLabel").AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
        // Ensure content is hidden at start up
        _inspectorContent.Visible = false;
        _selectFileContainer.Visible = false;
        _routingAccordian.Visible = false;
        _routingContainer.Visible = false;
        _waveformAccordian.Visible = false;
        
        _routingCollapseButton.Pressed += () => ToggleAccordian(_routingAccordian, _routingCollapseButton);
        _waveformCollapseButton.Pressed += () => ToggleAccordian(_waveformAccordian, _waveformCollapseButton);
        _buttonSelectFile.Pressed += OpenFileDialog;
        
    }
    
    /// <summary>
    /// Handles submission of time fields (start/end). Parses input, updates component, and recalculates duration.
    /// Blank or -1 input sets time to undefined (EndTime=-1, StartTime=0).
    /// End times at or beyond file duration are clamped to full duration (EndTime=-1).
    /// </summary>
    /// <param name="text">The submitted text.</param>
    /// <param name="textField">The LineEdit field.</param>
    private void TimeFieldSubmitted(string text, LineEdit textField)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0 || textField == null)
            return;
        if (_isSyncingUi || _globalData?.HistoryManager?.IsRestoring == true)
            return;

        try
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-1")
            {
                if (textField == _startTimeInput)
                {
                    if (targets.All(t => Math.Abs(t.Component.StartTime) < 1e-9))
                        return;
                    RecordAudioHistory("Edit audio start time");
                    foreach (var (_, comp) in targets)
                        comp.StartTime = 0.0;
                    textField.Text = "00:00.000";
                    textField.TooltipText = "00m:00s.000ms";
                }
                else if (textField == _endTimeInput)
                {
                    if (targets.All(t => t.Component.EndTime < 0))
                        return;
                    RecordAudioHistory("Edit audio end time");
                    foreach (var (_, comp) in targets)
                        comp.EndTime = -1.0;
                    double metaDur = _focusedAudioComponent?.Metadata?.Duration ?? 0;
                    textField.Text = $"Full ({UiUtilities.FormatTime(metaDur)})";
                    textField.TooltipText = "End time undefined (plays full file)";
                }

                SyncDuration();
                if (textField.HasFocus())
                    textField.ReleaseFocus();
                DrawWaveform();
                return;
            }

            var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out string labeledTime, out bool isValid);

            if (!isValid || string.IsNullOrEmpty(time))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid time format in {textField.Name}: {text}", 1);
                // Always re-sanitize the LineEdit so invalid text (e.g. "4'") cannot stick.
                RestoreAudioTimeFieldDisplay(textField);
                if (textField.HasFocus())
                    textField.ReleaseFocus();
                return;
            }

            if (textField == _startTimeInput)
            {
                if (targets.All(t => Math.Abs(t.Component.StartTime - timeSecs) < 1e-9))
                {
                    textField.Text = time;
                    textField.TooltipText = labeledTime;
                    return;
                }
                RecordAudioHistory("Edit audio start time");
                foreach (var (_, comp) in targets)
                    comp.StartTime = timeSecs;
            }
            else if (textField == _endTimeInput)
            {
                // At or beyond each file's duration = play to end for that target.
                bool anyChange = false;
                foreach (var (_, comp) in targets)
                {
                    double fileDuration = comp.Metadata?.Duration ?? 0;
                    if (fileDuration > 0 && timeSecs >= fileDuration)
                    {
                        if (comp.EndTime >= 0)
                            anyChange = true;
                    }
                    else if (Math.Abs(comp.EndTime - timeSecs) >= 1e-9)
                    {
                        anyChange = true;
                    }
                }

                if (!anyChange)
                {
                    textField.Text = time;
                    textField.TooltipText = labeledTime;
                    return;
                }

                RecordAudioHistory("Edit audio end time");
                foreach (var (_, comp) in targets)
                {
                    double fileDuration = comp.Metadata?.Duration ?? 0;
                    if (fileDuration > 0 && timeSecs >= fileDuration)
                        comp.EndTime = -1.0;
                    else
                        comp.EndTime = timeSecs;
                }

                double primaryMeta = _focusedAudioComponent?.Metadata?.Duration ?? 0;
                if (primaryMeta > 0 && timeSecs >= primaryMeta)
                {
                    textField.Text = $"Full ({UiUtilities.FormatTime(primaryMeta)})";
                    textField.TooltipText = "End time undefined (plays full file)";
                }
                else
                {
                    textField.Text = time;
                    textField.TooltipText = labeledTime;
                }

                SyncDuration();
                if (textField.HasFocus())
                    textField.ReleaseFocus();
                DrawWaveform();
                return;
            }

            textField.Text = time;
            textField.TooltipText = labeledTime;

            SyncDuration();
            if (textField.HasFocus())
                textField.ReleaseFocus();
            DrawWaveform();
        }
        catch (Exception ex)
        {
            GD.Print($"AudioInspector:TimeFieldSubmitted - Error parsing time: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing time: {ex.Message}", 2);
            RestoreAudioTimeFieldDisplay(textField);
            if (textField != null && textField.HasFocus())
                textField.ReleaseFocus();
        }
    }

    /// <summary>
    /// Writes the current model start/end time back into a time LineEdit (after invalid input).
    /// </summary>
    private void RestoreAudioTimeFieldDisplay(LineEdit textField)
    {
        if (textField == null || _focusedAudioComponent == null)
            return;

        if (textField == _startTimeInput)
        {
            string formatted = UiUtilities.FormatTime(_focusedAudioComponent.StartTime);
            textField.Text = formatted;
            UiUtilities.ParseAndFormatTime(formatted, out _, out string labeled, out _);
            textField.TooltipText = labeled;
        }
        else if (textField == _endTimeInput)
        {
            double metaDur = _focusedAudioComponent.Metadata?.Duration ?? 0;
            if (_focusedAudioComponent.EndTime < 0)
            {
                textField.Text = $"Full ({UiUtilities.FormatTime(metaDur)})";
                textField.TooltipText = "End time undefined (plays full file)";
            }
            else
            {
                string formatted = UiUtilities.FormatTime(_focusedAudioComponent.EndTime);
                textField.Text = formatted;
                UiUtilities.ParseAndFormatTime(formatted, out _, out string labeled, out _);
                textField.TooltipText = labeled;
            }
        }
    }

    private void OnLoopToggled(bool state)
    {
        if (_isSyncingUi) return;
        var targets = GetAudioTargets();
        if (targets.Count == 0) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (targets.All(t => t.Component.Loop == state)) return;
        RecordAudioHistory("Edit audio loop");
        foreach (var (_, comp) in targets)
            comp.Loop = state;
        SyncDuration();
    }

    /// <summary>
    /// Re-binds the audio component from the live cue and refreshes fields (undo/redo, external edits).
    /// </summary>
    private async void OnSyncFromHistory()
    {
        // SyncShellInspector is global (shell pre-wait edits, etc.). Skip if this inspector
        // is not in the live tree (tab not built / freed) to avoid get_node absolute-path errors.
        if (!IsInsideTree()) return;

        // Multi-edit / full rebind after cuelist restore.
        if (_focusedCue != null || InspectorMultiEditSupport.ShouldUseMultiEdit(_globalData))
        {
            int cueId = _focusedCue?.Id ?? _globalData?.FocusedCue ?? -1;
            if (cueId >= 0 || InspectorMultiEditSupport.ShouldUseMultiEdit(_globalData))
            {
                ShellSelected(cueId >= 0 ? cueId : (_globalData?.FocusedCue ?? -1));
                return;
            }
        }

        if (_focusedCue == null) return;
        // Re-fetch cue in case instance was replaced (cuelist-scope restore).
        var cue = CueList.FetchCueFromId(_focusedCue.Id);
        if (cue == null)
        {
            _focusedCue = null;
            _focusedAudioComponent = null;
            return;
        }
        _focusedCue = cue;
        _focusedAudioComponent = cue.GetAudioComponent();
        if (_focusedAudioComponent == null)
        {
            _infoLabel.Text = "No Audio File";
            _selectFileContainer.Visible = true;
            _inspectorContent.Visible = false;
            _fileUrl.Text = "";
            if (_deleteAudioComponentButton != null)
                _deleteAudioComponentButton.Visible = false;
            return;
        }

        UpdateAudioUiFields(_focusedAudioComponent.AudioFile ?? string.Empty);
        // Output routing is not part of the scalar time fields — refresh dropdown + matrix too.
        PopulateOutputOptions();
        // Heavy matrix rebuild only when the routing UI is actually visible (avoid thrashing
        // on every shell pre/post-wait keystroke commit while Audio tab is inactive).
        if (_routingContainer != null && _routingContainer.Visible)
            BuildRoutingMatrix();

        // History snapshots omit WaveformData; invalidate cache and regenerate peaks so start/end
        // selection colors + handles redraw after undo/redo.
        _cachedPeaks = null;
        _cachedPeaksSource = null;
        _isDraggingStart = false;
        _isDraggingEnd = false;

        if (!string.IsNullOrEmpty(_focusedAudioComponent.AudioFile)
            && (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0))
        {
            try
            {
                _focusedAudioComponent.WaveformData =
                    await _mediaEngine.GenerateWaveformAsync(_focusedAudioComponent.AudioFile);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AudioInspector:OnSyncFromHistory - Waveform regen failed: {ex.Message}");
            }
        }

        if (_focusedAudioComponent.Metadata == null && !string.IsNullOrEmpty(_focusedAudioComponent.AudioFile))
        {
            try
            {
                _focusedAudioComponent.Metadata =
                    await _mediaEngine.GetAudioFileMetadataAsync(_focusedAudioComponent.AudioFile);
                // Channel count now known — pan is stereo-only.
                UpdatePanUiVisibilityAndValues();
                RefreshRoutingInputPanLabels();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AudioInspector:OnSyncFromHistory - Metadata refresh failed: {ex.Message}");
            }
        }
        else
        {
            UpdatePanUiVisibilityAndValues();
        }

        await DrawWaveform();
    }
    
    
    /// <summary>
    /// Handles volume input submission. Converts dB to linear, updates component, and formats display.
    /// </summary>
    /// <param name="text">The submitted text.</param>
    /// <param name="textField">The LineEdit field.</param>
    private void VolumeInputSubmitted(string text, LineEdit textField)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0 || textField == null) return;
        if (_isSyncingUi || _globalData?.HistoryManager?.IsRestoring == true) return;
        try
        {
            if (!float.TryParse(text.Replace("dB", "").Trim(), out var dbValue))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid volume format: {text}", 1);
                UpdateAudioUiFields(_focusedAudioComponent?.AudioFile ?? string.Empty);
                if (textField.HasFocus()) textField.ReleaseFocus();
                return;
            }
            if (dbValue > 0)
            {
                dbValue = -dbValue;
            }
            var volume = UiUtilities.DbToLinear(dbValue.ToString());
            var dbReturn = UiUtilities.LinearToDb(volume);
            textField.Text = $"{dbReturn}dB";
            if (targets.All(t => Math.Abs(t.Component.Volume - volume) < 1e-6f))
            {
                if (textField.HasFocus()) textField.ReleaseFocus();
                return;
            }
            RecordAudioHistory("Edit audio volume");
            foreach (var (_, comp) in targets)
                comp.Volume = volume;
            if (textField.HasFocus()) textField.ReleaseFocus();
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing volume: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// True when pan UI should be shown (stereo source only).
    /// </summary>
    private bool IsStereoSource =>
        _focusedAudioComponent?.Metadata != null && _focusedAudioComponent.Metadata.Channels == 2;

    /// <summary>
    /// Shows or hides pan controls and syncs slider/text from the component.
    /// </summary>
    private void UpdatePanUiVisibilityAndValues()
    {
        bool show = IsStereoSource;
        if (_panLabel != null) _panLabel.Visible = show;
        if (_panSlider != null) _panSlider.Visible = show;
        if (_panInput != null) _panInput.Visible = show;
        if (!show || _focusedAudioComponent == null) return;
        SyncPanUiFromComponent();
    }

    /// <summary>
    /// Writes pan slider and text from <see cref="AudioComponent.Pan"/> without firing handlers.
    /// </summary>
    private void SyncPanUiFromComponent()
    {
        if (_focusedAudioComponent == null) return;
        _isUpdatingPanUi = true;
        try
        {
            float pan = Mathf.Clamp(_focusedAudioComponent.Pan, -1f, 1f);
            if (_panSlider != null)
                _panSlider.SetValueNoSignal(Mathf.Round(pan * 100f));
            if (_panInput != null && !_panInput.HasFocus())
                _panInput.Text = UiUtilities.FormatPan(pan);
        }
        finally
        {
            _isUpdatingPanUi = false;
        }
    }

    private void OnPanSliderChanged(double value)
    {
        if (_isUpdatingPanUi || _isSyncingUi) return;
        var targets = GetAudioTargets();
        if (targets.Count == 0) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (!IsStereoSource) return;

        float pan = Mathf.Clamp((float)value / 100f, -1f, 1f);
        if (targets.All(t => Math.Abs(t.Component.Pan - pan) < 1e-6f)) return;

        RecordAudioHistory("Edit audio pan", AudioCoalesceKey("pan"));
        foreach (var (_, comp) in targets)
            comp.Pan = pan;

        _isUpdatingPanUi = true;
        try
        {
            if (_panInput != null)
                _panInput.Text = UiUtilities.FormatPan(pan);
        }
        finally
        {
            _isUpdatingPanUi = false;
        }
        RefreshRoutingInputPanLabels();
    }

    private void OnPanSliderDragEnded(bool valueChanged)
    {
        var key = AudioCoalesceKey("pan");
        if (!string.IsNullOrEmpty(key))
            _globalData?.HistoryManager?.EndCoalesceSession(key);
    }

    /// <summary>
    /// Commits pan from the text field (C, L50, R25, −100…100).
    /// </summary>
    private void PanInputSubmitted(string text)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0 || _panInput == null) return;
        if (_globalData?.HistoryManager?.IsRestoring == true) return;
        if (_isUpdatingPanUi || _isSyncingUi) return;
        if (!IsStereoSource)
        {
            if (_panInput.HasFocus()) _panInput.ReleaseFocus();
            return;
        }

        if (!UiUtilities.TryParsePan(text, out float pan))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid pan format: {text}", 1);
            SyncPanUiFromComponent();
            if (_panInput.HasFocus()) _panInput.ReleaseFocus();
            return;
        }

        pan = Mathf.Clamp(pan, -1f, 1f);
        _panInput.Text = UiUtilities.FormatPan(pan);

        if (targets.All(t => Math.Abs(t.Component.Pan - pan) < 1e-6f))
        {
            SyncPanUiFromComponent();
            if (_panInput.HasFocus()) _panInput.ReleaseFocus();
            return;
        }

        RecordAudioHistory("Edit audio pan");
        foreach (var (_, comp) in targets)
            comp.Pan = pan;
        SyncPanUiFromComponent();
        RefreshRoutingInputPanLabels();
        if (_panInput.HasFocus()) _panInput.ReleaseFocus();
    }

    /// <summary>
    /// Updates Left/Right routing matrix row labels with the current pan status in parentheses.
    /// </summary>
    private void RefreshRoutingInputPanLabels()
    {
        if (_routingInputLabels.Count == 0 || _focusedAudioComponent == null) return;
        if (_focusedAudioComponent.Metadata?.Channels != 2) return;

        string panStatus = UiUtilities.FormatPan(_focusedAudioComponent.Pan);
        for (int i = 0; i < _routingInputLabels.Count && i < 2; i++)
        {
            var label = _routingInputLabels[i];
            if (label == null || !IsInstanceValid(label)) continue;
            string baseName = i == 0 ? "Left" : "Right";
            label.Text = $"{baseName} ({panStatus})";
        }
    }
    
    /// <summary>
    /// Handles play count submission with validation to prevent invalid integers.
    /// </summary>
    /// <param name="newText">The submitted text.</param>
    private void OnPlayCountSubmitted(string newText)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0) return;
        if (_isSyncingUi || _globalData?.HistoryManager?.IsRestoring == true) return;
        if (int.TryParse(newText, out var playCount) && playCount > 0)
        {
            if (targets.All(t => t.Component.PlayCount == playCount))
            {
                if (_playCountInput.HasFocus()) _playCountInput.ReleaseFocus();
                return;
            }
            RecordAudioHistory("Edit audio play count");
            foreach (var (_, comp) in targets)
                comp.PlayCount = playCount;
            SyncDuration();
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid play count: {newText}. Must be positive integer.", 1);
            UpdateAudioUiFields(_focusedAudioComponent?.AudioFile ?? string.Empty);
        }
        if (_playCountInput.HasFocus())
            _playCountInput.ReleaseFocus();
    }

    /// <summary>
    /// Commits fade-in or fade-out duration from a time LineEdit.
    /// </summary>
    /// <param name="text">User-entered time string.</param>
    /// <param name="isIn">True for fade-in; false for fade-out.</param>
    private void OnFadeSubmitted(string text, bool isIn)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0) return;
        if (_isSyncingUi || _globalData?.HistoryManager?.IsRestoring == true) return;

        var field = isIn ? _fadeInInput : _fadeOutInput;
        if (field == null) return;

        var formatted = UiUtilities.ParseAndFormatTime(text, out var seconds, out string labeled);
        if (string.IsNullOrEmpty(formatted))
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"Invalid audio fade time: {text}", 1);
            UpdateAudioUiFields(_focusedAudioComponent?.AudioFile ?? string.Empty);
            if (field.HasFocus()) field.ReleaseFocus();
            return;
        }

        seconds = Math.Max(0.0, seconds);
        field.Text = formatted;
        field.TooltipText = labeled + (isIn
            ? " (fade-in at play start)"
            : " (fade-out on stop)");

        bool anyChange = targets.Any(t =>
        {
            double existing = isIn ? t.Component.FadeInDuration : t.Component.FadeOutDuration;
            return !Mathf.IsEqualApprox((float)existing, (float)seconds);
        });
        if (!anyChange)
        {
            if (field.HasFocus()) field.ReleaseFocus();
            return;
        }

        RecordAudioHistory(isIn ? "Edit audio fade-in" : "Edit audio fade-out");
        foreach (var (_, comp) in targets)
        {
            if (isIn)
                comp.FadeInDuration = seconds;
            else
                comp.FadeOutDuration = seconds;
        }

        if (field.HasFocus()) field.ReleaseFocus();
    }
    
    private void PopulateOutputOptions()
    {
        if (_outputOptionButton == null || _focusedAudioComponent == null) return;

        // Keep PatchId aligned with the live Patch reference (drop/create assigns both; relink/history may not).
        if (_focusedAudioComponent.Patch != null && GodotObject.IsInstanceValid(_focusedAudioComponent.Patch)
            && _focusedAudioComponent.PatchId != _focusedAudioComponent.Patch.Id)
        {
            _focusedAudioComponent.PatchId = _focusedAudioComponent.Patch.Id;
        }

        int assignedPatchId = _focusedAudioComponent.Patch?.Id ?? _focusedAudioComponent.PatchId;

        // Block ItemSelected while rebuilding the list (Select would re-enter OutputOptionSelected).
        _outputOptionButton.SetBlockSignals(true);
        try
        {
            // Remove items from output options
            var itemCount = _outputOptionButton.GetItemCount();
            for (int i = 0; i < itemCount; i++)
            {
                _outputOptionButton.RemoveItem(_outputOptionButton.GetItemCount() - 1); // Removes last item
            }

            // Add patches as options
            _outputOptionButton.AddItem("No output");
            int selectedIndex = 0;

            foreach (var patch in _globalData.Settings.GetAudioOutputPatches())
            {
                _outputOptionButton.AddItem($"Patch: {patch.Value.Name}");
                int idx = _outputOptionButton.GetItemCount() - 1;
                _outputOptionButton.SetItemMetadata(idx, patch.Value.Id);
                if (patch.Value.Id == assignedPatchId)
                    selectedIndex = idx;
            }

            foreach (var output in _audioDevices.GetAvailableAudioDeviceNames())
            {
                _outputOptionButton.AddItem($"Direct Output: {output}");
                int idx = _outputOptionButton.GetItemCount() - 1;
                if (!string.IsNullOrEmpty(_focusedAudioComponent.DirectOutput)
                    && output == _focusedAudioComponent.DirectOutput)
                {
                    selectedIndex = idx;
                }
            }

            if (selectedIndex == 0 && !string.IsNullOrEmpty(_focusedAudioComponent.DirectOutput))
            {
                _outputOptionButton.AddItem($"!!! Missing output: {_focusedAudioComponent.DirectOutput}");
                selectedIndex = _outputOptionButton.GetItemCount() - 1;
            }
            if (selectedIndex == 0 && assignedPatchId >= 0
                && (_focusedAudioComponent.Patch != null
                    || !_globalData.Settings.GetAudioOutputPatches().ContainsKey(assignedPatchId)))
            {
                string name = _focusedAudioComponent.Patch?.Name ?? $"id {assignedPatchId}";
                _outputOptionButton.AddItem($"!!! Missing patch: {name}");
                selectedIndex = _outputOptionButton.GetItemCount() - 1;
            }

            _outputOptionButton.Select(selectedIndex);
        }
        finally
        {
            _outputOptionButton.SetBlockSignals(false);
        }
    }
    
    private void OutputOptionSelected(long index)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0 || _focusedAudioComponent == null) return;
        if (_isSyncingUi || _globalData?.HistoryManager?.IsRestoring == true) return;

        var item = _outputOptionButton.GetItemText((int)index);

        // Resolve intended new routing without writing yet (so we can skip no-ops).
        int newPatchId = -1;
        string newDirect = null;
        AudioOutputPatch newPatch = null;

        if (item.StartsWith("Patch"))
        {
            newPatchId = (int)_outputOptionButton.GetItemMetadata((int)index);
            if (_globalData.Settings.GetAudioOutputPatches().TryGetValue(newPatchId, out var patch))
            {
                newPatch = patch;
                newDirect = null;
            }
            else
            {
                newPatchId = -1;
                newPatch = null;
                newDirect = null;
            }
        }
        else if (item.StartsWith("Direct Output"))
        {
            newDirect = item.Replace("Direct Output: ", "");
            newPatchId = -1;
            newPatch = null;
        }
        else if (item.StartsWith("!!! Missing"))
        {
            // Keep current assignment when user re-selects a missing entry.
            return;
        }
        else
        {
            // "No output"
            newPatchId = -1;
            newPatch = null;
            newDirect = null;
        }

        // No-op when every target already has this routing.
        bool unchanged = targets.All(t =>
            newPatchId == t.Component.PatchId
            && string.Equals(
                newDirect ?? string.Empty,
                t.Component.DirectOutput ?? string.Empty,
                StringComparison.Ordinal));
        if (unchanged)
            return;

        // Discrete selection — do not coalesce; each change is its own undo step.
        RecordAudioHistory("Edit audio output");

        void ApplyRouting(AudioComponent comp)
        {
            if (item.StartsWith("Patch"))
            {
                if (newPatch != null)
                {
                    comp.Patch = newPatch;
                    comp.PatchId = newPatchId;
                    comp.DirectOutput = null;
                }
                else
                {
                    comp.Patch = null;
                    comp.PatchId = -1;
                    comp.DirectOutput = null;
                }
            }
            else if (item.StartsWith("Direct Output"))
            {
                comp.DirectOutput = newDirect;
                comp.Patch = null;
                comp.PatchId = -1;
            }
            else
            {
                comp.Patch = null;
                comp.PatchId = -1;
                comp.DirectOutput = null;
            }
        }

        foreach (var (_, comp) in targets)
            ApplyRouting(comp);

        if (item.StartsWith("Patch") && newPatch == null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"AudioInspector:OutputOptionSelected - Patch ID {newPatchId} not found, resetting output", 1);
            _outputOptionButton.SetBlockSignals(true);
            _outputOptionButton.Select(0);
            _outputOptionButton.SetBlockSignals(false);
        }

        // Routing matrix reflects primary target only.
        BuildRoutingMatrix();

        // Refresh shell ✕ for output not assigned / missing
        foreach (var (cue, _) in targets)
            GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(cue.Id);
    }

    
    /// <summary>
    /// Builds the per-cue routing matrix grid based on selected output (patch or direct).
    /// </summary>
    private async void BuildRoutingMatrix()
    {
        if (!IsInsideTree() || _routingMatrixGrid == null)
            return;

        int gen = _shellSelectGeneration;
        _routingInputLabels.Clear();
        foreach (var child in _routingMatrixGrid.GetChildren())
        {
            child.QueueFree();
        }

        if (_focusedAudioComponent == null)
        {
            GD.Print($"AudioInspector:BuildRoutingMatrix - No focused audio component");
            if (_routingContainer != null)
                _routingContainer.Visible = false;
            return;
        }

        var tree = GetTree();
        if (tree == null)
            return;

        await ToSignal(tree, "process_frame"); // Wait a frame for existing children to fully clear.
        if (!IsInsideTree())
            return;

        // Selection may have changed while waiting (multi-select focus flood).
        if (gen != _shellSelectGeneration || _focusedAudioComponent == null)
            return;
        if (_focusedAudioComponent.Metadata == null)
        {
            GD.Print("AudioInspector:BuildRoutingMatrix - Metadata not ready; skipping matrix.");
            if (_routingContainer != null)
                _routingContainer.Visible = false;
            return;
        }
        
        // Get ins and outs data
        var inputChannels = _focusedAudioComponent.Metadata.Channels;
        var inputLabels = GetChannelLabels(inputChannels, isInput: true);

        int outputChannels;
        List<string> outputLabels = new List<string>();
        
        // Prefer live Patch reference, then PatchId (default patch on create sets both).
        if (_focusedAudioComponent.Patch != null && GodotObject.IsInstanceValid(_focusedAudioComponent.Patch)
            && _focusedAudioComponent.PatchId != _focusedAudioComponent.Patch.Id)
        {
            _focusedAudioComponent.PatchId = _focusedAudioComponent.Patch.Id;
        }

        // Audio Output Patch
        if (_focusedAudioComponent.PatchId != -1 || _focusedAudioComponent.Patch != null)
        {
            AudioOutputPatch patch = _focusedAudioComponent.Patch;
            if (patch == null || !GodotObject.IsInstanceValid(patch))
            {
                _globalData.Settings.GetAudioOutputPatches()
                    .TryGetValue(_focusedAudioComponent.PatchId, out patch);
            }

            // Check if selected patch exists, if not clean the audio component of it.
            if (patch == null || !GodotObject.IsInstanceValid(patch))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:BuildRoutingMatrix - Patch ID {_focusedAudioComponent.PatchId} not found, resetting output", 2);
                _focusedAudioComponent.Patch = null;
                _focusedAudioComponent.PatchId = -1;
                _focusedAudioComponent.Routing = null;
                PopulateOutputOptions(); // Refresh UI to reflect missing patch
                _routingContainer.Visible = false;
                return;
            }

            _focusedAudioComponent.Patch = patch;
            _focusedAudioComponent.PatchId = patch.Id;
            outputChannels = patch.Channels.Count;
            outputLabels = patch.Channels.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        }
        
        // Direct output
        else if (!string.IsNullOrEmpty(_focusedAudioComponent.DirectOutput))
        {
            var device = _audioDevices.OpenAudioDevice(_focusedAudioComponent.DirectOutput, out var _);
            if (device == null)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:BuildRoutingMatrix - Direct output device not found: {_focusedAudioComponent.DirectOutput}", 2);
                _focusedAudioComponent.DirectOutput = null;
                PopulateOutputOptions(); // Refresh UI to reflect missing output
                _routingContainer.Visible = false;
                return;
            }
            outputChannels = device.Channels;
            for (int i = 0; i < outputChannels; i++)
            {
                outputLabels.Add($"Channel {i}");
            }
        }
        else
        {
            GD.Print($"AudioInspector:BuildRoutingMatrix - No output selected");
            _routingContainer.Visible = false;
            return; // No output selected
        }

        
        // Validate routing (CuePatch) matches what is expected
        var routing = _focusedAudioComponent.Routing;
        bool needsUpdate = routing == null ||
                           routing.OutputChannels != outputChannels ||
                           !routing.OutputLabels.SequenceEqual(outputLabels) ||
                           routing.InputChannels != inputChannels ||
                           !routing.InputLabels.SequenceEqual(inputLabels);
        
        if (needsUpdate)
        {
            // Preserve old volumes if possible
            var oldRouting = routing;

            // Create new CuePatch with current dimensions
            routing = new CuePatch(inputChannels, inputLabels, outputChannels, outputLabels);
            _focusedAudioComponent.Routing = routing;

            if (oldRouting != null)
            {
                // Copy over existing volumes for overlapping channels
                int copyInputs = Math.Min(oldRouting.InputChannels, inputChannels);
                int copyOutputs = Math.Min(oldRouting.OutputChannels, outputChannels);

                for (int i = 0; i < copyInputs; i++)
                {
                    for (int j = 0; j < copyOutputs; j++)
                    {
                        routing.SetVolume(i, j, oldRouting.GetVolume(i, j));
                    }
                }
            }

            GD.Print($"AudioInspector:BuildRoutingMatrix - Resized/created CuePatch to inputs: {inputChannels}, outputs: {outputChannels}"); //!!!
        }
        
        
        
        // Set grid columns: outputChannels + 1 (for input labels)
        _routingMatrixGrid.Columns = outputChannels + 1;
        
        // Add header row: empty + output labels
        _routingMatrixGrid.AddChild(new Label { Text = ""}); // Corner
        foreach (var outLabel in outputLabels)
        {
            var label = new Label { Text = outLabel };
            _routingMatrixGrid.AddChild(label);
        }
        
        // Add rows: input label (+ pan status for stereo) + volume fields
        string panStatus = inputChannels == 2
            ? UiUtilities.FormatPan(_focusedAudioComponent.Pan)
            : null;
        for (int row = 0; row < inputChannels; row++)
        {
            string labelText = inputLabels[row];
            if (panStatus != null && row < 2)
                labelText = $"{labelText} ({panStatus})";
            var inLabel = new Label { Text = labelText };
            _routingMatrixGrid.AddChild(inLabel);
            _routingInputLabels.Add(inLabel);
            
            for (int col = 0; col < outputChannels; col++)
            {
                var volumeEdit = new LineEdit();
                var linearVol = _focusedAudioComponent.Routing.GetVolume(row, col);
                if (linearVol > 0.0f)
                {
                    var dbVol = UiUtilities.LinearToDb(linearVol);
                    volumeEdit.Text = $"{dbVol}dB";
                }

                var row1 = row;
                var col1 = col;
                volumeEdit.TextSubmitted += (string newText) => OnMatrixVolumeSubmitted(newText, volumeEdit, row1, col1);
                volumeEdit.FocusExited += () => OnMatrixVolumeSubmitted(volumeEdit.Text, volumeEdit, row1, col1);
                LineEditDbDragSlider.EnableVolume(volumeEdit);
                _routingMatrixGrid.AddChild(volumeEdit);
            }
        }
        _routingContainer.Visible = true;

    }

    /// <summary>
    /// Handles matrix volume submission. Converts dB to linear and updates CuePatch.
    /// </summary>
    /// <param name="text">Submitted text.</param>
    /// <param name="textField">LineEdit field.</param>
    /// <param name="inputCh">Input channel index.</param>
    /// <param name="outputCh">Output channel index.</param>
    private void OnMatrixVolumeSubmitted(string text, LineEdit textField, int inputCh, int outputCh)
    {
        if (_focusedCue == null || _focusedAudioComponent?.Routing == null || textField == null)
            return;
        if (_globalData?.HistoryManager?.IsRestoring == true)
            return;

        GD.Print($"AudioInspector:OnMatrixVolumeSubmitted - In {inputCh}. Out {outputCh}");
        try
        {
            float dbValue;
            if (string.IsNullOrWhiteSpace((text ?? string.Empty).Replace("dB", "").Trim()))
            {
                dbValue = -60.0f;
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Blank input treated as OFF for In {inputCh}, Out {outputCh}", 0);
            }
            else if (!float.TryParse((text ?? string.Empty).Replace("dB", "").Trim(), out dbValue))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Invalid matrix volume: {text}", 1);
                return;
            }

            float linear = (float)UiUtilities.DbToLinear(dbValue.ToString());
            float current = _focusedAudioComponent.Routing.GetVolume(inputCh, outputCh);
            if (Math.Abs(current - linear) < 1e-6f)
            {
                if (linear > 0.0f)
                    textField.Text = $"{UiUtilities.LinearToDb(linear)}dB";
                if (textField.HasFocus())
                    textField.ReleaseFocus();
                return;
            }

            // Discrete cell commit — each matrix cell change is its own undo step.
            _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit audio routing volume");
            _focusedAudioComponent.Routing.SetVolume(inputCh, outputCh, linear);
            if (linear > 0.0f)
            {
                var dbReturn = UiUtilities.LinearToDb(linear);
                textField.Text = $"{dbReturn}dB";
            }
            else
            {
                textField.Text = string.Empty;
            }
            if (textField.HasFocus())
                textField.ReleaseFocus();
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Error: {ex.Message}", 2);
        } 
    }



    /// <summary>
    /// Gets standard channel labels based on count. For inputs (audio file) or outputs (patch/device).
    /// </summary>
    /// <param name="count">Number of channels.</param>
    /// <param name="isInput">True for input labels.</param>
    /// <returns>List of labels.</returns>
    private List<string> GetChannelLabels(int count, bool isInput) // New helper
    {
        return count switch
        {
            1 => new List<string> { "Mono" },
            2 => new List<string> { "Left", "Right" },
            4 => new List<string> { "Front Left", "Front Right", "Rear Left", "Rear Right" }, // Quad
            6 => new List<string> { "Front Left", "Front Right", "Center", "LFE", "Surround Left", "Surround Right" }, // 5.1
            8 => new List<string> { "Front Left", "Front Right", "Center", "LFE", "Surround Left", "Surround Right", "Surround Back Left", "Surround Back Right" }, // 7.1
            _ => Enumerable.Range(1, count).Select(i => $"Ch {i}").ToList() // Fallback for others
        };
    }
    

    private StyleBoxFlat _fileUrlMissingStyle;
    private bool _fileUrlMissing;
    private Button _deleteAudioComponentButton;

    /// <summary>
    /// Refreshes the file URL field when media paths are rewritten (e.g. after show-local backup).
    /// </summary>
    private void RefreshMediaPathDisplay()
    {
        if (!IsInsideTree() || _fileUrl == null || _focusedAudioComponent == null)
            return;

        string path = _focusedAudioComponent.AudioFile ?? string.Empty;
        if (!string.Equals(_fileUrl.Text, path, StringComparison.Ordinal))
            _fileUrl.Text = path;

        // Re-check missing state after path rewrite / backup (autoloads via SceneTree root).
        GetTree()?.Root?.GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")
            ?.CheckCue(_focusedCue?.Id ?? -1);
        ApplyFileUrlMissingStyleFromHealth();
    }

    private void OnCueMediaHealthChanged(int cueId, bool hasIssue, string message)
    {
        if (_focusedCue == null || _focusedCue.Id != cueId)
            return;
        // Only style this inspector's URL if *audio* is among the missing paths
        ApplyFileUrlMissingStyleFromHealth();
    }

    /// <summary>
    /// Styles the audio URL field only when this cue's audio path is reported missing
    /// (not when only video/other media is missing).
    /// </summary>
    private void ApplyFileUrlMissingStyleFromHealth()
    {
        if (!IsInsideTree())
            return;

        if (_focusedCue == null || _focusedAudioComponent == null ||
            string.IsNullOrWhiteSpace(_focusedAudioComponent.AudioFile))
        {
            ApplyFileUrlMissingStyle(false, null);
            return;
        }

        var health = GetTree()?.Root?.GetNodeOrNull<MediaHealthService>("/root/MediaHealthService");
        bool missing = health != null && health.IsPathMissing(_focusedCue.Id, _focusedAudioComponent.AudioFile);
        ApplyFileUrlMissingStyle(missing, missing ? "File Missing" : null);
    }

    /// <summary>
    /// Applies or clears italic + red border styling on the URL field for missing media.
    /// </summary>
    private void ApplyFileUrlMissingStyle(bool missing, string tooltip)
    {
        _fileUrlMissingStyle ??= InspectorMediaUrlStyle.CreateMissingStyle();
        InspectorMediaUrlStyle.Apply(_fileUrl, _fileUrlMissingStyle, missing, tooltip);
        _fileUrlMissing = missing;
    }

    /// <summary>
    /// Targets for the next edit: multi-edit subset, or the single focused audio component.
    /// </summary>
    private List<(Cue Cue, AudioComponent Component)> GetAudioTargets()
    {
        if (_isMultiEdit)
            return _audioTargets ?? new List<(Cue, AudioComponent)>();
        if (_focusedCue != null && _focusedAudioComponent != null)
            return new List<(Cue, AudioComponent)> { (_focusedCue, _focusedAudioComponent) };
        return new List<(Cue, AudioComponent)>();
    }

    private bool UseMultiHistory() => GetAudioTargets().Count > 1;

    /// <summary>
    /// Records history before mutating audio targets (cuelist when multi).
    /// </summary>
    private void RecordAudioHistory(string singleDescription, string coalesceKey = null)
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0)
            return;
        InspectorMultiEditSupport.RecordBeforeEdit(
            _globalData,
            UseMultiHistory(),
            targets[^1].Cue,
            singleDescription,
            "Multi-edit " + singleDescription,
            coalesceKey);
    }

    private string AudioCoalesceKey(string field) =>
        UseMultiHistory()
            ? $"multi:audio:{field}"
            : (_focusedCue != null ? $"cue:{_focusedCue.Id}:audio:{field}" : null);

    /// <summary>
    /// Called when a cue shell is selected. Updates UI based on presence of AudioComponent,
    /// including multi-edit when multiple cues are selected.
    /// </summary>
    /// <param name="cueId">The ID of the selected cue.</param>
    private async void ShellSelected(int cueId)
    {
        int gen = ++_shellSelectGeneration;

        if (cueId < 0)
        {
            _focusedCue = null;
            _focusedAudioComponent = null;
            _isMultiEdit = false;
            _audioTargets.Clear();
            _fileUrl.Text = "";
            ApplyFileUrlMissingStyle(false, null);
            if (_deleteAudioComponentButton != null)
                _deleteAudioComponentButton.Visible = false;
            _inspectorContent.Visible = false;
            _selectFileContainer.Visible = false;
            if (_infoLabel != null)
            {
                _infoLabel.Text = "";
                _infoLabel.TooltipText = "";
            }
            return;
        }

        _isMultiEdit = InspectorMultiEditSupport.ShouldUseMultiEdit(_globalData);
        if (_isMultiEdit)
        {
            await LoadMultiEditAudio(gen, cueId);
            return;
        }

        _audioTargets.Clear();

        // Only skip a full reload when we still hold a valid component reference on the same cue.
        // After undo/redo, ApplyFromData replaces component instances — early-out would leave a stale ref.
        // Still refresh output routing UI: a second ShellFocused (same cue) often hits this path while
        // the first async ShellSelected was cancelled via generation — without this, the dropdown
        // stays on "No output" even though the component already has a Default Patch assigned.
        if (_focusedCue != null && _focusedCue.Id == cueId
            && _focusedAudioComponent != null
            && _focusedCue.Components.Contains(_focusedAudioComponent))
        {
            UpdateAudioUiFields(_focusedAudioComponent.AudioFile ?? string.Empty);
            PopulateOutputOptions();
            BuildRoutingMatrix();
            return;
        }
        _focusedCue = CueList.FetchCueFromId(cueId);

        if (_focusedCue == null)
        {
            _focusedAudioComponent = null;
            ApplyFileUrlMissingStyle(false, null);
            if (_deleteAudioComponentButton != null)
                _deleteAudioComponentButton.Visible = false;
            return;
        }
        
        var hasAudio = UiUtilities.HasComponent<AudioComponent>(_focusedCue);
        if (!hasAudio) // No Audio component in Cue
        {
            _infoLabel.Text = "No Audio File";
            _infoLabel.TooltipText = "";
            _selectFileContainer.Visible = true;
            _inspectorContent.Visible = false;
            _focusedAudioComponent = null;
            _fileUrl.Text = "";
            ApplyFileUrlMissingStyle(false, null);
            if (_deleteAudioComponentButton != null)
                _deleteAudioComponentButton.Visible = false;
            return;
        }
        
        // Audio Component Found
        _focusedAudioComponent = _focusedCue.Components.OfType<AudioComponent>().First();
        if (_deleteAudioComponentButton != null)
            _deleteAudioComponentButton.Visible = true;
        var file = _focusedAudioComponent.AudioFile;
        
        if (_focusedAudioComponent.Metadata == null)
        {
            var refreshedMeta = await _mediaEngine.GetAudioFileMetadataAsync(file);
            if (gen != _shellSelectGeneration) return;
            if (_focusedAudioComponent == null) return;
            _focusedAudioComponent.Metadata = refreshedMeta;
            GD.Print("AudioInspector:ShellSelected - Refreshed metadata from file.");
        }

        if (gen != _shellSelectGeneration) return;
        
        UpdateAudioUiFields(file);
        
        PopulateOutputOptions();
        BuildRoutingMatrix();
        
        // Generate waveform data if not cached on the component
        _cachedPeaks = null;
        _cachedPeaksSource = null;
        _viewStartNorm = 0f;
        _viewSpanNorm = 1f;
        if (_zoomSlider != null) _zoomSlider.SetValueNoSignal(1);
        SyncWaveformScrollBar();
        if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
        {
            GD.Print("AudioInspector:ShellSelected - No waveform found");
            try
            {
                var wave = await _mediaEngine.GenerateWaveformAsync(_focusedAudioComponent.AudioFile);
                if (gen != _shellSelectGeneration || _focusedAudioComponent == null) return;
                _focusedAudioComponent.WaveformData = wave;
                if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
                {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Waveform generation failed for {_focusedAudioComponent.AudioFile}", 2);
                }
            }
            catch (Exception ex)
            {
                if (gen != _shellSelectGeneration) return;
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Error generating waveform: {ex.Message}", 2);
            }
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Using cached waveform for {_focusedAudioComponent.AudioFile}", 0);
        }

        if (gen != _shellSelectGeneration) return;
        await DrawWaveform();
        if (gen != _shellSelectGeneration || _focusedCue == null) return;

        // Validate media path for this cue (shell X + URL styling)
        GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
        ApplyFileUrlMissingStyleFromHealth();
        if (_deleteAudioComponentButton != null)
            _deleteAudioComponentButton.Visible = _focusedAudioComponent != null;
    }

    /// <summary>
    /// Loads multi-edit audio UI for the current selection.
    /// </summary>
    private async Task LoadMultiEditAudio(int gen, int focusedCueId)
    {
        _audioTargets = InspectorMultiEditSupport.CollectComponentTargets(c => c.GetAudioComponent());
        _focusedCue = CueList.FetchCueFromId(focusedCueId);
        _focusedAudioComponent = _focusedCue?.GetAudioComponent();
        if (_focusedAudioComponent == null && _audioTargets.Count > 0)
        {
            _focusedCue = _audioTargets[^1].Cue;
            _focusedAudioComponent = _audioTargets[^1].Component;
        }

        int selected = InspectorMultiEditSupport.GetSelectedCues().Count;
        if (_audioTargets.Count == 0)
        {
            _focusedAudioComponent = null;
            _infoLabel.Text = $"No audio on {selected} selected cue(s)";
            _infoLabel.TooltipText = "None of the selected cues have an audio component. Choose a file to add audio to all.";
            _selectFileContainer.Visible = true;
            _inspectorContent.Visible = false;
            _fileUrl.Text = "";
            ApplyFileUrlMissingStyle(false, null);
            if (_deleteAudioComponentButton != null)
                _deleteAudioComponentButton.Visible = false;
            return;
        }

        _infoLabel.Text = InspectorMultiEditSupport.FormatComponentMultiHeader("Audio", _audioTargets.Count, selected);
        _infoLabel.TooltipText = InspectorMultiEditSupport.FormatComponentMultiTooltip(
            "audio",
            _audioTargets.Select(t => (t.Cue, (object)t.Component)).ToList(),
            selected);

        // Ensure primary has metadata for waveform / pan visibility.
        if (_focusedAudioComponent != null
            && _focusedAudioComponent.Metadata == null
            && !string.IsNullOrEmpty(_focusedAudioComponent.AudioFile))
        {
            try
            {
                _focusedAudioComponent.Metadata =
                    await _mediaEngine.GetAudioFileMetadataAsync(_focusedAudioComponent.AudioFile);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AudioInspector:LoadMultiEditAudio - Metadata: {ex.Message}");
            }
            if (gen != _shellSelectGeneration) return;
        }

        if (gen != _shellSelectGeneration) return;

        UpdateAudioUiFields(_focusedAudioComponent?.AudioFile ?? string.Empty);
        PopulateOutputOptions();
        // Routing is primary-only in multi-edit (channel layouts may differ).
        BuildRoutingMatrix();

        _cachedPeaks = null;
        _cachedPeaksSource = null;
        _viewStartNorm = 0f;
        _viewSpanNorm = 1f;
        if (_zoomSlider != null) _zoomSlider.SetValueNoSignal(1);
        SyncWaveformScrollBar();

        if (_focusedAudioComponent != null
            && !string.IsNullOrEmpty(_focusedAudioComponent.AudioFile)
            && (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0))
        {
            try
            {
                _focusedAudioComponent.WaveformData =
                    await _mediaEngine.GenerateWaveformAsync(_focusedAudioComponent.AudioFile);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AudioInspector:LoadMultiEditAudio - Waveform: {ex.Message}");
            }
        }

        if (gen != _shellSelectGeneration) return;
        await DrawWaveform();
        if (gen != _shellSelectGeneration) return;

        if (_focusedCue != null)
            GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
        ApplyFileUrlMissingStyleFromHealth();
        if (_deleteAudioComponentButton != null)
        {
            _deleteAudioComponentButton.Visible = true;
            _deleteAudioComponentButton.TooltipText =
                $"Remove audio from {_audioTargets.Count} cue(s)";
        }
    }

    /// <summary>
    /// Removes the audio component from edit targets (all multi-edit targets, or focused cue).
    /// </summary>
    private void OnDeleteAudioComponentPressed()
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0)
            return;

        RecordAudioHistory("Remove audio component");
        foreach (var (cue, comp) in targets)
        {
            cue.RemoveICueComponent(comp);
            cue.CalculateTotalDuration();
            GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(cue.Id);
            _globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
        }

        _focusedAudioComponent = null;
        _audioTargets.Clear();
        _fileUrl.Text = "";
        ApplyFileUrlMissingStyle(false, null);
        if (_deleteAudioComponentButton != null)
            _deleteAudioComponentButton.Visible = false;

        _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
            $"Removed audio component from {targets.Count} cue(s)", 0);
        // Re-enter selection path for multi empty / single empty.
        if (_focusedCue != null)
            ShellSelected(_focusedCue.Id);
        else
        {
            _inspectorContent.Visible = false;
            _infoLabel.Text = "No Audio File";
        }
    }

    /// <summary>
    /// Updates the audio-related UI fields from the current AudioComponent state
    /// (or multi-edit uniform / blank values).
    /// </summary>
    /// <param name="file">Fallback file path when not multi-editing.</param>
    private void UpdateAudioUiFields(string file)
    {
        var targets = GetAudioTargets();
        _selectFileContainer.Visible = true;
        _inspectorContent.Visible = targets.Count > 0;
        if (_deleteAudioComponentButton != null)
            _deleteAudioComponentButton.Visible = targets.Count > 0;

        if (targets.Count == 0)
            return;

        _isSyncingUi = true;
        try
        {
            // File path
            if (InspectorMultiEditSupport.TryGetUniformString(
                    targets.Select(t => t.Component.AudioFile ?? string.Empty), out string path))
            {
                _fileUrl.Text = path;
                _fileUrl.PlaceholderText = string.Empty;
            }
            else
            {
                _fileUrl.Text = string.Empty;
                _fileUrl.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
            }

            if (!_isMultiEdit && _infoLabel != null)
            {
                _infoLabel.Text = "";
                _infoLabel.TooltipText = "";
            }

            ApplyFileUrlMissingStyleFromHealth();

            // Start time
            if (InspectorMultiEditSupport.TryGetUniformDouble(targets.Select(t => t.Component.StartTime), out double start))
            {
                _startTimeInput.Text =
                    UiUtilities.ParseAndFormatTime(start.ToString(), out _, out string startTip);
                _startTimeInput.TooltipText = startTip;
                _startTimeInput.PlaceholderText = string.Empty;
            }
            else
            {
                _startTimeInput.Text = string.Empty;
                _startTimeInput.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
            }

            // End time
            if (InspectorMultiEditSupport.TryGetUniformDouble(targets.Select(t => t.Component.EndTime), out double end))
            {
                double metaDur = _focusedAudioComponent?.Metadata?.Duration ?? 0;
                if (end < 0)
                    _endTimeInput.Text = $"Full ({UiUtilities.FormatTime(metaDur)})";
                else
                    _endTimeInput.Text = UiUtilities.FormatTime(end);
                _endTimeInput.PlaceholderText = string.Empty;
            }
            else
            {
                _endTimeInput.Text = string.Empty;
                _endTimeInput.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
            }

            // Duration / file duration from primary when multi
            double primaryMeta = _focusedAudioComponent?.Metadata?.Duration ?? 0;
            if (InspectorMultiEditSupport.TryGetUniformDouble(targets.Select(t => t.Component.Duration), out double dur))
                _durationValue.Text = UiUtilities.FormatTime(dur);
            else
            {
                _durationValue.Text = string.Empty;
                _durationValue.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
            }

            _fileDurationValue.Text = UiUtilities.FormatTime(primaryMeta);

            if (InspectorMultiEditSupport.TryGetUniform(targets.Select(t => t.Component.Loop), out bool loop))
                _loopInput.SetPressedNoSignal(loop);
            else
                _loopInput.SetPressedNoSignal(false);

            if (InspectorMultiEditSupport.TryGetUniform(targets.Select(t => t.Component.PlayCount), out int playCount))
            {
                _playCountInput.Text = playCount.ToString();
                _playCountInput.PlaceholderText = string.Empty;
            }
            else
            {
                _playCountInput.Text = string.Empty;
                _playCountInput.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
            }

            if (InspectorMultiEditSupport.TryGetUniformDouble(targets.Select(t => t.Component.Volume), out double vol))
            {
                var volumeDb = UiUtilities.LinearToDb((float)vol);
                _volumeInput.Text = $"{volumeDb}dB";
                _volumeInput.PlaceholderText = string.Empty;
            }
            else
            {
                _volumeInput.Text = string.Empty;
                _volumeInput.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
            }

            if (_fadeInInput != null)
            {
                if (InspectorMultiEditSupport.TryGetUniformDouble(
                        targets.Select(t => t.Component.FadeInDuration), out double fadeIn))
                {
                    _fadeInInput.Text = UiUtilities.FormatTime(fadeIn);
                    _fadeInInput.PlaceholderText = string.Empty;
                }
                else
                {
                    _fadeInInput.Text = string.Empty;
                    _fadeInInput.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
                }
            }

            if (_fadeOutInput != null)
            {
                if (InspectorMultiEditSupport.TryGetUniformDouble(
                        targets.Select(t => t.Component.FadeOutDuration), out double fadeOut))
                {
                    _fadeOutInput.Text = UiUtilities.FormatTime(fadeOut);
                    _fadeOutInput.PlaceholderText = string.Empty;
                }
                else
                {
                    _fadeOutInput.Text = string.Empty;
                    _fadeOutInput.PlaceholderText = InspectorMultiEditSupport.MultiPlaceholder;
                }
            }

            UpdatePanUiVisibilityAndValues();
        }
        finally
        {
            _isSyncingUi = false;
        }
    }

    private void StyleWaveformHandles()
    {
        // Wider hit targets; colors match markers (cyan start / orange end)
        _startDragHandle.CustomMinimumSize = new Vector2(10, 0);
        _endDragHandle.CustomMinimumSize = new Vector2(10, 0);
        _startDragHandle.Modulate = GlobalStyles.LowColor1;
        _endDragHandle.Modulate = GlobalStyles.HighColor1;
        _startDragHandle.TooltipText = "Start time (drag)";
        _endDragHandle.TooltipText = "End time (drag)";
    }

    private void OnZoomChanged(double value)
    {
        float zoom = Mathf.Max(1f, (float)value);
        float oldSpan = _viewSpanNorm;
        float center = _viewStartNorm + oldSpan * 0.5f;
        _viewSpanNorm = 1f / zoom;
        _viewStartNorm = Mathf.Clamp(center - _viewSpanNorm * 0.5f, 0f, 1f - _viewSpanNorm);
        SyncWaveformScrollBar();
        _ = DrawWaveform();
    }

    private void OnWaveformScrollChanged(double value)
    {
        float maxStart = Math.Max(0f, 1f - _viewSpanNorm);
        _viewStartNorm = maxStart <= 0 ? 0 : Mathf.Clamp((float)value, 0f, maxStart);
        _ = DrawWaveform();
    }

    private void SyncWaveformScrollBar()
    {
        if (_waveformScroll == null) return;
        bool zoomed = _viewSpanNorm < 0.999f;
        _waveformScroll.Visible = zoomed;
        if (!zoomed)
        {
            _viewStartNorm = 0f;
            return;
        }
        float maxStart = Math.Max(0.0001f, 1f - _viewSpanNorm);
        _waveformScroll.MinValue = 0;
        _waveformScroll.MaxValue = maxStart;
        _waveformScroll.Page = _viewSpanNorm * maxStart; // thumb size hint
        if (_waveformScroll.Page < 0.01)
            _waveformScroll.Page = 0.01;
        _waveformScroll.Step = maxStart / 200.0;
        _waveformScroll.SetValueNoSignal(Mathf.Clamp(_viewStartNorm, 0f, maxStart));
    }

    private void OnWaveformPanelGuiInput(InputEvent @event)
    {
        // Ctrl+wheel zoom, plain wheel scroll when zoomed
        if (@event is InputEventMouseButton mb && mb.Pressed &&
            (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
        {
            if (mb.CtrlPressed && _zoomSlider != null)
            {
                double z = _zoomSlider.Value;
                z += mb.ButtonIndex == MouseButton.WheelUp ? 0.5 : -0.5;
                _zoomSlider.Value = Mathf.Clamp((float)z, (float)_zoomSlider.MinValue, (float)_zoomSlider.MaxValue);
                AcceptEvent();
            }
            else if (_viewSpanNorm < 0.999f)
            {
                float delta = _viewSpanNorm * 0.15f * (mb.ButtonIndex == MouseButton.WheelUp ? -1f : 1f);
                float maxStart = 1f - _viewSpanNorm;
                _viewStartNorm = Mathf.Clamp(_viewStartNorm + delta, 0f, maxStart);
                SyncWaveformScrollBar();
                _ = DrawWaveform();
                AcceptEvent();
            }
        }
    }

    /// <summary>
    /// Updates the waveform display from cached peaks and start/end selection.
    /// </summary>
    private async Task DrawWaveform()
    {
        if (_waveformAccordian == null || _waveformAccordian.Visible == false) return;
        if (_focusedAudioComponent?.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:DrawWaveform - No waveform data available", 1);
            return;
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // Guard: component may have been rebound during the await (undo/redo).
        if (_focusedAudioComponent?.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
            return;

        float width = _waveformPanel.Size.X;
        if (width < 50)
            width = Math.Max(0, _inspectorContent.Size.X - 48);
        if (width < 50)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:DrawWaveform - Waveform panel too small to draw", 1);
            return;
        }

        if (_cachedPeaks == null || !ReferenceEquals(_cachedPeaksSource, _focusedAudioComponent.WaveformData))
        {
            _cachedPeaks = WaveformPeaks.FromBytes(_focusedAudioComponent.WaveformData);
            _cachedPeaksSource = _focusedAudioComponent.WaveformData;
        }
        if (_cachedPeaks == null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:DrawWaveform - Invalid waveform payload", 1);
            return;
        }

        double duration = _focusedAudioComponent.Metadata?.Duration ?? 0;
        if (duration <= 0) duration = 1;
        float startNorm = (float)(_focusedAudioComponent.StartTime / duration);
        float endTime = _focusedAudioComponent.EndTime < 0
            ? (float)duration
            : (float)_focusedAudioComponent.EndTime;
        float endNorm = (float)(endTime / duration);

        _viewSpanNorm = Mathf.Clamp(_viewSpanNorm, 0.01f, 1f);
        _viewStartNorm = Mathf.Clamp(_viewStartNorm, 0f, 1f - _viewSpanNorm);

        _waveformDisplay.SetData(_cachedPeaks, startNorm, endNorm, _viewStartNorm, _viewSpanNorm, duration);

        // Position handles in view coordinates; hide when off-screen
        PositionWaveformHandle(_startDragHandle, startNorm, width);
        PositionWaveformHandle(_endDragHandle, endNorm, width);
        SyncWaveformScrollBar();
    }

    private void PositionWaveformHandle(Button handle, float fileNorm, float width)
    {
        float x = _waveformDisplay.FileNormToX(fileNorm);
        bool visible = x >= -4 && x <= width + 4;
        handle.Visible = visible;
        if (!visible) return;
        float handleW = handle.CustomMinimumSize.X > 0 ? handle.CustomMinimumSize.X : 10f;
        handle.Position = new Vector2(x - handleW * 0.5f, 0);
        handle.Size = new Vector2(handleW, _waveformPanel.Size.Y);
    }

    private void OnStartHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                // Continuous drag session: one undo step for the whole drag (all multi targets).
                RecordAudioHistory("Edit audio start time", AudioCoalesceKey("start-drag"));
                _isDraggingStart = true;
            }
            else if (_isDraggingStart)
            {
                SyncDuration();
                _isDraggingStart = false;
                var key = AudioCoalesceKey("start-drag");
                if (!string.IsNullOrEmpty(key))
                    _globalData?.HistoryManager?.EndCoalesceSession(key);
            }
        }
        else if (@event is InputEventMouseMotion && _isDraggingStart)
        {
            if (_focusedAudioComponent == null) return;
            float localX = _waveformPanel.GetLocalMousePosition().X;
            float norm = _waveformDisplay.XToFileNorm(localX);
            double duration = _focusedAudioComponent.Metadata?.Duration ?? 0;
            if (duration <= 0) return;
            // Keep start before end (primary waveform geometry).
            float endN = _focusedAudioComponent.EndTime < 0
                ? 1f
                : (float)(_focusedAudioComponent.EndTime / duration);
            norm = Mathf.Min(norm, endN - 0.001f);
            norm = Mathf.Max(0f, norm);
            double startSecs = norm * duration;
            foreach (var (_, comp) in GetAudioTargets())
            {
                double d = comp.Metadata?.Duration ?? duration;
                if (d <= 0) d = duration;
                float localEndN = comp.EndTime < 0 ? 1f : (float)(comp.EndTime / d);
                float localNorm = Mathf.Min(norm, localEndN - 0.001f);
                localNorm = Mathf.Max(0f, localNorm);
                comp.StartTime = localNorm * d;
            }
            _startTimeInput.Text = UiUtilities.FormatTime(startSecs);
            _ = DrawWaveform();
        }
    }

    private void OnEndHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                RecordAudioHistory("Edit audio end time", AudioCoalesceKey("end-drag"));
                _isDraggingEnd = true;
            }
            else if (_isDraggingEnd)
            {
                SyncDuration();
                _isDraggingEnd = false;
                var key = AudioCoalesceKey("end-drag");
                if (!string.IsNullOrEmpty(key))
                    _globalData?.HistoryManager?.EndCoalesceSession(key);
            }
        }
        else if (@event is InputEventMouseMotion && _isDraggingEnd)
        {
            if (_focusedAudioComponent == null) return;
            float localX = _waveformPanel.GetLocalMousePosition().X;
            float norm = _waveformDisplay.XToFileNorm(localX);
            double duration = _focusedAudioComponent.Metadata?.Duration ?? 0;
            if (duration <= 0) return;
            float startN = (float)(_focusedAudioComponent.StartTime / duration);
            norm = Mathf.Max(norm, startN + 0.001f);
            norm = Mathf.Min(1f, norm);
            double endSecs = norm * duration;
            foreach (var (_, comp) in GetAudioTargets())
            {
                double d = comp.Metadata?.Duration ?? duration;
                if (d <= 0) d = duration;
                float localStartN = (float)(comp.StartTime / d);
                float localNorm = Mathf.Max(norm, localStartN + 0.001f);
                localNorm = Mathf.Min(1f, localNorm);
                comp.EndTime = localNorm * d;
            }
            _endTimeInput.Text = UiUtilities.FormatTime(endSecs);
            _ = DrawWaveform();
        }
    }

    
    private void SyncDuration()
    {
        var targets = GetAudioTargets();
        if (targets.Count == 0) return;

        foreach (var (cue, comp) in targets)
        {
            comp.RecalculateDuration();
            cue.CalculateTotalDuration();
            _globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
        }

        if (_focusedAudioComponent != null)
        {
            _durationValue.Text =
                UiUtilities.ParseAndFormatTime(
                    _focusedAudioComponent.Duration.ToString(), out var _, out string durLabeledTime);
            _durationValue.TooltipText = durLabeledTime;
        }

        // Shell list + shell inspector
        _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
    }
    
    
    /// <summary>
    /// Opens a file dialog for selecting an audio file.
    /// </summary>
    private void OpenFileDialog()
    {
        _fileDialog = new FileDialog();
        _fileDialog.FileSelected += FileSelected;
        _fileDialog.FileMode = FileDialog.FileModeEnum.OpenFile;
        _fileDialog.Access = FileDialog.AccessEnum.Filesystem;
        _fileDialog.Title = "Open an Audio File";
        _fileDialog.UseNativeDialog = true;
        _fileDialog.AddFilter(string.Join(",", GlobalData.AudioFileFilters), "Audio Files");
        AddChild(_fileDialog);
        _fileDialog.PopupCentered();
        _fileDialog.Canceled += ClearFileDialog;
    }
    
    

    /// <summary>
    /// Handles file selection from dialog. Adds/replaces AudioComponent and loads metadata + waveform.
    /// </summary>
    /// <param name="path">The selected file path.</param>
    private void FileSelected(string path)
    {
        ClearFileDialog();
        if (_focusedCue == null && !InspectorMultiEditSupport.ShouldUseMultiEdit(_globalData))
        {
            GD.Print("AudioInspector:FileSelected - No cue selected");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:No cue selected", 2);
            return;
        }
        // File picker always treats selection as a fresh media assignment (reset in/out).
        SetAudioFile(path, resetInOutPoints: true);
    }

    /// <summary>
    /// Handles setting audio file URL from drag-and-drop. Creates AudioComponent if none exists.
    /// </summary>
    /// <param name="filePath">The dropped file path.</param>
    public void SetAudioFileUrlFromDrop(string filePath)
    {
        if (_focusedCue == null && !InspectorMultiEditSupport.ShouldUseMultiEdit(_globalData))
        {
            GD.Print("AudioInspector:SetAudioFileUrlFromDrop - No cue selected");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:No cue selected for audio file drop", 2);
            return;
        }
        // Drop onto URL bar: replace media, clamp existing in/out if still valid.
        SetAudioFile(filePath, resetInOutPoints: false);
    }

    /// <summary>
    /// Sets the audio file for the focused cue (or all multi-edit selected cues): create or replace
    /// component, load metadata, generate waveform, refresh UI.
    /// </summary>
    /// <param name="filePath">The audio file path.</param>
    /// <param name="resetInOutPoints">If true, start/end are reset to full file; otherwise clamp to new duration.</param>
    private async void SetAudioFile(string filePath, bool resetInOutPoints)
    {
        bool multi = InspectorMultiEditSupport.ShouldUseMultiEdit(_globalData);
        var multiCues = multi ? InspectorMultiEditSupport.GetSelectedCues() : null;
        if (!multi && _focusedCue == null) return;
        if (multi && (multiCues == null || multiCues.Count == 0)) return;

        string resolvedPath = _globalData?.ResolveMediaPath(filePath) ?? filePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(resolvedPath))
        {
            GD.Print($"AudioInspector:SetAudioFile - File not found: {filePath}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:File not found: {filePath}", 2);
            return;
        }

        // Prefer show-relative path when media backup is enabled (copy runs in background)
        string pathToStore = filePath;
        try
        {
            var backup = GetNodeOrNull<MediaBackupManager>("/root/MediaBackupManager");
            string relative = backup?.EnsureMediaBackedUp(resolvedPath, MediaBackupKind.Audio);
            if (!string.IsNullOrEmpty(relative))
                pathToStore = relative;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"AudioInspector:SetAudioFile - Media backup: {ex.Message}");
        }

        if (multi)
        {
            bool anyNew = multiCues.Any(c => c.GetAudioComponent() == null);
            InspectorMultiEditSupport.RecordBeforeEdit(
                _globalData,
                multiCues.Count > 1,
                multiCues[^1],
                anyNew ? "Add audio component" : "Change audio file",
                anyNew ? "Multi-add audio components" : "Multi-edit audio file");

            AudioFileMetadata sharedMeta = null;
            try
            {
                sharedMeta = await _mediaEngine.GetAudioFileMetadataAsync(resolvedPath);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AudioInspector:SetAudioFile multi - Metadata: {ex.Message}");
            }

            foreach (var cue in multiCues)
            {
                var existing = cue.GetAudioComponent();
                bool isNew = existing == null;
                AudioComponent comp;
                if (existing != null)
                {
                    comp = existing;
                    bool pathChanged = !string.Equals(existing.AudioFile, pathToStore, StringComparison.OrdinalIgnoreCase);
                    existing.AudioFile = pathToStore;
                    if (pathChanged)
                    {
                        existing.WaveformData = null;
                        existing.Metadata = null;
                    }
                }
                else
                {
                    comp = cue.AddAudioComponent(pathToStore);
                }

                if (sharedMeta != null)
                    comp.Metadata = sharedMeta;

                if (resetInOutPoints || isNew)
                {
                    comp.StartTime = 0.0;
                    comp.EndTime = -1.0;
                }
                else if (sharedMeta != null)
                {
                    double fileDuration = sharedMeta.Duration > 0 ? sharedMeta.Duration : 0.0;
                    if (comp.StartTime >= fileDuration)
                        comp.StartTime = 0.0;
                    if (comp.EndTime >= 0 && (comp.EndTime > fileDuration || comp.EndTime <= comp.StartTime))
                        comp.EndTime = -1.0;
                }

                comp.RecalculateDuration();
                cue.CalculateTotalDuration();
                _globalSignals?.EmitSignal(nameof(GlobalSignals.UpdateShellBar), cue.Id);
                GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(cue.Id);
            }

            _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
            int focusId = _focusedCue?.Id ?? multiCues[^1].Id;
            ShellSelected(focusId);
            return;
        }

        // Resolve or create component; always assign the path (AddAudioComponent alone does not update existing).
        var existingAudio = _focusedCue.Components.OfType<AudioComponent>().FirstOrDefault();
        bool isNewComponent = existingAudio == null;
        if (_focusedCue != null)
            _globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id,
                isNewComponent ? "Add audio component" : "Change audio file");
        if (existingAudio != null)
        {
            _focusedAudioComponent = existingAudio;
            bool pathChanged = !string.Equals(existingAudio.AudioFile, pathToStore, StringComparison.OrdinalIgnoreCase);
            existingAudio.AudioFile = pathToStore;
            if (pathChanged)
            {
                // Stale peaks/metadata from previous file must not stick
                existingAudio.WaveformData = null;
                existingAudio.Metadata = null;
            }
        }
        else
        {
            _focusedAudioComponent = _focusedCue.AddAudioComponent(pathToStore);
        }

        _fileUrl.Text = pathToStore;
        _inspectorContent.Visible = true;
        _selectFileContainer.Visible = true;
        _infoLabel.Text = "";

        // Invalidate display cache while loading
        _cachedPeaks = null;
        _cachedPeaksSource = null;

        try
        {
            var fileMetadata = await _mediaEngine.GetAudioFileMetadataAsync(resolvedPath);
            if (fileMetadata == null)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"AudioInspector:SetAudioFile - Failed to read metadata for {Path.GetFileName(filePath)}", 2);
                return;
            }

            _focusedAudioComponent.Metadata = fileMetadata;
            var fileDuration = fileMetadata.Duration > 0 ? fileMetadata.Duration : 0.0;

            if (resetInOutPoints || isNewComponent)
            {
                _focusedAudioComponent.StartTime = 0.0;
                _focusedAudioComponent.EndTime = -1.0; // full file
                GD.Print($"AudioInspector:SetAudioFile - Metadata loaded: Duration {fileDuration}s, Channels {fileMetadata.Channels}");
            }
            else
            {
                if (_focusedAudioComponent.StartTime >= fileDuration)
                {
                    _focusedAudioComponent.StartTime = 0.0;
                    GD.Print("AudioInspector:SetAudioFile - Reset start time (exceeded file duration)");
                }

                if (_focusedAudioComponent.EndTime >= 0 && _focusedAudioComponent.EndTime > fileDuration)
                {
                    _focusedAudioComponent.EndTime = -1.0;
                    GD.Print("AudioInspector:SetAudioFile - Reset end time to undefined (exceeded file duration)");
                }
                else if (_focusedAudioComponent.EndTime >= 0 &&
                         _focusedAudioComponent.EndTime <= _focusedAudioComponent.StartTime)
                {
                    _focusedAudioComponent.EndTime = -1.0;
                    GD.Print("AudioInspector:SetAudioFile - Reset end time to undefined (was <= start time)");
                }
            }

            // Duration fields need RecalculateDuration after Metadata is set
            _focusedAudioComponent.RecalculateDuration();
            _focusedCue.CalculateTotalDuration();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"AudioInspector:SetAudioFile - Metadata error: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"AudioInspector:SetAudioFile - Metadata error: {ex.Message}", 2);
            return;
        }

        // Always (re)generate waveform for the assigned file
        try
        {
            // Use absolute source for waveform while background copy may still be running
            _focusedAudioComponent.WaveformData =
                await _mediaEngine.GenerateWaveformAsync(resolvedPath);
            if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"AudioInspector:SetAudioFile - Waveform generation failed for {pathToStore}", 2);
            }
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"AudioInspector:SetAudioFile - Error generating waveform: {ex.Message}", 2);
        }

        UpdateAudioUiFields(pathToStore);
        PopulateOutputOptions();
        BuildRoutingMatrix();
        SyncDuration();

        // Reset zoom/view for new media, then draw if accordion is open
        _viewStartNorm = 0f;
        _viewSpanNorm = 1f;
        if (_zoomSlider != null) _zoomSlider.SetValueNoSignal(1);
        SyncWaveformScrollBar();
        await DrawWaveform();

        GD.Print($"AudioInspector:SetAudioFile - Set audio file: {pathToStore}");
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
            $"AudioInspector:Set audio file to: {pathToStore}", 0);

        GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
        ApplyFileUrlMissingStyleFromHealth();
        if (_deleteAudioComponentButton != null)
            _deleteAudioComponentButton.Visible = true;
    }

    /// <summary>
    /// Clears the file dialog instance.
    /// </summary>
    private void ClearFileDialog()
    {
        _fileDialog.QueueFree();
        _fileDialog = null;
    }
    
    /// <summary>
    /// Toggles visibility of an accordion container and updates button icon.
    /// </summary>
    /// <param name="accordian">The VBoxContainer to toggle.</param>
    /// <param name="button">The Button controlling the toggle.</param>
    private async void ToggleAccordian(VBoxContainer accordian, Button button)
    {
        accordian.Visible = !accordian.Visible;
        button.Icon = GetThemeIcon(accordian.Visible ? "Down" : "Right", "AtlasIcons");

        if (accordian.Name == "WaveformAccordian")
        {
            await DrawWaveform();
        }
    }
}

