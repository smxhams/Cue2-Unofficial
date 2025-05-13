using Godot;
using System;
using System.Threading.Tasks;

namespace Cue2.UI;
public partial class TitleBarUI : Control
{
    public override void _Ready()
    {
        GetNode<Button>("%TitleCue2Menu").Pressed += _onTitleCue2MenuPressed;
        GetNode<Button>("%TitleFileMenu").Pressed += _onTitleFileMenuPressed;
        GetNode<Button>("%TitleHelpMenu").Pressed += _onTitleHelpMenuPressed;
        GetNode<Button>("%WindowMinimizeButton").Pressed += _onWindowMinimizeButtonPressed;
        GetNode<Button>("%WindowExpandButton").Pressed += _onWindowExpandButtonPressed;
        GetNode<Button>("%ExitButton").Pressed += _onExitButtonPressed;
    }

    private void _onExitButtonPressed()
    {
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        Task.Delay(100);
        GetTree().Quit();
    }

    private void _onWindowExpandButtonPressed()
    {
        var windowNumber = GetWindow().GetWindowId();
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Fullscreen){
            GD.Print("Maximise");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen, windowNumber);
        }
        else {
            GD.Print("Minimise");
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed, windowNumber);
            //DisplayServer.WindowSetSize(new Vector2I(600,400), window_number);
        }
    }

    private void _onWindowMinimizeButtonPressed()
    {
        throw new NotImplementedException();
    }

    private void _onTitleHelpMenuPressed()
    {
        throw new NotImplementedException();
    }

    private void _onTitleFileMenuPressed()
    {
        throw new NotImplementedException();
    }

    private void _onTitleCue2MenuPressed()
    {
        throw new NotImplementedException();
    }
}
