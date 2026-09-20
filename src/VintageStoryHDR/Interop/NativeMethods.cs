using System;
using System.Runtime.InteropServices;

namespace VintageStoryHDR.Interop;

/// <summary>Raised when some part of the HDR presentation path cannot be set up or has stopped working.</summary>
internal sealed class HdrUnavailableException : Exception
{
    public HdrUnavailableException()
    {
    }

    public HdrUnavailableException(string message)
        : base(message)
    {
    }

    public HdrUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static unsafe partial class NativeMethods
{
    internal const uint WsChild = 0x40000000;
    internal const uint WsVisible = 0x10000000;
    internal const uint WsDisabled = 0x08000000;
    internal const uint WsExNoParentNotify = 0x00000004;

    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("dxgi.dll")]
    internal static partial int CreateDXGIFactory2(uint flags, Guid* riid, void** factory);

    [LibraryImport("d3d11.dll")]
    internal static partial int D3D11CreateDevice(
        void* adapter,
        uint driverType,
        nint software,
        uint flags,
        uint* featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        void** device,
        uint* featureLevel,
        void** immediateContext);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
    internal static partial nint WglGetProcAddress(string name);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentContext")]
    internal static partial nint WglGetCurrentContext();
}
