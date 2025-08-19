using Godot;
using System;
using System.IO;
using System.Linq;
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
    private OptionButton _outputOptionButton;
    

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
        _outputOptionButton = GetNode<OptionButton>("%OutputOptionButton");
        
        _startTimeInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _startTimeInput);
        _endTimeInput.TextSubmitted += (string newText) => TimeFieldSubmitted(newText, _endTimeInput);
        _volumeInput.TextSubmitted += (string newText) => VolumeInputSubmitted(newText, _volumeInput);
        _loopInput.Toggled += (bool state) => { _focusedAudioComponent.Loop = state; };
        _playCountInput.TextChanged += (string newText) => { _focusedAudioComponent.PlayCount = int.Parse(newText); };
        _playCountInput.TextSubmitted += (string newText) => { _focusedAudioComponent.PlayCount = int.Parse(newText); _playCountInput.ReleaseFocus(); };
        _outputOptionButton.ItemSelected += OutputOptionSelected;
        
        
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
    
    private void PopulateOutputOptions()
    {
        // Remove items from output options
        var itemCount = _outputOptionButton.GetItemCount();
        for (int i = 0; i < itemCount; i++)
        {
            GD.Print($"i is {i} and item count is {_outputOptionButton.GetItemCount()}");
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
        }
        
        else if (item.StartsWith("Direct Output"))
        {
            var dirOutName = item.Replace("Direct Output: ", "");
            GD.Print($"Direct output selected is: {dirOutName}");
            _focusedAudioComponent.DirectOutput = dirOutName;
            _focusedAudioComponent.Patch = null;
            _focusedAudioComponent.PatchId = -1;
        }
    }

    private void SyncPatchMatrix()
    {
        
    }


    private void LoadWaveForm()
    {
        
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
        PopulateOutputOptions();

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

