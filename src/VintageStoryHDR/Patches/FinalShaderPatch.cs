using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VintageStoryHDR.Platform;
using VintageStoryHDR.Rendering;

namespace VintageStoryHDR.Patches;

/// <summary>
/// Runs the game's final fragment shader through <see cref="FinalShaderPatcher"/> every
/// time the game (re)loads it. The game reloads all shaders right after mods start, so
/// the patched source is in place before the first in-world frame.
/// </summary>
internal static class FinalShaderPatch
{
    private const string FinalPassName = "final";

    internal static void Apply(Harmony harmony)
    {
        harmony.Patch(HdrPatchTargets.LoadShader, postfix: new HarmonyMethod(typeof(FinalShaderPatch), nameof(AfterLoadShader)));
    }

    private static void AfterLoadShader(ShaderProgram program, EnumShaderType shaderType)
    {
        if (!HdrRuntime.Armed ||
            shaderType != EnumShaderType.FragmentShader ||
            program?.PassName != FinalPassName ||
            program.FragmentShader?.Code is not { } source)
        {
            return;
        }

        if (FinalShaderPatcher.TryPatch(source, out string patched, out IReadOnlyList<string> problems))
        {
            program.FragmentShader.Code = patched;
            HdrRuntime.FinalShaderPatched = true;
            return;
        }

        HdrRuntime.FinalShaderPatched = false;
        HdrRuntime.Log?.Warning(
            "The game's final shader has changed and could not be extended for HDR. Output is still HDR, " +
            "but the scene will stay within SDR range:");
        foreach (string problem in problems)
        {
            HdrRuntime.Log?.Warning("  - {0}", problem);
        }
    }
}
