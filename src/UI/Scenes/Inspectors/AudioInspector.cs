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
    
    private Label _infoLabel;
    private HBoxContainer _selectFileContainer;
    private Button _buttonSelectFile;
    private LineEdit _fileUrl;

    private FileDialog _fileDialog;
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
		
        _globalSignals.ShellFocused += ShellSelected;
        //_globalSignals.FileSelected += FileSelected;
        
        _infoLabel = GetNode<Label>("%InfoLabel");
        _selectFileContainer = GetNode<HBoxContainer>("%SelectFileContainer");
        _buttonSelectFile = GetNode<Button>("%ButtonSelectFile");
        _fileUrl = GetNode<LineEdit>("%FileURL");
        
        GetNode<Label>("%InfoLabel").AddThemeColorOverride("font_color", GlobalStyles.DisabledColor);
        
        _buttonSelectFile.Pressed += OpenFileDialog;
        
    }

    private void ShellSelected(int cueId)
    {
        _focusedCue = CueList.FetchCueFromId(cueId);
        
        var checker = CueComponentChecker.HasComponent<AudioComponent>(_focusedCue);
        GD.Print(checker);
        if (!checker)
        {
            _infoLabel.Text = $"No Audio File";
            _selectFileContainer.Visible = true;
            return;
        }
        
        _focusedAudioComponent = _focusedCue.Components.OfType<AudioComponent>().First();
        var file = _focusedAudioComponent.AudioFile;

        GD.Print($"File is : {file}");
        
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

        _fileDialog.TreeExiting += () => { GD.Print("Does this actually clear?"); };
    }

    private void FileSelected(string path)
    {
        var newPath = Path.Combine("res://Files/", Path.GetFileName(@path));
        GD.Print(@path + "    :    " + newPath);
        _fileUrl.Text = path;
        
        _focusedAudioComponent.AudioFile = path;
        
    }

}
/*private void _on_button_select_file_pressed()
{
    GetNode<FileDialog>("/root/Cue2Base/FileDialog").Visible = true;
}*/

