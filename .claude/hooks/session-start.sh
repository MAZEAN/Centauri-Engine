#!/bin/bash
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# .NET SDK matching Centauri.csproj's <TargetFramework>net10.0</TargetFramework>,
# glslangValidator for GLSL syntax/type checking without a GPU, and Xvfb + Mesa's
# software rasterizer (llvmpipe) so the engine can actually create a real OpenGL
# context and render — there's no physical GPU in this environment. All ship in
# Ubuntu 24.04's main archive, so this is a plain apt install rather than a curl
# to dot.net — fewer moving parts, no extra network endpoint to depend on.
if ! command -v dotnet >/dev/null 2>&1 || ! command -v glslangValidator >/dev/null 2>&1 \
  || ! command -v Xvfb >/dev/null 2>&1; then
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-10.0 glslang-tools \
    xvfb libgl1-mesa-dri libglx-mesa0 mesa-utils
fi

# Warm the NuGet cache (Silk.NET, ImageSharp, TinyEXR.NET) so the first real
# `dotnet build` in the session is fast, not a cold restore.
dotnet restore "$CLAUDE_PROJECT_DIR/Centauri-Engine.sln"

# Start a virtual display for headless rendering (Silk.NET.Windowing needs a display
# to create a context against, even off-screen). Runs for the life of the session;
# harmless if a hook re-runs and one is already up on :99.
if ! pgrep -f "Xvfb :99" >/dev/null 2>&1; then
  nohup Xvfb :99 -screen 0 1280x720x24 >/tmp/xvfb.log 2>&1 &
fi
mkdir -p /tmp/xdg-runtime && chmod 700 /tmp/xdg-runtime
{
  echo "export DISPLAY=:99"
  echo "export XDG_RUNTIME_DIR=/tmp/xdg-runtime"
} >> "$CLAUDE_ENV_FILE"
