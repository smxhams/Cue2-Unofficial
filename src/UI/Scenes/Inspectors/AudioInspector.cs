using Godot;
using System;
using System.IO;
using System.Linq;
using Cue2.Base.Classes;
using Cue2.Shared;
using Cue2.UI.Utilities;

namespace Cue2.UI.Scenes.Inspectors;

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
    private Button _patchCollapseButton;
    private VBoxContainer _patchAccordian;
    private Button _waveformCollapseButton;
    private VBoxContainer _waveformAccordian;
    
    private LineEdit _startTimeInput;
    private LineEdit _endTimeInput;
    private LineEdit _durationValue;
    private LineEdit _fileDurationValue;
    private CheckBox _loopInput;
    private LineEdit _playCountInput;
    private LineEdit _volumeInput;
    

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
        
        _patchCollapseButton = GetNode<Button>("%PatchCollapseButton");
        _patchCollapseButton.Icon = GetThemeIcon("Right", "AtlasIcons");
        _patchAccordian = GetNode<VBoxContainer>("%PatchAccordian");
        _patchAccordian.Visible = false;
        
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
        
        _startTimeInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _startTimeInput);
        _endTimeInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _endTimeInput);
        _volumeInput.TextSubmitted += (string newText) => VolumeInputSubmitted(newText, _volumeInput);
        _loopInput.Toggled += (bool state) => { _focusedAudioComponent.Loop = state; };
        _playCountInput.TextChanged += (string newText) => { _focusedAudioComponent.PlayCount = int.Parse(newText); };
        _playCountInput.TextSubmitted += (string newText) => { _focusedAudioComponent.PlayCount = int.Parse(newText); _playCountInput.ReleaseFocus(); };

        FormatLabels(this);
        
        GetNode<Label>("%InfoLabel").AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
        // Ensure content is hidden at start up
        _inspectorContent.Visible = false;
        _selectFileContainer.Visible = false;
        
        _patchCollapseButton.Pressed += () => ToggleAccordian(_patchAccordian, _patchCollapseButton);
        _waveformCollapseButton.Pressed += () => ToggleAccordian(_waveformAccordian, _waveformCollapseButton);
        _buttonSelectFile.Pressed += OpenFileDialog;
        
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

    private void TimeFieldSubmitted(string text, LineEdit textField)
    {
        var time = UiUtilities.ParseAndFormatTime(text, out var seconds, out var labeledTime);
        GD.Print($"Time is {time} and seconds is {seconds}");
        textField.Text = time;
        textField.TooltipText = labeledTime;
        if (textField == _startTimeInput) _focusedAudioComponent.StartTime = seconds;
        else if (textField == _endTimeInput) _focusedAudioComponent.EndTime = seconds;
        
        var durationSecs = _focusedAudioComponent.EndTime - _focusedAudioComponent.StartTime;
        _durationValue.Text = UiUtilities.ParseAndFormatTime(durationSecs.ToString(), out var _, out var durLabeledTime);
        _focusedAudioComponent.Duration = durationSecs;
        _durationValue.TooltipText = durLabeledTime;
        textField.ReleaseFocus();
        
    }
    
    private void VolumeInputSubmitted(string text, LineEdit textField)
    {
        var volume = UiUtilities.DbToLinear(text);
        var dbReturn = UiUtilities.LinearToDb(volume);
        textField.Text = $"{dbReturn}dB";
        _focusedAudioComponent.Volume = volume;
        textField.ReleaseFocus();
    }

    private void ShellSelected(int cueId)
    {
        _focusedCue = CueList.FetchCueFromId(cueId);
        
        var checker = UiUtilities.HasComponent<AudioComponent>(_focusedCue);
        if (!checker) // No Audio component in Cue
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
        
        
        _startTimeInput.Text = UiUtilities.ParseAndFormatTime(_focusedAudioComponent.StartTime.ToString(), out _, out var startTip);
        _startTimeInput.TooltipText = startTip;
        _endTimeInput.Text = UiUtilities.FormatTime(_focusedAudioComponent.EndTime);
        _durationValue.Text = UiUtilities.FormatTime(_focusedAudioComponent.Duration);
        _fileDurationValue.Text = UiUtilities.FormatTime(_focusedAudioComponent.FileDuration);
        _loopInput.ButtonPressed = _focusedAudioComponent.Loop;
        _playCountInput.Text = _focusedAudioComponent.PlayCount.ToString();
        var volumeDb = UiUtilities.LinearToDb((float)_focusedAudioComponent.Volume);
        _volumeInput.Text = $"{volumeDb}dB";

    }

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

    private void FileSelected(string path)
    {
        ClearFileDialog();
        var newPath = Path.Combine("res://Files/", Path.GetFileName(@path));
        GD.Print(@path + "    :    " + newPath);
        _fileUrl.Text = path;
        _focusedAudioComponent =_focusedCue.AddAudioComponent(path);
        _inspectorContent.Visible = true;
        var fileMetadata = _mediaEngine.GetAudioFileMetadata(path);
        
        _focusedAudioComponent.FileDuration = fileMetadata.TryGetValue("DurationSeconds", out var value) ? (double)value : 0.0;
        ShellSelected(_focusedCue.Id);
        GD.Print((double)fileMetadata["DurationSeconds"]);
        GD.Print(_focusedAudioComponent.FileDuration);
    }

    private void ClearFileDialog()
    {
        _fileDialog.QueueFree();
        _fileDialog = null;
    }
    
    private void ToggleAccordian(VBoxContainer accordian, Button button)
    {
        accordian.Visible = !accordian.Visible;
        button.Icon = GetThemeIcon(accordian.Visible ? "Down" : "Right", "AtlasIcons");
    }

}

