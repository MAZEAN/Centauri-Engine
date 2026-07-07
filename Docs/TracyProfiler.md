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

```bash
cp ThirdParty/tracy/build/libTracyClient.so Centauri/bin/Debug/net10.0/
```

Adjust the destination to your actual build output dir. Re-run after a `dotnet clean` — that
wipes `bin/`/`obj/`, including this copy.

## 3. Build the profiler viewer (Ubuntu / Pop!_OS — no apt package exists)

```bash
sudo apt install build-essential cmake pkg-config git libglfw3-dev libfreetype-dev libx11-dev

git clone --branch v0.13.4 https://github.com/wolfpld/tracy /tmp/tracy-profiler
cmake -B /tmp/tracy-profiler/profiler/build -S /tmp/tracy-profiler/profiler \
    -DCMAKE_BUILD_TYPE=Release -DNO_FILESELECTOR=ON -DLEGACY=ON
cmake --build /tmp/tracy-profiler/profiler/build --config Release -j"$(nproc)"
```

Binary: `/tmp/tracy-profiler/profiler/build/tracy-profiler` — move it wherever, e.g. `~/.local/bin/`.

- `-DLEGACY=ON`: X11/GLFW backend, needs only the three `-dev` packages above (works fine under
  XWayland too) instead of Wayland's longer EGL/xkbcommon/wayland-protocols chain.
- `-DNO_FILESELECTOR=ON`: skips the native-file-dialog dependency — not needed just to connect
  to a running game.
- Everything else (capstone, imgui, etc.) is fetched by CMake from GitHub on first configure.
- `--branch v0.13.4` matches the vendored client version
  (`ThirdParty/tracy/public/common/TracyVersion.hpp`) — not required, just keeps protocols in sync.

## 4. Use it

1. Run `tracy-profiler`.
2. Launch the game, Properties → Scene → Tracy Profiler → check **Enabled**
   (`debug.tracyEnabled` in `config.json`, off by default).
3. The game appears in the viewer's connect list automatically.
