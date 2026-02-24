# Cue2 - Unofficial Open-Source Event Playback Software

Cue2 is a cross-platform event playback and show control application, inspired by QLab. Built with Godot 4.5.1 Mono, FFmpeg.AutoGen 8.0, and SDL3-CS 3.3.2.1, it supports audio/video playback, OSC commands, text overlays, session management, and more. Targets Windows 10+, macOS, and Linux.

## Features
- Audio and video playback (wide format support via FFmpeg)
- OSC send/receive for integration
- Text overlays and cue library
- Session save/load with undo/redo
- Minimal latency triggering with pre-loading
- Cross-platform compatibility

## Platforms
- Windows 10+
- macOS
- Linux

## Installation
1. Clone the repository: `git clone https://github.com/smxhams/Cue2-Unofficial.git`
2. Install Godot 4.5.1 Mono from [godotengine.org](https://godotengine.org/download).
3. Open `Cue2.sln` in your IDE or use Godot editor to import the project.
4. Ensure FFmpeg and SDL dependencies are installed (via NuGet or manual).

For pre-built binaries, check GitHub Releases.

## Building
- **Build**: `dotnet build` (in project root)
- **Run**: Open in Godot editor or `godot --path .`
- **Clean**: `dotnet clean`

No dedicated tests; use Godot editor for manual testing.

## Dependencies
- Godot Engine (MIT License)
- FFmpeg (LGPLv2.1+; source distributed in releases)
- SDL3 (zlib License)
- FFmpeg.AutoGen (MIT)
- SDL3-CS (zlib)

Attributions in About dialog. For FFmpeg compliance: Dynamic linking used; source available in releases.

## License
This project is licensed under the MIT License - see [LICENSE](LICENSE) file.

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines. Follow code style: PascalCase, XML docs, 4-space indent.

## Community & Support
- GitHub Issues: For bugs/features
- Discussions: For questions

## Credits
Built on Godot Engine (MIT), FFmpeg (LGPL), SDL (zlib).