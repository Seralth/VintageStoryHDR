# Native HDR for Vintage Story

Real HDR output for Vintage Story on Windows. Torches, lava, forges, the sun, lightning
and the stars go above paper white instead of clipping at it; the GUI stays where you put
it. It is an ordinary client mod: no launcher, injector, wrapper DLL or ReShade.

This is not Auto HDR or an inverse tone-mapping filter over the finished image. The mod
keeps the game's scene in floating point, stops the final shader from clipping, and uses
the game's own emissive channel to decide what is a light source.

## Requirements

- Windows 10 1709 or later, with **Use HDR** turned on for the display (Settings > System > Display).
- An HDR display, and a GPU driver that implements `WGL_NV_DX_interop2`. Current NVIDIA,
  AMD and Intel drivers all do. Developed and tested on an NVIDIA RTX 3080 Ti.
- Vintage Story 1.22.

Client-side only. Works in single player and on any server; the server does not need it.
On Linux and macOS the mod loads, says so once in the log, and does nothing.

## Install

Download `vshdr-x.y.z.zip` from [Releases](../../releases) and put it, still zipped, in
`%APPDATA%\VintagestoryData\Mods`. Join a world. HDR starts with the first in-world frame;
the main menu stays SDR because the game does not load mods there.

Type `.hdr` in chat to check it is active.

## Tuning

Everything is live and saved to `ModConfig/vshdr.json`.

| Command | Meaning | Default |
| --- | --- | --- |
| `.hdr` | Status | |
| `.hdr on` / `.hdr off` | Toggle, or retry after a failure | on |
| `.hdr paperwhite <nits>` | Luminance of the GUI and of SDR white | 300 |
| `.hdr peak <nits>` | Brightest output; 0 = what the display reports | 0 |
| `.hdr emissive <x>` | Boost for emissive surfaces: torches, lava, sun, lightning | 10 |
| `.hdr highlight <x>` | Boost for non-emissive highlights that were about to clip | 0.75 |
| `.hdr stars <x>` | Boost for the night sky's stars | 4 |
| `.hdr gamma <g>` | SDR decoding gamma | 2.2 |
| `.hdr floatscene <0 or 1>` | RGBA16F scene buffer and bloom chain | 1 |
| `.hdr smoothsky <0 or 1>` | Linear filtering on the sky gradient texture | 1 |
| `.hdr dither <0 or 1>` | One-code-value dither at 10-bit PQ | 1 |

Start with `paperwhite`: set it so the GUI is as bright as you like your desktop. Then
`emissive` to taste. If your display reports an implausible peak (the activation line in
`client-main.log` shows what it reported), set `.hdr peak` to its real figure so the
highlight roll-off lands in the right place.

## Troubleshooting

`.hdr` says why when it is inactive, and the same reason is in `client-main.log` on a line
starting `[vshdr]`.

- **"Windows reports this display as SDR"** -- turn on Use HDR for the monitor the game is
  on, then `.hdr on`.
- **"wglDXOpenDeviceNV failed"** -- OpenGL and Direct3D are on different GPUs, typical of
  hybrid-graphics laptops. Set Vintage Story to the high-performance GPU in Windows
  graphics settings.
- **"game internals this mod hooks have moved"** -- a game update changed something the
  mod depends on. The game runs normally in SDR; check for a mod update.
- Whatever goes wrong, the mod falls back to the game's normal presentation rather than
  leaving you with a black screen.

## How it works

OpenGL on Windows cannot ask for an HDR surface, but DXGI can, and `WGL_NV_DX_interop2`
lets GL render into a D3D11 texture.

1. **Float scene.** The scene colour buffer and the bloom blur chain are RGBA8 in vanilla;
   they are re-specified as RGBA16F so values above 1.0 survive to the final pass.
2. **Shaders.** `final.fsh` and `nightsky.fsh` are patched at load, at named anchors. The
   final pass stops clipping, grades without flattening headroom, and expands highlights
   in linear light, weighted by the game's glow channel. Everything added is behind
   uniforms that default to off, where the shaders compute exactly what vanilla does.
3. **Redirected default framebuffer.** The game blits the scene to framebuffer 0 and draws
   the GUI over it. Framebuffer 0 is replaced by an RGBA16F framebuffer holding the same
   display-referred, gamma-encoded signal as vanilla -- so the GUI blends identically and
   screenshots still work -- except that scene highlights may exceed 1.0.
4. **Present.** At the end of the frame that buffer is decoded, scaled to paper white,
   rolled off towards the display's peak, dithered, and written as scRGB into a D3D11
   texture shared with GL. A flip-model DXGI swapchain on a disabled child window (input
   still goes to the game) presents it, replacing the GL buffer swap.

Vanilla's 8-bit output hides some 8-bit sources that HDR exposes. Two are fixed along the
way: the sky gradient texture is sampled with nearest filtering in vanilla, and Windows
converts scRGB to the display's 10-bit signal without dithering.

[docs/findings.md](docs/findings.md) has the game internals this rests on, and the dead ends.

## Limitations

- Windows only. The main menu is SDR. Toggling Windows HDR mid-session needs `.hdr off` then `.hdr on`.
- Adaptive vsync is treated as vsync on. Vsync off uses tearing presents where supported.
- Colours stay within Rec.709; only luminance is extended.
- Screenshots are SDR.
- Mods that replace `final.fsh` wholesale, or bind framebuffer 0 with raw GL calls, will not mix.
- Overlays that hook OpenGL's buffer swap will not see frames; ones that hook DXGI will.

## Build

Needs the .NET 10 SDK, PowerShell 7 and an installed copy of the game (found in
`%APPDATA%\Vintagestory`, or wherever `VINTAGE_STORY` points).

```powershell
pwsh ./package.ps1            # tests, Release build, artifacts/vshdr-<version>.zip
pwsh ./deploy.ps1 -StopGame   # build and install straight into the game's Mods folder
```

The tests include two that drive the real DXGI runtime and a real OpenGL context, so they
need a GPU and a desktop session.
