namespace VintageStoryHDR.Tests;

public class HdrConfigTests
{
    [Fact]
    public void DefaultsSurviveSanitise()
    {
        HdrConfig config = new();
        config.Sanitise();

        Assert.True(config.Enabled);
        Assert.Equal(300f, config.PaperWhiteNits);
        Assert.Equal(0f, config.PeakNits);
        Assert.Equal(10f, config.EmissiveBoost);
        Assert.Equal(2.2f, config.SdrGamma);
    }

    [Fact]
    public void NonsenseIsPulledIntoRange()
    {
        HdrConfig config = new()
        {
            PaperWhiteNits = float.NaN,
            PeakNits = -5f,
            EmissiveBoost = float.PositiveInfinity,
            HighlightBoost = -1f,
            SdrGamma = 9f,
        };
        config.Sanitise();

        Assert.Equal(300f, config.PaperWhiteNits);
        Assert.Equal(0f, config.PeakNits);
        Assert.Equal(10f, config.EmissiveBoost);
        Assert.Equal(0f, config.HighlightBoost);
        Assert.Equal(2.6f, config.SdrGamma);
    }

    [Fact]
    public void AnExplicitPeakIsKeptWithinWhatHdr10CanCarry()
    {
        HdrConfig config = new() { PeakNits = 50000f };
        config.Sanitise();
        Assert.Equal(10000f, config.PeakNits);
    }
}
