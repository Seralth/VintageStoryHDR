using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using VintageStoryHDR.Patches;
using VintageStoryHDR.Platform;
using VintageStoryHDR.Rendering;

namespace VintageStoryHDR;

/// <summary>
/// Client-only mod that presents the game in HDR on Windows. See README.md for how the
/// pipeline fits together and docs/findings.md for the game internals behind it.
/// </summary>
public sealed class HdrModSystem : ModSystem
{
    private const string ConfigFile = "vshdr.json";
    private const string HarmonyId = "net.johnstone.vshdr";

    private Harmony? harmony;
    private ICoreClientAPI? capi;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        HdrRuntime.Log = Mod.Logger;
        LoadConfig(api);
        RegisterCommand(api);

        if (!OperatingSystem.IsWindows())
        {
            Mod.Logger.Notification("HDR output goes through DXGI, which only exists on Windows. Vanilla presentation left untouched.");
            return;
        }

        IReadOnlyList<string> problems = HdrPatchTargets.Verify();
        if (problems.Count > 0)
        {
            Mod.Logger.Warning(
                "Not taking over presentation: {0} of the game internals this mod hooks have moved. " +
                "Vanilla presentation is left in charge.",
                problems.Count);
            foreach (string problem in problems)
            {
                Mod.Logger.Warning("  - {0}", problem);
            }

            return;
        }

        harmony = new Harmony(HarmonyId);
        FinalShaderPatch.Apply(harmony);
        PresentationPatches.Apply(harmony);

        // From here the hooks are live. The presenter itself is built at the start of the
        // next frame, on the render thread, and the game reloads its shaders right after
        // mods have started, which is when the final shader picks up its HDR path.
        HdrRuntime.Armed = true;
        Mod.Logger.Notification(
            HdrRuntime.Config.Enabled
                ? "Hooks installed. HDR presentation starts with the next frame."
                : "Hooks installed, but disabled in config. Type .hdr on to enable.");
    }

    public override void Dispose()
    {
        HdrRuntime.Armed = false;

        try
        {
            List<FrameBufferRef>? frameBuffers = null;
            if (ScreenManager.Platform is ClientPlatformWindows platform)
            {
                frameBuffers = HdrPatchTargets.FrameBuffers?.GetValue(platform) as List<FrameBufferRef>;
            }

            HdrRuntime.Shutdown(frameBuffers);
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Error while shutting HDR presentation down: {0}", e.Message);
        }

        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        HdrRuntime.Log = null;
        capi = null;
        base.Dispose();
    }

    private void RegisterCommand(ICoreClientAPI api)
    {
        CommandArgumentParsers parsers = api.ChatCommands.Parsers;
        api.ChatCommands
            .Create("hdr")
            .WithDescription("Native HDR: status and live tuning. .hdr [on|off|paperwhite|peak|emissive|highlight|gamma|floatscene] [value]")
            .WithArgs(parsers.OptionalWord("setting"), parsers.OptionalFloat("value"))
            .HandleWith(OnCommand);
    }

    private TextCommandResult OnCommand(TextCommandCallingArgs args)
    {
        HdrConfig config = HdrRuntime.Config;
        string? setting = (args[0] as string)?.ToLowerInvariant();
        float? value = args.Parsers[1].IsMissing ? null : (float)args[1];

        switch (setting)
        {
            case null or "":
                return TextCommandResult.Success(Status());
            case "on":
                config.Enabled = true;
                HdrRuntime.Retry();
                break;
            case "off":
                config.Enabled = false;
                break;
            case "paperwhite" when value is not null:
                config.PaperWhiteNits = value.Value;
                break;
            case "peak" when value is not null:
                config.PeakNits = value.Value;
                break;
            case "emissive" when value is not null:
                config.EmissiveBoost = value.Value;
                break;
            case "highlight" when value is not null:
                config.HighlightBoost = value.Value;
                break;
            case "gamma" when value is not null:
                config.SdrGamma = value.Value;
                break;
            case "floatscene" when value is not null:
                config.FloatSceneBuffer = value.Value != 0f;
                break;
            default:
                return TextCommandResult.Error(
                    "Usage: .hdr | .hdr on | .hdr off | .hdr paperwhite <nits> | .hdr peak <nits, 0 = display> | " +
                    ".hdr emissive <x> | .hdr highlight <x> | .hdr gamma <g> | .hdr floatscene <0|1>");
        }

        config.Sanitise();
        StoreConfig();
        return TextCommandResult.Success(Status());
    }

    private static string Status()
    {
        HdrConfig config = HdrRuntime.Config;
        string state;
        if (HdrRuntime.Presenter is { } presenter)
        {
            state = string.Format(
                CultureInfo.InvariantCulture,
                "active, {0}x{1}, display peak {2:0} nits{3}",
                presenter.Width,
                presenter.Height,
                presenter.Display.MaxNits,
                HdrRuntime.FinalShaderPatched ? string.Empty : ", scene limited to SDR range (final shader not patched)");
        }
        else
        {
            state = "inactive: " + (HdrRuntime.InactiveReason ?? (HdrRuntime.Armed ? "starting" : "hooks not installed, see client-main.log"));
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "HDR {0}. paperwhite {1:0} nits, peak {2}, emissive {3:0.##}, highlight {4:0.##}, gamma {5:0.##}, floatscene {6}",
            state,
            config.PaperWhiteNits,
            config.PeakNits > 0f ? config.PeakNits.ToString("0", CultureInfo.InvariantCulture) + " nits" : "from display",
            config.EmissiveBoost,
            config.HighlightBoost,
            config.SdrGamma,
            config.FloatSceneBuffer ? 1 : 0);
    }

    private void LoadConfig(ICoreClientAPI api)
    {
        HdrConfig? loaded = null;

        try
        {
            loaded = api.LoadModConfig<HdrConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not read ModConfig/{0}, using defaults: {1}", ConfigFile, e.Message);
        }

        HdrRuntime.Config = loaded ?? new HdrConfig();
        HdrRuntime.Config.Sanitise();
        StoreConfig();
    }

    private void StoreConfig()
    {
        try
        {
            capi?.StoreModConfig(HdrRuntime.Config, ConfigFile);
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not write ModConfig/{0}: {1}", ConfigFile, e.Message);
        }
    }
}
