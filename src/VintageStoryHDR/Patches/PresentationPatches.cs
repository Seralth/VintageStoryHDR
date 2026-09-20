using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VintageStoryHDR.Platform;
using VintageStoryHDR.Rendering;

namespace VintageStoryHDR.Patches;

/// <summary>
/// Redirects the game's use of GL's default framebuffer to the presenter's float
/// framebuffer, and replaces the buffer swap with a DXGI present.
///
/// <para>
/// The game binds framebuffer 0 through two tiny property setters, which the JIT is free
/// to inline into callers that were compiled long before this mod loaded. The patches
/// therefore sit on the large methods that use those setters, never on the setters.
/// </para>
/// </summary>
internal static class PresentationPatches
{
    internal static void Apply(Harmony harmony)
    {
        System.Type self = typeof(PresentationPatches);

        harmony.Patch(HdrPatchTargets.LoadFrameBuffer, postfix: new HarmonyMethod(self, nameof(AfterLoadFrameBuffer)));
        harmony.Patch(HdrPatchTargets.CreateFramebuffer, postfix: new HarmonyMethod(self, nameof(AfterCreateFramebuffer)));
        harmony.Patch(HdrPatchTargets.SetupDefaultFrameBuffers, postfix: new HarmonyMethod(self, nameof(AfterSetupDefaultFrameBuffers)));
        harmony.Patch(HdrPatchTargets.RenderFinalComposition, prefix: new HarmonyMethod(self, nameof(BeforeRenderFinalComposition)));
        harmony.Patch(
            HdrPatchTargets.WindowRenderFrame,
            prefix: new HarmonyMethod(self, nameof(BeforeRenderFrame)),
            transpiler: new HarmonyMethod(self, nameof(ReplaceSwapBuffers)));
    }

    private static int seenMask;

    /// <summary>Logs the first time each hook runs, so a hook the JIT has bypassed shows up as a missing line.</summary>
    private static void Seen(int bit, string hook)
    {
        if ((seenMask & bit) == 0)
        {
            seenMask |= bit;
            HdrRuntime.Log?.VerboseDebug("Hook reached: {0}", hook);
        }
    }

    // Frame start. The obvious hook, ClearFrameBuffer(Default), is no use: by the time a
    // mod loads, the PGO JIT has inlined it into the hot ScreenManager.Render loop (measured
    // -- the postfix fired for every call site except that one). window_RenderFrame is
    // invoked through an event delegate and cannot be inlined anywhere.
    private static void BeforeRenderFrame(ClientPlatformWindows __instance, List<FrameBufferRef> ___frameBuffers)
    {
        Seen(1, "window_RenderFrame");
        if (___frameBuffers is { Count: > 0 } && ___frameBuffers[0] is not null)
        {
            HdrRuntime.OnFrameStart(__instance, ___frameBuffers);
        }
    }

    private static void AfterLoadFrameBuffer(EnumFrameBuffer framebuffer)
    {
        Seen(2, "LoadFrameBuffer");
        if (framebuffer == EnumFrameBuffer.Default)
        {
            BindRedirect();
        }
    }

    private static void AfterCreateFramebuffer() => BindRedirect();

    private static void AfterSetupDefaultFrameBuffers(List<FrameBufferRef> __result)
    {
        if (__result is not null)
        {
            HdrRuntime.OnFrameBuffersRebuilt(__result);
        }
    }

    private static void BeforeRenderFinalComposition()
    {
        Seen(4, "RenderFinalComposition");
        if (HdrRuntime.Armed)
        {
            bool checking = HdrRuntime.GlCheckBegin();
            FinalShaderUniforms.Apply();
            HdrRuntime.GlCheckEnd(checking, "final shader uniforms");
        }
    }

    private static void BindRedirect()
    {
        if (HdrRuntime.Armed && HdrRuntime.Presenter is { } presenter)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, presenter.RedirectFramebuffer);
        }
    }

    private static IEnumerable<CodeInstruction> ReplaceSwapBuffers(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo replacement = AccessTools.Method(typeof(PresentationPatches), nameof(SwapBuffers));
        foreach (CodeInstruction instruction in instructions)
        {
            if (HdrPatchTargets.IsSwapBuffersCall(instruction))
            {
                // Same stack shape: the window reference in, nothing out.
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }

            yield return instruction;
        }
    }

    /// <summary>Stands in for <c>window.SwapBuffers()</c> at the end of window_RenderFrame.</summary>
    private static void SwapBuffers(NativeWindow window)
    {
        Seen(8, "SwapBuffers");
        // vsyncMode: 0 off, 1 on, 2 adaptive. DXGI has no adaptive mode; treat it as on.
        if (!HdrRuntime.Present(ClientSettings.VsyncMode != 0))
        {
            window.Context.SwapBuffers();
        }
    }
}
