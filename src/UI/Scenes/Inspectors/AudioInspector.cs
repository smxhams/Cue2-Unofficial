using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.Shared.Audio;
using Cue2.UI.Utilities;

namespace Cue2.UI.Scenes.Inspectors;


/// <summary>
/// Inspector UI for managing audio components in cues. Handles file selection, playback settings,
/// and output patching. Ensures user inputs are validated and updates the underlying AudioComponent.
/// </summary>
public partial class AudioInspector : Control
{

    private GlobalData _globalData;
    private GlobalSignals _globalSignals;
    private AudioDevices _audioDevices;
    
    private Cue _focusedCue;
    private AudioComponent _focusedAudioComponent;
    private MediaEngine _mediaEngine;
    
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
    private OptionButton _outputOptionButton;
    
    // Routing matrix
    private GridContainer _routingMatrixGrid;
    private VBoxContainer _routingContainer;
    
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
        

        
        
        // Ui Node setup
        _infoLabel = GetNode<Label>("%InfoLabel");
        _selectFileContainer = GetNode<HBoxContainer>("%SelectFileContainer");
        _inspectorContent = GetNode<VBoxContainer>("%InspectorContent");
        _buttonSelectFile = GetNode<Button>("%ButtonSelectFile");
        _fileUrl = GetNode<LineEdit>("%FileURL");
        
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
        _volumeInput.TextSubmitted += newText => VolumeInputSubmitted(newText, _volumeInput);
        _loopInput.Toggled += state => { _focusedAudioComponent.Loop = state; SyncDuration();};
        _playCountInput.TextSubmitted += OnPlayCountSubmitted;
        _outputOptionButton.ItemSelected += OutputOptionSelected;
        
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
    /// </summary>
    /// <param name="text">The submitted text.</param>
    /// <param name="textField">The LineEdit field.</param>
    private void TimeFieldSubmitted(string text, LineEdit textField)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-1")
            {
                if (textField == _startTimeInput)
                {
                    _focusedAudioComponent.StartTime = 0.0;
                    textField.Text = "00:00.000";
                    textField.TooltipText = "00m:00s.000ms";
                    GD.Print("AudioInspector:TimeFieldSubmitted - Start time reset to 0");
                }
                else if (textField == _endTimeInput)
                {
                    _focusedAudioComponent.EndTime = -1.0; // Undefined = play to end
                    textField.Text = $"Full ({UiUtilities.FormatTime(_focusedAudioComponent.Metadata.Duration)})";
                    textField.TooltipText = "End time undefined (plays full file)";
                    GD.Print("AudioInspector:TimeFieldSubmitted - End time set to undefined (full)");
                }
                
                SyncDuration();
                textField.ReleaseFocus();
                DrawWaveform();
                return;
            }
            
            var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out string labeledTime);
            
            if (time == "")
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid time format in {textField.Name}: {text}", 1);
                return;
            }
            
            textField.Text = time;
            textField.TooltipText = labeledTime;
            if (textField == _startTimeInput)
            {
                _focusedAudioComponent.StartTime = timeSecs;
            }
            else if (textField == _endTimeInput)
            {
                _focusedAudioComponent.EndTime = timeSecs;
            }
            
            SyncDuration();
            textField.ReleaseFocus();
            DrawWaveform();

        }
        catch (Exception ex)
        {
            GD.Print($"AudioInspector:TimeFieldSubmitted - Error parsing time: {ex.Message}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing time: {ex.Message}", 2);
        }
    }
    
    
    /// <summary>
    /// Handles volume input submission. Converts dB to linear, updates component, and formats display.
    /// </summary>
    /// <param name="text">The submitted text.</param>
    /// <param name="textField">The LineEdit field.</param>
    private void VolumeInputSubmitted(string text, LineEdit textField)
    {
        try
        {
            if (!float.TryParse(text.Replace("dB", "").Trim(), out var dbValue))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid volume format: {text}", 1);
                textField.Text = $"{UiUtilities.LinearToDb((float)_focusedAudioComponent.Volume)}dB";
                textField.ReleaseFocus();
                return;
            }
            if (dbValue > 0)
            {
                dbValue = -dbValue;
            }
            var volume = UiUtilities.DbToLinear(dbValue.ToString());
            var dbReturn = UiUtilities.LinearToDb(volume);
            textField.Text = $"{dbReturn}dB";
            _focusedAudioComponent.Volume = volume;
            textField.ReleaseFocus();
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing volume: {ex.Message}", 2);
        }
    }
    
    /// <summary>
    /// Handles play count submission with validation to prevent invalid integers.
    /// </summary>
    /// <param name="newText">The submitted text.</param>
    private void OnPlayCountSubmitted(string newText)
    {
        if (int.TryParse(newText, out var playCount) && playCount > 0)
        {
            _focusedAudioComponent.PlayCount = playCount;
            SyncDuration();
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid play count: {newText}. Must be positive integer.", 1);
            _playCountInput.Text = _focusedAudioComponent.PlayCount.ToString(); // Revert to previous
        }
        _playCountInput.ReleaseFocus();
    }
    
    private void PopulateOutputOptions()
    {
        // Remove items from output options
        var itemCount = _outputOptionButton.GetItemCount();
        for (int i = 0; i < itemCount; i++)
        {
            _outputOptionButton.RemoveItem(_outputOptionButton.GetItemCount()-1); // Removes last item
        }
        // Add patches as options
        _outputOptionButton.AddItem("No output");
        foreach (var patch in _globalData.Settings.GetAudioOutputPatches())
        {
            _outputOptionButton.AddItem($"Patch: {patch.Value.Name}");
            _outputOptionButton.SetItemMetadata(_outputOptionButton.GetItemCount()-1,patch.Value.Id);
            if (patch.Value.Id == _focusedAudioComponent.PatchId)
            {
                _outputOptionButton.Select(_outputOptionButton.GetItemCount()-1);
            }
        }

        foreach (var output in _audioDevices.GetAvailableAudioDeviceNames())
        {
            _outputOptionButton.AddItem($"Direct Output: {output}");
            if (output == _focusedAudioComponent.DirectOutput)
            {
                _outputOptionButton.Select(_outputOptionButton.GetItemCount()-1);
            }
        }

        if (_outputOptionButton.Selected == 0 && _focusedAudioComponent.DirectOutput != null)
        {
            _outputOptionButton.AddItem($"!!! Missing output: {_focusedAudioComponent.DirectOutput}");
            _outputOptionButton.Select(_outputOptionButton.GetItemCount()-1);
            
        }
        if (_outputOptionButton.Selected == 0 && _focusedAudioComponent.Patch != null)
        {
            _outputOptionButton.AddItem($"!!! Missing patch: {_focusedAudioComponent.Patch.Name}");
            _outputOptionButton.Select(_outputOptionButton.GetItemCount()-1);
            
        }
    }
    
    private void OutputOptionSelected(long index)
    {
        var item = _outputOptionButton.GetItemText((int)index);
        if (item.StartsWith("Patch"))
        {
            var patchId = (int)_outputOptionButton.GetItemMetadata((int)index);
            GD.Print($"AudioInspector:OutputOptionSelected - Patch selected with id {patchId}");
            if (_globalData.Settings.GetAudioOutputPatches().TryGetValue(patchId, out var patch))
            {
                _focusedAudioComponent.Patch = patch;
                _focusedAudioComponent.PatchId = patchId;
                _focusedAudioComponent.DirectOutput = null;
                GD.Print($"AudioInspector:OutputOptionSelected - Patch set to: {patch.Name}");
            }
            else
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OutputOptionSelected - Patch ID {patchId} not found, resetting output", 1);
                _focusedAudioComponent.Patch = null;
                _focusedAudioComponent.PatchId = -1;
                _focusedAudioComponent.DirectOutput = null;
                _outputOptionButton.Select(0); // Select "No output"
            }
            BuildRoutingMatrix();
        }
        else if (item.StartsWith("Direct Output"))
        {
            var dirOutName = item.Replace("Direct Output: ", "");
            GD.Print($"AudioInspector:OutputOptionSelected - Direct output selected: {dirOutName}");
            _focusedAudioComponent.DirectOutput = dirOutName;
            _focusedAudioComponent.Patch = null;
            _focusedAudioComponent.PatchId = -1;
            BuildRoutingMatrix();
        }
        else // "No output" or missing patch/output case
        {
            _focusedAudioComponent.Patch = null;
            _focusedAudioComponent.PatchId = -1;
            _focusedAudioComponent.DirectOutput = null;
            BuildRoutingMatrix();
        }
    }

    
    /// <summary>
    /// Builds the per-cue routing matrix grid based on selected output (patch or direct).
    /// </summary>
    private async void BuildRoutingMatrix()
    {
        foreach (var child in _routingMatrixGrid.GetChildren())
        {
            child.QueueFree();
        }

        if (_focusedAudioComponent == null)
        {
            GD.Print($"AudioInspector:BuildRoutingMatrix - No focused audio component");
            _routingContainer.Visible = false;
            return;
        }
        
        await ToSignal(GetTree(), "process_frame"); // Wait a frame for existing children to fully clear.
        
        
        // Get ins and outs data
        var inputChannels = _focusedAudioComponent.Metadata.Channels;
        var inputLabels = GetChannelLabels(inputChannels, isInput: true);

        int outputChannels;
        List<string> outputLabels = new List<string>();
        
        // Audio Output Patch
        if (_focusedAudioComponent.PatchId != -1)
        {
            // Check if selected patch exists, if not clean the audio component of it.
            if (!_globalData.Settings.GetAudioOutputPatches().TryGetValue(_focusedAudioComponent.PatchId, out var patch))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:BuildRoutingMatrix - Patch ID {_focusedAudioComponent.PatchId} not found, resetting output", 2);
                _focusedAudioComponent.Patch = null;
                _focusedAudioComponent.PatchId = -1;
                _focusedAudioComponent.Routing = null;
                PopulateOutputOptions(); // Refresh UI to reflect missing patch
                _routingContainer.Visible = false;
                return;
            }
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
        
        // Add rows: input label + volume fields
        for (int row = 0; row < inputChannels; row++)
        {
            var inLabel = new Label { Text = inputLabels[row] };
            _routingMatrixGrid.AddChild(inLabel);
            
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
        GD.Print($"In {inputCh}. Out {outputCh}");
        try
        {
            float dbValue;
            if (string.IsNullOrWhiteSpace(text.Replace("dB", "").Trim()))
            {
                dbValue = -60.0f;
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Blank input treated as OFF for In {inputCh}, Out {outputCh}", 0);
            }
            else if (!float.TryParse(text.Replace("dB", "").Trim(), out dbValue))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:OnMatrixVolumeSubmitted - Invalid matrix volume: {text}", 1);
                return;
            }

            var linear = UiUtilities.DbToLinear(dbValue.ToString());
            _focusedAudioComponent.Routing.SetVolume(inputCh, outputCh, linear);
            if (linear > 0.0f)
            {
                var dbReturn = UiUtilities.LinearToDb(linear);
                textField.Text = $"{dbReturn}dB";
            }
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
    

    /// <summary>
    /// Called when a cue shell is selected. Updates UI based on presence of AudioComponent.
    /// </summary>
    /// <param name="cueId">The ID of the selected cue.</param>
    private async void ShellSelected(int cueId)
    {
        if (_focusedCue != null && _focusedCue.Id == cueId) return;
        _focusedCue = CueList.FetchCueFromId(cueId);

        if (_focusedCue == null)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Audio Inspector: Cue with ID {cueId} not found.", 2);
            return;
        }
        
        var hasAudio = UiUtilities.HasComponent<AudioComponent>(_focusedCue);
        if (!hasAudio) // No Audio component in Cue
        {
            _infoLabel.Text = $"No Audio File";
            _selectFileContainer.Visible = true;
            _inspectorContent.Visible = false;
            _focusedAudioComponent = null;
            _fileUrl.Text = "";
            return;
        }
        
        // Audio Component Found
        _focusedAudioComponent = _focusedCue.Components.OfType<AudioComponent>().First();
        var file = _focusedAudioComponent.AudioFile;
        
        if (_focusedAudioComponent.Metadata == null)
        {
            var refreshedMeta = await _mediaEngine.GetAudioFileMetadataAsync(file);
            _focusedAudioComponent.Metadata = refreshedMeta;
            GD.Print("AudioInspector:ShellSelected - Refreshed metadata from file.");
        }
        
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
                _focusedAudioComponent.WaveformData = await _mediaEngine.GenerateWaveformAsync(_focusedAudioComponent.AudioFile);
                if (_focusedAudioComponent.WaveformData.Length == 0)
                {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Waveform generation failed for {_focusedAudioComponent.AudioFile}", 2);
                }
            }
            catch (Exception ex)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Error generating waveform: {ex.Message}", 2);
            }
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Using cached waveform for {_focusedAudioComponent.AudioFile}", 0);
        }
        await DrawWaveform();
    }

    /// <summary>
    /// Updates the audio-related UI fields from the current AudioComponent state.
    /// </summary>
    /// <param name="file">The audio file path to display.</param>
    private void UpdateAudioUiFields(string file)
    {
        _selectFileContainer.Visible = true;
        _fileUrl.Text = file;
        _infoLabel.Text = "";
        _inspectorContent.Visible = true;

        _startTimeInput.Text =
            UiUtilities.ParseAndFormatTime(_focusedAudioComponent.StartTime.ToString(), out _, out string startTip);
        _startTimeInput.TooltipText = startTip;
        
        if (_focusedAudioComponent.EndTime < 0)
        {
            _endTimeInput.Text = $"Full ({UiUtilities.FormatTime(_focusedAudioComponent.Metadata.Duration)})";
        }
        else
        {
            _endTimeInput.Text = UiUtilities.FormatTime(_focusedAudioComponent.EndTime);
        }
        _durationValue.Text = UiUtilities.FormatTime(_focusedAudioComponent.Duration);
        _fileDurationValue.Text = UiUtilities.FormatTime(_focusedAudioComponent.Metadata.Duration);
        _loopInput.ButtonPressed = _focusedAudioComponent.Loop;
        _playCountInput.Text = _focusedAudioComponent.PlayCount.ToString();
        var volumeDb = UiUtilities.LinearToDb((float)_focusedAudioComponent.Volume);
        _volumeInput.Text = $"{volumeDb}dB";
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
        if (_waveformAccordian.Visible == false) return;
        if (_focusedAudioComponent?.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:DrawWaveform - No waveform data available", 1);
            return;
        }

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

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
                _isDraggingStart = true;
            else if (_isDraggingStart)
            {
                SyncDuration();
                _isDraggingStart = false;
            }
        }
        else if (@event is InputEventMouseMotion && _isDraggingStart)
        {
            float localX = _waveformPanel.GetLocalMousePosition().X;
            float norm = _waveformDisplay.XToFileNorm(localX);
            double duration = _focusedAudioComponent.Metadata?.Duration ?? 0;
            if (duration <= 0) return;
            // Keep start before end
            float endN = _focusedAudioComponent.EndTime < 0
                ? 1f
                : (float)(_focusedAudioComponent.EndTime / duration);
            norm = Mathf.Min(norm, endN - 0.001f);
            norm = Mathf.Max(0f, norm);
            _focusedAudioComponent.StartTime = norm * duration;
            _startTimeInput.Text = UiUtilities.FormatTime(_focusedAudioComponent.StartTime);
            _ = DrawWaveform();
        }
    }

    private void OnEndHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
                _isDraggingEnd = true;
            else if (_isDraggingEnd)
            {
                SyncDuration();
                _isDraggingEnd = false;
            }
        }
        else if (@event is InputEventMouseMotion && _isDraggingEnd)
        {
            float localX = _waveformPanel.GetLocalMousePosition().X;
            float norm = _waveformDisplay.XToFileNorm(localX);
            double duration = _focusedAudioComponent.Metadata?.Duration ?? 0;
            if (duration <= 0) return;
            float startN = (float)(_focusedAudioComponent.StartTime / duration);
            norm = Mathf.Max(norm, startN + 0.001f);
            norm = Mathf.Min(1f, norm);
            _focusedAudioComponent.EndTime = norm * duration;
            _endTimeInput.Text = UiUtilities.FormatTime(_focusedAudioComponent.EndTime);
            _ = DrawWaveform();
        }
    }

    
    private void SyncDuration()
    {
        if (_focusedCue == null || _focusedAudioComponent == null) return;

        _focusedAudioComponent.RecalculateDuration();
        var durationSecs = _focusedCue.CalculateTotalDuration();
        _durationValue.Text =
            UiUtilities.ParseAndFormatTime(
                _focusedAudioComponent.Duration.ToString(), out var _, out string durLabeledTime);
        _durationValue.TooltipText = durLabeledTime;

        // Shell list + shell inspector
        _globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
        _globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), _focusedCue.Id);
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
        if (_focusedCue == null)
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
        if (_focusedCue == null)
        {
            GD.Print("AudioInspector:SetAudioFileUrlFromDrop - No cue selected");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:No cue selected for audio file drop", 2);
            return;
        }
        // Drop onto URL bar: replace media, clamp existing in/out if still valid.
        SetAudioFile(filePath, resetInOutPoints: false);
    }

    /// <summary>
    /// Sets the audio file for the focused cue: create or replace component, load metadata, generate waveform, refresh UI.
    /// </summary>
    /// <param name="filePath">The audio file path.</param>
    /// <param name="resetInOutPoints">If true, start/end are reset to full file; otherwise clamp to new duration.</param>
    private async void SetAudioFile(string filePath, bool resetInOutPoints)
    {
        if (_focusedCue == null) return;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            GD.Print($"AudioInspector:SetAudioFile - File not found: {filePath}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:File not found: {filePath}", 2);
            return;
        }

        // Resolve or create component; always assign the path (AddAudioComponent alone does not update existing).
        var existingAudio = _focusedCue.Components.OfType<AudioComponent>().FirstOrDefault();
        bool isNewComponent = existingAudio == null;
        if (existingAudio != null)
        {
            _focusedAudioComponent = existingAudio;
            bool pathChanged = !string.Equals(existingAudio.AudioFile, filePath, StringComparison.OrdinalIgnoreCase);
            existingAudio.AudioFile = filePath;
            if (pathChanged)
            {
                // Stale peaks/metadata from previous file must not stick
                existingAudio.WaveformData = null;
                existingAudio.Metadata = null;
            }
        }
        else
        {
            _focusedAudioComponent = _focusedCue.AddAudioComponent(filePath);
        }

        _fileUrl.Text = filePath;
        _inspectorContent.Visible = true;
        _selectFileContainer.Visible = true;
        _infoLabel.Text = "";

        // Invalidate display cache while loading
        _cachedPeaks = null;
        _cachedPeaksSource = null;

        try
        {
            var fileMetadata = await _mediaEngine.GetAudioFileMetadataAsync(filePath);
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
            _focusedAudioComponent.WaveformData =
                await _mediaEngine.GenerateWaveformAsync(_focusedAudioComponent.AudioFile);
            if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                    $"AudioInspector:SetAudioFile - Waveform generation failed for {_focusedAudioComponent.AudioFile}", 2);
            }
        }
        catch (Exception ex)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
                $"AudioInspector:SetAudioFile - Error generating waveform: {ex.Message}", 2);
        }

        UpdateAudioUiFields(filePath);
        PopulateOutputOptions();
        BuildRoutingMatrix();
        SyncDuration();

        // Reset zoom/view for new media, then draw if accordion is open
        _viewStartNorm = 0f;
        _viewSpanNorm = 1f;
        if (_zoomSlider != null) _zoomSlider.SetValueNoSignal(1);
        SyncWaveformScrollBar();
        await DrawWaveform();

        GD.Print($"AudioInspector:SetAudioFile - Set audio file: {filePath}");
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log),
            $"AudioInspector:Set audio file to: {Path.GetFileName(filePath)}", 0);
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

