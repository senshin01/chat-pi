$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    $archive = "third-party/soxr-0.1.3.tar.gz"
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne "db6ca1b1e8405c6ef92f8294fc123d910abf0a114003b3f0f13fa57a95fd62d0") { throw "libsoxr source checksum mismatch" }
    New-Item -ItemType Directory -Force artifacts/soxr-source, artifacts/native | Out-Null
    tar -xzf $archive -C artifacts/soxr-source
    if ($LASTEXITCODE -ne 0) { throw "Cannot extract libsoxr" }
    cmake -S artifacts/soxr-source/soxr-0.1.3 -B artifacts/soxr-build -A x64 "-DCMAKE_POLICY_VERSION_MINIMUM=3.5" `
        -DBUILD_SHARED_LIBS=ON -DBUILD_SHARED_RUNTIME=OFF -DBUILD_TESTS=OFF -DBUILD_EXAMPLES=OFF -DWITH_OPENMP=OFF -DWITH_LSR_BINDINGS=OFF `
        -DWITH_CR32=OFF -DWITH_CR32S=OFF -DWITH_VR32=OFF -DWITH_CR64=ON -DWITH_CR64S=ON -DWITH_DEV_TRACE=OFF
    if ($LASTEXITCODE -ne 0) { throw "Cannot configure libsoxr" }
    cmake --build artifacts/soxr-build --config Release --parallel 2
    if ($LASTEXITCODE -ne 0) { throw "Cannot build libsoxr" }
    Copy-Item artifacts/soxr-build/bin/Release/soxr.dll artifacts/native/soxr.dll -Force
}
finally { Pop-Location }
