using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Cue2.Shared;

namespace Cue2.Base.Minor;

public partial class FileDropper : Node
{
    private GlobalSignals _globalSignals;
    private Vector2 _lastDropPosition;
    private List<string> pendingFiles;
    private Control targetControl;

    public override void _Ready()
    {
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        GetWindow().FilesDropped += OnFilesDropped;
    }

    public override void _ExitTree()
    {
        GetWindow().FilesDropped -= OnFilesDropped;
    }

    private void OnFilesDropped(string[] files)
    {
        Control targetControl = GetControlAtPosition(_lastDropPosition);
        string targetName = targetControl?.Name ?? "Unknown";

        _globalSignals.EmitSignal(nameof(GlobalSignals.Log), $"FileDropper:Dropped {files.Length} file(s) on '{targetName}'", 0);

        ProcessDroppedFiles(files, targetControl);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
        {
            _lastDropPosition = mouseButton.GlobalPosition;
        }
    }

    private Control GetControlAtPosition(Vector2 screenPosition)
    {
        var root = GetTree().Root;
        
        for (int i = 0; i < root.GetChildCount(); i++)
        {
            if (root.GetChild(i) is Control child && child.Visible)
            {
                Control result = FindControlAt(child, screenPosition);
                if (result != null)
                    return result;
            }
        }
        
        return null;
    }

    private Control FindControlAt(Control parent, Vector2 localPosition)
    {
        if (!parent.Visible)
            return null;

        var rect = parent.GetGlobalRect();
        if (!rect.HasPoint(localPosition))
            return null;

        for (int i = parent.GetChildCount() - 1; i >= 0; i--)
        {
            if (parent.GetChild(i) is Control child)
            {
                Control result = FindControlAt(child, localPosition);
                if (result != null)
                    return result;
            }
        }

        return parent;
    }

    private void ProcessDroppedFiles(string[] files, Control targetControl)
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
            pendingFiles = validFiles;
            targetControl = this.targetControl;  // Assuming from context, adjust if needed
            var popup = new PopupMenu();
            popup.AddItem("Play immediately");
            popup.AddItem("Add to playlist/queue");
            popup.AddItem("Import/copy to project assets");
            popup.AddItem("Copy file paths to clipboard");
            popup.AddItem("Open containing folder");
            popup.AddItem("Cancel");
            popup.IdPressed.Connect(Callable.From<int>(OnPopupItemSelected));
            AddChild(popup);  // Add to scene tree
            popup.Popup(new Rect2(_lastDropPosition, new Vector2(200, 150)));
        }
    }

    private bool IsValidMediaExtension(string extension)
    {
        return GlobalData.AudioFileFilters.Any(ext => ext.TrimStart('*').Equals(extension, System.StringComparison.OrdinalIgnoreCase));
    }
}
