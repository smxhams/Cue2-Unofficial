using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes.Inspectors;

public partial class VideoInspector : Control
	
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private MediaEngine _mediaEngine;
	private AudioDevices _audioDevices;
	
	private Cue _focusedCue;
	private VideoComponent _focusedVideoComponent;
	
	// Ui Nodes
	private Label _infoLabel;
	private HBoxContainer _selectFileContainer;
	private VBoxContainer _inspectorContent;
	private Button _selectFileButton;
	private LineEdit _fileUrl;
    
	private LineEdit _startTimeInput;
	private LineEdit _endTimeInput;
	private LineEdit _durationValue;
	private LineEdit _fileDurationValue;
	private CheckBox _loopInput;
	private LineEdit _playCountInput;

	private Label _fileMetadataLabel;
	private OptionButton _targetLayerOptionButton;
	private LineEdit _scaleWidthLineEdit;
	private LineEdit _scaleHeightLineEdit;
	private LineEdit _offsetXLineEdit;
	private LineEdit _offsetYLineEdit;

	// Video Preview
	private Button _previewCollapseButton;
	private HBoxContainer _previewContainer;
	private VideoPreviewer _videoPreviewer;
    
	// Audio
	private Button _audioCollapseButton;
	private HBoxContainer _audioAccordian;
	private CheckButton _useAudioCheckButton;
	private Label _useAudioLabel;
	private OptionButton _outputOptionButton;
	private LineEdit _volumeInput;
	
	// Routing matrix
	private Button _routingCollapseButton;
	private VBoxContainer _routingAccordian;
	private GridContainer _routingMatrixGrid;
	private VBoxContainer _routingContainer;
    
	// Waveform
	private Button _waveformCollapseButton;
	private VBoxContainer _waveformAccordian;
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
		_globalSignals= GetNode<GlobalSignals>("/root/GlobalSignals");
		_mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
		_audioDevices = GetNode<AudioDevices>("/root/AudioDevices");

		_globalSignals.ShellFocused += ShellSelected;

		AssignUiNodeParameters();

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
		

		_startTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _startTimeInput);
		_endTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _endTimeInput);
		_loopInput.Toggled += state => { _focusedVideoComponent.Loop = state; SyncDuration(); };
		_playCountInput.TextSubmitted += OnPlayCountSubmitted;
		_scaleWidthLineEdit.TextSubmitted += newText => OnScaleWidthSubmitted(newText);
		_scaleHeightLineEdit.TextSubmitted += newText => OnScaleHeightSubmitted(newText);
		_offsetXLineEdit.TextSubmitted += newText => OnOffsetXSubmitted(newText);
		_offsetYLineEdit.TextSubmitted += newText => OnOffsetYSubmitted(newText);
		_useAudioCheckButton.Toggled += OnUseAudioToggled;
		_volumeInput.TextSubmitted += newText => VolumeInputSubmitted(newText, _volumeInput);
		_outputOptionButton.ItemSelected += OutputOptionSelected;
		_targetLayerOptionButton.ItemSelected += TargetLayerSelected;
		
		UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);
        
		GetNode<Label>("%InfoLabel").AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
		// Ensure content is hidden at start up
		_inspectorContent.Visible = false;
		_selectFileContainer.Visible = false;
		_previewContainer.Visible = false;
		_audioAccordian.Visible = false;
		_routingAccordian.Visible = false;
		_waveformAccordian.Visible = false;
		
		_previewCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
		_audioCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
		_routingCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
		_waveformCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
		
		// Connect Ui input methods.
		_selectFileButton.Pressed += OpenFileDialog;

		// Accordion connections
		_previewCollapseButton.Pressed += () => ToggleAccordian(_previewContainer, _previewCollapseButton);
		_audioCollapseButton.Pressed += () => ToggleAccordian(_audioAccordian, _audioCollapseButton);
		_routingCollapseButton.Pressed += () => ToggleAccordian(_routingAccordian, _routingCollapseButton);
		_waveformCollapseButton.Pressed += () => ToggleAccordian(_waveformAccordian, _waveformCollapseButton);
		_previewCollapseButton.Pressed += PreviewToggled;
	}

	private void AssignUiNodeParameters()
	{
		// Ui Node setup
		_infoLabel = GetNode<Label>("%InfoLabel");
		_selectFileContainer = GetNode<HBoxContainer>("%SelectFileContainer");
		_inspectorContent = GetNode<VBoxContainer>("%InspectorContent");
		_selectFileButton =  GetNode<Button>("%ButtonSelectFile");
		_fileUrl = GetNode<LineEdit>("%FileUrl");
		
		_startTimeInput = GetNode<LineEdit>("%StartTimeInput");
		_endTimeInput = GetNode<LineEdit>("%EndTimeInput");
		_durationValue = GetNode<LineEdit>("%DurationValue");
		_fileDurationValue = GetNode<LineEdit>("%FileDurationValue");
		_loopInput = GetNode<CheckBox>("%LoopInput");
		_playCountInput  = GetNode<LineEdit>("%PlayCountInput");
		
		_fileMetadataLabel = GetNode<Label>("%FileMetadataLabel");
		_targetLayerOptionButton = GetNode<OptionButton>("%TargetLayerOptionButton");
		_scaleWidthLineEdit  = GetNode<LineEdit>("%ScaleWidthLineEdit");
		_scaleHeightLineEdit  = GetNode<LineEdit>("%ScaleHeightLineEdit");
		_offsetXLineEdit  = GetNode<LineEdit>("%OffsetXLineEdit");
		_offsetYLineEdit  = GetNode<LineEdit>("%OffsetYLineEdit");

		
		// Video Previewer
		_previewCollapseButton = GetNode<Button>("%PreviewCollapseButton");
		_previewContainer = GetNode<HBoxContainer>("%PreviewContainer");
		_videoPreviewer = GetNode<VideoPreviewer>("%VideoPreviewer");
	    
		// Audio
		_audioCollapseButton  = GetNode<Button>("%AudioCollapseButton");
		_audioAccordian = GetNode<HBoxContainer>("%AudioAccordian");
		_useAudioCheckButton = GetNode<CheckButton>("%UseAudioCheckButton");
		_useAudioLabel = GetNode<Label>("%UseAudioLabel");
		_outputOptionButton = GetNode<OptionButton>("%OutputOptionButton");
		_routingCollapseButton = GetNode<Button>("%RoutingCollapseButton");
		_routingAccordian =  GetNode<VBoxContainer>("%RoutingAccordian");
		_routingMatrixGrid = GetNode<GridContainer>("%RoutingMatrixGrid");
		_routingContainer = GetNode<VBoxContainer>("%RoutingContainer");
		_waveformCollapseButton  = GetNode<Button>("%WaveformCollapseButton");
		_waveformAccordian =   GetNode<VBoxContainer>("%WaveformAccordian");
		_volumeInput = GetNode<LineEdit>("%VolumeInput");
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
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:ShellSelected - Cue with ID {cueId} not found.", 2);
			return;
		}
		
		var hasVideo = UiUtilities.HasComponent<VideoComponent>(_focusedCue);
		if (!hasVideo) // No Video component in Cue
		{
			_infoLabel.Text = "No Video File";
			_selectFileContainer.Visible = true;
			_inspectorContent.Visible = false;
			_focusedVideoComponent = null;
			_fileUrl.Text = "";
			return;
		}
		
		// Video Component Found
		_focusedVideoComponent = _focusedCue.Components.OfType<VideoComponent>().First();
		var file = _focusedVideoComponent.VideoFile;
		
		if (_focusedVideoComponent.Metadata == null)
		{
			var refreshedMeta = await _mediaEngine.GetVideoFileMetadataAsync(file);
			_focusedVideoComponent.Metadata = refreshedMeta;
			GD.Print("VideoInspector:ShellSelected - Refreshed metadata from file");
		}

		_selectFileContainer.Visible = true;
		_infoLabel.Text = "";
		_inspectorContent.Visible = true;
		
		// Insert values from data
		_fileUrl.Text = file;
		_startTimeInput.Text =
			UiUtilities.ParseAndFormatTime(_focusedVideoComponent.StartTime.ToString(), out _, out string startTip);
		_startTimeInput.TooltipText = startTip;
		_endTimeInput.Text = UiUtilities.FormatTime(_focusedVideoComponent.EndTime);
		_durationValue.Text = UiUtilities.FormatTime(_focusedVideoComponent.Duration);
		_fileDurationValue.Text = UiUtilities.FormatTime(_focusedVideoComponent.Metadata.Duration);
		_loopInput.ButtonPressed = _focusedVideoComponent.Loop;
		_playCountInput.Text = _focusedVideoComponent.PlayCount.ToString();

		// Populate metadata label
		var meta = _focusedVideoComponent.Metadata;
		string metadataText = $"Duration: {UiUtilities.FormatTime(meta.Duration)} \n" +
		                      $"Resolution: {meta.Width}x{meta.Height} \n" +
		                      $"Frame Rate: {meta.FrameRate:F1} fps \n" +
		                      $"Codec: {meta.Codec} \n" +
		                      $"Format: {meta.Format}";
		if (meta.AudioChannels > 0)
		{
			metadataText += $"\nAudio Channels: {meta.AudioChannels} \n" +
			                $"Audio Sample Rate: {meta.AudioSampleRate} Hz \n" +
			                $"Audio Bit Depth: {meta.AudioBitDepth} \n" +
			                $"Audio Codec: {meta.AudioCodec}";
		}
		else
		{
			metadataText += "\nNo Audio";
		}
		_fileUrl.TooltipText = metadataText;

		// Populate target layer options
		_targetLayerOptionButton.Clear();
		for (int i = 0; i < DisplaysManager.Layers.Count; i++)
		{
			var layer = DisplaysManager.Layers[i];
			_targetLayerOptionButton.AddItem(layer.LayerName, layer.LayerId);
		}
		// Select the current target layer
		for (int i = 0; i < _targetLayerOptionButton.ItemCount; i++)
		{
			if (_targetLayerOptionButton.GetItemId(i) == _focusedVideoComponent.TargetLayerId)
			{
				_targetLayerOptionButton.Select(i);
				break;
			}
		}
		// Handle missing layer
		if (_targetLayerOptionButton.Selected == -1)
		{
			_targetLayerOptionButton.AddItem($"!!! Missing layer: {_focusedVideoComponent.TargetLayerId}");
			_targetLayerOptionButton.Select(_targetLayerOptionButton.ItemCount - 1);
		} 

		// Set scale and offset values
		_scaleWidthLineEdit.Text = _focusedVideoComponent.ScaledWidth.ToString();
		_scaleHeightLineEdit.Text = _focusedVideoComponent.ScaledHeight.ToString();
		_offsetXLineEdit.Text = _focusedVideoComponent.OffsetX.ToString();
		_offsetYLineEdit.Text = _focusedVideoComponent.OffsetY.ToString();
		
		
		// Initalize preview
		if (_previewContainer.Visible)
		{
			_videoPreviewer.SetAreasDeferred(_focusedVideoComponent.TargetLayerId);
			_videoPreviewer.LoadDecoder(file);
		}
		else
		{
			// Fluch video decoder if residual from previous shell selected remains
			_videoPreviewer.ClearDecoder();
		}
		

		var volume = _focusedVideoComponent.UseAudio ? _focusedVideoComponent.AudioVolume : _focusedVideoComponent.Volume;
		var volumeDb = UiUtilities.LinearToDb((float)volume);
		_volumeInput.Text = $"{volumeDb}dB";

		// Generate waveform data if not cached
		if (_focusedVideoComponent.HasAudio && _focusedVideoComponent.UseAudio && (_focusedVideoComponent.WaveformData == null || _focusedVideoComponent.WaveformData.Length == 0)) // Check cache
		{
			GD.Print($"VideoInspector:ShellSelected - No waveform found");
			try
			{
				_focusedVideoComponent.WaveformData = await _mediaEngine.GenerateWaveformAsync(_focusedVideoComponent.VideoFile);
				if (_focusedVideoComponent.WaveformData.Length == 0)
				{
					_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:ShellSelected - Waveform generation failed for {_focusedVideoComponent.VideoFile}", 2);
				}
			}
			catch (Exception ex)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:ShellSelected - Error generating waveform: {ex.Message}", 2);
			}
		}
		else if (_focusedVideoComponent.HasAudio && _focusedVideoComponent.UseAudio)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:ShellSelected - Using cached waveform for {_focusedVideoComponent.VideoFile}", 0);
		}

		// Handle audio UI based on presence of audio in video file
		if (_focusedVideoComponent.HasAudio)
		{
			GD.Print($"VideoInspector:ShellSelected - Patch: {_focusedVideoComponent.PatchId}");
			_useAudioCheckButton.Visible = true;
			_useAudioCheckButton.ButtonPressed = _focusedVideoComponent.UseAudio;
			_useAudioLabel.Text = "Use Embedded Audio";
			if (_focusedVideoComponent.UseAudio) ToggleAccordian(_audioAccordian, _audioCollapseButton);
			PopulateOutputOptions();
			BuildRoutingMatrix();
			await DrawWaveform(); // This can move to if useaudio selected.
		}
		else
		{
			_audioCollapseButton.Visible = false;
			_useAudioCheckButton.Visible = false;
			_useAudioLabel.Text = "No audio in file";
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
		_fileDialog.Title = "Select Video or Image File";
		_fileDialog.UseNativeDialog = true;

		// Add filters from GlobalData
		_fileDialog.AddFilter(string.Join(",", GlobalData.VideoFileFilters), "Video Files");
		_fileDialog.AddFilter(string.Join(",", GlobalData.ImageFileFilters), "Image Files");
		
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
			GD.Print("AudioInspector:FileSelected - Selected audio file not found.");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"AudioInspector:FileSelected -  Selected audio file not found: {path}", 2);
			return;
		}
		
		_fileUrl.Text = path;
		_focusedVideoComponent = _focusedCue.AddVideoComponent(path);
		_inspectorContent.Visible = true;
		
		// Fetch metadata asynchronously to avoid UI blocking
		var fileMetadata = await _mediaEngine.GetVideoFileMetadataAsync(path);
		_focusedVideoComponent.Metadata = fileMetadata;
		var fileDuration = fileMetadata.Duration;
		_focusedVideoComponent.EndTime = fileDuration > 0 ? fileDuration : 0;
		_focusedVideoComponent.StartTime = 0.0;
		_focusedVideoComponent.HasAudio = fileMetadata.AudioChannels > 0 ? true : false; // If Audiochannels in metadata Audio is present.
		_focusedVideoComponent.UseAudio = _focusedVideoComponent.HasAudio; // Enable by default if has audio
		_focusedVideoComponent.ScaledWidth = fileMetadata.Width;
		_focusedVideoComponent.ScaledHeight = fileMetadata.Height;
		
		ShellSelected(_focusedCue.Id);
		GD.Print($"VideoInspector:FileSelected - Metadata loaded: Duration {fileDuration}s, HasAudio: {_focusedVideoComponent.HasAudio}");

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
			var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out string labeledTime);
            
			if (time == "")
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid time format in {textField.Name}: {text}", 1); // Warning log
				return;
			}
			textField.Text = time;
			textField.TooltipText = labeledTime;
			if (textField == _startTimeInput)
			{
				_focusedVideoComponent.StartTime = timeSecs;
			}
			else if (textField == _endTimeInput)
			{
				_focusedVideoComponent.EndTime = timeSecs < 0 ? _focusedVideoComponent.Metadata.Duration : timeSecs; // Handles -1 as full duration
			}
            
			// Recalculate duration
			SyncDuration();

			textField.ReleaseFocus();

			// Update waveform
			_ = DrawWaveform();

		}
		catch (Exception ex)
		{
			GD.Print($"AudioInspector:TimeFieldSubmitted - Error parsing time: {ex.Message}");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing time: {ex.Message}", 2);
		}
	}
	
	private void SyncDuration()
	{
		_focusedCue.CalculateTotalDuration();
		var durationSecs = _focusedVideoComponent.Duration;
		_durationValue.Text =
			UiUtilities.ParseAndFormatTime(durationSecs.ToString(), out var _, out string durLabeledTime);
		_durationValue.TooltipText = durLabeledTime;
		_globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
	}
	
	/// <summary>
	/// Handles play count submission with validation to prevent invalid integers.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnPlayCountSubmitted(string newText)
	{
		if (int.TryParse(newText, out var playCount) && playCount > 0)
		{
			_focusedVideoComponent.PlayCount = playCount;
			SyncDuration();
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid play count: {newText}. Must be positive integer.", 1);
			_playCountInput.Text = _focusedVideoComponent.PlayCount.ToString(); // Revert to previous
		}
		_playCountInput.ReleaseFocus();
	}

	/// <summary>
	/// Handles scaled width submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnScaleWidthSubmitted(string newText)
	{
		if (int.TryParse(newText, out var width) && width > 0)
		{
			_focusedVideoComponent.ScaledWidth = width;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid scaled width: {newText}. Must be positive integer.", 1);
			_scaleWidthLineEdit.Text = _focusedVideoComponent.ScaledWidth.ToString(); // Revert
		}
		_scaleWidthLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles scaled height submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnScaleHeightSubmitted(string newText)
	{
		if (int.TryParse(newText, out var height) && height > 0)
		{
			_focusedVideoComponent.ScaledHeight = height;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid scaled height: {newText}. Must be positive integer.", 1);
			_scaleHeightLineEdit.Text = _focusedVideoComponent.ScaledHeight.ToString(); // Revert
		}
		_scaleHeightLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles offset X submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnOffsetXSubmitted(string newText)
	{
		if (int.TryParse(newText, out var offsetX))
		{
			_focusedVideoComponent.OffsetX = offsetX;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid offset X: {newText}. Must be integer.", 1);
			_offsetXLineEdit.Text = _focusedVideoComponent.OffsetX.ToString(); // Revert
		}
		_offsetXLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles offset Y submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnOffsetYSubmitted(string newText)
	{
		if (int.TryParse(newText, out var offsetY))
		{
			_focusedVideoComponent.OffsetY = offsetY;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid offset Y: {newText}. Must be integer.", 1);
			_offsetYLineEdit.Text = _focusedVideoComponent.OffsetY.ToString(); // Revert
		}
		_offsetYLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles toggling of the use audio checkbox. Expands audio accordion when enabled.
	/// </summary>
	/// <param name="state">The toggle state.</param>
	private void OnUseAudioToggled(bool state)
	{
		_focusedVideoComponent.UseAudio = state;
	}

	/// <summary>
	/// Handles volume input submission with validation and conversion.
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
				textField.Text = $"{UiUtilities.LinearToDb((float)_focusedVideoComponent.Volume)}dB";
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
			if (_focusedVideoComponent.UseAudio)
			{
				_focusedVideoComponent.AudioVolume = volume;
			}
			else
			{
				_focusedVideoComponent.Volume = volume;
			}
			textField.ReleaseFocus();
		}
		catch (Exception ex)
		{
			GD.Print($"VideoInspector:VolumeInputSubmitted - Error: {ex.Message}");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing volume: {ex.Message}", 2);
		}
	}

	/// <summary>
	/// Handles output option selection for audio routing.
	/// </summary>
	/// <param name="index">The selected index.</param>
	private void OutputOptionSelected(long index)
	{
		var item = _outputOptionButton.GetItemText((int)index);
		if (item.StartsWith("Patch"))
		{
			var patchId = (int)_outputOptionButton.GetItemMetadata((int)index);
			GD.Print($"VideoInspector:OutputOptionSelected - Patch selected with id {patchId}");
			if (_globalData.Settings.GetAudioOutputPatches().TryGetValue(patchId, out var patch))
			{
				_focusedVideoComponent.Patch = patch;
				_focusedVideoComponent.PatchId = patchId;
				
				_focusedVideoComponent.DirectOutput = null;
				GD.Print($"VideoInspector:OutputOptionSelected - Patch set to: {patch.Name}");
			}
			else
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:OutputOptionSelected - Patch ID {patchId} not found, resetting output", 1);
				_focusedVideoComponent.Patch = null;
				_focusedVideoComponent.PatchId = -1;
				_focusedVideoComponent.DirectOutput = null;
				_outputOptionButton.Select(0); // Select "No output"
			}
			BuildRoutingMatrix();
		}
		else if (item.StartsWith("Direct Output"))
		{
			var dirOutName = item.Replace("Direct Output: ", "");
			GD.Print($"VideoInspector:OutputOptionSelected - Direct output selected: {dirOutName}");
			_focusedVideoComponent.DirectOutput = dirOutName;
			_focusedVideoComponent.Patch = null;
			_focusedVideoComponent.PatchId = -1;
			BuildRoutingMatrix();
		}
		else
		{
			// No output
			_focusedVideoComponent.DirectOutput = null;
			_focusedVideoComponent.Patch = null;
			_focusedVideoComponent.PatchId = -1;
			_routingContainer.Visible = false;
		}
	}

	/// <summary>
	/// Handles target layer selection.
	/// </summary>
	/// <param name="index">The selected index.</param>
	private void TargetLayerSelected(long index)
	{
		int layerId = _targetLayerOptionButton.GetItemId((int)index);
		_focusedVideoComponent.TargetLayerId = layerId;
		_videoPreviewer.SetAreasDeferred(layerId);
		GD.Print($"VideoInspector:TargetLayerSelected - Target layer set to ID {layerId}");
	}

	
	/// <summary>
	/// Populates the output option button with available audio outputs.
	/// </summary>
	private void PopulateOutputOptions()
	{
		// Remove items from output options
		var itemCount = _outputOptionButton.GetItemCount();
		for (int i = 0; i < itemCount; i++)
		{
			_outputOptionButton.RemoveItem(_outputOptionButton.GetItemCount() - 1); // Removes last item
		}
		// Add patches as options
		_outputOptionButton.AddItem("No output");
		foreach (var patch in _globalData.Settings.GetAudioOutputPatches())
		{
			_outputOptionButton.AddItem($"Patch: {patch.Value.Name}");
			_outputOptionButton.SetItemMetadata(_outputOptionButton.GetItemCount() - 1, patch.Value.Id);
			if (patch.Value.Id == _focusedVideoComponent.PatchId)
			{
				_outputOptionButton.Select(_outputOptionButton.GetItemCount() - 1);
			}
		}

		foreach (var output in _audioDevices.GetAvailableAudioDeviceNames())
		{
			_outputOptionButton.AddItem($"Direct Output: {output}");
			if (output == _focusedVideoComponent.DirectOutput)
			{
				_outputOptionButton.Select(_outputOptionButton.GetItemCount() - 1);
			}
		}

		if (_outputOptionButton.Selected == 0 && _focusedVideoComponent.DirectOutput != null)
		{
			_outputOptionButton.AddItem($"!!! Missing output: {_focusedVideoComponent.DirectOutput}");
			_outputOptionButton.Select(_outputOptionButton.GetItemCount() - 1);
		}
		if (_outputOptionButton.Selected == 0 && _focusedVideoComponent.PatchId != -1)
		{
			_outputOptionButton.AddItem($"!!! Missing patch: ID {_focusedVideoComponent.PatchId}");
			_outputOptionButton.Select(_outputOptionButton.GetItemCount() - 1);
		}
	}

	/// <summary>
	/// Builds the routing matrix for audio channels.
	/// </summary>
	private async void BuildRoutingMatrix()
	{
		foreach (var child in _routingMatrixGrid.GetChildren())
		{
			child.QueueFree();
		}

		if (_focusedVideoComponent == null || !_focusedVideoComponent.HasAudio || !_focusedVideoComponent.UseAudio)
		{
			GD.Print($"VideoInspector:BuildRoutingMatrix - No focused video component, no audio, or audio not enabled");
			_routingContainer.Visible = false;
			return;
		}

		await ToSignal(GetTree(), "process_frame"); // Wait a frame for existing children to fully clear.

		GD.Print($"BUILDING ROUTING MATRIX");
		
		// Get ins and outs data
		var inputChannels = _focusedVideoComponent.Metadata.AudioChannels;
		var inputLabels = GetChannelLabels(inputChannels, isInput: true);

		int outputChannels;
		List<string> outputLabels = new List<string>();

        // Audio Output Patch
        if (_focusedVideoComponent.PatchId != -1)
        {
            // Check if selected patch exists, if not clean the video component of it.
            if (!_globalData.Settings.GetAudioOutputPatches().TryGetValue(_focusedVideoComponent.PatchId, out var patch))
            {
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:BuildRoutingMatrix - Patch ID {_focusedVideoComponent.PatchId} not found, resetting output", 2);
                _focusedVideoComponent.Patch = null;
                _focusedVideoComponent.PatchId = -1;
                _focusedVideoComponent.Routing = null;
                PopulateOutputOptions(); // Refresh UI to reflect missing patch
                _routingContainer.Visible = false;
                return;
            }
            outputChannels = patch.Channels.Count;
            outputLabels = patch.Channels.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
        }

		// Direct output
		else if (!string.IsNullOrEmpty(_focusedVideoComponent.DirectOutput))
		{
			var device = _audioDevices.OpenAudioDevice(_focusedVideoComponent.DirectOutput, out var _);
			if (device == null)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:BuildRoutingMatrix - Direct output device not found: {_focusedVideoComponent.DirectOutput}", 2);
				_focusedVideoComponent.DirectOutput = null;
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
			GD.Print($"VideoInspector:BuildRoutingMatrix - No output selected");
			_routingContainer.Visible = false;
			return;
		}

		_routingContainer.Visible = true;

		// Validate routing (CuePatch) matches what is expected
		var routing = _focusedVideoComponent.Routing;
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
            _focusedVideoComponent.Routing = routing;

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

			GD.Print($"VideoInspector:BuildRoutingMatrix - Resized/created CuePatch to inputs: {inputChannels}, outputs: {outputChannels}"); //!!!
		}

		// Create grid
		_routingMatrixGrid.Columns = outputChannels + 1; // +1 for input labels

		// Header row
		_routingMatrixGrid.AddChild(new Label { Text = "" }); // Empty corner
		foreach (var label in outputLabels)
		{
			_routingMatrixGrid.AddChild(new Label { Text = label, HorizontalAlignment = HorizontalAlignment.Center });
		}

		// Add rows: input label + volume fields
		for (int row = 0; row < inputChannels; row++)
		{
			var inLabel = new Label { Text = inputLabels[row] };
			_routingMatrixGrid.AddChild(inLabel);

			for (int col = 0; col < outputChannels; col++)
			{
				var volumeEdit = new LineEdit();
				var routingForGet = _focusedVideoComponent.Routing;
				var linearVol = routingForGet.GetVolume(row, col);
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
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:OnMatrixVolumeSubmitted - Blank input treated as OFF for In {inputCh}, Out {outputCh}", 0);
			}
			else if (!float.TryParse(text.Replace("dB", "").Trim(), out dbValue))
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:OnMatrixVolumeSubmitted - Invalid matrix volume: {text}", 1);
				return;
			}

			var linear = UiUtilities.DbToLinear(dbValue.ToString());
			var routingForSet = _focusedVideoComponent.Routing;
			routingForSet.SetVolume(inputCh, outputCh, linear);
			if (linear > 0.0f)
			{
				var dbReturn = UiUtilities.LinearToDb(linear);
				textField.Text = $"{dbReturn}dB";
			}
			textField.ReleaseFocus();
		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:OnMatrixVolumeSubmitted - Error: {ex.Message}", 2);
		}
	}

	/// <summary>
	/// Generates channel labels for routing matrix.
	/// </summary>
	/// <param name="channels">Number of channels.</param>
	/// <param name="isInput">Whether these are input channels.</param>
	/// <returns>List of channel labels.</returns>
	private List<string> GetChannelLabels(int channels, bool isInput)
	{
		return channels switch
		{
			1 => new List<string> { "Mono" },
			2 => new List<string> { "Left", "Right" },
			4 => new List<string> { "Front Left", "Front Right", "Rear Left", "Rear Right" }, // Quad
			6 => new List<string> { "Front Left", "Front Right", "Center", "LFE", "Surround Left", "Surround Right" }, // 5.1
			8 => new List<string> { "Front Left", "Front Right", "Center", "LFE", "Surround Left", "Surround Right", "Surround Back Left", "Surround Back Right" }, // 7.1
			_ => Enumerable.Range(1, channels).Select(i => $"Ch {i}").ToList() // Fallback for others
		};
	}

	/// <summary>
	/// Updates the waveform display based on current zoom and start/end times.
	/// </summary>
	private async Task DrawWaveform()
	{
		if (_waveformAccordian.Visible == false) return; // Don't bother drawing if not open.
		if (!_focusedVideoComponent.UseAudio || _focusedVideoComponent.WaveformData == null || _focusedVideoComponent.WaveformData.Length == 0)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "VideoInspector:DrawWaveform - No waveform data available or audio not enabled", 1);
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
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "VideoInspector:DrawWaveform - Waveform panel too small to draw", 1);
			return;
		}

		// Deserialize

		float[] minMax = new float[_focusedVideoComponent.WaveformData.Length / sizeof(float)];
		Buffer.BlockCopy(_focusedVideoComponent.WaveformData, 0, minMax, 0, _focusedVideoComponent.WaveformData.Length);

		int binCount = minMax.Length / 2;
		var pointsLeft = new List<Vector2>();
		var pointsMiddle = new List<Vector2>();
		var pointsRight = new List<Vector2>();

		float height = _waveformPanel.Size.Y / 2f;
		float binWidth = width / binCount;

		float startNorm = (float)(_focusedVideoComponent.StartTime / _focusedVideoComponent.Metadata.Duration);
		float endNorm = (float)(_focusedVideoComponent.EndTime / _focusedVideoComponent.Metadata.Duration);
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
				if (mouseButton.Pressed)
				{
					_isDraggingStart = true;
				}
				else // Released
				{
					if (_isDraggingStart)
					{
						//Recaluclate duration only on release
						SyncDuration();
						_isDraggingStart = false;
					}
				}
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
			_focusedVideoComponent.StartTime = normX * _focusedVideoComponent.Metadata.Duration;
			_startTimeInput.Text = UiUtilities.FormatTime(_focusedVideoComponent.StartTime); // Update input
			DrawWaveform(); // Refresh
		}
	}

	private void OnEndHandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					_isDraggingEnd = true;
				}
				else // Released
				{
					if (_isDraggingEnd)
					{
						SyncDuration();
						_isDraggingEnd = false;
					}
				}
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
			_focusedVideoComponent.EndTime = normX * _focusedVideoComponent.Metadata.Duration;
			_endTimeInput.Text = UiUtilities.FormatTime(_focusedVideoComponent.EndTime); // Update input
			DrawWaveform(); // Refresh
		}
	}

	/// <summary>
	/// Toggles the visibility of an accordion container and updates the button icon.
	/// </summary>
	/// <param name="accordian">The container to toggle.</param>
	/// <param name="button">The button controlling the accordion.</param>
	private async void ToggleAccordian(Control accordian, Button button)
	{
		accordian.Visible = !accordian.Visible;
		button.Icon = GetThemeIcon(accordian.Visible ? "Down" : "Right", "AtlasIcons");

		if (accordian.Name == "WaveformAccordian")
		{
			await DrawWaveform();
		}
	}
	
	private void PreviewToggled()
	{
		if (_previewContainer.Visible)
		{
			_videoPreviewer.SetAreasDeferred(_focusedVideoComponent.TargetLayerId);
			_videoPreviewer.LoadDecoder(_focusedVideoComponent.VideoFile);
		}
		else
		{
			// Fluch video decoder if preview not opened
			_videoPreviewer.ClearDecoder();
		}
	}


	/// <summary>
	/// Clears the file dialog instance.
	/// </summary>
	private void ClearFileDialog()
	{
		_fileDialog.QueueFree();
		_fileDialog = null;
	}
}