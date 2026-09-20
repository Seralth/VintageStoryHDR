using System.Collections.Generic;
using System.IO;
using VintageStoryHDR.Rendering;

namespace VintageStoryHDR.Tests;

public class NightSkyShaderPatcherTests
{
    private static string InstalledShader()
    {
        string? gameDir = GameAssemblyResolver.ResolveGameDirectory();
        Assert.NotNull(gameDir);
        return File.ReadAllText(Path.Combine(gameDir, "assets", "game", "shaders", "nightsky.fsh"));
    }

    [Fact]
    public void PatchesTheInstalledGamesNightSkyShader()
    {
        bool ok = NightSkyShaderPatcher.TryPatch(InstalledShader(), out string patched, out IReadOnlyList<string> problems);

        Assert.True(ok, string.Join("\n", problems));
        Assert.Contains("uniform float vshdrStarBoost = 0.0;", patched);

        // The boost has to act on the scaled colour, before alpha and night vision are applied.
        int scale = patched.IndexOf("skyCol.rgb *= 2;", System.StringComparison.Ordinal);
        int boost = patched.IndexOf("if (vshdrStarBoost > 0.0)", System.StringComparison.Ordinal);
        int alpha = patched.IndexOf("skyCol.a =", System.StringComparison.Ordinal);
        Assert.True(scale < boost && boost < alpha);
    }

    [Fact]
    public void PatchingTwiceChangesNothing()
    {
        Assert.True(NightSkyShaderPatcher.TryPatch(InstalledShader(), out string once, out _));
        Assert.True(NightSkyShaderPatcher.TryPatch(once, out string twice, out _));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void AMovedAnchorLeavesTheSourceAlone()
    {
        string source = InstalledShader().Replace("skyCol.rgb *= 2;", "skyCol.rgb *= 3;");

        bool ok = NightSkyShaderPatcher.TryPatch(source, out string patched, out IReadOnlyList<string> problems);

        Assert.False(ok);
        Assert.Equal(source, patched);
        Assert.Single(problems);
    }
}
