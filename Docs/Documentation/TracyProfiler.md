# Tracy Profiler Setup

Optional. With no native library present, the engine runs exactly as before — every call in
`Centauri/Rendering/Profiling/Tracy.cs` is a no-op.

## 1. Build the client library (repo root)

```bash
sudo apt install build-essential cmake pkg-config
cmake -B ThirdParty/tracy/build -S ThirdParty/tracy -DBUILD_SHARED_LIBS=ON -DTRACY_ENABLE=ON -DCMAKE_BUILD_TYPE=Release
cmake --build ThirdParty/tracy/build --config Release
```

## 2. Deploy it

`Tracy.cs` loads the library via `NativeLibrary.Load(name, assembly, DllImportSearchPath.SafeDirectories)`,
which on Linux does **not** check the bare output directory — it probes a RID-specific
`runtimes/<rid>/native/` subfolder instead (the standard NuGet native-asset layout), plus the
shared .NET framework directory. Put it there, not directly in `net10.0/`:

```bash
mkdir -p Centauri/bin/Debug/net10.0/runtimes/linux-x64/native
cp ThirdParty/tracy/build/libTracyClient.so Centauri/bin/Debug/net10.0/runtimes/linux-x64/native/
```

Adjust `Debug`/`linux-x64` to your actual build config/RID. Re-run after a `dotnet clean` — that
wipes `bin/`/`obj/`, including this copy. If it's still not found, check the exact error now
shown under Properties → Scene → Tracy Profiler — it reports the real dlopen failure reason,
including every path that was tried.

## 3. Build the profiler viewer (Ubuntu / Pop!_OS — no apt package exists)

```bash
sudo apt install build-essential cmake pkg-config git libglfw3-dev libfreetype-dev libx11-dev
git clone https://github.com/wolfpld/tracy /tmp/tracy-profiler
cd /tmp/tracy-profiler && git checkout ba8e4bdcbf38417b1a66f5e3dfdd8fc39ac7ec8f && cd -
cmake -B /tmp/tracy-profiler/profiler/build -S /tmp/tracy-profiler/profiler \
    -DCMAKE_BUILD_TYPE=Release -DNO_FILESELECTOR=ON -DLEGACY=ON \
    -DCMAKE_CXX_FLAGS="-DTRACY_NO_FILESELECTOR"    
cmake --build /tmp/tracy-profiler/profiler/build --config Release -j"$(nproc)"
```

Binary: `/tmp/tracy-profiler/profiler/build/tracy-profiler` — move it wherever, e.g. `~/.local/bin/`.

- `-DLEGACY=ON`: X11/GLFW backend, needs only the three `-dev` packages above (works fine under
  XWayland too) instead of Wayland's longer EGL/xkbcommon/wayland-protocols chain.
- `-DNO_FILESELECTOR=ON`: skips fetching the native-file-dialog dependency (`nfd`) — not needed
  just to connect to a running game.
- `-DCMAKE_CXX_FLAGS="-DTRACY_NO_FILESELECTOR"`: required alongside the above. At this vendored
  commit, `profiler/CMakeLists.txt`'s `NO_FILESELECTOR` option only skips fetching `nfd` — it
  never defines the `TRACY_NO_FILESELECTOR` macro that `BackendGlfw.cpp` actually checks before
  including `nfd_glfw3.h`, so without this flag the build fails with that header missing even
  though `-DNO_FILESELECTOR=ON` is set. A gap in Tracy's own CMake at this unreleased commit, not
  a packaging issue on your end.
- Everything else (capstone, imgui, etc.) is fetched by CMake from GitHub on first configure.
- The checkout pins the exact commit vendored in `ThirdParty/tracy/` — its version string reports
  0.13.4 (`ThirdParty/tracy/public/common/TracyVersion.hpp`), but that's unreleased; the latest
  actual tag is v0.13.1, so `--branch v0.13.4` (an earlier, wrong version of this doc) fails
  outright since no such ref exists. Pinning the commit keeps the viewer's protocol version
  matched to the vendored client either way.

## 3.1 Move it
```bash
mkdir -p ~/.local/bin
cp /tmp/tracy-profiler/profiler/build/tracy-profiler ~/.local/bin/

grep -q '.local/bin' ~/.zshrc || echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.zshrc
source ~/.zshrc

which tracy-profiler
```

## 4. Use it

```bash
tracy-profiler
```

1. Launch the game, Properties → Scene → Tracy Profiler → check **Enabled**
   (`debug.tracyEnabled` in `config.json`, off by default).
2. The game appears in the viewer's connect list automatically.
