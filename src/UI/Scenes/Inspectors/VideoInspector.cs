using Godot;
using System;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Utilities;

public partial class VideoInspector : Control
	
{
	private GlobalData _globalData;
	private GlobalSignals _globalSignals;
	
	private Cue _focusedCue;
	private VideoComponent _focusedVideoComponent;
	
	// Ui Nodes
	private Label _infoLabel;
	private HBoxContainer _selectFileContainer;
	private VBoxContainer _inspectorContent;
	private Button _selectFileButton;
	private LineEdit _fileUrl;

	private FileDialog _fileDialog;
	
	public override void _Ready()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		_globalSignals= GetNode<GlobalSignals>("/root/GlobalSignals");

		_globalSignals.ShellFocused += ShellSelected;

		_infoLabel = GetNode<Label>("%InfoLabel");
		_selectFileContainer = GetNode<HBoxContainer>("%SelectFileContainer");
		_inspectorContent = GetNode<VBoxContainer>("%InspectorContent");
		_selectFileButton =  GetNode<Button>("%ButtonSelectFile");
		_fileUrl = GetNode<LineEdit>("%FileUrl");
		
		UiUtilities.FormatLabelsColours(this, GlobalStyles.SoftFontColor);
        
		GetNode<Label>("%InfoLabel").AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
		// Ensure content is hidden at start up
		_inspectorContent.Visible = false;
		_selectFileContainer.Visible = false;

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
		_infoLabel.Text = "";
	}

	private void OpenFileDialog()
	{
		_fileDialog = new FileDialog();
		_fileDialog.FileSelected += FileSelected;
	}

	private void FileSelected(string path)
	{
		
	}


}
