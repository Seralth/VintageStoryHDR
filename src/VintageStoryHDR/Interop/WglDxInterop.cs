using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VintageStoryHDR.Interop;

/// <summary>
/// <c>WGL_NV_DX_interop2</c>: lets the current GL context use a D3D11 texture as a GL
/// texture. Despite the vendor prefix it is implemented by NVIDIA, AMD and Intel drivers.
/// The entry points come from <c>wglGetProcAddress</c>, so a GL context must be current
/// when this is constructed and whenever it is used.
/// </summary>
internal sealed unsafe class WglDxInterop : IDisposable
{
    private const uint AccessWriteDiscard = 0x0002;
    private const uint GlTexture2D = 0x0DE1;

    private readonly delegate* unmanaged[Stdcall]<nint, int> closeDevice;
    private readonly delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, nint> registerObject;
    private readonly delegate* unmanaged[Stdcall]<nint, nint, int> unregisterObject;
    private readonly delegate* unmanaged[Stdcall]<nint, int, nint*, int> lockObjects;
    private readonly delegate* unmanaged[Stdcall]<nint, int, nint*, int> unlockObjects;

    private nint deviceHandle;

    internal WglDxInterop(nint d3dDevice)
    {
        if (NativeMethods.WglGetCurrentContext() == 0)
        {
            throw new HdrUnavailableException("No OpenGL context is current on this thread.");
        }

        var openDevice = (delegate* unmanaged[Stdcall]<nint, nint>)Resolve("wglDXOpenDeviceNV");
        closeDevice = (delegate* unmanaged[Stdcall]<nint, int>)Resolve("wglDXCloseDeviceNV");
        registerObject = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, nint>)Resolve("wglDXRegisterObjectNV");
        unregisterObject = (delegate* unmanaged[Stdcall]<nint, nint, int>)Resolve("wglDXUnregisterObjectNV");
        lockObjects = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Resolve("wglDXLockObjectsNV");
        unlockObjects = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Resolve("wglDXUnlockObjectsNV");

        deviceHandle = openDevice(d3dDevice);
        if (deviceHandle == 0)
        {
            throw new HdrUnavailableException(
                "wglDXOpenDeviceNV failed" + LastError() +
                ". This usually means OpenGL and Direct3D are running on different GPUs.");
        }
    }

    /// <summary>Makes a D3D11 texture usable as the GL texture <paramref name="glTexture"/>. Returns the interop handle.</summary>
    internal nint RegisterTexture(nint d3dTexture, int glTexture)
    {
        nint handle = registerObject(deviceHandle, d3dTexture, (uint)glTexture, GlTexture2D, AccessWriteDiscard);
        if (handle == 0)
        {
            throw new HdrUnavailableException("wglDXRegisterObjectNV failed" + LastError() + ".");
        }

        return handle;
    }

    internal void Unregister(nint handle)
    {
        if (handle != 0 && deviceHandle != 0)
        {
            _ = unregisterObject(deviceHandle, handle);
        }
    }

    /// <summary>Hands the texture to GL. D3D must not touch it until <see cref="Unlock"/>.</summary>
    internal void Lock(nint handle)
    {
        if (lockObjects(deviceHandle, 1, &handle) == 0)
        {
            throw new HdrUnavailableException("wglDXLockObjectsNV failed" + LastError() + ".");
        }
    }

    /// <summary>Hands the texture back to D3D, after the GL work that touched it has been queued.</summary>
    internal void Unlock(nint handle)
    {
        if (unlockObjects(deviceHandle, 1, &handle) == 0)
        {
            throw new HdrUnavailableException("wglDXUnlockObjectsNV failed" + LastError() + ".");
        }
    }

    public void Dispose()
    {
        if (deviceHandle != 0)
        {
            _ = closeDevice(deviceHandle);
            deviceHandle = 0;
        }
    }

    private static nint Resolve(string name)
    {
        nint address = NativeMethods.WglGetProcAddress(name);

        // wglGetProcAddress reports failure as NULL, or on some drivers as 1, 2, 3 or -1.
        if (address is >= -1 and <= 3)
        {
            throw new HdrUnavailableException("The OpenGL driver does not expose " + name + " (WGL_NV_DX_interop2).");
        }

        return address;
    }

    private static string LastError() =>
        string.Format(CultureInfo.InvariantCulture, " (Win32 error 0x{0:X})", Marshal.GetLastSystemError());
}
