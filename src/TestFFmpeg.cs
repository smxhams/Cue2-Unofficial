using Cue2.Shared;
using Cue2.Shared.Decoders;
using Cue2.UI.Utilities;
using Godot;
using System;
using System.Diagnostics;

namespace Cue2;

/// <summary>
/// Dev harness for pull-based video decoding / presentation.
/// </summary>
public partial class TestFFmpeg : TextureRect
{
    private GlobalData _globalData;
    private MediaEngine _mediaEngine;
    private GlobalSignals _globalSignals;

    private ImageTexture _godotTexture;
    private Image _godotImage;
    private byte[] _displayRgba;

    private string file = "C:\\MyFiles\\Cue2_Home\\TestCues\\sample_1280x720_surfing_with_audio.mp4";

    private VideoSourceDecoder _decoder;
    private bool _isExiting;
    private bool _isPlaying = true;
    private bool _updatingFromDecoder;
    private readonly Stopwatch _clock = new Stopwatch();
    private long _mediaOriginUs;

    private Label _currentTimeLabel;
    private Button _playPauseButton;
    private ProgressBar _seekProgressBar;
    private bool _isDraggingProgress;

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        _currentTimeLabel = GetNode<Label>("%CurrentTimeLabel");
        _playPauseButton = GetNode<Button>("%PlayPauseButton");
        _playPauseButton.Icon = GetThemeIcon("Pause", "AtlasIcons");
        _playPauseButton.Pressed += OnPlayPausePressed;
        _seekProgressBar = GetNode<ProgressBar>("%SeekProgressBar");
        _seekProgressBar.MaxValue = 100;
        _seekProgressBar.GuiInput += OnProgressGuiInput;

        GD.Print($"TestFFmpeg initialised: {file}");

        _godotImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);
        Texture = _godotTexture;

        _decoder = new VideoSourceDecoder();
        OpenAsync();
    }

    private async void OpenAsync()
    {
        try
        {
            await _decoder.OpenAsync(file);
            var info = _decoder.Info;
            _godotImage = Image.CreateEmpty(info.Width, info.Height, false, Image.Format.Rgba8);
            _godotTexture = ImageTexture.CreateFromImage(_godotImage);
            Texture = _godotTexture;
            _displayRgba = new byte[info.FrameByteSize];
            _decoder.Prefetch(6);
            _mediaOriginUs = 0;
            _clock.Restart();
            SetProcess(true);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"TestFFmpeg:Open - {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        if (_isExiting || !_isPlaying || _decoder?.Info == null) return;

        long masterUs = _mediaOriginUs + _clock.ElapsedMilliseconds * 1000;
        UpdateTimeLabel(masterUs / 1_000_000.0);

        if (_decoder.Info.DurationUs > 0 && masterUs >= _decoder.Info.DurationUs)
        {
            _decoder.Seek(0);
            _decoder.Prefetch(6);
            _mediaOriginUs = 0;
            _clock.Restart();
            return;
        }

        int n = 0;
        while (n < 3 && _decoder.TryPeekPts(out long pts))
        {
            if (pts > masterUs + 8000) break;
            if (!_decoder.ReadFrame(out var frame)) break;
            if (masterUs - pts > 80_000 && _decoder.TryPeekPts(out long p2) && p2 <= masterUs)
            {
                n++;
                continue;
            }
            Present(frame);
            n++;
        }

        if (_decoder.BufferedFrames < 3 && !_decoder.EndOfStream)
            _decoder.Prefetch(6);
    }

    private void Present(VideoFrame frame)
    {
        if (frame?.Rgba == null || _isExiting) return;
        int needed = frame.Width * frame.Height * 4;
        if (_displayRgba == null || _displayRgba.Length < needed)
            _displayRgba = new byte[needed];
        Buffer.BlockCopy(frame.Rgba, 0, _displayRgba, 0, needed);

        if (_godotImage.GetWidth() != frame.Width || _godotImage.GetHeight() != frame.Height)
        {
            _godotImage = Image.CreateEmpty(frame.Width, frame.Height, false, Image.Format.Rgba8);
            _godotTexture = ImageTexture.CreateFromImage(_godotImage);
            Texture = _godotTexture;
        }

        _godotImage.SetData(frame.Width, frame.Height, false, Image.Format.Rgba8, _displayRgba);
        _godotTexture.Update(_godotImage);
    }

    private void OnPlayPausePressed()
    {
        if (_isPlaying)
        {
            _isPlaying = false;
            _clock.Stop();
            _playPauseButton.Icon = GetThemeIcon("Play", "AtlasIcons");
        }
        else
        {
            _isPlaying = true;
            _clock.Start();
            _playPauseButton.Icon = GetThemeIcon("Pause", "AtlasIcons");
        }
    }

    private void UpdateTimeLabel(double time)
    {
        if (_isExiting || _isDraggingProgress || !IsInstanceValid(_currentTimeLabel) || !IsInstanceValid(_seekProgressBar)) return;
        _updatingFromDecoder = true;
        _currentTimeLabel.Text = UiUtilities.FormatTime(time);
        double dur = (_decoder?.Info?.DurationUs ?? 0) / 1_000_000.0;
        _seekProgressBar.Value = dur > 0 ? time / dur * 100 : 0;
        _updatingFromDecoder = false;
    }

    private void OnProgressGuiInput(InputEvent @event)
    {
        if (_isExiting || _decoder?.Info == null || !IsInstanceValid(_seekProgressBar)) return;

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _isDraggingProgress = true;
                    UpdateProgressFromMouse();
                }
                else if (_isDraggingProgress)
                {
                    _isDraggingProgress = false;
                    double dur = _decoder.Info.DurationUs / 1_000_000.0;
                    double time = (_seekProgressBar.Value / 100) * dur;
                    long us = (long)(time * 1_000_000);
                    _decoder.Seek(us);
                    _decoder.Prefetch(6);
                    _mediaOriginUs = us;
                    if (_isPlaying) _clock.Restart();
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
        double dur = (_decoder?.Info?.DurationUs ?? 0) / 1_000_000.0;
        _currentTimeLabel.Text = UiUtilities.FormatTime(percent * dur);
    }

    public override void _ExitTree()
    {
        _isExiting = true;
        SetProcess(false);
        _decoder?.Dispose();
        _decoder = null;
        base._ExitTree();
    }
}
