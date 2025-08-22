using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Shared;
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
    private Line2D _waveformLineLeftGrey;
    private Line2D _waveformLineMiddle;
    private Line2D _waveformLineRightGrey;
    private Button _startDragHandle;
    private Button _endDragHandle;
    private bool _isDraggingStart;
    private bool _isDraggingEnd;
    private float _dragStartX;
    
    private FileDialog _fileDialog;
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
		
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
        _routingAccordian.Visible = false;
        
        _waveformCollapseButton = GetNode<Button>("%WaveformCollapseButton");
        _waveformCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        _waveformAccordian = GetNode<VBoxContainer>("%WaveformAccordian");
        _waveformAccordian.Visible = false;
        
        _startTimeInput = GetNode<LineEdit>("%StartTimeInput");
        _endTimeInput = GetNode<LineEdit>("%EndTimeInput");
        _durationValue = GetNode<LineEdit>("%DurationValue");
        _fileDurationValue = GetNode<LineEdit>("%FileDurationValue");
        _loopInput = GetNode<CheckBox>("%LoopInput");
        _playCountInput = GetNode<LineEdit>("%PlayCountInput");
        _volumeInput = GetNode<LineEdit>("%VolumeInput");
        _outputOptionButton = GetNode<OptionButton>("%OutputOptionButton");
        
        // Waveform UI setup
        _waveformPanel = GetNode<PanelContainer>("%WaveformPanel");
        _waveformLineLeftGrey = new Line2D { DefaultColor = GlobalStyles.LowColor3, Width = 1.0f };
        _waveformLineMiddle = new Line2D { DefaultColor = GlobalStyles.HighColor1, Width = 1.0f };
        _waveformLineRightGrey = new Line2D { DefaultColor = GlobalStyles.LowColor3, Width = 1.0f };
        _waveformPanel.AddChild(_waveformLineLeftGrey);
        _waveformPanel.AddChild(_waveformLineMiddle);
        _waveformPanel.AddChild(_waveformLineRightGrey);
        
        // Draggable handles (assume as children of a Control under %WaveformPanel
        _startDragHandle = GetNode<Button>("%StartDragHandle");
        _endDragHandle = GetNode<Button>("%EndDragHandle");
        _startDragHandle.GuiInput += OnStartHandleInput; 
        _endDragHandle.GuiInput += OnEndHandleInput; 
        
        
        
        _startTimeInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _startTimeInput);
        _endTimeInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _endTimeInput);
        _volumeInput.TextSubmitted += (string newText) => VolumeInputSubmitted(newText, _volumeInput);
        _loopInput.Toggled += (bool state) => { _focusedAudioComponent.Loop = state; };
        //_playCountInput.TextChanged += (string newText) => { _focusedAudioComponent.PlayCount = int.Parse(newText); };
        _playCountInput.TextSubmitted+= OnPlayCountSubmitted;
        _outputOptionButton.ItemSelected += OutputOptionSelected;
        
        
        
        _routingContainer = GetNode<VBoxContainer>("%RoutingContainer");
        _routingMatrixGrid = GetNode<GridContainer>("%RoutingMatrixGrid");
        _routingContainer.Visible = false; // Hidden until needed.
        
        
        FormatLabels(this);
        
        GetNode<Label>("%InfoLabel").AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
        // Ensure content is hidden at start up
        _inspectorContent.Visible = false;
        _selectFileContainer.Visible = false;
        
        _routingCollapseButton.Pressed += () => ToggleAccordian(_routingAccordian, _routingCollapseButton);
        _waveformCollapseButton.Pressed += () => ToggleAccordian(_waveformAccordian, _waveformCollapseButton);
        _buttonSelectFile.Pressed += OpenFileDialog;
        
    }
    
    /// <summary>
    /// Handles submission of time fields (start/end). Parses input, updates component, and recalculates duration.
    /// </summary>
    /// <param name="text">The submitted text.</param>
    /// <param name="textField">The LineEdit field.</param>
    private void TimeFieldSubmitted(string text, LineEdit textField)
    {
        try
        {
            var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out var labeledTime);
            
            if (time == "")
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid time format in {textField.Name}: {text}", 1); // Warning log
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
                _focusedAudioComponent.EndTime = timeSecs < 0 ? _focusedAudioComponent.FileDuration : timeSecs; // Handles -1 as full duration
            }
            
            // Recalculate duration
            var durationSecs = _focusedAudioComponent.EndTime < 0 
                ? _focusedAudioComponent.FileDuration - _focusedAudioComponent.StartTime 
                : _focusedAudioComponent.EndTime - _focusedAudioComponent.StartTime;
            _durationValue.Text =
                UiUtilities.ParseAndFormatTime(durationSecs.ToString(), out var _, out var durLabeledTime);
                //? durLabeledTime : _durationValue.Text; // Fallback to previous if parse fails
            _focusedAudioComponent.Duration = durationSecs;
            _durationValue.TooltipText = durLabeledTime;
            textField.ReleaseFocus();
            
            // Update waveform
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
                return;
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
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid play count: {newText}. Must be positive integer.", 1);
            _playCountInput.Text = _focusedAudioComponent.PlayCount.ToString(); // Revert to previous
        }
        _playCountInput.ReleaseFocus();
    }

    private void FormatLabels(Node root)
    {

        if (root is Label label)
        {
            label.AddThemeColorOverride("font_color", GlobalStyles.SoftFontColor);
        }
        foreach (var child in root.GetChildren())
        {
            FormatLabels(child);
        }
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

        foreach (var output in _globalData.AudioDevices.GetAvailableAudioDeviceNames())
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
            var patchName = item.Replace("Patch: ", "");
            var patchId = (int)_outputOptionButton.GetItemMetadata((int)index);
            //_focusedAudioComponent.Patch = _globalData.Settings.GetAudioOutputPatches()[patchName];
            GD.Print($"Patch selected is: {patchName} with id {patchId}");
            _focusedAudioComponent.Patch = _globalData.Settings.GetPatch(patchId);
            _focusedAudioComponent.PatchId = patchId;
            _focusedAudioComponent.DirectOutput = null;

            GD.Print($"Patch set? {_focusedAudioComponent.Patch.Name}");
            BuildRoutingMatrix();
        }
        
        else if (item.StartsWith("Direct Output"))
        {
            var dirOutName = item.Replace("Direct Output: ", "");
            GD.Print($"Direct output selected is: {dirOutName}");
            _focusedAudioComponent.DirectOutput = dirOutName;
            _focusedAudioComponent.Patch = null;
            _focusedAudioComponent.PatchId = -1;
            BuildRoutingMatrix();
        }
    }

    
    /// <summary>
    /// Builds the per-cue routing matrix grid based on selected output (patch or direct).
    /// </summary>
    private async void BuildRoutingMatrix()
    {
        GD.Print($"Building routing matrix start");
        foreach (var child in _routingMatrixGrid.GetChildren())
        {
            child.QueueFree();
        }

        if (_focusedAudioComponent == null) return;
        await ToSignal(GetTree(), "process_frame"); // Wait a frame for exisisting chilren to fully clear.

        var inputChannels = _focusedAudioComponent.ChannelCount;
        var inputLabels = GetChannelLabels(inputChannels, isInput: true);

        int outputChannels;
        List<string> outputLabels = new List<string>();
        if (_focusedAudioComponent.PatchId != -1)
        {
            var patch = _globalData.Settings.GetPatch(_focusedAudioComponent.PatchId);
            outputChannels = patch.Channels.Count;
            outputLabels = patch.Channels.Values.ToList();
        }
        else if (!string.IsNullOrEmpty(_focusedAudioComponent.DirectOutput))
        {
            var device = _globalData.AudioDevices.OpenAudioDevice(_focusedAudioComponent.DirectOutput, out var _);
            if (device == null)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Direct output device not found: {_focusedAudioComponent.DirectOutput}", 2);
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
            GD.Print($"AudioInspector: BuildRoutingMatrix - No output");
            return; // No output selected
        }
        
        // Ensure CuePatch exists or create default
        if (_focusedAudioComponent.Routing == null)
        {
            _focusedAudioComponent.Routing = new CuePatch(inputChannels, inputLabels, outputChannels, outputLabels);
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
            if (!float.TryParse(text.Replace("dB", "").Trim(), out var dbValue))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid matrix volume: {text}", 1);
                return;
            }

            var linear = UiUtilities.DbToLinear(dbValue.ToString());
            _focusedAudioComponent.Routing.SetVolume(inputCh, outputCh, linear);
            var dbReturn = UiUtilities.LinearToDb(linear);
            textField.Text = $"{dbReturn}dB";
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
        _selectFileContainer.Visible = true;
        _fileUrl.Text = file;
        _infoLabel.Text = "";
        _inspectorContent.Visible = true;

        _startTimeInput.Text =
            UiUtilities.ParseAndFormatTime(_focusedAudioComponent.StartTime.ToString(), out _, out var startTip);
        _startTimeInput.TooltipText = startTip;
        _endTimeInput.Text = UiUtilities.FormatTime(_focusedAudioComponent.EndTime);
        _durationValue.Text = UiUtilities.FormatTime(_focusedAudioComponent.Duration);
        _fileDurationValue.Text = UiUtilities.FormatTime(_focusedAudioComponent.FileDuration);
        _loopInput.ButtonPressed = _focusedAudioComponent.Loop;
        _playCountInput.Text = _focusedAudioComponent.PlayCount.ToString();
        var volumeDb = UiUtilities.LinearToDb((float)_focusedAudioComponent.Volume);
        _volumeInput.Text = $"{volumeDb}dB";
        
        PopulateOutputOptions();
        BuildRoutingMatrix();
        
        // Generate waveform data if not cached
        if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0) // Check cache
        {
            GD.Print($"AudioInspector:ShellSelected - No waveform found");
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
    /// Updates the waveform display based on current zoom and start/end times.
    /// </summary>
    private async Task DrawWaveform()
    {
        if (_waveformAccordian.Visible == false) return; // Don't bother drawing if not open.
        if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:DrawWaveform - No waveform data available", 1);
            return;
        }
        
        // Check UI has corrected it's size once made visible.
        float width = _waveformPanel.Size.X;

        await Task.Delay(50); // This for the most part corrects for width being wrong
        
        // If width isn't correct, wait a bit before drawing.
        if (width < 50)
        {
            width = _inspectorContent.Size.X-48; // Remove width of margin containers
            GD.Print($"Width too small, checking it's parents width - Inspector Content width: {width}px");
        }

        if (width < 50)
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "AudioInspector:DrawWaveform - Waveform panel too small to draw", 1);
            return;
        }
        
        // Deserialize

        float[] minMax = new float[_focusedAudioComponent.WaveformData.Length / sizeof(float)];
        Buffer.BlockCopy(_focusedAudioComponent.WaveformData, 0, minMax, 0, _focusedAudioComponent.WaveformData.Length);
        
        int binCount = minMax.Length / 2;
        var pointsLeft = new List<Vector2>();
        var pointsMiddle = new List<Vector2>(); 
        var pointsRight = new List<Vector2>();
        
        float height = _waveformPanel.Size.Y / 2f;
        float binWidth = width / binCount;

        float startNorm = (float)(_focusedAudioComponent.StartTime / _focusedAudioComponent.FileDuration);
        float endNorm = (float)(_focusedAudioComponent.EndTime / _focusedAudioComponent.FileDuration);
        int startBin = (int)(startNorm * binCount);
        int endBin = (int)(endNorm * binCount);
        
        
        for (int i = 0; i < binCount; i++)
        {
            float x = i * binWidth;
            float minVal = minMax[i * 2];
            float maxVal = minMax[i * 2 + 1];

            float yMin = height - (minVal * height); // Normalize [-1,1]
            float yMax = height - (maxVal * height);

            var pointMin = new Vector2(x, yMin);
            var pointMax = new Vector2(x, yMax);
            
            // Split sections based on bins
            if (i < startBin)
            {
                pointsLeft.Add(pointMin);
                pointsLeft.Add(pointMax);
            } 
            else if (i >= endBin)
            {
                pointsRight.Add(pointMin); 
                pointsRight.Add(pointMax); 
            } 
            else 
            {
                pointsMiddle.Add(pointMin); 
                pointsMiddle.Add(pointMax); 
            } 
        }

        _waveformLineLeftGrey.Points = pointsLeft.ToArray();
        _waveformLineMiddle.Points = pointsMiddle.ToArray();
        _waveformLineRightGrey.Points = pointsRight.ToArray();
        
        // Position handles
        float startX = startNorm * width;
        float endX = endNorm * width;
        if (endX >= width - 2) endX -= 1;
        _startDragHandle.Position = new Vector2(startX - 2 , 0); // Center on line
        _endDragHandle.Position = new Vector2(endX - 2, 0);
    }

    
    private void OnStartHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                _isDraggingStart = mouseButton.Pressed;
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isDraggingStart)
        {
            var width = _waveformPanel.Size.X;
            var mouseX = mouseMotion.Position.X;
            var barPos = _startDragHandle.Position.X; 
            float newX = barPos + mouseX;
            newX = Mathf.Clamp(newX, 0, _waveformPanel.Size.X); // Bound
            float normX = newX / width;
            _focusedAudioComponent.StartTime = normX * _focusedAudioComponent.FileDuration;
            _startTimeInput.Text = UiUtilities.FormatTime(_focusedAudioComponent.StartTime); // Update input
            DrawWaveform(); // Refresh
        }
    }
    
    private void OnEndHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                _isDraggingEnd = mouseButton.Pressed;
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isDraggingEnd)
        {
            var width = _waveformPanel.Size.X;
            var mouseX = mouseMotion.Position.X;
            var barPos = _endDragHandle.Position.X; 
            float newX = barPos + mouseX;
            newX = Mathf.Clamp(newX, 0, _waveformPanel.Size.X); // Bound
            float normX = newX / width;
            _focusedAudioComponent.EndTime = normX * _focusedAudioComponent.FileDuration;
            _endTimeInput.Text = UiUtilities.FormatTime(_focusedAudioComponent.EndTime); // Update input
            DrawWaveform(); // Refresh
        }
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
        _fileDialog.AddFilter("*.wav,*.mp3,*.mp4,*.mov,*.avi,*.mpg,*.ogg, *.aac, *.flac, *.m4a", "Audio Files");
        AddChild(_fileDialog);
        _fileDialog.PopupCentered();
        _fileDialog.Canceled += ClearFileDialog;
    }
    
    

    /// <summary>
    /// Handles file selection from dialog. Adds AudioComponent, fetches metadata asynchronously if possible.
    /// </summary>
    /// <param name="path">The selected file path.</param>
    private async void FileSelected(string path)
    {
        ClearFileDialog();
        if (!File.Exists(path))
        {
            GD.Print($"Audio Inspector: Selected audio file not found: {path}");
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Audio Inspector: Selected audio file not found: {path}", 2);
            return;
        }
        
        _fileUrl.Text = path;
        _focusedAudioComponent =_focusedCue.AddAudioComponent(path);
        _inspectorContent.Visible = true;
        
        // Fetch metadata asynchronously to avoid UI blocking
            
        var fileMetadata = await Task.Run(() => _mediaEngine.GetAudioFileMetadata(path));
        _focusedAudioComponent.ChannelCount = fileMetadata.TryGetValue("Channels", out var value) ? (int)value : 0;
        var vlcDuration = fileMetadata.TryGetValue("Duration", out var dur) ? (int)dur : 0;
        var fileDuration = await Task.Run(() => _mediaEngine.GetFileDurationAsync(path));
        if (fileDuration == 0.0 && vlcDuration > 0) // Fallback
        {
            fileDuration = vlcDuration;
        }
        _focusedAudioComponent.FileDuration = fileDuration > 0 ? fileDuration :0.0;
        _focusedAudioComponent.EndTime = fileDuration > 0 ? fileDuration :0.0;
        _focusedAudioComponent.StartTime = 0.0;
        
        ShellSelected(_focusedCue.Id);
        GD.Print($"AudioInspector:FileSelected - File duration (seconds): {_focusedAudioComponent.FileDuration}");
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

