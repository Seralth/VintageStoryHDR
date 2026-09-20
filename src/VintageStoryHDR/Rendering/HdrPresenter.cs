using System;
using OpenTK.Graphics.OpenGL;
using VintageStoryHDR.Interop;

namespace VintageStoryHDR.Rendering;

/// <summary>
/// Owns everything between "the game thinks it is drawing to the window" and "an HDR
/// frame is on screen".
///
/// <list type="bullet">
/// <item>
/// A <b>redirect framebuffer</b> (RGBA16F + depth/stencil) stands in for GL's default
/// framebuffer. The game blits the scene into it and draws the GUI over the top, exactly
/// as it would into the window. Its contents are display-referred and gamma-encoded like
/// vanilla's, except that scene highlights may exceed 1.0.
/// </item>
/// <item>
/// A <b>child window</b> covering the game window's client area carries the DXGI
/// swapchain. The game window itself has already been presented to by GL, which rules it
/// out as a flip-model target; the child is disabled, so all input still reaches the game.
/// </item>
/// <item>
/// At the end of the frame the redirect texture is decoded to linear light, scaled to
/// paper white, rolled off towards the display's peak and written, as scRGB, into a D3D11
/// texture shared with GL. DXGI presents that.
/// </item>
/// </list>
///
/// Every method must be called on the render thread with the game's GL context current.
/// </summary>
internal sealed class HdrPresenter : IDisposable
{
    private const string VertexSource = @"#version 330 core
out vec2 uv;
void main() {
	vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
	// GL's row 0 is the bottom of the image, D3D's is the top.
	uv = vec2(p.x, 1.0 - p.y);
	gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}";

    private const string FragmentSource = @"#version 330 core
uniform sampler2D source;
uniform float gamma;
uniform float paperWhite; // in scRGB units, 1.0 = 80 nits
uniform float peak;       // in scRGB units
in vec2 uv;
out vec4 outColor;
void main() {
	vec3 encoded = max(texture(source, uv).rgb, vec3(0.0));
	vec3 lin = pow(encoded, vec3(gamma)) * paperWhite;

	// Roll the top quarter of the range off towards the display's peak, scaling all
	// three channels together so bright colours keep their hue instead of going white.
	float m = max(lin.r, max(lin.g, lin.b));
	float knee = 0.75 * peak;
	if (m > knee) {
		float range = peak - knee;
		float rolled = knee + range * (1.0 - exp(-(m - knee) / range));
		lin *= rolled / m;
	}

	outColor = vec4(lin, 1.0);
}";

    private readonly nint parentWindow;
    private nint childWindow;
    private DxgiSwapChain? swapChain;
    private WglDxInterop? interop;
    private nint sharedHandle;

    private int sharedTexture;
    private int sharedFramebuffer;
    private int redirectTexture;
    private int redirectDepthStencil;
    private int program;
    private int vertexArray;
    private int uniformGamma;
    private int uniformPaperWhite;
    private int uniformPeak;

    private HdrPresenter(nint parentWindow)
    {
        this.parentWindow = parentWindow;
    }

    /// <summary>The framebuffer object that stands in for framebuffer 0.</summary>
    internal int RedirectFramebuffer { get; private set; }

    internal int Width { get; private set; }

    internal int Height { get; private set; }

    internal DisplayInfo Display { get; private set; }

    internal bool TearingSupported => swapChain?.TearingSupported ?? false;

    /// <summary>
    /// Builds the whole presentation path for <paramref name="parentWindow"/>. Throws
    /// <see cref="HdrUnavailableException"/> and leaves nothing behind if any part of it
    /// is not available on this machine.
    /// </summary>
    internal static HdrPresenter Create(nint parentWindow)
    {
        HdrPresenter presenter = new(parentWindow);
        try
        {
            presenter.Initialise();
            return presenter;
        }
        catch
        {
            presenter.Dispose();
            throw;
        }
    }

    private void Initialise()
    {
        (int width, int height) = ClientSize();
        if (width <= 0 || height <= 0)
        {
            throw new HdrUnavailableException("The game window has no client area (minimised?).");
        }

        childWindow = NativeMethods.CreateWindowEx(
            NativeMethods.WsExNoParentNotify,
            "STATIC",
            string.Empty,
            NativeMethods.WsChild | NativeMethods.WsVisible | NativeMethods.WsDisabled,
            0,
            0,
            width,
            height,
            parentWindow,
            0,
            0,
            0);
        if (childWindow == 0)
        {
            throw new HdrUnavailableException("Could not create the presentation window.");
        }

        swapChain = DxgiSwapChain.Create(childWindow, width, height);
        Display = swapChain.QueryDisplay();
        interop = new WglDxInterop(swapChain.Device);

        int previousTexture = GL.GetInteger(GetPName.TextureBinding2D);
        int previousFramebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int previousRenderbuffer = GL.GetInteger(GetPName.RenderbufferBinding);
        try
        {
            program = BuildProgram();
            uniformGamma = GL.GetUniformLocation(program, "gamma");
            uniformPaperWhite = GL.GetUniformLocation(program, "paperWhite");
            uniformPeak = GL.GetUniformLocation(program, "peak");
            GL.ProgramUniform1(program, GL.GetUniformLocation(program, "source"), 0);
            vertexArray = GL.GenVertexArray();

            redirectTexture = GL.GenTexture();
            redirectDepthStencil = GL.GenRenderbuffer();
            RedirectFramebuffer = GL.GenFramebuffer();
            sharedFramebuffer = GL.GenFramebuffer();

            Width = width;
            Height = height;
            AllocateRedirectTargets();
            AttachSharedTexture();
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, previousTexture);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, previousRenderbuffer);
        }
    }

    /// <summary>
    /// Follows the game window's client size. Call once at the start of a frame, never in
    /// the middle of one. Returns false while the window has no area to present to.
    /// </summary>
    internal bool SyncSize()
    {
        (int width, int height) = ClientSize();
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (width == Width && height == Height)
        {
            return true;
        }

        int previousTexture = GL.GetInteger(GetPName.TextureBinding2D);
        int previousFramebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int previousRenderbuffer = GL.GetInteger(GetPName.RenderbufferBinding);
        try
        {
            // The interop registration pins the old D3D texture, and the swapchain cannot
            // resize while anything still references its buffers.
            interop!.Unregister(sharedHandle);
            sharedHandle = 0;
            GL.DeleteTexture(sharedTexture);
            sharedTexture = 0;

            _ = NativeMethods.SetWindowPos(childWindow, 0, 0, 0, width, height, NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
            swapChain!.Resize(width, height);

            Width = width;
            Height = height;
            AllocateRedirectTargets();
            AttachSharedTexture();
            Display = swapChain.QueryDisplay();
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, previousTexture);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, previousFramebuffer);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, previousRenderbuffer);
        }

        return true;
    }

    /// <summary>
    /// Encodes the redirect framebuffer to scRGB and presents it. Replaces the GL buffer
    /// swap. Leaves the GL state it touched as it found it.
    /// </summary>
    internal void Present(HdrConfig config, bool vsync)
    {
        if (sharedHandle == 0 || swapChain is null || interop is null)
        {
            return;
        }

        float peakNits = config.PeakNits > 0f ? config.PeakNits : Display.MaxNits;
        if (!(peakNits >= config.PaperWhiteNits))
        {
            // No usable figure from the display (SDR, or a driver that reports 0).
            peakNits = Math.Max(config.PaperWhiteNits, 1000f);
        }

        int previousProgram = GL.GetInteger(GetPName.CurrentProgram);
        int previousVertexArray = GL.GetInteger(GetPName.VertexArrayBinding);
        int previousActiveTexture = GL.GetInteger(GetPName.ActiveTexture);
        bool blend = GL.IsEnabled(EnableCap.Blend);
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool cullFace = GL.IsEnabled(EnableCap.CullFace);
        bool scissorTest = GL.IsEnabled(EnableCap.ScissorTest);
        bool stencilTest = GL.IsEnabled(EnableCap.StencilTest);

        GL.ActiveTexture(TextureUnit.Texture0);
        int previousTexture = GL.GetInteger(GetPName.TextureBinding2D);

        interop.Lock(sharedHandle);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, sharedFramebuffer);
            GL.Viewport(0, 0, Width, Height);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest);
            GL.Disable(EnableCap.StencilTest);

            GL.UseProgram(program);
            GL.Uniform1(uniformGamma, config.SdrGamma);
            GL.Uniform1(uniformPaperWhite, config.PaperWhiteNits / 80f);
            GL.Uniform1(uniformPeak, peakNits / 80f);
            GL.BindTexture(TextureTarget.Texture2D, redirectTexture);
            GL.BindVertexArray(vertexArray);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, previousTexture);
            GL.ActiveTexture((TextureUnit)previousActiveTexture);
            GL.BindVertexArray(previousVertexArray);
            GL.UseProgram(previousProgram);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, RedirectFramebuffer);
            Restore(EnableCap.Blend, blend);
            Restore(EnableCap.DepthTest, depthTest);
            Restore(EnableCap.CullFace, cullFace);
            Restore(EnableCap.ScissorTest, scissorTest);
            Restore(EnableCap.StencilTest, stencilTest);

            interop.Unlock(sharedHandle);
        }

        swapChain.Present(vsync);
    }

    public void Dispose()
    {
        bool haveContext = NativeMethods.WglGetCurrentContext() != 0;

        if (interop is not null && haveContext)
        {
            interop.Unregister(sharedHandle);
            sharedHandle = 0;
            interop.Dispose();
        }

        interop = null;

        if (haveContext)
        {
            DeleteIfSet(ref sharedTexture, GL.DeleteTexture);
            DeleteIfSet(ref redirectTexture, GL.DeleteTexture);
            DeleteIfSet(ref redirectDepthStencil, GL.DeleteRenderbuffer);
            DeleteIfSet(ref sharedFramebuffer, GL.DeleteFramebuffer);
            int redirect = RedirectFramebuffer;
            DeleteIfSet(ref redirect, GL.DeleteFramebuffer);
            RedirectFramebuffer = 0;
            DeleteIfSet(ref vertexArray, GL.DeleteVertexArray);
            DeleteIfSet(ref program, GL.DeleteProgram);
        }

        swapChain?.Dispose();
        swapChain = null;

        if (childWindow != 0)
        {
            _ = NativeMethods.DestroyWindow(childWindow);
            childWindow = 0;
        }
    }

    private (int Width, int Height) ClientSize() =>
        NativeMethods.GetClientRect(parentWindow, out NativeMethods.Rect rect)
            ? (rect.Right - rect.Left, rect.Bottom - rect.Top)
            : (0, 0);

    private void AllocateRedirectTargets()
    {
        GL.BindTexture(TextureTarget.Texture2D, redirectTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, Width, Height, 0, PixelFormat.Rgba, PixelType.HalfFloat, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, redirectDepthStencil);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, Width, Height);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, RedirectFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, redirectTexture, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, redirectDepthStencil);
        RequireComplete("redirect");
    }

    private void AttachSharedTexture()
    {
        sharedTexture = GL.GenTexture();
        sharedHandle = interop!.RegisterTexture(swapChain!.SharedTexture, sharedTexture);

        // The texture only has storage, as far as GL is concerned, while it is locked.
        interop.Lock(sharedHandle);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, sharedFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, sharedTexture, 0);
            RequireComplete("shared D3D11");
        }
        finally
        {
            interop.Unlock(sharedHandle);
        }
    }

    private static void RequireComplete(string name)
    {
        FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new HdrUnavailableException($"The {name} framebuffer is incomplete ({status}).");
        }
    }

    private static int BuildProgram()
    {
        int vertex = Compile(ShaderType.VertexShader, VertexSource);
        int fragment = Compile(ShaderType.FragmentShader, FragmentSource);
        int linked = GL.CreateProgram();
        GL.AttachShader(linked, vertex);
        GL.AttachShader(linked, fragment);
        GL.LinkProgram(linked);
        GL.GetProgram(linked, GetProgramParameterName.LinkStatus, out int ok);
        string log = ok == 0 ? GL.GetProgramInfoLog(linked) : string.Empty;
        GL.DetachShader(linked, vertex);
        GL.DetachShader(linked, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        if (ok == 0)
        {
            GL.DeleteProgram(linked);
            throw new HdrUnavailableException("The presentation shader failed to link: " + log);
        }

        return linked;
    }

    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new HdrUnavailableException($"The presentation {type} failed to compile: {log}");
        }

        return shader;
    }

    private static void Restore(EnableCap cap, bool enabled)
    {
        if (enabled)
        {
            GL.Enable(cap);
        }
        else
        {
            GL.Disable(cap);
        }
    }

    private static void DeleteIfSet(ref int name, Action<int> delete)
    {
        if (name != 0)
        {
            delete(name);
            name = 0;
        }
    }
}
