using System;
using System.Runtime.InteropServices;
using Godot;
using LibVLCSharp.Shared;
using Timer = System.Timers.Timer;

namespace Cue2.Base.Classes;

public partial class MediaPlayerState : TextureRect
{
    public MediaPlayer MediaPlayer { get; }
    public Timer FadeOutTimer { get; set; }
    public int CurrentVolume { get; set; } = 100;
    public int CurrentAlpha { get; set; } = 255;
    public bool HasVideo { get; }
    public bool HasAudio { get; }
    public bool IsFading { get; set; } = false;
    
    
    public TextureRect TargetTextureRect { get; set; }
    


    public MediaPlayerState(MediaPlayer mediaPlayer, bool hasVideo, bool hasAudio)
    {
        MediaPlayer = mediaPlayer;
        HasVideo = hasVideo;
        HasAudio = hasAudio;
    }

    public MediaPlayerState()
    {
    }
}