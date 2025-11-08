using Godot;
using System;
using System.IO;
using System.Linq;
using System.Net;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;

public partial class VideoInspector : Control
	
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	private MediaEngine _mediaEngine;
	
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
	private LineEdit _scaleWidthLineEdit;
	private LineEdit _scaleHeightLineEdit;
	private LineEdit _scaleXLineEdit;
	private LineEdit _scaleYLineEdit;

	private Button _previewCollapseButton;
	private HBoxContainer _previewContainer;
    
	// Audio
	private Button _audioCollapseButton;
	private CheckButton _useAudioCheckButton;
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

		_globalSignals.ShellFocused += ShellSelected;

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
		_scaleWidthLineEdit  = GetNode<LineEdit>("%ScaleWidthLineEdit");
		_scaleHeightLineEdit  = GetNode<LineEdit>("%ScaleHeightLineEdit");

		_previewCollapseButton = GetNode<Button>("%PreviewCollapseButton");
		_previewContainer = GetNode<HBoxContainer>("%PreviewContainer");
	    
		// Audio
		_audioCollapseButton  = GetNode<Button>("%AudioCollapseButton");
		_useAudioCheckButton = GetNode<CheckButton>("%UseAudioCheckButton");
		_outputOptionButton = GetNode<OptionButton>("%OutputOptionButton");
		_volumeInput = GetNode<LineEdit>("%VolumeInput");

		_startTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _startTimeInput);
		_endTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _endTimeInput);
		_loopInput.Toggled += state => { _focusedVideoComponent.Loop = state; SyncDuration(); };
		_playCountInput.TextSubmitted += OnPlayCountSubmitted;
		
		UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);
        
		GetNode<Label>("%InfoLabel").AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
		// Ensure content is hidden at start up
		_inspectorContent.Visible = false;
		_selectFileContainer.Visible = false;

		// Connect Ui input methods.
		_selectFileButton.Pressed += OpenFileDialog;
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
		_startTimeInput.Text =
			UiUtilities.ParseAndFormatTime(_focusedVideoComponent.StartTime.ToString(), out _, out var startTip);
		_startTimeInput.TooltipText = startTip;
		_endTimeInput.Text = UiUtilities.FormatTime(_focusedVideoComponent.EndTime);
		_durationValue.Text = UiUtilities.FormatTime(_focusedVideoComponent.Duration);
		_fileDurationValue.Text = UiUtilities.FormatTime(_focusedVideoComponent.Metadata.Duration);
		_loopInput.ButtonPressed = _focusedVideoComponent.Loop;
		_playCountInput.Text = _focusedVideoComponent.PlayCount.ToString();
		var volumeDb = UiUtilities.LinearToDb((float)_focusedVideoComponent.Volume);
		_volumeInput.Text = $"{volumeDb}dB";
		
		
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
		_fileDialog.AddFilter(GlobalData.VideoFileFilters, "Video Files");
		_fileDialog.AddFilter(GlobalData.ImageFileFilters, "Image Files");
		
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
			//DrawWaveform();

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
			UiUtilities.ParseAndFormatTime(durationSecs.ToString(), out var _, out var durLabeledTime);
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
	/// Clears the file dialog instance.
	/// </summary>
	private void ClearFileDialog()
	{
		_fileDialog.QueueFree();
		_fileDialog = null;
	}
}
