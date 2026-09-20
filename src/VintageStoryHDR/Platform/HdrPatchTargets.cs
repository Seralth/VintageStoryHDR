using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VintageStoryHDR.Platform;

/// <summary>
/// Every game internal this mod reaches into, in one place.
///
/// <para>
/// Nothing is patched until <see cref="Verify"/> comes back clean. When it does not, the
/// mod logs what moved and leaves vanilla presentation alone. <c>dotnet test</c> runs the
/// same check against the installed game.
/// </para>
/// </summary>
internal static class HdrPatchTargets
{
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags AnyStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// ClientPlatformWindows.window_RenderFrame(FrameEventArgs). Ends every frame with
    /// <c>window.SwapBuffers()</c>; that one call is rewritten to go through
    /// <c>PresentationPatches.SwapBuffers</c>, and a prefix makes it the frame-start hook.
    /// The method is an event handler, so unlike SwapBuffers or ClearFrameBuffer it can
    /// never have been inlined into an already-compiled caller.
    /// </summary>
    internal static MethodInfo? WindowRenderFrame =>
        typeof(ClientPlatformWindows).GetMethod("window_RenderFrame", AnyInstance, null, new[] { typeof(FrameEventArgs) }, null);

    /// <summary>
    /// ClientPlatformWindows.LoadFrameBuffer(EnumFrameBuffer). With Default it binds
    /// framebuffer 0: the scene blit, the item atlas renderer and screenshots all go
    /// through here.
    /// </summary>
    internal static MethodInfo? LoadFrameBuffer =>
        typeof(ClientPlatformWindows).GetMethod("LoadFrameBuffer", AnyInstance, null, new[] { typeof(EnumFrameBuffer) }, null);

    /// <summary>ClientPlatformWindows.CreateFramebuffer(FramebufferAttrs). Leaves framebuffer 0 bound when it returns.</summary>
    internal static MethodInfo? CreateFramebuffer =>
        typeof(ClientPlatformWindows).GetMethod("CreateFramebuffer", AnyInstance, null, new[] { typeof(FramebufferAttrs) }, null);

    /// <summary>
    /// ClientPlatformWindows.SetupDefaultFrameBuffers(). Rebuilds every framebuffer on
    /// resize and on graphics settings changes, with the scene colour buffer as RGBA8.
    /// </summary>
    internal static MethodInfo? SetupDefaultFrameBuffers =>
        typeof(ClientPlatformWindows).GetMethod("SetupDefaultFrameBuffers", AnyInstance, null, System.Type.EmptyTypes, null);

    /// <summary>ClientPlatformWindows.RenderFinalComposition(). Runs the final shader; its uniforms are set just before.</summary>
    internal static MethodInfo? RenderFinalComposition =>
        typeof(ClientPlatformWindows).GetMethod("RenderFinalComposition", AnyInstance, null, System.Type.EmptyTypes, null);

    /// <summary>ShaderRegistry.LoadShader(ShaderProgram, EnumShaderType). Where a shader's source, includes expanded, lands on the program.</summary>
    internal static MethodInfo? LoadShader =>
        typeof(ShaderRegistry).GetMethod("LoadShader", AnyStatic, null, new[] { typeof(ShaderProgram), typeof(EnumShaderType) }, null);

    /// <summary>ClientPlatformWindows.frameBuffers -- private; index 0 is the scene.</summary>
    internal static FieldInfo? FrameBuffers =>
        typeof(ClientPlatformWindows).GetField("frameBuffers", AnyInstance);

    /// <summary>ClientMain.skyTextureId -- internal; the GL name of sky.png, which vanilla samples with nearest filtering.</summary>
    internal static FieldInfo? SkyTextureId =>
        typeof(ClientMain).GetField("skyTextureId", AnyInstance);

    /// <summary>
    /// The 8-bit colour targets that sit between the scene and the screen: the scene itself,
    /// then the bloom blur chain (half and quarter resolution, two passes each).
    /// </summary>
    internal static readonly int[] PromotedFrameBuffers =
    {
        (int)EnumFrameBuffer.Primary,
        (int)EnumFrameBuffer.BlurHorizontalMedRes,
        (int)EnumFrameBuffer.BlurVerticalMedRes,
        (int)EnumFrameBuffer.BlurHorizontalLowRes,
        (int)EnumFrameBuffer.BlurVerticalLowRes,
    };

    /// <summary>
    /// Returns one line per problem found, or an empty list when every target is where
    /// this mod expects it.
    /// </summary>
    internal static IReadOnlyList<string> Verify()
    {
        List<string> problems = new();

        RequireMethod(problems, WindowRenderFrame, "ClientPlatformWindows.window_RenderFrame(FrameEventArgs)");
        RequireMethod(problems, LoadFrameBuffer, "ClientPlatformWindows.LoadFrameBuffer(EnumFrameBuffer)");
        RequireMethod(problems, CreateFramebuffer, "ClientPlatformWindows.CreateFramebuffer(FramebufferAttrs)");
        RequireMethod(problems, RenderFinalComposition, "ClientPlatformWindows.RenderFinalComposition()");
        RequireMethod(problems, LoadShader, "ShaderRegistry.LoadShader(ShaderProgram, EnumShaderType)");

        if (SetupDefaultFrameBuffers is null)
        {
            problems.Add("ClientPlatformWindows.SetupDefaultFrameBuffers() not found.");
        }
        else if (SetupDefaultFrameBuffers.ReturnType != typeof(List<FrameBufferRef>))
        {
            problems.Add($"ClientPlatformWindows.SetupDefaultFrameBuffers() returns {SetupDefaultFrameBuffers.ReturnType.Name}, expected List<FrameBufferRef>.");
        }

        RequireField(problems, FrameBuffers, "ClientPlatformWindows.frameBuffers", typeof(List<FrameBufferRef>));
        RequireField(problems, typeof(ClientPlatformWindows).GetField("window", AnyInstance), "ClientPlatformWindows.window", typeof(GameWindowNative));
        RequireField(problems, typeof(ShaderPrograms).GetField("Final", AnyStatic), "ShaderPrograms.Final", typeof(ShaderProgramFinal));
        RequireField(problems, SkyTextureId, "ClientMain.skyTextureId", typeof(int));
        RequireField(problems, typeof(ShaderPrograms).GetField("Nightsky", AnyStatic), "ShaderPrograms.Nightsky", typeof(ShaderProgramNightsky));
        RequireField(problems, typeof(ShaderProgramBase).GetField("ProgramId", AnyInstance), "ShaderProgramBase.ProgramId", typeof(int));

        if (!typeof(NativeWindow).IsAssignableFrom(typeof(GameWindowNative)))
        {
            problems.Add("GameWindowNative no longer derives from OpenTK's NativeWindow.");
        }

        // The scene framebuffer is addressed as frameBuffers[0], the way vanilla does it.
        VerifyEnumValue(problems, "EnumFrameBuffer.Primary", (int)EnumFrameBuffer.Primary, 0);

        if (WindowRenderFrame is not null && CountSwapBuffersCalls(WindowRenderFrame) != 1)
        {
            problems.Add("ClientPlatformWindows.window_RenderFrame no longer contains exactly one SwapBuffers() call.");
        }

        return problems;
    }

    /// <summary>True for the <c>window.SwapBuffers()</c> call that ends a frame.</summary>
    internal static bool IsSwapBuffersCall(CodeInstruction instruction) =>
        (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) &&
        instruction.operand is MethodInfo { Name: "SwapBuffers" } method &&
        method.GetParameters().Length == 0 &&
        method.DeclaringType is not null &&
        method.DeclaringType.IsAssignableFrom(typeof(GameWindowNative));

    private static int CountSwapBuffersCalls(MethodInfo method)
    {
        int count = 0;
        foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
        {
            if (IsSwapBuffersCall(instruction))
            {
                count++;
            }
        }

        return count;
    }

    private static void VerifyEnumValue(List<string> problems, string name, int actual, int expected)
    {
        if (actual != expected)
        {
            problems.Add($"{name} is {actual}, expected {expected}.");
        }
    }

    private static void RequireMethod(List<string> problems, MethodInfo? method, string name)
    {
        if (method is null)
        {
            problems.Add($"{name} not found.");
        }
    }

    private static void RequireField(List<string> problems, FieldInfo? field, string name, System.Type expected)
    {
        if (field is null)
        {
            problems.Add($"{name} not found.");
        }
        else if (field.FieldType != expected)
        {
            problems.Add($"{name} is {field.FieldType.Name}, expected {expected.Name}.");
        }
    }
}
