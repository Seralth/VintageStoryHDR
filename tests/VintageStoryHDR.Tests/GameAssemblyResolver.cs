using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace VintageStoryHDR.Tests;

/// <summary>
/// The game assemblies are referenced with <c>Private=false</c> because the game supplies
/// them at run time, so they are not next to the test binaries. Point the loader at the
/// install folder instead, using the same resolution order as Directory.Build.props.
/// </summary>
internal static class GameAssemblyResolver
{
    [ModuleInitializer]
    internal static void Install()
    {
        // Native libraries the game ships (glfw3.dll) are found through PATH.
        if (ResolveGameDirectory() is { } nativeDir)
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                Path.Combine(nativeDir, "Lib") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        }

        AssemblyLoadContext.Default.Resolving += static (context, name) =>
        {
            string? gameDir = ResolveGameDirectory();
            if (gameDir is null)
            {
                return null;
            }

            foreach (string dir in new[] { gameDir, Path.Combine(gameDir, "Lib") })
            {
                string candidate = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(candidate))
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
            }

            return null;
        };
    }

    internal static string? ResolveGameDirectory()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(Path.Combine(fromEnv, "VintagestoryAPI.dll")))
        {
            return fromEnv;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string candidate = Path.Combine(appData, "Vintagestory");
        return File.Exists(Path.Combine(candidate, "VintagestoryAPI.dll")) ? candidate : null;
    }
}
