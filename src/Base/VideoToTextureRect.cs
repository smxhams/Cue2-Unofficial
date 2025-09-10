using Godot;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Cue2.Base.Classes;
using Cue2.Shared;
using LibVLCSharp.Shared;

namespace Cue2.Base;

public partial class VideoToTextureRect : TextureRect
{
	private GlobalData _globalData;
	private int VideoWidth { get; set; }
	private int VideoHeight { get; set; }
	private ImageTexture _godotTexture;
	private Image _godotImage;
	private byte[] _videoBuffer;
	//private Playback _playback;
	private Window _window;
	
	[Export]
	public byte VideoAlpha = 255;
	
	
	// Called when the node enters the scene tree for the first time.
	public bool Initialize()
	{
		_globalData = GetNode<GlobalData>("/root/GlobalData");
		return true;
	}
	
	public void InitVideoTexture(int id, int width, int height)
	{
		VideoWidth = width;
		VideoHeight = height;
		_godotImage = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		_godotTexture = ImageTexture.CreateFromImage(_godotImage);

		Texture = _godotTexture;
		
		var buffersize = VideoWidth * VideoHeight * 4;
		_videoBuffer = new byte[buffersize];
		
		// TODO: Replace once MediaEngine.c active
		/*var mediaPlaterState = _globalData.Playback.GetMediaPlayerState(id);
		mediaPlaterState.MediaPlayer.SetVideoFormatCallbacks(VideoFormat, null);
		mediaPlaterState.MediaPlayer.SetVideoCallbacks(Lock, null, Display);*/
		
		//GD.Print(_globalData.VideoCanvas.);
		
	}


	private uint VideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
		ref uint pitches, ref uint lines)
	{
		width = (uint)VideoWidth;
		height = (uint)VideoHeight;

		// Chroma: "RV32" = RGBA 32-bit
		Marshal.Copy(System.Text.Encoding.ASCII.GetBytes("RV32"), 0, chroma, 4);

		// Pitch = width * bytes per pixel (RGBA = 4 bytes)
		pitches = width * 4;
		lines = height;

		return width * height * 4; // Frame buffer size
	}
    
	private IntPtr Lock(IntPtr opaque, IntPtr planes)
	{
		Marshal.WriteIntPtr(planes, Marshal.UnsafeAddrOfPinnedArrayElement(_videoBuffer, 0));
		return IntPtr.Zero;
	}
    
	private void Display(IntPtr opaque, IntPtr picture)
	{
		for (int i = 0; i < _videoBuffer.Length; i += 4)
		{
			_videoBuffer[i + 3] = VideoAlpha; // Alpha
		}
        
		// Copy VLC frame buffer to Godot image
		//_godotImage.Lock();
		_godotImage.SetData(VideoWidth, VideoHeight, false, Image.Format.Rgba8, _videoBuffer);
		//_godotImage.Unlock();

		// Update the texture in the main thread
		this.CallDeferred(nameof(UpdateTexture));
	}

	// Called from main thread to update the texture
	private void UpdateTexture()
	{
		_godotTexture.Update(_godotImage);
	}
}
