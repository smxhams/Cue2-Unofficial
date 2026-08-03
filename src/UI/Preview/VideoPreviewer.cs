// SPDX-FileCopyrightText: 2025-2026 Samuel Moxham
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using Cue2.Domain.Cues;
using Cue2.Services;
using Cue2.Media.Audio;
using Cue2.Media.Decoders;
using Cue2.UI.Utilities;
using Godot;

namespace Cue2.UI.Preview;

/// <summary>
/// Lightweight video inspector preview using pull-based <see cref="VideoSourceDecoder"/>.
/// Presentation is driven by a wall-clock master on the main thread.
/// </summary>
public partial class VideoPreviewer : Control
{
    private GlobalData _globalData;
    private MediaEngine _mediaEngine;
    private GlobalSignals _globalSignals;

    private ImageTexture _godotTexture;
    private Image _godotImage;
    private byte[] _displayRgba;

    private VideoSourceDecoder _decoder;
    private bool _isExiting;
    private bool _isPlaying;
    private bool _updatingFromDecoder;
    private readonly Stopwatch _clock = new Stopwatch();
    private long _mediaOriginUs;

    private Label _currentTimeLabel;
    private Button _playPauseButton;
    private ProgressBar _seekProgressBar;

    private Control _viewArea;
    private Panel _canvasArea;
    private TextureRect _previewTextRect;

    private bool _isDraggingProgress;

    public override void _Ready()
    {
        _globalData = GetNode<GlobalData>("/root/GlobalData");
        _mediaEngine = GetNode<MediaEngine>("/root/MediaEngine");
        _globalSignals = GetNode<GlobalSignals>("/root/GlobalSignals");

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

        _godotImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);
        _previewTextRect.Texture = _godotTexture;

        SetProcess(false);
    }

    public void LoadDecoder(string file)
    {
        ClearDecoder();
        _decoder = new VideoSourceDecoder();
        _playPauseButton.Icon = GetThemeIcon("Play", "AtlasIcons");
        _isPlaying = false;
        string resolved = _globalData?.ResolveMediaPath(file) ?? file;
        OpenAsync(resolved);
    }

    private async void OpenAsync(string file)
    {
        try
        {
            await _decoder.OpenAsync(file);
            var info = _decoder.Info;
            _godotImage = Image.CreateEmpty(info.Width, info.Height, false, Image.Format.Rgba8);
            _godotTexture = ImageTexture.CreateFromImage(_godotImage);
            _previewTextRect.Texture = _godotTexture;
            _displayRgba = new byte[info.FrameByteSize];
            _decoder.Prefetch(4);
            // Show first frame
            if (_decoder.ReadFrame(out var frame))
                Present(frame);
            _mediaOriginUs = 0;
            UpdateTimeLabel(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"VideoPreviewer:LoadDecoder - {ex.Message}");
        }
    }

    public void SetAreasDeferred(int layerId)
    {
        CallDeferred(nameof(SetAreas), layerId);
    }

    /// <summary>
    /// Applies TextureRect expand + stretch modes for the inspector preview.
    /// </summary>
    public void ApplyTextureLayout(TextureRect.ExpandModeEnum expand, TextureRect.StretchModeEnum stretch)
    {
        if (_previewTextRect == null || !IsInstanceValid(_previewTextRect))
            return;
        _previewTextRect.ClipContents = true;
        VideoComponent.ApplyTextureLayout(_previewTextRect, expand, stretch);
    }

    /// <summary>
    /// Applies this component's texture layout to the preview.
    /// </summary>
    public void ApplyTextureLayout(VideoComponent component)
    {
        if (component == null)
            return;
        ApplyTextureLayout(component.TextureExpandMode, component.TextureStretchMode);
    }

    /// <summary>
    /// Applies opacity (0–1) to the inspector preview.
    /// </summary>
    public void ApplyOpacity(float opacity)
    {
        if (_previewTextRect == null || !IsInstanceValid(_previewTextRect))
            return;
        float a = Mathf.Clamp(opacity, 0f, 1f);
        _previewTextRect.Modulate = new Color(1f, 1f, 1f, a);
    }

    private void SetAreas(int layerId)
    {
        var canvas = DisplaysManager.Canvas;
        var layer = DisplaysManager.GetLayerById(layerId);
        if (layer == null || canvas == null)
            return;

        var viewArea = _viewArea.Size;
        var canvasSize = new Vector2(canvas.CanvasSize.X, canvas.CanvasSize.Y);
        var scale = Mathf.Min(viewArea.X / canvasSize.X, viewArea.Y / canvasSize.Y);
        var scaledSize = canvasSize * scale;

        _canvasArea.Size = scaledSize;

        var scaledLayerPos = new Vector2(layer.CanvasPosition.X * scale, layer.CanvasPosition.Y * scale);
        var scaledLayerSize = new Vector2(layer.Size.X * scale, layer.Size.Y * scale);

        _previewTextRect.Position = scaledLayerPos;
        _previewTextRect.Size = scaledLayerSize;

        _seekProgressBar.CustomMinimumSize = new Vector2(scaledSize.X - 93, _seekProgressBar.CustomMinimumSize.Y);
    }

    public override void _Process(double delta)
    {
        if (_isExiting || !_isPlaying || _decoder == null) return;

        long masterUs = _mediaOriginUs + _clock.ElapsedMilliseconds * 1000;
        double durationSec = (_decoder.Info?.DurationUs ?? 0) / 1_000_000.0;
        UpdateTimeLabel(masterUs / 1_000_000.0);

        if (_decoder.Info != null && _decoder.Info.DurationUs > 0 && masterUs >= _decoder.Info.DurationUs)
        {
            _decoder.Seek(0);
            _decoder.Prefetch(4);
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
                continue; // drop late
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

        // Inspector-only scale; house outputs never read this path.
        float scale = ResolvePreviewScale();
        int srcW = frame.Width;
        int srcH = frame.Height;
        int dstW = Math.Max(1, (int)Math.Round(srcW * scale));
        int dstH = Math.Max(1, (int)Math.Round(srcH * scale));

        // Full-quality path: present source buffer directly.
        if (dstW == srcW && dstH == srcH)
        {
            int needed = srcW * srcH * 4;
            if (_displayRgba == null || _displayRgba.Length < needed)
                _displayRgba = new byte[needed];
            Buffer.BlockCopy(frame.Rgba, 0, _displayRgba, 0, needed);

            if (_godotImage == null || !IsInstanceValid(_godotImage)
                || _godotImage.GetWidth() != srcW || _godotImage.GetHeight() != srcH)
            {
                _godotImage = Image.CreateEmpty(srcW, srcH, false, Image.Format.Rgba8);
                _godotTexture = ImageTexture.CreateFromImage(_godotImage);
                if (_previewTextRect != null && IsInstanceValid(_previewTextRect))
                    _previewTextRect.Texture = _godotTexture;
            }

            _godotImage.SetData(srcW, srcH, false, Image.Format.Rgba8, _displayRgba);
            _godotTexture.Update(_godotImage);
            return;
        }

        // Downscale for laptop programming sessions (nearest-neighbour, cheap).
        int neededDst = dstW * dstH * 4;
        if (_displayRgba == null || _displayRgba.Length < neededDst)
            _displayRgba = new byte[neededDst];

        byte[] src = frame.Rgba;
        for (int y = 0; y < dstH; y++)
        {
            int srcY = Math.Min(srcH - 1, (y * srcH) / dstH);
            int srcRow = srcY * srcW * 4;
            int dstRow = y * dstW * 4;
            for (int x = 0; x < dstW; x++)
            {
                int srcX = Math.Min(srcW - 1, (x * srcW) / dstW);
                int si = srcRow + srcX * 4;
                int di = dstRow + x * 4;
                _displayRgba[di] = src[si];
                _displayRgba[di + 1] = src[si + 1];
                _displayRgba[di + 2] = src[si + 2];
                _displayRgba[di + 3] = src[si + 3];
            }
        }

        if (_godotImage == null || !IsInstanceValid(_godotImage)
            || _godotImage.GetWidth() != dstW || _godotImage.GetHeight() != dstH)
        {
            _godotImage = Image.CreateEmpty(dstW, dstH, false, Image.Format.Rgba8);
            _godotTexture = ImageTexture.CreateFromImage(_godotImage);
            if (_previewTextRect != null && IsInstanceValid(_previewTextRect))
                _previewTextRect.Texture = _godotTexture;
        }

        _godotImage.SetData(dstW, dstH, false, Image.Format.Rgba8, _displayRgba);
        _godotTexture.Update(_godotImage);
    }

    /// <summary>
    /// Reads show-scoped preview quality (defaults to full resolution).
    /// </summary>
    private float ResolvePreviewScale()
    {
        try
        {
            var quality = _globalData?.Settings?.VideoPreviewQuality
                ?? Cue2.Domain.ShowSettings.VideoPreviewQuality.Full;
            return Cue2.Domain.ShowSettings.VideoPresentTuning.PreviewScale(quality);
        }
        catch
        {
            return 1f;
        }
    }

    private void OnPlayPausePressed()
    {
        if (_decoder == null) return;
        if (_isPlaying)
        {
            _isPlaying = false;
            _clock.Stop();
            SetProcess(false);
            _playPauseButton.Icon = GetThemeIcon("Play", "AtlasIcons");
        }
        else
        {
            _isPlaying = true;
            _clock.Start();
            SetProcess(true);
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
                    _decoder.Prefetch(4);
                    _mediaOriginUs = us;
                    if (_isPlaying) _clock.Restart();
                    if (_decoder.ReadFrame(out var frame))
                        Present(frame);
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
        double time = percent * dur;
        _currentTimeLabel.Text = UiUtilities.FormatTime(time);
    }

    public void ClearDecoder()
    {
        SetProcess(false);
        _isPlaying = false;
        _clock.Reset();
        if (_decoder != null)
        {
            _decoder.Dispose();
            _decoder = null;
        }
        if (_displayRgba != null)
        {
            MediaMemory.NoteReleased(MediaMemory.ByteBufferBytes(_displayRgba));
            _displayRgba = null;
        }
        if (_previewTextRect != null && IsInstanceValid(_previewTextRect))
            _previewTextRect.Texture = null;
        if (_godotTexture != null && IsInstanceValid(_godotTexture))
        {
            try { _godotTexture.Dispose(); } catch { /* ignore */ }
            _godotTexture = null;
        }
        if (_godotImage != null && IsInstanceValid(_godotImage))
        {
            try { _godotImage.Dispose(); } catch { /* ignore */ }
            _godotImage = null;
        }
        if (_seekProgressBar != null && IsInstanceValid(_seekProgressBar))
            _seekProgressBar.Value = 0;
        _godotImage = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        _godotTexture = ImageTexture.CreateFromImage(_godotImage);
        if (_previewTextRect != null && IsInstanceValid(_previewTextRect))
            _previewTextRect.Texture = _godotTexture;
        MediaMemory.ReclaimIfNeeded();
    }

    public override void _ExitTree()
    {
        _isExiting = true;
        ClearDecoder();
        base._ExitTree();
    }
}
