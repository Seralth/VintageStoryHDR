# Findings (Vintage Story 1.22.7, decompiled with ilspycmd)

## Presentation path

- `ScreenManager.Render`: clear Default and Primary, scene into Primary, post-processing,
  `RenderFinalComposition` (final.fsh, Luma[10] into Primary), `BlitPrimaryToDefault`, GUI
  drawn straight into framebuffer 0, then `window.SwapBuffers()` in `window_RenderFrame`.
- Primary colour 0 is `GL_RGBA8`. FindBright, GodRays and Luma are already `RGBA16F`, so
  promoting that one texture makes the chain float end to end.
- `final.fsh` clips three times: `min(color, 1)` after godrays, the HSL lightness clamp in
  `ColorGrade`, and the "Limit brightness" block.
- Glow channel (`outGlow.r`): per-surface emissive level. The sky writes 1, which is why
  the emissive boost is weighted by brightness.
- Framebuffer 0 is only ever bound through the `CurrentFrameBuffer` and
  `CurrentFrameBufferKeepVw` setters, reached from `LoadFrameBuffer(Default)`,
  `ClearFrameBuffer(Default)` and the tail of `CreateFramebuffer`. Screenshots
  `glReadPixels` from whatever is bound, so they read the redirect buffer and come out as
  clamped SDR.
- The game reloads all shaders right after `StartModsFully`, so a `ShaderRegistry.LoadShader`
  postfix installed in `StartClientSide` catches `final` before the first in-world frame.

## Dead ends

- **Postfix on `ClearFrameBuffer(EnumFrameBuffer)` as the frame-start hook.** It fired for
  every call site except the one in `ScreenManager.Render`: PGO had inlined it there.
  Frame start now hangs off `window_RenderFrame` (event delegate, not inlinable).
- **Swapchain on the game window itself.** GL has already presented to it; flip-model
  DXGI wants a window nothing else presents to. Hence the disabled `STATIC` child window.
- **Float pixel format for the GL window.** Needs the pixel format chosen before the
  window exists, i.e. a launcher, and is vendor-specific. The DXGI route needs neither.

## Verified on

RTX 3080 Ti, driver 616.92, Windows 11, 3840x2160 HDR: activates, right way up; GUI,
inventory item rendering, chat, escape menu, mouse and keyboard all work.
