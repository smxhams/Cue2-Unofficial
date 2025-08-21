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
    private ScrollContainer _waveformScroll;
    private PanelContainer _waveformPanel;
    private Line2D _waveformLineLeftGrey;
    private Line2D _waveformLineMiddleColor;
    private Line2D _waveformLineRightGrey;
    private Button _startDragHandle; // Draggable for start 
    private VSeparator _endDragHandle; // Draggable for end 
    private HBoxContainer _timeBar;
    private HSlider _zoomSlider;
    private float _zoomFactor = 1.0f; // 1.0 = full view
    private int _displayPoints; // From settings
    private bool _isDraggingStart = false; 
    private bool _isDraggingEnd = false; 
    private float _dragStartX = 0f;
    
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
        _waveformScroll = GetNode<ScrollContainer>("%WaveformScroll");
        _waveformScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowAlways; // Always show hbar
        _waveformPanel = GetNode<PanelContainer>("%WaveformPanel");
        _waveformLineLeftGrey = new Line2D { DefaultColor = GlobalStyles.LowColor4, Width = 1.0f };
        _waveformLineMiddleColor = new Line2D { DefaultColor = GlobalStyles.HighColor1, Width = 1.0f };
        _waveformLineRightGrey = new Line2D { DefaultColor = GlobalStyles.LowColor4, Width = 1.0f };
        _startDragHandle = GetNode<Button>("%StartDragHandle");
        _endDragHandle = GetNode<VSeparator>("%EndDragHandle");
        _waveformPanel.AddChild(_waveformLineLeftGrey);
        _waveformPanel.AddChild(_waveformLineMiddleColor);
        _waveformPanel.AddChild(_waveformLineRightGrey);

        _timeBar = GetNode<HBoxContainer>("%TimeBar");
        _zoomSlider = GetNode<HSlider>("%ZoomSlider");
        _zoomSlider.ValueChanged += OnZoomChanged;

        _displayPoints = _globalData.Settings.WaveformResolution;

        // Draggable handles
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
            
        }
        catch (Exception ex)
        {
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
    private List<string> GetChannelLabels(int count, bool isInput) // New helper //!!!
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
        
        if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0) // Check cache
        {
            GD.Print($"AudioInspector:ShellSelected - No waveform found");
            try
            {
                _focusedAudioComponent.WaveformData = await _mediaEngine.GenerateWaveformAsync(_focusedAudioComponent.AudioFile);
                if (_focusedAudioComponent.WaveformData.Length == 0)
                {
                    _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Waveform generation failed for {_focusedAudioComponent.AudioFile}", 2); //!!!
                }
            }
            catch (Exception ex)
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Error generating waveform: {ex.Message}", 2); //!!!
            }
        }
        else
        {
            _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:ShellSelected - Using cached waveform for {_focusedAudioComponent.AudioFile}", 0); //!!!
        }
        UpdateWaveformDisplay();
    }

    /// <summary>
    /// Updates the waveform display based on current zoom and start/end times.
    /// </summary>
    private void UpdateWaveformDisplay()
    {
        if (_focusedAudioComponent.WaveformData == null || _focusedAudioComponent.WaveformData.Length == 0) return;

        GD.Print($"Lets try draw a waveform");
        
        float[] minMax = new float[_focusedAudioComponent.WaveformData.Length / sizeof(float)];
        Buffer.BlockCopy(_focusedAudioComponent.WaveformData, 0, minMax, 0, _focusedAudioComponent.WaveformData.Length);

        int binCount = minMax.Length / 2;
        float duration = (float)_focusedAudioComponent.FileDuration;

        // Downsample to display points based on zoom
        int visibleBins = (int)(binCount / _zoomFactor);
        int startBin = (int)((binCount - visibleBins) * (_zoomSlider.Value / _zoomSlider.MaxValue)); // Scroll position from slider
        var pointsLeft = new List<Vector2>();
        var pointsMiddle = new List<Vector2>();
        var pointsRight = new List<Vector2>();

        float width = _waveformPanel.Size.X;
        float height = _waveformPanel.Size.Y / 2f; // Half for amplitude
        GD.Print($"Height: {height}");
        float binWidth = width / _displayPoints;
        
        int startTimeBin = (int)(_focusedAudioComponent.StartTime / duration * binCount);
        int endTimeBin = (int)(_focusedAudioComponent.EndTime / duration * binCount);

        for (int i = 0; i < _displayPoints; i++)
        {
            int srcBinStart = startBin + (i * visibleBins / _displayPoints);
            int srcBinEnd = startBin + ((i + 1) * visibleBins / _displayPoints);
            float minVal = float.MaxValue;
            float maxVal = float.MinValue;

            for (int j = srcBinStart; j < srcBinEnd && j < binCount; j++)
            {
                minVal = Math.Min(minVal, minMax[j * 2]);
                maxVal = Math.Max(maxVal, minMax[j * 2 + 1]);
            }
            
            var x = i * binWidth;
            var yMin = height - (minVal * height); // Normalize [-1,1] to height
            var yMax = height - (maxVal * height);

            var pointMin = new Vector2(x, yMin);
            var pointMax = new Vector2(x, yMax);

            // Assign to sections
            if (srcBinStart < startTimeBin)
            {
                pointsLeft.Add(pointMin);
                pointsLeft.Add(pointMax); // For lines; reverse for polygon if filled
            }
            else if (srcBinStart >= endTimeBin)
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
        // Update lines (for outline; use Polygon2D for fill by connecting to baseline)
        _waveformLineLeftGrey.Points = pointsLeft.ToArray();
        _waveformLineMiddleColor.Points = pointsMiddle.ToArray();
        _waveformLineRightGrey.Points = pointsRight.ToArray();
        
        // Start/End lines
        float startX = ((float)_focusedAudioComponent.StartTime / duration) * width * _zoomFactor;
        float endX = ((float)_focusedAudioComponent.EndTime / duration) * width * _zoomFactor;
        //_startDragHandle.Points = new[] { new Vector2(startX, 0), new Vector2(startX, _waveformPanel.Size.Y) };
        //_endLine.Points = new[] { new Vector2(endX, 0), new Vector2(endX, _waveformPanel.Size.Y) };
        GD.Print($"Point count of middle line: {_waveformLineMiddleColor.Points.Length}");
        // Time bar
        _timeBar.GetChildren().ToList().ForEach(c => c.QueueFree());
        float tickInterval = 10f / _zoomFactor; // Seconds per tick, adjust dynamically
        for (float t = 0; t < duration; t += tickInterval)
        {
            var label = new Label { Text = UiUtilities.FormatTime(t) };
            label.Position = new Vector2((t / duration) * width * _zoomFactor, 0);
            _timeBar.AddChild(label);
        }

        // Update panel size for scroll (zoom stretches width) 
        _waveformPanel.CustomMinimumSize = new Vector2(width * _zoomFactor, _waveformPanel.CustomMinimumSize.Y); // Stretch on zoom

    }
    
    private void OnZoomChanged(double value)
    {
        _zoomFactor = (float)value; // Map slider e.g., 1-10
        UpdateWaveformDisplay(); // Pin to start: No offset change needed, as scroll=0 on zoom out
        _waveformScroll.ScrollHorizontal = 0; // Reset to left on zoom change
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
    
    
    // Draggable input handlers //!!!
    private void OnStartHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                _isDraggingStart = mouseButton.Pressed;
                _dragStartX = mouseButton.Position.X;
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isDraggingStart)
        {
            float deltaX = mouseMotion.Position.X - _dragStartX;
            float newX = _startDragHandle.Position.X + deltaX;
            newX = Mathf.Clamp(newX, 0, _waveformPanel.Size.X); // Bound
            float normX = (newX + _waveformScroll.ScrollHorizontal) / (_waveformPanel.Size.X); // Account scroll
            _focusedAudioComponent.StartTime = normX * _focusedAudioComponent.FileDuration;
            UpdateWaveformDisplay(); // Refresh
            _dragStartX = mouseMotion.Position.X; // Update for smooth
            _startDragHandle.SetPosition(new Vector2(_dragStartX, _startDragHandle.Position.Y)); // Update info
            
        }
    }

    private void OnEndHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                _isDraggingEnd = mouseButton.Pressed;
                _dragStartX = mouseButton.Position.X; // Reuse var //!!!
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _isDraggingEnd)
        {
            float deltaX = mouseMotion.Position.X - _dragStartX;
            float newX = _endDragHandle.Position.X + deltaX;
            newX = Mathf.Clamp(newX, 0, _waveformPanel.Size.X);
            float normX = (newX + _waveformScroll.ScrollHorizontal) / (_waveformPanel.Size.X);
            _focusedAudioComponent.EndTime = normX * _focusedAudioComponent.FileDuration;
            UpdateWaveformDisplay();
            _dragStartX = mouseMotion.Position.X;
        }
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
        _focusedAudioComponent.FileDuration = fileMetadata.TryGetValue("DurationSeconds", out var value) ? (double)value : 0.0;
        _focusedAudioComponent.ChannelCount = fileMetadata.TryGetValue("Channels", out value) ? (int)value : 0;
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
    private void ToggleAccordian(VBoxContainer accordian, Button button)
    {
        accordian.Visible = !accordian.Visible;
        button.Icon = GetThemeIcon(accordian.Visible ? "Down" : "Right", "AtlasIcons");
    }

}

