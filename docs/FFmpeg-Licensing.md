# FFmpeg Licensing and Compliance

Cue2 uses FFmpeg for audio and video decoding/encoding via the FFmpeg.AutoGen C# bindings.

## License of the Native Libraries
The core FFmpeg libraries (libavcodec, libavformat, libavutil, libswresample, libswscale, etc.) are licensed under the **GNU Lesser General Public License version 2.1 or later (LGPLv2.1+)**.

Some optional FFmpeg components can be licensed under the GPL. The libraries bundled with Cue2 are intended to have been built using only LGPL-compatible configuration options (i.e. **without** `--enable-gpl` and **without** `--enable-nonfree`).

## Dynamic Linking
Cue2 loads the FFmpeg shared libraries dynamically at runtime using `NativeLibrary.Load` and `ffmpeg.RootPath`. This is the recommended approach for LGPL compliance.

## Compliance Steps Taken / Required
- The native libraries are distributed as shared objects (DLLs on Windows, dylibs on macOS).
- A notice is shown in the application's About window.
- Corresponding source code for the FFmpeg version used to produce the bundled libraries **must** be made available.

## Obtaining Corresponding Source
The exact version of FFmpeg used to build the libraries provided in `bin/` is documented here when known.

**To obtain source:**
1. Visit the official FFmpeg releases: https://ffmpeg.org/releases/
2. Download the tarball matching the version used for the bundled `.dll`/`.dylib` files.
3. Build instructions used for the distributed binaries (example):

   ```bash
   ./configure --prefix=... --enable-shared --disable-static \
               --disable-gpl --disable-nonfree \
               # ... other options as used for the specific release
   make -j$(nproc)
   ```

If you modified FFmpeg, a `changes.diff` should be provided alongside the source.

## User Rights
Under the LGPL you are entitled to:
- Receive the source code of the LGPL libraries.
- Modify the LGPL libraries and relink/replace the shared libraries in this application.
- Redistribute modified versions of the libraries (subject to LGPL terms).

You may replace the files in `bin/<platform>/` with your own build of FFmpeg as long as the ABI is compatible.

## Patents
Certain codecs and formats supported by FFmpeg (e.g. H.264, AAC, etc.) may be covered by patents in some jurisdictions. This is a separate issue from copyright licensing. Consult local laws and/or legal counsel if you intend to distribute content using patented technologies.

## References
- Official FFmpeg legal page: https://ffmpeg.org/legal.html
- LGPLv2.1: https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html
- FFmpeg.AutoGen (the C# binding wrapper, MIT): https://github.com/Ruslan-B/FFmpeg.AutoGen

## Disclaimer
This document is provided for informational purposes. It is not legal advice. For production releases, especially commercial ones, have a qualified attorney review your distribution.

**Important**: The legality of bundling depends on:
- How the specific FFmpeg binaries were configured and built (must be LGPL-only).
- Providing the exact corresponding source.
- Proper notices in the application and documentation.

Cue2 currently bundles the libraries for convenience. If you redistribute Cue2 (especially commercially), ensure you meet all LGPL obligations.