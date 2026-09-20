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
    /// Luminance of SDR white -- the GUI, and a fully lit white block -- in nits.
    /// </summary>
    public float PaperWhiteNits { get; set; } = 200f;

    /// <summary>
    /// Brightest luminance to output, in nits. 0 uses what the display reports over DXGI.
    /// </summary>
    public float PeakNits { get; set; }

    /// <summary>
    /// How far emissive surfaces (the game's glow channel: torches, lava, forges, the sun,
    /// lightning) are pushed above paper white, as a multiple of their SDR luminance. 0
    /// leaves them at SDR level.
    /// </summary>
    public float EmissiveBoost { get; set; } = 3f;

    /// <summary>
    /// How far non-emissive highlights that were about to clip in SDR (sunlit snow,
    /// water glints, clouds) are pushed above paper white. 0 disables.
    /// </summary>
    public float HighlightBoost { get; set; } = 0.75f;

    /// <summary>
    /// Decoding gamma for the game's display-referred output. 2.2 matches what an SDR
    /// monitor does with the same signal; the Windows desktop uses piecewise sRGB, which
    /// looks washed out in the shadows by comparison.
    /// </summary>
    public float SdrGamma { get; set; } = 2.2f;

    /// <summary>
    /// Keep the scene colour buffer in RGBA16F so values above 1.0 survive to the final
    /// pass. Off keeps the vanilla RGBA8 buffer; highlights then come from the boost
    /// options alone.
    /// </summary>
    public bool FloatSceneBuffer { get; set; } = true;

    internal void Sanitise()
    {
        PaperWhiteNits = Math.Clamp(Finite(PaperWhiteNits, 200f), 80f, 1000f);
        PeakNits = Finite(PeakNits, 0f) <= 0f ? 0f : Math.Clamp(PeakNits, 200f, 10000f);
        EmissiveBoost = Math.Clamp(Finite(EmissiveBoost, 3f), 0f, 20f);
        HighlightBoost = Math.Clamp(Finite(HighlightBoost, 0.75f), 0f, 10f);
        SdrGamma = Math.Clamp(Finite(SdrGamma, 2.2f), 1.8f, 2.6f);
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
}
