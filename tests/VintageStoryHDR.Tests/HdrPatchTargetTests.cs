using System.Collections.Generic;
using VintageStoryHDR.Platform;

namespace VintageStoryHDR.Tests;

/// <summary>
/// Runs the mod's own pre-flight check against the game assemblies it was built
/// against. If a game update moves one of the members the patches reach for, this test
/// names it -- which beats finding out from a "left vanilla in charge" line in the
/// client log.
/// </summary>
public class HdrPatchTargetTests
{
    [Fact]
    public void EveryPatchTargetResolves()
    {
        IReadOnlyList<string> problems = HdrPatchTargets.Verify();

        Assert.True(
            problems.Count == 0,
            "Patch targets have moved:\n  " + string.Join("\n  ", problems));
    }
}
