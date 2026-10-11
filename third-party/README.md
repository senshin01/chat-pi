# libsoxr source

`soxr-0.1.3.tar.gz` is the unmodified upstream 0.1.3 source archive:
https://codeload.github.com/chirlu/soxr/tar.gz/refs/tags/0.1.3

SHA-256: `db6ca1b1e8405c6ef92f8294fc123d910abf0a114003b3f0f13fa57a95fd62d0`

License: LGPL-2.1-or-later, with upstream Ooura FFT and PFFFT notices retained in `licenses/`.
Build scripts check the archive hash before extraction. No source patches are applied.

From the repository root, use `scripts/build-soxr.ps1` with CMake and Visual Studio C++ tools on Windows, or `bash scripts/build-soxr.sh` with CMake and a C compiler on Linux. The resulting replaceable `soxr.dll` is loaded from the application directory. For a binary distribution, run the included `third-party/build-soxr.ps1` from PowerShell; it locates the bundled source under the application directory and writes the rebuilt library to `artifacts/native/soxr.dll`. Replace the application-directory DLL with that compatible build while the app is closed.
