# Third-party notices

Chat Pi uses the following packages. Their source and license details are available at the linked upstream repositories and in the NuGet packages.

| Component | Version | License | Upstream |
| --- | --- | --- | --- |
| NAudio (including Core, Wasapi, Asio, WinMM, Midi) | 2.3.0 | MIT | https://github.com/naudio/NAudio |
| TagLibSharp | 2.3.0 | LGPL-2.1 | https://github.com/mono/taglib-sharp |
| NVorbis | 1.0.0-rc.2 | MIT | https://github.com/NVorbis/NVorbis |
| libsoxr | 0.1.3 | LGPL-2.1-or-later, Ooura FFT / PFFFT notices | https://github.com/chirlu/soxr |
| .NET / Windows Desktop runtime | 10.0 | MIT and bundled third-party notices | https://github.com/dotnet/runtime |

TagLibSharp is distributed as an unmodified, separate `TagLibSharp.dll` so it can be replaced with a compatible modified version. The application is not trimmed or bundled as a single executable. Its corresponding source for 2.3.0 is at https://github.com/mono/taglib-sharp/tree/TaglibSharp-2.3.0.0 .

The binary distribution includes upstream license texts in `licenses/`, including the licenses and third-party notices from the .NET runtime packages used for that build.

libsoxr is distributed as a separate, replaceable `soxr.dll`, built from unmodified source. The full corresponding source archive `third-party/soxr-0.1.3.tar.gz`, rebuild script, LGPL/GPL texts and embedded FFT notices are included in the binary distribution. It uses double-precision scalar/SIMD engines without OpenMP or external codec dependencies. See the repository's `third-party/README.md` for source provenance and build details.

Development-only test packages include xUnit (Apache-2.0), the xUnit Visual Studio adapter (Apache-2.0), and Microsoft.NET.Test.Sdk (MIT).

FFmpeg is used only by the optional fixture regeneration script. It is not used or distributed by the application.
