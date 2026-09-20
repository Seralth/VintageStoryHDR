using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VintageStoryHDR.Interop;
using VintageStoryHDR.Rendering;
using ErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;

namespace VintageStoryHDR.Tests;

/// <summary>
/// Drives the real DXGI runtime and a real OpenGL context. The COM interfaces are called
/// through hand-numbered vtable slots, and a wrong slot does not fail politely, so every
/// slot the mod uses is exercised here. Needs Windows, a GPU and a desktop session;
/// skipped elsewhere.
/// </summary>
public class PresentationIntegrationTests
{
    [Fact]
    public void SwapChainCreatesQueriesPresentsAndResizes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DXGI is Windows-only.");

        nint hwnd = NativeMethods.CreateWindowEx(0, "STATIC", "vshdr-test", 0x80000000 /* WS_POPUP, hidden */, 0, 0, 320, 200, 0, 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            using DxgiSwapChain swapChain = DxgiSwapChain.Create(hwnd, 320, 200);
            Assert.NotEqual(0, swapChain.Device);
            Assert.NotEqual(0, swapChain.SharedTexture);

            DisplayInfo display = swapChain.QueryDisplay();
            Assert.True(float.IsFinite(display.MaxNits) && display.MaxNits >= 0f);
            Assert.True(display.MinNits <= display.MaxNits);

            swapChain.Present(vsync: false);
            swapChain.Present(vsync: true);

            nint before = swapChain.SharedTexture;
            swapChain.Resize(640, 360);
            Assert.Equal(640, swapChain.Width);
            Assert.Equal(360, swapChain.Height);
            Assert.NotEqual(0, swapChain.SharedTexture);
            _ = before;

            swapChain.Present(vsync: false);
        }
        finally
        {
            NativeMethods.DestroyWindow(hwnd);
        }
    }

    [Fact]
    public unsafe void PresenterRunsWholeFramesAgainstARealGlContext()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DXGI is Windows-only.");

        GLFWProvider.CheckForMainThread = false;
        NativeWindowSettings settings = new()
        {
            ClientSize = (480, 270),
            StartVisible = false,
            API = ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = ContextProfile.Core,
            Title = "vshdr-test",
        };

        using NativeWindow window = new(settings);
        window.MakeCurrent();
        nint hwnd = GLFW.GetWin32Window(window.WindowPtr);

        using (HdrPresenter presenter = HdrPresenter.Create(hwnd))
        {
            Assert.NotEqual(0, presenter.RedirectFramebuffer);
            Assert.Equal(480, presenter.Width);
            Assert.Equal(270, presenter.Height);

            HdrConfig config = new();
            for (int frame = 0; frame < 3; frame++)
            {
                Assert.True(presenter.SyncSize());
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, presenter.RedirectFramebuffer);
                GL.ClearColor(4f, 0.5f, 0.25f, 1f); // above 1.0 on purpose
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                GL.UseProgram(0);
                presenter.Present(config, vsync: false);

                // Present puts back what it touched, and leaves the redirect framebuffer bound for the next frame.
                Assert.Equal(0, GL.GetInteger(GetPName.CurrentProgram));
                Assert.Equal(presenter.RedirectFramebuffer, GL.GetInteger(GetPName.DrawFramebufferBinding));
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }

            window.ClientSize = (640, 360);
            Assert.True(presenter.SyncSize());
            Assert.Equal(640, presenter.Width);
            Assert.Equal(360, presenter.Height);
            presenter.Present(config, vsync: false);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }

        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
}
