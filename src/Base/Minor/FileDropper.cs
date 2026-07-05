using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Cue2.Base.Classes;
using Cue2.Base.Classes.CueTypes;
using Cue2.Shared;
using Cue2.UI.Scenes.Inspectors;
using Cue2.UI.Scenes.Popups;

namespace Cue2.Base.Minor;

/// <summary>
/// Handles file drag-and-drop events for the application window.
/// </summary>
public enum FileDropTargetType
{
    None,
    FileUrlAudio,
    FileUrlVideo,
    CueList,
    ShellBar
}

// Conditions on file drop
// Onto audio URL -> only one file -> validate valid audio file -> replace URL
// Onto video URL -> Only one file -> validate valid video file -> replace video URL
// Onto cuelist -> filecount /
//                  if one file -> new cue
//                  if multiple files options: All new cues, as group
// Onto existing shell bar -> as above + option to add as children, above or below. 

/// <summary>
/// Manages file drop detection and routing to appropriate targets.
/// </summary>
public partial class FileDropper : Control
{
    private GlobalSignals _globalSignals;
    private FileDropPopup _activeFileDropPopup;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");
        
        GD.Print("FileDropper ready");

        GetWindow().FilesDropped += OnFilesDropped;
    }

    public override void _ExitTree()
    {
        GetWindow().FilesDropped -= OnFilesDropped;
        CloseActivePopup();
    }

    private void CloseActivePopup()
    {
        if (_activeFileDropPopup != null && IsInstanceValid(_activeFileDropPopup))
        {
            _activeFileDropPopup.QueueFree();
            _activeFileDropPopup = null;
        }
    }

    private void OnFilesDropped(string[] files)
    {
        GD.Print("Files dropped");
        
        var (targetType, targetInfo) = GetDropTarget(GetGlobalMousePosition());
        GD.Print($"FileDropper:OnFilesDropped - targetType={targetType}, files={files.Length}");
        
        if (targetType == FileDropTargetType.FileUrlAudio)
        {
            GD.Print("FileDropper:Audio URL drop detected");
            if (files.Length != 1)
            {
                GD.Print("FileDropper:Audio URL drop requires exactly 1 file");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "FileDropper:Audio URL drop requires exactly 1 file", 1);
            }
            else
            {
                var cue2Base = GetTree().Root.GetNode("Cue2Base");
                var audioInspector = cue2Base?.GetNode<AudioInspector>("%Audio");
                GD.Print($"FileDropper:audioInspector={(audioInspector != null ? "found" : "null")}");
                if (audioInspector != null)
                {
                    audioInspector.SetAudioFileUrlFromDrop(files[0]);
                }
            }
            return;
        }
        
        if (targetType == FileDropTargetType.FileUrlVideo)
        {
            GD.Print("FileDropper:Video URL drop detected");
            if (files.Length != 1)
            {
                GD.Print("FileDropper:Video URL drop requires exactly 1 file");
                _globalSignals.EmitSignal(nameof(GlobalSignals.Log), "FileDropper:Video URL drop requires exactly 1 file", 1);
            }
            else
            {
                var cue2Base = GetTree().Root.GetNode("Cue2Base");
                var videoInspector = cue2Base?.GetNode<VideoInspector>("%Video");
                GD.Print($"FileDropper:videoInspector={(videoInspector != null ? "found" : "null")}");
                if (videoInspector != null)
                {
                    videoInspector.SetVideoFileUrlFromDrop(files[0]);
                }
            }
            return;
        }
        
        if (targetType == FileDropTargetType.ShellBar || targetType == FileDropTargetType.CueList)
        {
            string targetName = targetType.ToString();
            
            if (targetType == FileDropTargetType.ShellBar && int.TryParse(targetInfo, out int cueId))
            {
                CueList.CueIndex.TryGetValue(cueId, out Cue cue);
                targetName = cue?.Name ?? "Unknown";
                GD.Print($"FileDropper:Dropped {files.Length} file(s) on ShellBar for cue '{targetName}' (ID: {cueId})");
            }
            
            GD.Print("FileDropper:Showing FileDropPopup");
            
            CloseActivePopup();
            
            var fileDropPopup = SceneLoader.LoadScene("uid://cwvgtrsfp0vjh", out string error);
            if (fileDropPopup != null && fileDropPopup is FileDropPopup popup)
            {
                _activeFileDropPopup = popup;
                popup.SetDropInfo(files, targetName);
                popup.TreeExiting += () => _activeFileDropPopup = null;
                AddChild(popup);
            }
            else
            {
                GD.PrintErr($"FileDropper:Failed to load FileDropPopup: {error}");
            }
        }
        
        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), 
            $"FileDropper:Dropped {files.Length} file(s) on '{targetType}'. {targetInfo}", 0);

        ProcessDroppedFiles(files, targetType, targetInfo);
    }

    private (FileDropTargetType, string) GetDropTarget(Vector2 mousePos)
    {
        var root = GetTree().Root;
        var cue2Base = root.GetNode("Cue2Base");
        if (cue2Base == null)
            return (FileDropTargetType.None, "");
        
        var audioInspector = cue2Base.GetNode<Control>("%Audio");
        if (audioInspector != null && audioInspector.Visible)
        {
            var audioFileUrl = audioInspector?.GetNode<LineEdit>("%FileURL");
            if (audioFileUrl != null && audioFileUrl.Visible && audioFileUrl.GetGlobalRect().HasPoint(mousePos))
            {
                GD.Print("FileDropper:GetDropTarget - Audio URL drop detected");
                return (FileDropTargetType.FileUrlAudio, "AudioFileURL");
            }
        }
        
        var videoInspector = cue2Base.GetNode<Control>("%Video");
        if (videoInspector != null && videoInspector.Visible)
        {
            var videoFileUrl = videoInspector?.GetNode<LineEdit>("%FileUrl");
            if (videoFileUrl != null && videoFileUrl.Visible && videoFileUrl.GetGlobalRect().HasPoint(mousePos))
            {
                GD.Print("FileDropper:GetDropTarget - Video URL drop detected");
                return (FileDropTargetType.FileUrlVideo, "VideoFileURL");
            }
        }
         
        var cueListUi = cue2Base.GetNode("%CueListUi") as Control;
        var cueContainer = cueListUi?.GetNode<VBoxContainer>("%CueContainer");
        
        if (cueContainer != null)
        {
            var shellBar = FindShellBarInContainer(cueContainer, mousePos);
            if (shellBar != null && IsMouseInVisibleShellBarArea(shellBar, mousePos))
                return (FileDropTargetType.ShellBar, shellBar.CueId.ToString());
        }
        
        if (cueListUi != null && cueListUi.GetGlobalRect().HasPoint(mousePos))
            return (FileDropTargetType.CueList, "CueList");
        
        return (FileDropTargetType.None, "");
    }



    private ShellBar FindShellBarInContainer(VBoxContainer container, Vector2 screenPosition)
    {
        for (int i = 0; i < container.GetChildCount(); i++)
        {
            if (container.GetChild(i) is ShellBar shellBar && shellBar.Visible)
            {
                var childContainer = shellBar.GetNodeOrNull<VBoxContainer>("%ShellChildContainer");
                if (childContainer != null && childContainer.Visible)
                {
                    ShellBar nestedResult = FindShellBarInContainer(childContainer, screenPosition);
                    if (nestedResult != null)
                        return nestedResult;
                }

                if (shellBar.GetGlobalRect().HasPoint(screenPosition))
                {
                    return shellBar;
                }
            }
        }

        return null;
    }

    private bool IsMouseInVisibleShellBarArea(ShellBar shellBar, Vector2 mousePos)
    {
        var globalRect = shellBar.GetGlobalRect();
        
        if (!shellBar.ShellChildContainer.Visible)
        {
            float visibleHeight = shellBar.Size.Y;
            if (mousePos.Y > globalRect.Position.Y + visibleHeight)
                return false;
        }
        
        return true;
    }

    private void ProcessDroppedFiles(string[] files, FileDropTargetType targetType, string targetInfo)
    {
        List<string> validFiles = new();

        foreach (string file in files)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (IsValidMediaExtension(extension))
            {
                validFiles.Add(file);
            }
        }

        if (validFiles.Count > 0)
        {
            string targetName = targetType.ToString();
            _globalSignals.EmitSignal(nameof(GlobalSignals.FileDropped), validFiles.ToArray(), targetName);
        }
    }

    private bool IsValidMediaExtension(string extension)
    {
        return GlobalData.AudioFileFilters.Any(ext => ext.TrimStart('*').Equals(extension, System.StringComparison.OrdinalIgnoreCase));
    }
}
