using Cue2.Base.Classes;
using Cue2.Shared;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Scenes;

public partial class VideoPreviewer : Control
{
    private GlobalData _globalData;
    private MediaEngine _mediaEngine;
    private GlobalSignals _globalSignals;

    private ImageTexture _godotTexture;
    private Image _godotImage;
    
    private FFmpegVideoDecoder _decoder;
    private bool _isExiting = false;
    private bool _updatingFromDecoder = false;

    // Ui elements
    private Label _currentTimeLabel;
    private Button _playPauseButton;
    private ProgressBar _seekProgressBar;

    private Control _viewArea;
    private Panel _canvasArea;
    private TextureRect _previewTextRect;
    
    
    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

        //Ui
        _currentTimeLabel = GetNode<Label>("%CurrentTimeLabel");
        _playPauseButton = GetNode<Button>("%PlayPauseButton");
        _playPauseButton.Icon = GetThemeIcon("Play", "AtlasIcons");
        _playPauseButton.Pressed += OnPlayPausePressed;
        _seekProgressBar = GetNode<ProgressBar>("%SeekProgressBar");
        _seekProgressBar.MaxValue = 100;
        _seekProgressBar.GuiInput += OnProgressGuiInput;
        
        _viewArea = GetNode<Control>("%ViewArea");
        _canvasArea = GetNode<Panel>("%CanvasArea");
        _previewTextRect = GetNode<TextureRect>("%PreviewTextRect");
        
        // Create Godot image and texture (will resize after first frame)
        _godotImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);
        _previewTextRect.Texture = _godotTexture;
    }

    public void LoadDecoder(string file)
    {
        if (_decoder != null)
        {
            ClearDecoder();
        }

        _decoder = new FFmpegVideoDecoder(this);
        _decoder.FrameReady += OnFrameReady;
        _decoder.TimeUpdated += OnTimeUpdated;
        _decoder.EndReached += OnEndReached;
        
        // Start decoding asynchronously
        _ = _decoder.StartDecodingAsync(file);
    }

    public void SetAreasDeferred(int layerId)
    {
        CallDeferred(nameof(SetAreas), layerId);
    }

    private void SetAreas(int layerId)
    {
        var canvas = _globalData.VideoCanvas;
        var layer = DisplaysManager.GetLayerById(layerId);
        
        var viewArea = _viewArea.Size;//GetParent<VBoxContainer>().Size;
        var canvasSize = new Vector2(canvas.CanvasSize.X, canvas.CanvasSize.Y);
        var scale = Mathf.Min(viewArea.X / canvasSize.X, viewArea.Y / canvasSize.Y);
        var scaledSize = canvasSize * scale;
        
        // Scale is coming up 0, checkl sizes are being gotten correct. 

        _canvasArea.Size = scaledSize;

        var scaledLayerPos = new Vector2(layer.CanvasPosition.X * scale, layer.CanvasPosition.Y * scale);
        var scaledLayerSize = new Vector2(layer.Size.X * scale, layer.Size.Y * scale);
        
        _previewTextRect.Position = scaledLayerPos;
        _previewTextRect.Size = scaledLayerSize;

        _seekProgressBar.CustomMinimumSize = new Vector2(scaledSize.X - 116, _seekProgressBar.CustomMinimumSize.Y);
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
            _previewTextRect.Texture = _godotTexture;
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
        GD.Print("VideoPreviewer: Video reached end, paused at EOF.");
        // Decoder remains alive; call _decoder.Seek(0) to continue from start
        _decoder.Seek(0);
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

    public void ClearDecoder()
    {
        if (_decoder != null)
        {
            _decoder.FrameReady -= OnFrameReady;
            _decoder.TimeUpdated -= OnTimeUpdated;
            _decoder.EndReached -= OnEndReached;
            _decoder.StopDecodingAsync().Wait();
            _decoder.Dispose();
            _decoder = null;
        }
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