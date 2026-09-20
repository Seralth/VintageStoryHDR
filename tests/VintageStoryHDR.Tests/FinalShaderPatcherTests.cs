using System.Collections.Generic;
using System.IO;
using VintageStoryHDR.Rendering;

namespace VintageStoryHDR.Tests;

public class FinalShaderPatcherTests
{
    private static string InstalledFinalShader()
    {
        string? gameDir = GameAssemblyResolver.ResolveGameDirectory();
        Assert.NotNull(gameDir);
        return File.ReadAllText(Path.Combine(gameDir, "assets", "game", "shaders", "final.fsh"));
    }

    [Fact]
    public void PatchesTheInstalledGamesFinalShader()
    {
        string source = InstalledFinalShader();

        bool ok = FinalShaderPatcher.TryPatch(source, out string patched, out IReadOnlyList<string> problems);

        Assert.True(ok, string.Join("\n", problems));
        Assert.Contains("uniform int vshdrEnabled = 0;", patched);
        Assert.Contains("uniform float vshdrGamut = 0.0;", patched);
        Assert.Contains("uniform float vshdrSceneScale = 1.0;", patched);
        Assert.Contains("vec4 gradedColor = vshdrColorGrade(color);", patched);
        Assert.Contains("outColor.rgb = vshdrExpand(outColor.rgb, texCoord);", patched);
        Assert.Contains("if (vshdrEnabled == 0) color.rgb = min(color.rgb, vec3(1));", patched);

        // The helpers call ColorGrade and sample glowParts, so they must come after both and before main.
        int helpers = patched.IndexOf("vec4 vshdrColorGrade", System.StringComparison.Ordinal);
        Assert.True(helpers > patched.IndexOf("vec4 ColorGrade(vec4 color)", System.StringComparison.Ordinal));
        Assert.True(helpers > patched.IndexOf("uniform sampler2D glowParts", System.StringComparison.Ordinal));
        Assert.True(helpers < patched.IndexOf("void main(void)", System.StringComparison.Ordinal));
    }

    [Fact]
    public void IncludesOfTheFinalShaderDoNotCarryAnAnchor()
    {
        // The game expands #include before the patcher sees the source, and every anchor must stay unique.
        string? gameDir = GameAssemblyResolver.ResolveGameDirectory();
        Assert.NotNull(gameDir);
        foreach (string include in new[] { "fxaa.fsh", "colorutil.ash", "noise3d.ash" })
        {
            string path = Path.Combine(gameDir, "assets", "game", "shaderincludes", include);
            if (!File.Exists(path))
            {
                path = Path.Combine(gameDir, "assets", "game", "shaders", include);
            }

            string text = File.ReadAllText(path);
            Assert.DoesNotContain("void main(void)", text);
            Assert.DoesNotContain("ColorGrade(", text);
        }
    }

    [Fact]
    public void PatchingTwiceChangesNothing()
    {
        Assert.True(FinalShaderPatcher.TryPatch(InstalledFinalShader(), out string once, out _));
        Assert.True(FinalShaderPatcher.TryPatch(once, out string twice, out _));
        Assert.Equal(once, twice);
    }

    [Fact]
    public void AMovedAnchorLeavesTheSourceAlone()
    {
        string source = InstalledFinalShader().Replace("vec4 gradedColor = ColorGrade(color);", "vec4 gradedColor = Grade2(color);");

        bool ok = FinalShaderPatcher.TryPatch(source, out string patched, out IReadOnlyList<string> problems);

        Assert.False(ok);
        Assert.Equal(source, patched);
        Assert.Single(problems);
    }
}
