# Native HDR for Vintage Story

A client mod that presents Vintage Story in real HDR on Windows. No launcher, injector or
wrapper DLL: it is an ordinary mod that takes over presentation from inside the game.

## How it works

OpenGL on Windows cannot ask for an HDR surface, but DXGI can, and `WGL_NV_DX_interop2`
(NVIDIA, AMD and Intel all implement it) lets GL render into a D3D11 texture.

1. **Float scene.** The scene colour buffer is RGBA8 in vanilla; it is re-specified as
   RGBA16F so values above 1.0 survive to the final pass.
2. **Final shader.** `final.fsh` is patched at load, at four anchors. It stops clipping,
   grades without flattening headroom, and expands highlights in linear light: surfaces
   in the game's glow channel (torches, lava, forges, sun, lightning) by `EmissiveBoost`,
   and near-clipping non-emissive highlights by `HighlightBoost`. Everything is behind a
   uniform that defaults to off, where the shader computes exactly what vanilla does.
3. **Redirected default framebuffer.** The game blits the scene to framebuffer 0 and draws
   the GUI over it. Framebuffer 0 is replaced by an RGBA16F framebuffer holding the same
   display-referred, gamma-encoded signal as vanilla -- so the GUI blends identically and
   screenshots still work -- except that scene highlights may exceed 1.0.
4. **Present.** At the end of the frame that buffer is decoded (gamma 2.2), scaled to
   paper white, rolled off towards the display's peak, and written as scRGB into a D3D11
   texture shared with GL. A flip-model DXGI swapchain on a disabled child window (input
   still goes to the game) presents it, replacing the GL buffer swap.

If any of this is unavailable -- SDR display, GL and D3D on different GPUs, a game update
that moved a hook -- the mod logs why and vanilla presentation stays in charge.

## Using it

Turn on **Use HDR** in Windows display settings, install the mod, join a world. HDR starts
with the first in-world frame (mods are not loaded on the main menu, which stays SDR).

`.hdr` shows status. Everything is live and saved to `ModConfig/vshdr.json`:

| Command | Meaning | Default |
| --- | --- | --- |
| `.hdr on` / `.hdr off` | Toggle, or retry after a failure | on |
| `.hdr paperwhite <nits>` | Luminance of the GUI and of SDR white | 200 |
| `.hdr peak <nits>` | Brightest output; 0 = what the display reports | 0 |
| `.hdr emissive <x>` | Boost for emissive surfaces | 3 |
| `.hdr highlight <x>` | Boost for non-emissive highlights | 0.75 |
| `.hdr gamma <g>` | SDR decoding gamma | 2.2 |
| `.hdr floatscene <0 or 1>` | RGBA16F scene buffer | 1 |

If your display reports an implausible peak (the log line on activation shows it), set
`.hdr peak` to its real figure.

## Limitations

- Windows only. The main menu is SDR. Toggling Windows HDR mid-session needs `.hdr off` then `.hdr on`.
- Adaptive vsync is treated as vsync on. Vsync off uses tearing presents where supported.
- Colours stay within Rec.709; only luminance is extended.
- Mods that replace `final.fsh` wholesale, or bind framebuffer 0 with raw GL calls, will not mix.
- Overlays that hook OpenGL's buffer swap will not see frames; ones that hook DXGI will.

## Build

```powershell
dotnet test tests/VintageStoryHDR.Tests/VintageStoryHDR.Tests.csproj -c Release
pwsh ./deploy.ps1 -StopGame
```
