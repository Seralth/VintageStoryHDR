# CLAUDE.md

Guidance for Claude Code in this repo.

## What this is

A Vintage Story **client** mod that presents the game in HDR on Windows through an scRGB
DXGI swapchain shared with OpenGL. [README.md](README.md) explains the pipeline;
[docs/findings.md](docs/findings.md) records the game internals and the dead ends.

## Build and test (Windows, PowerShell 7)

```powershell
dotnet test tests/VintageStoryHDR.Tests/VintageStoryHDR.Tests.csproj -c Release
pwsh ./deploy.ps1 -StopGame
& "$env:APPDATA\Vintagestory\Vintagestory.exe" -o vshdr-test -p creativebuilding   # straight into a test world
```

The game is at `C:\Users\chris\AppData\Roaming\Vintagestory` (1.22.7); set `VINTAGE_STORY`
to point elsewhere. Logs are in `%APPDATA%\VintagestoryData\Logs`; grep for `[vshdr]`.
To inspect game internals: `ilspycmd -t <FullTypeName> VintagestoryLib.dll`.

## Rules

- **Every game internal goes through `Platform/HdrPatchTargets.cs` first**, with a doc
  comment and a check in `Verify()`. Nothing is patched unless `Verify()` is clean.
- **Never patch a small method.** The game runs with TieredPGO; by the time a mod loads,
  hot callers have small callees inlined and a Harmony patch silently never fires. This
  bit `ClearFrameBuffer(Default)`. Patch large methods or delegate-invoked ones, and keep
  the one-shot "Hook reached" debug lines so a bypassed hook shows up in client-debug.log.
- **Fail back to vanilla, never to a black screen.** Anything that can fail throws
  `HdrUnavailableException`; `HdrRuntime` catches it, logs the reason and restores the GL
  swap, the RGBA8 scene buffer and the final shader's vanilla path.
- **The patched `final.fsh` must be vanilla-identical with `vshdrEnabled == 0`.**
- **COM is called through raw vtable slots.** Any new slot gets exercised in
  `PresentationIntegrationTests` before it ships; a wrong slot is a process crash.
- **Leave GL state as found.** `Present` and `OnFrameStart` save and restore what they touch.
- **The render path allocates nothing per frame.**

## Code style

.NET 10, nullable, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`. Use
`CultureInfo.InvariantCulture` for formatting. Tests are xUnit v3 with a global `using Xunit`.
