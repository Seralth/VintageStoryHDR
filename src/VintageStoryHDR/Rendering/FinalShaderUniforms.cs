using OpenTK.Graphics.OpenGL;
using Vintagestory.Client.NoObf;

namespace VintageStoryHDR.Rendering;

/// <summary>
/// Feeds the uniforms <see cref="FinalShaderPatcher"/> added to the game's final shader.
/// They are written with glProgramUniform, which needs no program bound and so cannot
/// disturb the game's own shader bookkeeping. Locations are cached per program object;
/// a shader reload produces a new one.
/// </summary>
internal static class FinalShaderUniforms
{
    private static int cachedProgram;
    private static int locationEnabled = -1;
    private static int locationEmissiveBoost = -1;
    private static int locationHighlightBoost = -1;
    private static int locationGamma = -1;

    /// <summary>Call right before the final composition pass draws.</summary>
    internal static void Apply()
    {
        if (!Resolve())
        {
            return;
        }

        HdrConfig config = HdrRuntime.Config;
        GL.ProgramUniform1(cachedProgram, locationEnabled, HdrRuntime.Active ? 1 : 0);
        GL.ProgramUniform1(cachedProgram, locationEmissiveBoost, config.EmissiveBoost);
        GL.ProgramUniform1(cachedProgram, locationHighlightBoost, config.HighlightBoost);
        GL.ProgramUniform1(cachedProgram, locationGamma, config.SdrGamma);
    }

    /// <summary>Puts the final shader back on its vanilla path. The value lives in the program object, so it has to be cleared explicitly.</summary>
    internal static void Disable()
    {
        if (Resolve())
        {
            GL.ProgramUniform1(cachedProgram, locationEnabled, 0);
        }
    }

    private static bool Resolve()
    {
        int program = ShaderPrograms.Final?.ProgramId ?? 0;
        if (program == 0 || !HdrRuntime.FinalShaderPatched)
        {
            return false;
        }

        if (program != cachedProgram)
        {
            cachedProgram = program;
            locationEnabled = GL.GetUniformLocation(program, FinalShaderPatcher.UniformEnabled);
            locationEmissiveBoost = GL.GetUniformLocation(program, FinalShaderPatcher.UniformEmissiveBoost);
            locationHighlightBoost = GL.GetUniformLocation(program, FinalShaderPatcher.UniformHighlightBoost);
            locationGamma = GL.GetUniformLocation(program, FinalShaderPatcher.UniformGamma);
        }

        return locationEnabled >= 0;
    }
}
