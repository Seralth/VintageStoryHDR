using System;

namespace VintageStoryHDR;

/// <summary>
/// ModConfig/vshdr.json. Every value is read live by the render path, so edits made with
/// <c>.hdr</c> in game take effect on the next frame.
/// </summary>
public sealed class HdrConfig
{
    /// <summary>Master switch. Off leaves vanilla presentation completely untouched.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Take over presentation even when Windows reports the display as SDR. The image is
    /// then clipped by the compositor, so this is only useful for checking the pipeline.
    /// </summary>
    public bool ForceOnSdrDisplay { get; set; }

    /// <summary>
    /// Luminance of SDR white in the scene -- a fully lit white block -- in nits.
    /// </summary>
    public float PaperWhiteNits { get; set; } = 300f;

    /// <summary>
    /// Luminance of white in the GUI, in nits. 0 follows <see cref="PaperWhiteNits"/>.
    /// Needs the patched final shader; without it the GUI and the scene share this level.
    /// </summary>
    public float UiNits { get; set; } = 400f;

    /// <summary>The GUI white level actually in effect.</summary>
    internal float EffectiveUiNits => UiNits > 0f ? UiNits : PaperWhiteNits;

    /// <summary>
    /// Brightest luminance to output, in nits. 0 uses what the display reports over DXGI.
    /// </summary>
    public float PeakNits { get; set; }

    /// <summary>
    /// How far emissive surfaces (the game's glow channel: torches, lava, forges, the sun,
    /// lightning) are pushed above paper white, as a multiple of their SDR luminance. 0
    /// leaves them at SDR level.
    /// </summary>
    public float EmissiveBoost { get; set; } = 10f;

    /// <summary>
    /// How far non-emissive highlights that were about to clip in SDR (sunlit snow,
    /// water glints, clouds) are pushed above paper white. 0 disables.
    /// </summary>
    public float HighlightBoost { get; set; } = 0.75f;

    /// <summary>
    /// How far vivid scene colours are pushed from Rec.709 towards the P3 gamut, 0 to 1.
    /// Weighted by saturation, so neutrals and muted colours do not move at all, and
    /// luminance is preserved. The GUI is never touched. 0 keeps everything inside Rec.709.
    /// </summary>
    public float GamutExpansion { get; set; } = 0.5f;

    /// <summary>
    /// How far the stars of the night sky are pushed above paper white. The star cubemap
    /// has no glow channel, so <see cref="EmissiveBoost"/> never reaches it. Needs
    /// <see cref="FloatSceneBuffer"/>. 0 disables.
    /// </summary>
    public float StarBoost { get; set; } = 4f;

    /// <summary>
    /// Decoding gamma for the game's display-referred output. 2.2 matches what an SDR
    /// monitor does with the same signal; the Windows desktop uses piecewise sRGB, which
    /// looks washed out in the shadows by comparison.
    /// </summary>
    public float SdrGamma { get; set; } = 2.2f;

    /// <summary>
    /// Keep the scene colour buffer and the bloom blur chain in RGBA16F, so values above
    /// 1.0 survive to the final pass and bloom over smooth gradients does not band. Off
    /// keeps the vanilla RGBA8 buffers; highlights then come from the boost options alone.
    /// </summary>
    public bool FloatSceneBuffer { get; set; } = true;

    /// <summary>
    /// Sample the sky gradient texture with linear filtering. Vanilla uses nearest, which
    /// turns a 512-row 8-bit gradient into visible steps once the sky is no longer
    /// quantised to 8 bits on the way out.
    /// </summary>
    public bool SmoothSkyGradient { get; set; } = true;

    /// <summary>
    /// Dither the output by one 10-bit PQ code value. The compositor converts scRGB to the
    /// display's 10-bit signal without dithering, which bands smooth gradients.
    /// </summary>
    public bool Dither { get; set; } = true;

    internal void Sanitise()
    {
        PaperWhiteNits = Math.Clamp(Finite(PaperWhiteNits, 300f), 80f, 1000f);
        UiNits = Finite(UiNits, 400f) <= 0f ? 0f : Math.Clamp(UiNits, 40f, 1000f);
        PeakNits = Finite(PeakNits, 0f) <= 0f ? 0f : Math.Clamp(PeakNits, 200f, 10000f);
        EmissiveBoost = Math.Clamp(Finite(EmissiveBoost, 10f), 0f, 20f);
        HighlightBoost = Math.Clamp(Finite(HighlightBoost, 0.75f), 0f, 10f);
        GamutExpansion = Math.Clamp(Finite(GamutExpansion, 0.5f), 0f, 1f);
        StarBoost = Math.Clamp(Finite(StarBoost, 4f), 0f, 50f);
        SdrGamma = Math.Clamp(Finite(SdrGamma, 2.2f), 1.8f, 2.6f);
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}
