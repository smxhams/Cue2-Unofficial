using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;
using System;
using System.Runtime.InteropServices;

namespace Cue2;

public partial class TestFFmpeg : TextureRect
{
    private GlobalData _globalData;
    private MediaEngine _mediaEngine;
    private GlobalSignals _globalSignals;

    private ImageTexture _godotTexture;
    private Image _godotImage;

    private string file = "C:\\MyFiles\\Cue2_Home\\TestCues\\sample_1280x720_surfing_with_audio.mp4";

    private FFmpegVideoDecoder _decoder;
    private bool _isExiting = false;
    private bool _updatingFromDecoder = false;

    // Ui elements
    private Label _currentTimeLabel;
    private Button _playPauseButton;
    private ProgressBar _seekProgressBar;

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        //Ui
        _currentTimeLabel = GetNode<Label>("%CurrentTimeLabel");
        _playPauseButton = GetNode<Button>("%PlayPauseButton");
        _playPauseButton.Icon = GetThemeIcon("Pause", "AtlasIcons");
        _playPauseButton.Pressed += OnPlayPausePressed;
        _seekProgressBar = GetNode<ProgressBar>("%SeekProgressBar");
        _seekProgressBar.MaxValue = 100;
        _seekProgressBar.GuiInput += OnProgressGuiInput;
        
        GD.Print($"TestFFmpeg initialised: {file}");

        // Create decoder and subscribe to events
        _decoder = new FFmpegVideoDecoder(this);
        _decoder.FrameReady += OnFrameReady;
        _decoder.TimeUpdated += OnTimeUpdated;
        _decoder.EndReached += OnEndReached;

        // Create Godot image and texture (will resize after first frame)
        _godotImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);
        Texture = _godotTexture;
        

        // Start decoding asynchronously
        _ = _decoder.StartDecodingAsync(file);
    }

    private void OnFrameReady(byte[] data)
    {
        // Update texture on main thread
        CallDeferred(nameof(UpdateTexture), data);
    }

    private void OnTimeUpdated(double time)
    {
        // Update label on main thread
        CallDeferred(nameof(UpdateTimeLabel), time);
    }

    private void OnPlayPausePressed()
    {
        if (_decoder.IsPaused)
        {
            _decoder.Resume();
            _playPauseButton.Icon = GetThemeIcon("Pause", "AtlasIcons");
        }
        else
        {
            _decoder.Pause();
            _playPauseButton.Icon = GetThemeIcon("Play", "AtlasIcons");
        }
    }

    private void UpdateTimeLabel(double time)
    {
        if (_isExiting || _isDraggingProgress || !IsInstanceValid(_currentTimeLabel) || !IsInstanceValid(_seekProgressBar)) return;
        _updatingFromDecoder = true;
        _currentTimeLabel.Text = UiUtilities.FormatTime(time);
        _seekProgressBar.Value = _decoder?.Duration > 0 ? time / _decoder.Duration * 100 : 0;
        _updatingFromDecoder = false;
    }

    private void UpdateTexture(byte[] rgbaData)
    {
        if (_isExiting || !IsInstanceValid(_godotImage) || !IsInstanceValid(_godotTexture)) return;
        // Resize image if dimensions changed
        if (_godotImage.GetWidth() != _decoder?.Width || _godotImage.GetHeight() != _decoder?.Height)
        {
            _godotImage = Image.CreateEmpty(_decoder.Width, _decoder.Height, false, Image.Format.Rgba8);
            _godotTexture = ImageTexture.CreateFromImage(_godotImage);
            Texture = _godotTexture;
        }

        _godotImage.SetData(_decoder.Width, _decoder.Height, false, Image.Format.Rgba8, rgbaData);
        _godotTexture.Update(_godotImage);
    }

    private bool _isDraggingProgress = false;

    private void OnProgressGuiInput(InputEvent @event)
    {
        if (_isExiting || _decoder == null || !IsInstanceValid(_seekProgressBar) || !IsInstanceValid(_currentTimeLabel)) return;

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _isDraggingProgress = true;
                    UpdateProgressFromMouse();
                }
                else
                {
                    if (_isDraggingProgress)
                    {
                        _isDraggingProgress = false;
                        double time = (_seekProgressBar.Value / 100) * _decoder.Duration;
                        _decoder.Seek(time);
                    }
                }
            }
        }
        else if (@event is InputEventMouseMotion && _isDraggingProgress)
        {
            UpdateProgressFromMouse();
        }
    }

    private void UpdateProgressFromMouse()
    {
        var localPos = _seekProgressBar.GetLocalMousePosition();
        float percent = Mathf.Clamp(localPos.X / _seekProgressBar.Size.X, 0f, 1f);
        _seekProgressBar.Value = percent * 100;
        double time = percent * _decoder.Duration;
        _currentTimeLabel.Text = UiUtilities.FormatTime(time);
    }

    private void OnEndReached()
    {
        GD.Print("TestFFmpeg: Video ended, cleaning up...");
        _isExiting = true;
        if (_decoder != null)
        {
            _decoder.FrameReady -= OnFrameReady;
            _decoder.TimeUpdated -= OnTimeUpdated;
            _decoder.EndReached -= OnEndReached;
            GD.Print("TestFFmpeg: Stopping decoder...");
            _decoder.StopDecodingAsync().Wait();
            GD.Print("TestFFmpeg: Disposing decoder...");
            _decoder.Dispose();
            _decoder = null;
            GD.Print("TestFFmpeg: Cleanup complete.");
        }
    }

    public void InvokeFrameReady(byte[] data)
    {
        OnFrameReady(data);
    }

    public void InvokeTimeUpdated(double time)
    {
        OnTimeUpdated(time);
    }

    public void InvokeEndReached()
    {
        OnEndReached();
    }

    public override void _ExitTree()
    {
        _isExiting = true;
        if (_decoder != null)
        {
            _decoder.FrameReady -= OnFrameReady;
            _decoder.TimeUpdated -= OnTimeUpdated;
            _decoder.EndReached -= OnEndReached;
            GD.Print("TestFFmpeg: Stopping decoder...");
            _decoder.StopDecodingAsync().Wait();
            GD.Print("TestFFmpeg: Disposing decoder...");
            _decoder.Dispose();
            _decoder = null;
            GD.Print("TestFFmpeg: Cleanup complete.");
        }
        base._ExitTree();
    }
}