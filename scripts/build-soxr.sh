#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
archive=third-party/soxr-0.1.3.tar.gz
echo 'db6ca1b1e8405c6ef92f8294fc123d910abf0a114003b3f0f13fa57a95fd62d0  third-party/soxr-0.1.3.tar.gz' | sha256sum --check
mkdir -p artifacts/soxr-source artifacts/native
tar -xzf "$archive" -C artifacts/soxr-source
cmake -S artifacts/soxr-source/soxr-0.1.3 -B artifacts/soxr-build -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON -DBUILD_TESTS=OFF -DBUILD_EXAMPLES=OFF -DWITH_OPENMP=OFF -DWITH_LSR_BINDINGS=OFF \
  -DWITH_CR32=OFF -DWITH_CR32S=OFF -DWITH_VR32=OFF -DWITH_CR64=ON -DWITH_CR64S=ON -DWITH_DEV_TRACE=OFF
cmake --build artifacts/soxr-build --config Release --parallel 2
cp -L artifacts/soxr-build/src/libsoxr.so artifacts/native/libsoxr.so
