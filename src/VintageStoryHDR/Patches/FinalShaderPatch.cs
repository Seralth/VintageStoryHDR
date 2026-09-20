using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VintageStoryHDR.Platform;
using VintageStoryHDR.Rendering;

namespace VintageStoryHDR.Patches;

/// <summary>
/// Runs the game's final and night sky fragment shaders through their patchers every
/// time the game (re)loads them. The game reloads all shaders right after mods start, so
/// the patched source is in place before the first in-world frame.
/// </summary>
internal static class FinalShaderPatch
{
    private const string FinalPassName = "final";
    private const string NightSkyPassName = "nightsky";

    internal static void Apply(Harmony harmony)
    {
        harmony.Patch(HdrPatchTargets.LoadShader, postfix: new HarmonyMethod(typeof(FinalShaderPatch), nameof(AfterLoadShader)));
    }

    private static void AfterLoadShader(ShaderProgram program, EnumShaderType shaderType)
    {
        if (!HdrRuntime.Armed ||
            shaderType != EnumShaderType.FragmentShader ||
            program?.FragmentShader?.Code is not { } source)
        {
            return;
        }

        switch (program.PassName)
        {
            case FinalPassName:
            {
                bool ok = FinalShaderPatcher.TryPatch(source, out string patched, out IReadOnlyList<string> problems);
                program.FragmentShader.Code = patched;
                HdrRuntime.FinalShaderPatched = ok;
                Report(ok, problems, "Output is still HDR, but the scene will stay within SDR range");
                break;
            }

            case NightSkyPassName:
            {
                bool ok = NightSkyShaderPatcher.TryPatch(source, out string patched, out IReadOnlyList<string> problems);
                program.FragmentShader.Code = patched;
                HdrRuntime.NightSkyShaderPatched = ok;
                Report(ok, problems, "Stars will stay at SDR brightness");
                break;
            }
        }
    }

    private static void Report(bool ok, IReadOnlyList<string> problems, string consequence)
    {
        if (ok)
        {
            return;
        }

        HdrRuntime.Log?.Warning("A game shader has changed and could not be extended for HDR. {0}:", consequence);
        foreach (string problem in problems)
        {
            HdrRuntime.Log?.Warning("  - {0}", problem);
        }
    }
}
