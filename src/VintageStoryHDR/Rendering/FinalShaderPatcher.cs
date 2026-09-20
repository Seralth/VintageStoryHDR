using System;
using System.Collections.Generic;

namespace VintageStoryHDR.Rendering;

/// <summary>
/// Rewrites the game's <c>final.fsh</c> so the last scene pass stops clipping to 1.0 and
/// pushes highlights above paper white.
///
/// <para>
/// The vanilla source is edited at four named anchors rather than replaced wholesale, so
/// a game update that changes the rest of the shader keeps working, and one that moves
/// an anchor is detected: <see cref="TryPatch"/> then returns the source untouched and
/// the scene simply stays in SDR range inside the HDR container.
/// </para>
///
/// <para>
/// Everything added is behind <c>vshdrEnabled</c>, which defaults to 0. With the uniform
/// left alone the patched shader computes exactly what vanilla does.
/// </para>
/// </summary>
internal static class FinalShaderPatcher
{
    internal const string UniformEnabled = "vshdrEnabled";
    internal const string UniformEmissiveBoost = "vshdrEmissiveBoost";
    internal const string UniformHighlightBoost = "vshdrHighlightBoost";
    internal const string UniformGamma = "vshdrGamma";
    internal const string UniformGamut = "vshdrGamut";
    internal const string UniformSceneScale = "vshdrSceneScale";

    /// <summary>Marker that says a source has already been through <see cref="TryPatch"/>.</summary>
    private const string Marker = "// vshdr: patched";

    private const string AnchorMain = "void main(void)";
    private const string AnchorGrade = "vec4 gradedColor = ColorGrade(color);";
    private const string AnchorCompose = "outColor = mix(color, gradedColor, gradedColor.a);";
    private const string AnchorGodrayClamp = "color.rgb = min(color.rgb, vec3(1));";

    private const string Functions = Marker + @"
uniform int " + UniformEnabled + @" = 0;
uniform float " + UniformEmissiveBoost + @" = 0.0;
uniform float " + UniformHighlightBoost + @" = 0.0;
uniform float " + UniformGamma + @" = 2.2;
uniform float " + UniformGamut + @" = 0.0;
uniform float " + UniformSceneScale + @" = 1.0;

// Vanilla grading works in HSL and clamps lightness to [0,1], which would flatten
// anything the float scene buffer kept above 1.0. Grade the in-range colour and put
// the headroom back afterwards.
vec4 vshdrColorGrade(vec4 color) {
	if (vshdrEnabled == 0) return ColorGrade(color);
	float headroom = max(1.0, max(color.r, max(color.g, color.b)));
	vec4 graded = ColorGrade(vec4(color.rgb / headroom, color.a));
	return vec4(graded.rgb * headroom, graded.a);
}

// Display-referred in, display-referred out, but no longer limited to 1.0. The boost is
// applied in linear light and re-encoded so the GUI can keep blending over the result
// exactly as it does in vanilla; the presenter decodes once, at the very end.
vec3 vshdrExpand(vec3 encoded, vec2 uv) {
	if (vshdrEnabled == 0) return encoded;
	encoded = max(encoded, vec3(0.0));
	float peak = max(encoded.r, max(encoded.g, encoded.b));
	float glow = clamp(texture(glowParts, uv).r, 0.0, 1.0);
	float emissive = glow * smoothstep(0.45, 1.0, peak);
	float highlight = smoothstep(0.8, 1.0, peak);
	float gain = 1.0 + vshdrEmissiveBoost * emissive + vshdrHighlightBoost * highlight * highlight;
	// The presenter maps encoded 1.0 to the GUI's white level. The scene has its own, so it
	// is scaled here, before the GUI is drawn over it, by the ratio of the two.
	vec3 lin = pow(encoded, vec3(vshdrGamma)) * gain * vshdrSceneScale;

	// Gamut expansion. Read the colour as if its primaries were P3 and express that back
	// in Rec.709 coordinates, which takes vivid colours outside [0,1] -- negative
	// components are how a colour beyond Rec.709 is written. Weighted by saturation so
	// neutrals and muted colours stay exactly where they were, and renormalised so only
	// colourfulness changes, not luminance.
	if (vshdrGamut > 0.0 && peak > 0.0001) {
		const mat3 p3To709 = mat3(
			 1.2249401, -0.0420569, -0.0196376,
			-0.2249402,  1.0420571, -0.0786361,
			 0.0,        0.0,        1.0982735);
		const vec3 luma709 = vec3(0.2126, 0.7152, 0.0722);
		float low = min(encoded.r, min(encoded.g, encoded.b));
		float saturation = (peak - low) / peak;
		vec3 wide = p3To709 * lin;
		wide *= dot(lin, luma709) / max(dot(wide, luma709), 0.000001);
		lin = mix(lin, wide, vshdrGamut * smoothstep(0.25, 0.85, saturation));
	}

	// Sign-preserving encode: the GUI blends over this buffer in encoded space, and the
	// presenter decodes it the same way.
	return sign(lin) * pow(abs(lin), vec3(1.0 / vshdrGamma));
}

";

    /// <summary>
    /// Returns true and the rewritten source when every anchor was found exactly once.
    /// Otherwise returns false, the original source, and one line per anchor that moved.
    /// </summary>
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
        foreach (string anchor in new[] { AnchorMain, AnchorGrade, AnchorCompose, AnchorGodrayClamp })
        {
            int count = CountOccurrences(source, anchor);
            if (count != 1)
            {
                missing.Add($"final.fsh: expected exactly one \"{anchor}\", found {count}.");
            }
        }

        problems = missing;
        if (missing.Count > 0)
        {
            return false;
        }

        patched = source
            .Replace(AnchorMain, Functions + AnchorMain, StringComparison.Ordinal)
            .Replace(AnchorGrade, "vec4 gradedColor = vshdrColorGrade(color);", StringComparison.Ordinal)
            .Replace(
                AnchorCompose,
                AnchorCompose + "\n\toutColor.rgb = vshdrExpand(outColor.rgb, texCoord);",
                StringComparison.Ordinal)
            .Replace(
                AnchorGodrayClamp,
                "if (" + UniformEnabled + " == 0) " + AnchorGodrayClamp,
                StringComparison.Ordinal);
        return true;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
