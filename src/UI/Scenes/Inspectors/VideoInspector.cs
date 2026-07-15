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
	private OptionButton _expandModeOptionButton;
	private OptionButton _stretchModeOptionButton;
	private LineEdit _opacityLineEdit;
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
		_globalSignals= GetNode<GlobalSignals>("/root/GlobalSignals");
		_mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
		_audioDevices = GetNode<AudioDevices>("/root/AudioDevices");

		_globalSignals.ShellFocused += ShellSelected;
		// Media backup rewrites paths while a cue stays selected — refresh URL without re-select
		_globalSignals.SyncShellInspector += RefreshMediaPathDisplay;
		_globalSignals.CueMediaHealthChanged += OnCueMediaHealthChanged;
		// Layer add/remove/rename while inspector is open — refresh without failover
		_globalSignals.DisplaysChanged += OnDisplaysChangedForTargetLayers;

		AssignUiNodeParameters();
		SetupWaveformUi();
		_fileUrlMissingStyle = InspectorMediaUrlStyle.CreateMissingStyle();

		_deleteVideoComponentButton = GetNodeOrNull<Button>("%DeleteVideoComponentButton");
		if (_deleteVideoComponentButton != null)
		{
			_deleteVideoComponentButton.Pressed += OnDeleteVideoComponentPressed;
			_deleteVideoComponentButton.AddThemeColorOverride("font_color", GlobalStyles.Danger);
			try
			{
				_deleteVideoComponentButton.Icon = GetThemeIcon("DeleteBin", "AtlasIcons");
				_deleteVideoComponentButton.ExpandIcon = true;
			}
			catch { /* optional */ }
			_deleteVideoComponentButton.Visible = false;
		}

		_startTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _startTimeInput);
		_endTimeInput.TextSubmitted += newText => TimeFieldSubmitted(newText, _endTimeInput);
		_startTimeInput.FocusExited += () => TimeFieldSubmitted(_startTimeInput.Text, _startTimeInput);
		_endTimeInput.FocusExited += () => TimeFieldSubmitted(_endTimeInput.Text, _endTimeInput);
		_loopInput.Toggled += OnLoopToggled;
		_playCountInput.TextSubmitted += OnPlayCountSubmitted;
		_playCountInput.FocusExited += () => OnPlayCountSubmitted(_playCountInput.Text);
		_scaleWidthLineEdit.TextSubmitted += newText => OnScaleWidthSubmitted(newText);
		_scaleHeightLineEdit.TextSubmitted += newText => OnScaleHeightSubmitted(newText);
		_offsetXLineEdit.TextSubmitted += newText => OnOffsetXSubmitted(newText);
		_offsetYLineEdit.TextSubmitted += newText => OnOffsetYSubmitted(newText);
		_scaleWidthLineEdit.FocusExited += () => OnScaleWidthSubmitted(_scaleWidthLineEdit.Text);
		_scaleHeightLineEdit.FocusExited += () => OnScaleHeightSubmitted(_scaleHeightLineEdit.Text);
		_offsetXLineEdit.FocusExited += () => OnOffsetXSubmitted(_offsetXLineEdit.Text);
		_offsetYLineEdit.FocusExited += () => OnOffsetYSubmitted(_offsetYLineEdit.Text);
		_useAudioCheckButton.Toggled += OnUseAudioToggled;
		_volumeInput.TextSubmitted += newText => VolumeInputSubmitted(newText, _volumeInput);
		_volumeInput.FocusExited += () => VolumeInputSubmitted(_volumeInput.Text, _volumeInput);
		_outputOptionButton.ItemSelected += OutputOptionSelected;
		_targetLayerOptionButton.ItemSelected += TargetLayerSelected;
		_expandModeOptionButton.ItemSelected += ExpandModeSelected;
		_stretchModeOptionButton.ItemSelected += StretchModeSelected;
		_opacityLineEdit.TextSubmitted += OnOpacitySubmitted;
		_opacityLineEdit.FocusExited += () => OnOpacitySubmitted(_opacityLineEdit.Text);

		_globalSignals.SyncShellInspector += OnSyncFromHistory;
		
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
		_expandModeOptionButton = GetNode<OptionButton>("%ExpandModeOptionButton");
		_stretchModeOptionButton = GetNode<OptionButton>("%StretchModeOptionButton");
		_opacityLineEdit = GetNode<LineEdit>("%OpacityLineEdit");
		_scaleWidthLineEdit  = GetNode<LineEdit>("%ScaleWidthLineEdit");
		_scaleHeightLineEdit  = GetNode<LineEdit>("%ScaleHeightLineEdit");
		_offsetXLineEdit  = GetNode<LineEdit>("%OffsetXLineEdit");
		_offsetYLineEdit  = GetNode<LineEdit>("%OffsetYLineEdit");

		PopulateTextureLayoutOptions();
		
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
	/// Fills Expand / Stretch option buttons with user-facing labels and tooltips.
	/// </summary>
	private void PopulateTextureLayoutOptions()
	{
		if (_expandModeOptionButton != null)
		{
			_expandModeOptionButton.Clear();
			AddTextureOption(_expandModeOptionButton, "Keep Size", (int)TextureRect.ExpandModeEnum.KeepSize,
				"Show the video at its original pixel size. It will not grow to fill the layer.");
			AddTextureOption(_expandModeOptionButton, "Ignore Size", (int)TextureRect.ExpandModeEnum.IgnoreSize,
				"Fill the full layer area. Use Stretch to control how the picture fits (recommended default).");
			AddTextureOption(_expandModeOptionButton, "Fit Width Proportional", (int)TextureRect.ExpandModeEnum.FitWidthProportional,
				"Match the layer width; height scales automatically to keep the video’s aspect ratio.");
			AddTextureOption(_expandModeOptionButton, "Fit Height Proportional", (int)TextureRect.ExpandModeEnum.FitHeightProportional,
				"Match the layer height; width scales automatically to keep the video’s aspect ratio.");
		}

		if (_stretchModeOptionButton != null)
		{
			_stretchModeOptionButton.Clear();
			AddTextureOption(_stretchModeOptionButton, "Scale", (int)TextureRect.StretchModeEnum.Scale,
				"Stretch the picture to fill the area. Aspect ratio may change (distort).");
			AddTextureOption(_stretchModeOptionButton, "Tile", (int)TextureRect.StretchModeEnum.Tile,
				"Repeat the picture like tiles to fill the area.");
			AddTextureOption(_stretchModeOptionButton, "Keep", (int)TextureRect.StretchModeEnum.Keep,
				"Show at original size, top-left aligned. May crop or leave empty space.");
			AddTextureOption(_stretchModeOptionButton, "Keep Centered", (int)TextureRect.StretchModeEnum.KeepCentered,
				"Show at original size, centered. May crop or leave empty space.");
			AddTextureOption(_stretchModeOptionButton, "Keep Aspect", (int)TextureRect.StretchModeEnum.KeepAspect,
				"Scale to fit inside without cropping; keeps aspect ratio. Top-left aligned.");
			AddTextureOption(_stretchModeOptionButton, "Keep Aspect Centered", (int)TextureRect.StretchModeEnum.KeepAspectCentered,
				"Scale to fit inside without cropping; keeps aspect ratio and centers the picture (letterbox / pillarbox).");
			AddTextureOption(_stretchModeOptionButton, "Keep Aspect Covered", (int)TextureRect.StretchModeEnum.KeepAspectCovered,
				"Scale to cover the whole area while keeping aspect ratio. Edges may be cropped.");
		}
	}

	private static void AddTextureOption(OptionButton button, string label, int id, string tooltip)
	{
		int index = button.ItemCount;
		button.AddItem(label, id);
		button.SetItemTooltip(index, tooltip);
	}

	private void SetupWaveformUi()
	{
		_waveformPanel = GetNode<PanelContainer>("%WaveformPanel");
		_waveformDisplay = new WaveformDisplay();
		_waveformPanel.AddChild(_waveformDisplay);
		_waveformPanel.MoveChild(_waveformDisplay, 0);
		_waveformPanel.Resized += () => { if (_waveformAccordian.Visible) _ = DrawWaveform(); };
		_waveformPanel.GuiInput += OnWaveformPanelGuiInput;

		_startDragHandle = GetNode<Button>("%StartDragHandle");
		_endDragHandle = GetNode<Button>("%EndDragHandle");
		_startDragHandle.CustomMinimumSize = new Vector2(10, 0);
		_endDragHandle.CustomMinimumSize = new Vector2(10, 0);
		_startDragHandle.Modulate = GlobalStyles.LowColor1;
		_endDragHandle.Modulate = GlobalStyles.HighColor1;
		_startDragHandle.TooltipText = "Start time (drag)";
		_endDragHandle.TooltipText = "End time (drag)";
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
		_waveformScroll.Page = Math.Max(0.01, _viewSpanNorm * maxStart);
		_waveformScroll.Step = maxStart / 200.0;
		_waveformScroll.SetValueNoSignal(Mathf.Clamp(_viewStartNorm, 0f, maxStart));
	}

	private void OnWaveformPanelGuiInput(InputEvent @event)
	{
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
	/// Called when a cue shell is selected. Updates UI based on presence of AudioComponent.
	/// </summary>
	/// <param name="cueId">The ID of the selected cue.</param>
	private StyleBoxFlat _fileUrlMissingStyle;
	private Button _deleteVideoComponentButton;

	/// <summary>
	/// Refreshes the file URL field when media paths are rewritten (e.g. after show-local backup).
	/// </summary>
	private void RefreshMediaPathDisplay()
	{
		if (_fileUrl == null || _focusedVideoComponent == null)
			return;

		string path = _focusedVideoComponent.VideoFile ?? string.Empty;
		if (!string.Equals(_fileUrl.Text, path, StringComparison.Ordinal))
			_fileUrl.Text = path;

		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue?.Id ?? -1);
		ApplyFileUrlMissingStyleFromHealth();
	}

	private void OnCueMediaHealthChanged(int cueId, bool hasIssue, string message)
	{
		if (_focusedCue == null || _focusedCue.Id != cueId)
			return;
		// Only style this inspector's URL if *video* is among the missing paths
		ApplyFileUrlMissingStyleFromHealth();
	}

	/// <summary>
	/// Styles the video URL field only when this cue's video path is reported missing
	/// (not when only audio/other media is missing).
	/// </summary>
	private void ApplyFileUrlMissingStyleFromHealth()
	{
		if (_focusedCue == null || _focusedVideoComponent == null ||
		    string.IsNullOrWhiteSpace(_focusedVideoComponent.VideoFile))
		{
			ApplyFileUrlMissingStyle(false, null);
			return;
		}

		var health = GetNodeOrNull<MediaHealthService>("/root/MediaHealthService");
		bool missing = health != null && health.IsPathMissing(_focusedCue.Id, _focusedVideoComponent.VideoFile);
		ApplyFileUrlMissingStyle(missing, missing ? "File Missing" : null);
	}

	/// <summary>
	/// Applies or clears italic + red border styling on the URL field for missing media.
	/// </summary>
	private void ApplyFileUrlMissingStyle(bool missing, string tooltip)
	{
		_fileUrlMissingStyle ??= InspectorMediaUrlStyle.CreateMissingStyle();
		InspectorMediaUrlStyle.Apply(_fileUrl, _fileUrlMissingStyle, missing, tooltip);
	}

	private async void ShellSelected(int cueId)
	{
		if (cueId < 0)
		{
			_focusedCue = null;
			_focusedVideoComponent = null;
			_fileUrl.Text = "";
			ApplyFileUrlMissingStyle(false, null);
			if (_deleteVideoComponentButton != null)
				_deleteVideoComponentButton.Visible = false;
			_inspectorContent.Visible = false;
			_selectFileContainer.Visible = false;
			_previewContainer.Visible = false;
			try { _videoPreviewer?.ClearDecoder(); } catch { /* optional */ }
			return;
		}

		_focusedCue = CueList.FetchCueFromId(cueId);

		if (_focusedCue == null)
		{
			_focusedVideoComponent = null;
			ApplyFileUrlMissingStyle(false, null);
			if (_deleteVideoComponentButton != null)
				_deleteVideoComponentButton.Visible = false;
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
			ApplyFileUrlMissingStyle(false, null);
			if (_deleteVideoComponentButton != null)
				_deleteVideoComponentButton.Visible = false;
			return;
		}
		
		// Video Component Found
		_focusedVideoComponent = _focusedCue.Components.OfType<VideoComponent>().First();
		if (_deleteVideoComponentButton != null)
			_deleteVideoComponentButton.Visible = true;
		var file = _focusedVideoComponent.VideoFile;
		
		if (_focusedVideoComponent.Metadata == null)
		{
			var refreshedMeta = await _mediaEngine.GetVideoFileMetadataAsync(file);
			_focusedVideoComponent.Metadata = refreshedMeta;
			GD.Print("VideoInspector:ShellSelected - Refreshed metadata from file");
		}

		UpdateVideoUiFields(file);

		PopulateTargetLayerOptions();

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

		await RefreshAudioUiState();

		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
		ApplyFileUrlMissingStyleFromHealth();
		if (_deleteVideoComponentButton != null)
			_deleteVideoComponentButton.Visible = _focusedVideoComponent != null;
	}

	/// <summary>
	/// Removes the video component from the focused cue and resets the inspector UI.
	/// </summary>
	private void OnDeleteVideoComponentPressed()
	{
		if (_focusedCue == null || _focusedVideoComponent == null)
			return;

		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Remove video component");
		_focusedCue.RemoveICueComponent(_focusedVideoComponent);
		_focusedVideoComponent = null;
		_focusedCue.CalculateTotalDuration();

		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);

		_infoLabel.Text = "No Video File";
		_inspectorContent.Visible = false;
		_previewContainer.Visible = false;
		_fileUrl.Text = "";
		ApplyFileUrlMissingStyle(false, null);
		if (_deleteVideoComponentButton != null)
			_deleteVideoComponentButton.Visible = false;

		try { _videoPreviewer?.ClearDecoder(); } catch { /* optional */ }

		_globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
		_globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), _focusedCue.Id);
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
			$"Removed video component from cue {_focusedCue.Name}", 0);
		GD.Print($"VideoInspector:OnDeleteVideoComponentPressed - Removed video from cue {_focusedCue.Id}");
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
	/// Handles file selection from dialog. Adds/replaces VideoComponent and loads metadata + waveform.
	/// </summary>
	/// <param name="path">The selected file path.</param>
	private void FileSelected(string path)
	{
		ClearFileDialog();
		if (_focusedCue == null)
		{
			GD.Print("VideoInspector:FileSelected - No cue selected");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "VideoInspector:No cue selected", 2);
			return;
		}
		SetVideoFile(path, resetInOutPoints: true);
	}
	
	/// <summary>
	/// Handles setting video file URL from drag-and-drop. Creates VideoComponent if none exists.
	/// </summary>
	/// <param name="filePath">The dropped file path.</param>
	public void SetVideoFileUrlFromDrop(string filePath)
	{
		if (_focusedCue == null)
		{
			GD.Print("VideoInspector:SetVideoFileUrlFromDrop - No cue selected");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), "VideoInspector:No cue selected for video file drop", 2);
			return;
		}
		SetVideoFile(filePath, resetInOutPoints: false);
	}
	
	/// <summary>
	/// Sets the video file for the focused cue: create or replace component, load metadata, generate waveform, refresh UI.
	/// </summary>
	/// <param name="filePath">The video file path.</param>
	/// <param name="resetInOutPoints">If true, start/end are reset to full file; otherwise clamp to new duration.</param>
	private async void SetVideoFile(string filePath, bool resetInOutPoints)
	{
		if (_focusedCue == null) return;

		string resolvedPath = _globalData?.ResolveMediaPath(filePath) ?? filePath;
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(resolvedPath))
		{
			GD.Print($"VideoInspector:SetVideoFile - File not found: {filePath}");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:File not found: {filePath}", 2);
			return;
		}

		// Prefer show-relative path when media backup is enabled (copy runs in background)
		string pathToStore = filePath;
		try
		{
			var backup = GetNodeOrNull<MediaBackupManager>("/root/MediaBackupManager");
			var kind = MediaBackupManager.DetectKindFromPath(resolvedPath);
			if (kind != MediaBackupKind.Image)
				kind = MediaBackupKind.Video;
			string relative = backup?.EnsureMediaBackedUp(resolvedPath, kind);
			if (!string.IsNullOrEmpty(relative))
				pathToStore = relative;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"VideoInspector:SetVideoFile - Media backup: {ex.Message}");
		}

		// Resolve or create component; always assign the path (AddVideoComponent alone does not update existing).
		var existingVideo = _focusedCue.Components.OfType<VideoComponent>().FirstOrDefault();
		bool isNewComponent = existingVideo == null;
		if (_focusedCue != null)
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id,
				isNewComponent ? "Add video component" : "Change video file");
		if (existingVideo != null)
		{
			_focusedVideoComponent = existingVideo;
			bool pathChanged = !string.Equals(existingVideo.VideoFile, pathToStore, StringComparison.OrdinalIgnoreCase);
			existingVideo.VideoFile = pathToStore;
			if (pathChanged)
			{
				// Force re-fetch of metadata/waveform for the new file
				existingVideo.WaveformData = null;
				existingVideo.Metadata = null;
			}
		}
		else
		{
			_focusedVideoComponent = _focusedCue.AddVideoComponent(pathToStore);
		}

		_fileUrl.Text = pathToStore;
		_inspectorContent.Visible = true;
		_selectFileContainer.Visible = true;
		_infoLabel.Text = "";

		_cachedPeaks = null;
		_cachedPeaksSource = null;

		try
		{
			var fileMetadata = await _mediaEngine.GetVideoFileMetadataAsync(resolvedPath);
			if (fileMetadata == null)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					$"VideoInspector:SetVideoFile - Failed to read metadata for {Path.GetFileName(filePath)}", 2);
				return;
			}

			_focusedVideoComponent.Metadata = fileMetadata;
			_focusedVideoComponent.HasAudio = fileMetadata.AudioChannels > 0;
			_focusedVideoComponent.UseAudio = _focusedVideoComponent.HasAudio;
			_focusedVideoComponent.ScaledWidth = fileMetadata.Width;
			_focusedVideoComponent.ScaledHeight = fileMetadata.Height;

			var fileDuration = fileMetadata.Duration > 0 ? fileMetadata.Duration : 0.0;

			if (resetInOutPoints || isNewComponent)
			{
				_focusedVideoComponent.StartTime = 0.0;
				_focusedVideoComponent.EndTime = -1.0;
				GD.Print($"VideoInspector:SetVideoFile - Metadata loaded: Duration {fileDuration}s, HasAudio: {_focusedVideoComponent.HasAudio}");
			}
			else
			{
				if (_focusedVideoComponent.StartTime >= fileDuration)
				{
					_focusedVideoComponent.StartTime = 0.0;
					GD.Print("VideoInspector:SetVideoFile - Reset start time (exceeded file duration)");
				}

				if (_focusedVideoComponent.EndTime >= 0 && _focusedVideoComponent.EndTime > fileDuration)
				{
					_focusedVideoComponent.EndTime = -1.0;
					GD.Print("VideoInspector:SetVideoFile - Reset end time to undefined (exceeded file duration)");
				}
				else if (_focusedVideoComponent.EndTime >= 0 &&
				         _focusedVideoComponent.EndTime <= _focusedVideoComponent.StartTime)
				{
					_focusedVideoComponent.EndTime = -1.0;
					GD.Print("VideoInspector:SetVideoFile - Reset end time to undefined (was <= start time)");
				}
			}

			_focusedVideoComponent.RecalculateDuration();
			_focusedCue.CalculateTotalDuration();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"VideoInspector:SetVideoFile - Metadata error: {ex.Message}");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				$"VideoInspector:SetVideoFile - Metadata error: {ex.Message}", 2);
			return;
		}

		UpdateVideoUiFields(pathToStore);

		// Always regenerate waveform when audio is present (RefreshAudioUiState skips if old data remains)
		if (_focusedVideoComponent.HasAudio && _focusedVideoComponent.UseAudio)
		{
			try
			{
				_focusedVideoComponent.WaveformData =
					await _mediaEngine.GenerateWaveformAsync(_focusedVideoComponent.VideoFile);
				if (_focusedVideoComponent.WaveformData == null || _focusedVideoComponent.WaveformData.Length == 0)
				{
					_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
						$"VideoInspector:SetVideoFile - Waveform generation failed for {_focusedVideoComponent.VideoFile}", 2);
				}
			}
			catch (Exception ex)
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					$"VideoInspector:SetVideoFile - Error generating waveform: {ex.Message}", 2);
			}
		}

		await RefreshAudioUiState();

		// Preview decoder for new path
		if (_previewContainer != null && _previewContainer.Visible && _videoPreviewer != null)
		{
			_videoPreviewer.SetAreasDeferred(_focusedVideoComponent.TargetLayerId);
			_videoPreviewer.LoadDecoder(filePath);
		}

		_globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
		_globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), _focusedCue.Id);

		GD.Print($"VideoInspector:SetVideoFile - Set video file: {pathToStore}");
		_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
			$"VideoInspector:Set video file to: {pathToStore}", 0);

		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
		ApplyFileUrlMissingStyleFromHealth();
		if (_deleteVideoComponentButton != null)
			_deleteVideoComponentButton.Visible = true;
	}
	
	/// <summary>
	/// Refreshes the audio-related UI elements based on the current VideoComponent's audio state.
	/// Handles visibility of audio controls, labels, output options, routing matrix, and waveform.
	/// </summary>
	private async Task RefreshAudioUiState()
	{
		if (_focusedVideoComponent.HasAudio)
		{
			_useAudioCheckButton.Visible = true;
			_useAudioCheckButton.ButtonPressed = _focusedVideoComponent.UseAudio;
			_useAudioLabel.Text = "Use Embedded Audio";
			_audioCollapseButton.Visible = true;
			
			PopulateOutputOptions();
			BuildRoutingMatrix();
			
			if (_focusedVideoComponent.UseAudio)
			{
				if (_focusedVideoComponent.WaveformData == null || _focusedVideoComponent.WaveformData.Length == 0)
				{
					try
					{
						_focusedVideoComponent.WaveformData = await _mediaEngine.GenerateWaveformAsync(_focusedVideoComponent.VideoFile);
						if (_focusedVideoComponent.WaveformData.Length == 0)
						{
							_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:RefreshAudioUiState - Waveform generation failed for {_focusedVideoComponent.VideoFile}", 2);
						}
					}
					catch (Exception ex)
					{
						_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:RefreshAudioUiState - Error generating waveform: {ex.Message}", 2);
					}
				}
				
				await DrawWaveform();
			}
			else
			{
				_waveformAccordian.Visible = false;
				_waveformCollapseButton.ButtonPressed = false;
			}
		}
		else
		{
			_audioCollapseButton.Visible = false;
			_useAudioCheckButton.Visible = false;
			_useAudioLabel.Text = "No audio in file";
			_audioAccordian.Visible = false;
			_audioCollapseButton.ButtonPressed = false;
			_waveformAccordian.Visible = false;
			_waveformCollapseButton.ButtonPressed = false;
			_routingAccordian.Visible = false;
			_routingCollapseButton.ButtonPressed = false;
		}
	}
	
	/// <summary>
	/// Updates the video-related UI fields from the current VideoComponent state.
	/// </summary>
	/// <param name="file">The video file path to display.</param>
	private void UpdateVideoUiFields(string file)
	{
		if (_focusedVideoComponent == null) return;

		_selectFileContainer.Visible = true;
		_infoLabel.Text = "";
		_inspectorContent.Visible = true;
		
		_fileUrl.Text = file;
		ApplyFileUrlMissingStyleFromHealth();
		if (_deleteVideoComponentButton != null)
			_deleteVideoComponentButton.Visible = true;
		_startTimeInput.Text = UiUtilities.ParseAndFormatTime(_focusedVideoComponent.StartTime.ToString(), out _, out string startTip);
		_startTimeInput.TooltipText = startTip;

		double metaDur = _focusedVideoComponent.Metadata?.Duration ?? 0;
		if (_focusedVideoComponent.EndTime < 0)
		{
			_endTimeInput.Text = $"Full ({UiUtilities.FormatTime(metaDur)})";
		}
		else
		{
			_endTimeInput.Text = UiUtilities.FormatTime(_focusedVideoComponent.EndTime);
		}
		_durationValue.Text = UiUtilities.FormatTime(_focusedVideoComponent.Duration);
		_fileDurationValue.Text = UiUtilities.FormatTime(metaDur);
		_loopInput.SetPressedNoSignal(_focusedVideoComponent.Loop);
		_playCountInput.Text = _focusedVideoComponent.PlayCount.ToString();
		
		// Update metadata label
		var meta = _focusedVideoComponent.Metadata;
		if (meta != null)
		{
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
		}
		
		// Update scale and offset
		_scaleWidthLineEdit.Text = _focusedVideoComponent.ScaledWidth.ToString();
		_scaleHeightLineEdit.Text = _focusedVideoComponent.ScaledHeight.ToString();
		_offsetXLineEdit.Text = _focusedVideoComponent.OffsetX.ToString();
		_offsetYLineEdit.Text = _focusedVideoComponent.OffsetY.ToString();

		// TextureRect expand + stretch + opacity
		SelectOptionById(_expandModeOptionButton, (int)_focusedVideoComponent.TextureExpandMode);
		SelectOptionById(_stretchModeOptionButton, (int)_focusedVideoComponent.TextureStretchMode);
		_videoPreviewer?.ApplyTextureLayout(_focusedVideoComponent);
		float opacityPct = Mathf.Clamp(_focusedVideoComponent.Opacity, 0f, 1f) * 100f;
		_opacityLineEdit.Text = $"{opacityPct:0.#}";
		_videoPreviewer?.ApplyOpacity(_focusedVideoComponent.Opacity);
		
		// Update volume
		var volume = _focusedVideoComponent.UseAudio ? _focusedVideoComponent.AudioVolume : _focusedVideoComponent.Volume;
		var volumeDb = UiUtilities.LinearToDb((float)volume);
		_volumeInput.Text = $"{volumeDb}dB";
	}

	private static void SelectOptionById(OptionButton button, int id)
	{
		if (button == null)
			return;

		for (int i = 0; i < button.ItemCount; i++)
		{
			if (button.GetItemId(i) == id)
			{
				button.Select(i);
				return;
			}
		}

		button.Select(0);
	}
	
	/// <summary>
	/// Handles submission of time fields (start/end). Parses input, updates component, and recalculates duration.
	/// Blank or -1 input sets end time to undefined.
	/// End times at or beyond file duration are clamped to full duration (EndTime=-1).
	/// </summary>
	/// <param name="text">The submitted text.</param>
	/// <param name="textField">The LineEdit field.</param>
	private void TimeFieldSubmitted(string text, LineEdit textField)
	{
		if (_focusedCue == null || _focusedVideoComponent == null || textField == null)
			return;
		if (_globalData?.HistoryManager?.IsRestoring == true)
			return;
		if (!_focusedCue.Components.Contains(_focusedVideoComponent))
			return;

		try
		{
			if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-1")
			{
				if (textField == _startTimeInput)
				{
					if (Math.Abs(_focusedVideoComponent.StartTime) < 1e-9)
						return;
					_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video start time");
					_focusedVideoComponent.StartTime = 0.0;
					textField.Text = "00:00.000";
					textField.TooltipText = "00m:00s.000ms";
					GD.Print("VideoInspector:TimeFieldSubmitted - Start time reset to 0");
				}
				else if (textField == _endTimeInput)
				{
					if (_focusedVideoComponent.EndTime < 0)
						return;
					_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video end time");
					_focusedVideoComponent.EndTime = -1.0; // Undefined = play to end
					double metaDur = _focusedVideoComponent.Metadata?.Duration ?? 0;
					textField.Text = $"Full ({UiUtilities.FormatTime(metaDur)})";
					textField.TooltipText = "End time undefined (plays full file)";
					GD.Print("VideoInspector:TimeFieldSubmitted - End time set to undefined (full)");
				}
				
				SyncDuration();
				if (textField.HasFocus())
					textField.ReleaseFocus();
				_ = DrawWaveform();
				return;
			}
			
			var time = UiUtilities.ParseAndFormatTime(text, out var timeSecs, out string labeledTime);
            
			if (time == "")
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid time format in {textField.Name}: {text}", 1);
				return;
			}

			if (textField == _startTimeInput)
			{
				if (Math.Abs(_focusedVideoComponent.StartTime - timeSecs) < 1e-9)
				{
					textField.Text = time;
					textField.TooltipText = labeledTime;
					return;
				}
				_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video start time");
				_focusedVideoComponent.StartTime = timeSecs;
			}
			else if (textField == _endTimeInput)
			{
				// At or beyond file duration = play to end (same as blank field).
				double fileDuration = _focusedVideoComponent.Metadata?.Duration ?? 0;
				if (fileDuration > 0 && timeSecs >= fileDuration)
				{
					if (_focusedVideoComponent.EndTime < 0)
					{
						textField.Text = $"Full ({UiUtilities.FormatTime(fileDuration)})";
						textField.TooltipText = "End time undefined (plays full file)";
						return;
					}
					_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video end time");
					_focusedVideoComponent.EndTime = -1.0;
					textField.Text = $"Full ({UiUtilities.FormatTime(fileDuration)})";
					textField.TooltipText = "End time undefined (plays full file)";
					GD.Print("VideoInspector:TimeFieldSubmitted - End time clamped to full (exceeded file duration)");
					SyncDuration();
					if (textField.HasFocus())
						textField.ReleaseFocus();
					_ = DrawWaveform();
					return;
				}

				if (Math.Abs(_focusedVideoComponent.EndTime - timeSecs) < 1e-9)
				{
					textField.Text = time;
					textField.TooltipText = labeledTime;
					return;
				}
				_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video end time");
				_focusedVideoComponent.EndTime = timeSecs;
			}

			textField.Text = time;
			textField.TooltipText = labeledTime;
            
			SyncDuration();
			if (textField.HasFocus())
				textField.ReleaseFocus();
			_ = DrawWaveform();

		}
		catch (Exception ex)
		{
			GD.Print($"VideoInspector:TimeFieldSubmitted - Error parsing time: {ex.Message}");
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing time: {ex.Message}", 2);
		}
	}

	private void OnLoopToggled(bool state)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (_focusedVideoComponent.Loop == state) return;
		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video loop");
		_focusedVideoComponent.Loop = state;
		SyncDuration();
	}

	/// <summary>
	/// Re-binds the video component from the live cue and refreshes fields (undo/redo, external edits).
	/// </summary>
	private async void OnSyncFromHistory()
	{
		if (_focusedCue == null) return;
		var cue = CueList.FetchCueFromId(_focusedCue.Id);
		if (cue == null)
		{
			_focusedCue = null;
			_focusedVideoComponent = null;
			return;
		}
		_focusedCue = cue;
		_focusedVideoComponent = cue.GetVideoComponent();
		if (_focusedVideoComponent == null)
		{
			_infoLabel.Text = "No Video File";
			_selectFileContainer.Visible = true;
			_inspectorContent.Visible = false;
			_previewContainer.Visible = false;
			_fileUrl.Text = "";
			if (_deleteVideoComponentButton != null)
				_deleteVideoComponentButton.Visible = false;
			return;
		}

		UpdateVideoUiFields(_focusedVideoComponent.VideoFile ?? string.Empty);

		// Target layer / output assignment may have changed externally (delete→unassign/replace, undo).
		PopulateTargetLayerOptions();
		if (_videoPreviewer != null && _focusedVideoComponent.TargetLayerId >= 0)
			_videoPreviewer.SetAreasDeferred(_focusedVideoComponent.TargetLayerId);

		// Invalidate waveform cache; history omits peak payloads and component instance is new.
		_cachedPeaks = null;
		_cachedPeaksSource = null;
		_isDraggingStart = false;
		_isDraggingEnd = false;

		// Rebuild output + routing matrix; RefreshAudioUiState also regenerates waveform peaks when missing.
		await RefreshAudioUiState();
		await DrawWaveform();
	}
	
	private void SyncDuration()
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;

		_focusedVideoComponent.RecalculateDuration();
		_focusedCue.CalculateTotalDuration();
		_durationValue.Text =
			UiUtilities.ParseAndFormatTime(
				_focusedVideoComponent.Duration.ToString(), out var _, out string durLabeledTime);
		_durationValue.TooltipText = durLabeledTime;

		_globalSignals.EmitSignal(nameof(GlobalSignals.SyncShellInspector));
		_globalSignals.EmitSignal(nameof(GlobalSignals.UpdateShellBar), _focusedCue.Id);
	}
	
	/// <summary>
	/// Handles play count submission with validation to prevent invalid integers.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnPlayCountSubmitted(string newText)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (int.TryParse(newText, out var playCount) && playCount > 0)
		{
			if (_focusedVideoComponent.PlayCount == playCount)
			{
				if (_playCountInput.HasFocus()) _playCountInput.ReleaseFocus();
				return;
			}
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video play count");
			_focusedVideoComponent.PlayCount = playCount;
			SyncDuration();
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid play count: {newText}. Must be positive integer.", 1);
			_playCountInput.Text = _focusedVideoComponent.PlayCount.ToString(); // Revert to previous
		}
		if (_playCountInput.HasFocus())
			_playCountInput.ReleaseFocus();
	}

	/// <summary>
	/// Handles scaled width submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnScaleWidthSubmitted(string newText)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (int.TryParse(newText, out var width) && width > 0)
		{
			if (_focusedVideoComponent.ScaledWidth == width)
			{
				if (_scaleWidthLineEdit.HasFocus()) _scaleWidthLineEdit.ReleaseFocus();
				return;
			}
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video scale width");
			_focusedVideoComponent.ScaledWidth = width;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid scaled width: {newText}. Must be positive integer.", 1);
			_scaleWidthLineEdit.Text = _focusedVideoComponent.ScaledWidth.ToString(); // Revert
		}
		if (_scaleWidthLineEdit.HasFocus())
			_scaleWidthLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles scaled height submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnScaleHeightSubmitted(string newText)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (int.TryParse(newText, out var height) && height > 0)
		{
			if (_focusedVideoComponent.ScaledHeight == height)
			{
				if (_scaleHeightLineEdit.HasFocus()) _scaleHeightLineEdit.ReleaseFocus();
				return;
			}
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video scale height");
			_focusedVideoComponent.ScaledHeight = height;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid scaled height: {newText}. Must be positive integer.", 1);
			_scaleHeightLineEdit.Text = _focusedVideoComponent.ScaledHeight.ToString(); // Revert
		}
		if (_scaleHeightLineEdit.HasFocus())
			_scaleHeightLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles offset X submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnOffsetXSubmitted(string newText)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (int.TryParse(newText, out var offsetX))
		{
			if (_focusedVideoComponent.OffsetX == offsetX)
			{
				if (_offsetXLineEdit.HasFocus()) _offsetXLineEdit.ReleaseFocus();
				return;
			}
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video offset X");
			_focusedVideoComponent.OffsetX = offsetX;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid offset X: {newText}. Must be integer.", 1);
			_offsetXLineEdit.Text = _focusedVideoComponent.OffsetX.ToString(); // Revert
		}
		if (_offsetXLineEdit.HasFocus())
			_offsetXLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles offset Y submission with validation.
	/// </summary>
	/// <param name="newText">The submitted text.</param>
	private void OnOffsetYSubmitted(string newText)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (int.TryParse(newText, out var offsetY))
		{
			if (_focusedVideoComponent.OffsetY == offsetY)
			{
				if (_offsetYLineEdit.HasFocus()) _offsetYLineEdit.ReleaseFocus();
				return;
			}
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video offset Y");
			_focusedVideoComponent.OffsetY = offsetY;
		}
		else
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid offset Y: {newText}. Must be integer.", 1);
			_offsetYLineEdit.Text = _focusedVideoComponent.OffsetY.ToString(); // Revert
		}
		if (_offsetYLineEdit.HasFocus())
			_offsetYLineEdit.ReleaseFocus();
	}

	/// <summary>
	/// Handles toggling of the use audio checkbox. Expands audio accordion when enabled.
	/// </summary>
	/// <param name="state">The toggle state.</param>
	private void OnUseAudioToggled(bool state)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (_focusedVideoComponent.UseAudio == state) return;
		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video use-audio");
		_focusedVideoComponent.UseAudio = state;
		// Embedded-audio output is only required while Use Audio is on
		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
		_ = RefreshAudioUiState();
	}

	/// <summary>
	/// Handles volume input submission with validation and conversion.
	/// </summary>
	/// <param name="text">The submitted text.</param>
	/// <param name="textField">The LineEdit field.</param>
	private void VolumeInputSubmitted(string text, LineEdit textField)
	{
		if (_focusedCue == null || _focusedVideoComponent == null || textField == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		try
		{
			if (!float.TryParse(text.Replace("dB", "").Trim(), out var dbValue))
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid volume format: {text}", 1);
				textField.Text = $"{UiUtilities.LinearToDb((float)_focusedVideoComponent.Volume)}dB";
				if (textField.HasFocus()) textField.ReleaseFocus();
				return;
			}
			if (dbValue > 0)
			{
				dbValue = -dbValue;
			}
			float volume = (float)UiUtilities.DbToLinear(dbValue.ToString());
			var dbReturn = UiUtilities.LinearToDb(volume);
			textField.Text = $"{dbReturn}dB";
			float current = _focusedVideoComponent.UseAudio
				? _focusedVideoComponent.AudioVolume
				: (float)_focusedVideoComponent.Volume;
			if (Math.Abs(current - volume) < 1e-6f)
			{
				if (textField.HasFocus()) textField.ReleaseFocus();
				return;
			}
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video volume");
			if (_focusedVideoComponent.UseAudio)
			{
				_focusedVideoComponent.AudioVolume = volume;
			}
			else
			{
				_focusedVideoComponent.Volume = volume;
			}
			if (textField.HasFocus()) textField.ReleaseFocus();
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
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

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

		bool unchanged =
			newPatchId == _focusedVideoComponent.PatchId
			&& string.Equals(
				newDirect ?? string.Empty,
				_focusedVideoComponent.DirectOutput ?? string.Empty,
				StringComparison.Ordinal);
		if (unchanged)
			return;

		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video audio output");

		if (item.StartsWith("Patch"))
		{
			GD.Print($"VideoInspector:OutputOptionSelected - Patch selected with id {newPatchId}");
			if (newPatch != null)
			{
				_focusedVideoComponent.Patch = newPatch;
				_focusedVideoComponent.PatchId = newPatchId;
				_focusedVideoComponent.DirectOutput = null;
				GD.Print($"VideoInspector:OutputOptionSelected - Patch set to: {newPatch.Name}");
			}
			else
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
					$"VideoInspector:OutputOptionSelected - Patch ID {newPatchId} not found, resetting output", 1);
				_focusedVideoComponent.Patch = null;
				_focusedVideoComponent.PatchId = -1;
				_focusedVideoComponent.DirectOutput = null;
				_outputOptionButton.SetBlockSignals(true);
				_outputOptionButton.Select(0);
				_outputOptionButton.SetBlockSignals(false);
			}
			BuildRoutingMatrix();
		}
		else if (item.StartsWith("Direct Output"))
		{
			GD.Print($"VideoInspector:OutputOptionSelected - Direct output selected: {newDirect}");
			_focusedVideoComponent.DirectOutput = newDirect;
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

		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
	}

	/// <summary>
	/// Refreshes the target-layer list when layers are added/removed/renamed, or when cues are
	/// reassigned after a layer delete (unassign / replace) while this inspector is open.
	/// Preserves the cue's stored <see cref="VideoComponent.TargetLayerId"/> (no failover).
	/// </summary>
	private void OnDisplaysChangedForTargetLayers()
	{
		if (_focusedCue == null)
			return;

		// Re-bind from live cue — external reassignment may have changed TargetLayerId.
		var live = CueList.FetchCueFromId(_focusedCue.Id);
		if (live != null)
		{
			_focusedCue = live;
			_focusedVideoComponent = live.GetVideoComponent();
		}

		if (_focusedVideoComponent == null)
			return;

		PopulateTargetLayerOptions();
		if (_videoPreviewer != null && _focusedVideoComponent.TargetLayerId >= 0)
			_videoPreviewer.SetAreasDeferred(_focusedVideoComponent.TargetLayerId);
	}

	/// <summary>
	/// Builds the target-layer OptionButton: "No Output", live layers, or a missing-layer entry.
	/// Does not rewrite <see cref="VideoComponent.TargetLayerId"/> when the layer is gone
	/// (no silent failover to another layer).
	/// </summary>
	private void PopulateTargetLayerOptions()
	{
		if (_targetLayerOptionButton == null || _focusedVideoComponent == null)
			return;

		// Block ItemSelected while rebuilding — OptionButton would otherwise auto-select
		// the first real layer and overwrite a missing / No Output assignment.
		_targetLayerOptionButton.SetBlockSignals(true);
		try
		{
			_targetLayerOptionButton.Clear();

			// Index 0: explicit none. Use metadata so id does not collide with layer 0
			// (Godot remaps AddItem id -1 to the item index).
			_targetLayerOptionButton.AddItem("No Output");
			_targetLayerOptionButton.SetItemMetadata(0, -1);

			int targetId = _focusedVideoComponent.TargetLayerId;
			int selectedIndex = 0;
			bool matched = targetId < 0; // -1 = No Output

			if (DisplaysManager.Layers != null)
			{
				foreach (var layer in DisplaysManager.Layers)
				{
					if (layer == null) continue;
					_targetLayerOptionButton.AddItem(layer.LayerName);
					int idx = _targetLayerOptionButton.ItemCount - 1;
					_targetLayerOptionButton.SetItemMetadata(idx, layer.LayerId);
					if (layer.LayerId == targetId)
					{
						selectedIndex = idx;
						matched = true;
					}
				}
			}

			// Keep the stored id when the layer was deleted — show missing entry, do not reassign.
			if (!matched && targetId >= 0)
			{
				_targetLayerOptionButton.AddItem("!!! Missing Layer");
				int missIdx = _targetLayerOptionButton.ItemCount - 1;
				_targetLayerOptionButton.SetItemMetadata(missIdx, targetId);
				selectedIndex = missIdx;
			}

			_targetLayerOptionButton.Select(selectedIndex);
		}
		finally
		{
			_targetLayerOptionButton.SetBlockSignals(false);
		}
	}

	/// <summary>
	/// Handles target layer selection.
	/// </summary>
	/// <param name="index">The selected index.</param>
	private void TargetLayerSelected(long index)
	{
		if (_focusedCue == null || _focusedVideoComponent == null) return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;
		if (_targetLayerOptionButton == null) return;

		var item = _targetLayerOptionButton.GetItemText((int)index);
		if (item != null && item.StartsWith("!!! Missing"))
		{
			// Keep stored missing id; do not reassign.
			return;
		}

		int layerId = (int)_targetLayerOptionButton.GetItemMetadata((int)index);
		if (_focusedVideoComponent.TargetLayerId == layerId) return;

		_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video target layer");
		_focusedVideoComponent.TargetLayerId = layerId;
		if (layerId >= 0)
			_videoPreviewer?.SetAreasDeferred(layerId);
		GD.Print($"VideoInspector:TargetLayerSelected - Target layer set to ID {layerId}");

		// Refresh shell ✕ for target layer not assigned / missing
		GetNodeOrNull<MediaHealthService>("/root/MediaHealthService")?.CheckCue(_focusedCue.Id);
	}

	private void ExpandModeSelected(long index)
	{
		if (_focusedVideoComponent == null || _expandModeOptionButton == null)
			return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		int id = _expandModeOptionButton.GetItemId((int)index);
		if ((int)_focusedVideoComponent.TextureExpandMode == id) return;
		if (_focusedCue != null)
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video expand mode");
		_focusedVideoComponent.TextureExpandMode = (TextureRect.ExpandModeEnum)id;
		_videoPreviewer?.ApplyTextureLayout(_focusedVideoComponent);
		ApplyVisualsToPlayingCues();
		GD.Print($"VideoInspector:ExpandModeSelected - Expand={_focusedVideoComponent.TextureExpandMode}");
	}

	private void StretchModeSelected(long index)
	{
		if (_focusedVideoComponent == null || _stretchModeOptionButton == null)
			return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		int id = _stretchModeOptionButton.GetItemId((int)index);
		if ((int)_focusedVideoComponent.TextureStretchMode == id) return;
		if (_focusedCue != null)
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video stretch mode");
		_focusedVideoComponent.TextureStretchMode = (TextureRect.StretchModeEnum)id;
		_videoPreviewer?.ApplyTextureLayout(_focusedVideoComponent);
		ApplyVisualsToPlayingCues();
		GD.Print($"VideoInspector:StretchModeSelected - Stretch={_focusedVideoComponent.TextureStretchMode}");
	}

	/// <summary>
	/// Parses opacity as a percentage (0–100) and stores 0–1 on the component.
	/// </summary>
	private void OnOpacitySubmitted(string text)
	{
		if (_focusedVideoComponent == null || _opacityLineEdit == null)
			return;
		if (_globalData?.HistoryManager?.IsRestoring == true) return;

		try
		{
			string cleaned = (text ?? string.Empty).Replace("%", "").Trim();
			if (!float.TryParse(cleaned, out float pct))
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Invalid opacity: {text}", 1);
				_opacityLineEdit.Text = $"{_focusedVideoComponent.Opacity * 100f:0.#}";
				if (_opacityLineEdit.HasFocus()) _opacityLineEdit.ReleaseFocus();
				return;
			}

			pct = Mathf.Clamp(pct, 0f, 100f);
			float opacity = pct / 100f;
			if (Math.Abs(_focusedVideoComponent.Opacity - opacity) < 1e-6f)
			{
				_opacityLineEdit.Text = $"{pct:0.#}";
				if (_opacityLineEdit.HasFocus()) _opacityLineEdit.ReleaseFocus();
				return;
			}
			if (_focusedCue != null)
				_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video opacity");
			_focusedVideoComponent.Opacity = opacity;
			_opacityLineEdit.Text = $"{pct:0.#}";
			_videoPreviewer?.ApplyOpacity(_focusedVideoComponent.Opacity);
			ApplyVisualsToPlayingCues();
			if (_opacityLineEdit.HasFocus()) _opacityLineEdit.ReleaseFocus();
			GD.Print($"VideoInspector:OnOpacitySubmitted - Opacity set to {pct:0.#}%");
		}
		catch (Exception ex)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"Error parsing opacity: {ex.Message}", 2);
			_opacityLineEdit.Text = $"{_focusedVideoComponent.Opacity * 100f:0.#}";
			if (_opacityLineEdit.HasFocus()) _opacityLineEdit.ReleaseFocus();
		}
	}

	/// <summary>
	/// Pushes expand/stretch/opacity to any currently playing instance of this video component.
	/// </summary>
	private void ApplyVisualsToPlayingCues()
	{
		if (_focusedVideoComponent == null)
			return;

		// CueCommandExectutor is owned by GlobalData (class name is historically misspelled).
		_globalData?.CueCommandExectutor?.RefreshPlayingVideoVisuals(_focusedVideoComponent);
	}

	
	/// <summary>
	/// Populates the output option button with available audio outputs.
	/// </summary>
	private void PopulateOutputOptions()
	{
		if (_outputOptionButton == null || _focusedVideoComponent == null) return;

		_outputOptionButton.SetBlockSignals(true);
		try
		{
			var itemCount = _outputOptionButton.GetItemCount();
			for (int i = 0; i < itemCount; i++)
				_outputOptionButton.RemoveItem(_outputOptionButton.GetItemCount() - 1);

			_outputOptionButton.AddItem("No output");
			int selectedIndex = 0;

			foreach (var patch in _globalData.Settings.GetAudioOutputPatches())
			{
				_outputOptionButton.AddItem($"Patch: {patch.Value.Name}");
				int idx = _outputOptionButton.GetItemCount() - 1;
				_outputOptionButton.SetItemMetadata(idx, patch.Value.Id);
				if (patch.Value.Id == _focusedVideoComponent.PatchId)
					selectedIndex = idx;
			}

			foreach (var output in _audioDevices.GetAvailableAudioDeviceNames())
			{
				_outputOptionButton.AddItem($"Direct Output: {output}");
				int idx = _outputOptionButton.GetItemCount() - 1;
				if (!string.IsNullOrEmpty(_focusedVideoComponent.DirectOutput)
				    && output == _focusedVideoComponent.DirectOutput)
				{
					selectedIndex = idx;
				}
			}

			if (selectedIndex == 0 && !string.IsNullOrEmpty(_focusedVideoComponent.DirectOutput))
			{
				_outputOptionButton.AddItem($"!!! Missing output: {_focusedVideoComponent.DirectOutput}");
				selectedIndex = _outputOptionButton.GetItemCount() - 1;
			}
			if (selectedIndex == 0 && _focusedVideoComponent.PatchId >= 0)
			{
				_outputOptionButton.AddItem($"!!! Missing patch: ID {_focusedVideoComponent.PatchId}");
				selectedIndex = _outputOptionButton.GetItemCount() - 1;
			}

			_outputOptionButton.Select(selectedIndex);
		}
		finally
		{
			_outputOptionButton.SetBlockSignals(false);
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
				volumeEdit.FocusExited += () => OnMatrixVolumeSubmitted(volumeEdit.Text, volumeEdit, row1, col1);
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
		if (_focusedCue == null || _focusedVideoComponent?.Routing == null || textField == null)
			return;
		if (_globalData?.HistoryManager?.IsRestoring == true)
			return;

		GD.Print($"VideoInspector:OnMatrixVolumeSubmitted - In {inputCh}. Out {outputCh}");
		try
		{
			float dbValue;
			if (string.IsNullOrWhiteSpace((text ?? string.Empty).Replace("dB", "").Trim()))
			{
				dbValue = -60.0f;
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:OnMatrixVolumeSubmitted - Blank input treated as OFF for In {inputCh}, Out {outputCh}", 0);
			}
			else if (!float.TryParse((text ?? string.Empty).Replace("dB", "").Trim(), out dbValue))
			{
				_globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"VideoInspector:OnMatrixVolumeSubmitted - Invalid matrix volume: {text}", 1);
				return;
			}

			float linear = (float)UiUtilities.DbToLinear(dbValue.ToString());
			var routingForSet = _focusedVideoComponent.Routing;
			float current = routingForSet.GetVolume(inputCh, outputCh);
			if (Math.Abs(current - linear) < 1e-6f)
			{
				if (linear > 0.0f)
					textField.Text = $"{UiUtilities.LinearToDb(linear)}dB";
				if (textField.HasFocus())
					textField.ReleaseFocus();
				return;
			}

			// Discrete cell commit — each matrix cell change is its own undo step.
			_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video routing volume");
			routingForSet.SetVolume(inputCh, outputCh, linear);
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
	/// Updates the waveform display from peak data and start/end selection.
	/// </summary>
	private async Task DrawWaveform()
	{
		if (_waveformAccordian == null || _waveformAccordian.Visible == false) return;
		if (_focusedVideoComponent == null || !_focusedVideoComponent.UseAudio ||
		    _focusedVideoComponent.WaveformData == null ||
		    _focusedVideoComponent.WaveformData.Length == 0)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				"VideoInspector:DrawWaveform - No waveform data available or audio not enabled", 1);
			return;
		}

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Guard: component may have been rebound during the await (undo/redo).
		if (_focusedVideoComponent == null || !_focusedVideoComponent.UseAudio ||
		    _focusedVideoComponent.WaveformData == null ||
		    _focusedVideoComponent.WaveformData.Length == 0)
			return;

		float width = _waveformPanel.Size.X;
		if (width < 50)
			width = Math.Max(0, _inspectorContent.Size.X - 48);
		if (width < 50)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				"VideoInspector:DrawWaveform - Waveform panel too small to draw", 1);
			return;
		}

		if (_cachedPeaks == null || !ReferenceEquals(_cachedPeaksSource, _focusedVideoComponent.WaveformData))
		{
			_cachedPeaks = WaveformPeaks.FromBytes(_focusedVideoComponent.WaveformData);
			_cachedPeaksSource = _focusedVideoComponent.WaveformData;
		}
		if (_cachedPeaks == null)
		{
			_globalSignals.EmitSignal(nameof(GlobalSignals.Log),
				"VideoInspector:DrawWaveform - Invalid waveform payload", 1);
			return;
		}

		double duration = _focusedVideoComponent.Metadata?.Duration ?? 0;
		if (duration <= 0) duration = 1;
		float startNorm = (float)(_focusedVideoComponent.StartTime / duration);
		float endTime = _focusedVideoComponent.EndTime < 0
			? (float)duration
			: (float)_focusedVideoComponent.EndTime;
		float endNorm = (float)(endTime / duration);

		_viewSpanNorm = Mathf.Clamp(_viewSpanNorm, 0.01f, 1f);
		_viewStartNorm = Mathf.Clamp(_viewStartNorm, 0f, 1f - _viewSpanNorm);

		_waveformDisplay.SetData(_cachedPeaks, startNorm, endNorm, _viewStartNorm, _viewSpanNorm, duration);

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
				// One step per drag (record only on press; motion does not re-record).
				if (_focusedCue != null)
					_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video start time");
				_isDraggingStart = true;
			}
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
			double duration = _focusedVideoComponent.Metadata?.Duration ?? 0;
			if (duration <= 0) return;
			float endN = _focusedVideoComponent.EndTime < 0
				? 1f
				: (float)(_focusedVideoComponent.EndTime / duration);
			norm = Mathf.Clamp(norm, 0f, endN - 0.001f);
			_focusedVideoComponent.StartTime = norm * duration;
			_startTimeInput.Text = UiUtilities.FormatTime(_focusedVideoComponent.StartTime);
			_ = DrawWaveform();
		}
	}

	private void OnEndHandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (mouseButton.Pressed)
			{
				if (_focusedCue != null)
					_globalData?.HistoryManager?.RecordCueChange(_focusedCue.Id, "Edit video end time");
				_isDraggingEnd = true;
			}
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
			double duration = _focusedVideoComponent.Metadata?.Duration ?? 0;
			if (duration <= 0) return;
			float startN = (float)(_focusedVideoComponent.StartTime / duration);
			norm = Mathf.Clamp(norm, startN + 0.001f, 1f);
			_focusedVideoComponent.EndTime = norm * duration;
			_endTimeInput.Text = UiUtilities.FormatTime(_focusedVideoComponent.EndTime);
			_ = DrawWaveform();
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