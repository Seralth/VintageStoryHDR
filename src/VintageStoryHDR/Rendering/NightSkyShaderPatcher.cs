using System;
using System.Collections.Generic;

namespace VintageStoryHDR.Rendering;

/// <summary>
/// Rewrites the game's <c>nightsky.fsh</c> so stars exceed paper white.
///
/// <para>
/// The star cubemap is drawn without touching the glow channel, so the final pass's
/// emissive boost never sees it. With the scene buffer in floating point the fix does not
/// need the glow channel: the shader simply outputs bright texels above 1.0. The weight
/// keeps the dim nebula background where it was and lifts only the points of light.
/// </para>
///
/// <para>
/// Behind <c>vshdrStarBoost</c>, which defaults to 0: untouched, the shader is vanilla.
/// </para>
/// </summary>
internal static class NightSkyShaderPatcher
{
    internal const string UniformStarBoost = "vshdrStarBoost";
    internal const string UniformGamma = "vshdrGamma";

    private const string Marker = "// vshdr: patched";
    private const string AnchorMain = "void main () {";
    private const string AnchorScale = "skyCol.rgb *= 2;";

    private const string Declarations = Marker + @"
uniform float " + UniformStarBoost + @" = 0.0;
uniform float " + UniformGamma + @" = 2.2;

";

    // The colour is gamma-encoded; the boost is meant in linear light.
    private const string Boost = @"
	if (vshdrStarBoost > 0.0) {
		float star = smoothstep(0.2, 0.7, max(skyCol.r, max(skyCol.g, skyCol.b)));
		skyCol.rgb *= pow(1.0 + vshdrStarBoost * star, 1.0 / vshdrGamma);
	}";

    internal static bool TryPatch(string source, out string patched, out IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(source);

        patched = source;
        if (source.Contains(Marker, StringComparison.Ordinal))
        {
            problems = Array.Empty<string>();
            return true;
        }

        List<string> missing = new();
        foreach (string anchor in new[] { AnchorMain, AnchorScale })
        {
            int first = source.IndexOf(anchor, StringComparison.Ordinal);
            if (first < 0 || source.IndexOf(anchor, first + anchor.Length, StringComparison.Ordinal) >= 0)
            {
                missing.Add($"nightsky.fsh: expected exactly one \"{anchor}\".");
            }
        }

        problems = missing;
        if (missing.Count > 0)
        {
            return false;
        }

        patched = source
            .Replace(AnchorMain, Declarations + AnchorMain, StringComparison.Ordinal)
            .Replace(AnchorScale, AnchorScale + Boost, StringComparison.Ordinal);
        return true;
    }
}
