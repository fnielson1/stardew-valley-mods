using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;

namespace EnricherTinker;

/// <summary>Reads whether Ultimate Fertilizer is installed and set to <see cref="FertilizerModes.MultiStack"/>.</summary>
/// <remarks>Ultimate Fertilizer's API doesn't include its settings, so they're read from its static <c>_config</c> field. That's the live copy, so a change in its Generic Mod Config Menu page applies straight away.</remarks>
internal class UltimateFertilizer
{
    /*********
    ** Fields
    *********/
    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the console and log file.</summary>
    private readonly IMonitor Monitor;

    /// <summary>Ultimate Fertilizer's static settings field, once found.</summary>
    private FieldInfo? ConfigField;

    /// <summary>The "Fertilizer Mode" field in Ultimate Fertilizer's settings, once found.</summary>
    private FieldInfo? ModeField;

    /// <summary>Whether looking for <see cref="ConfigField"/> or <see cref="ModeField"/> failed, so they aren't looked for again.</summary>
    private bool ConfigFieldMissing;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="monitor">Writes messages to the console and log file.</param>
    public UltimateFertilizer(IModHelper helper, IMonitor monitor)
    {
        this.Helper = helper;
        this.Monitor = monitor;
    }

    /// <summary>Get whether Ultimate Fertilizer is installed.</summary>
    public bool IsInstalled()
    {
        return this.Helper.ModRegistry.IsLoaded(FertilizerModes.UltimateFertilizerId);
    }

    /// <summary>Get Ultimate Fertilizer's API, or <c>null</c> if it isn't installed.</summary>
    public IUltimateFertilizerApi? GetApi()
    {
        return this.IsInstalled()
            ? this.Helper.ModRegistry.GetApi<IUltimateFertilizerApi>(FertilizerModes.UltimateFertilizerId)
            : null;
    }

    /// <summary>Get Ultimate Fertilizer's "Fertilizer Mode" setting, or <c>null</c> if it isn't installed or the setting can't be read.</summary>
    public string? GetMode()
    {
        if (!this.IsInstalled() || this.ConfigFieldMissing)
            return null;

        try
        {
            this.ConfigField ??= AccessTools.Field(AccessTools.TypeByName("UltimateFertilizer.ModEntry"), "_config");
            if (this.ConfigField != null)
            {
                object? config = this.ConfigField.GetValue(null);
                if (config == null)
                    return null; // not loaded yet

                this.ModeField ??= AccessTools.Field(config.GetType(), "FertilizerMode");
                if (this.ModeField != null)
                    return this.ModeField.GetValue(config) as string;
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed reading Ultimate Fertilizer's settings:\n{ex}", LogLevel.Trace);
        }

        this.ConfigFieldMissing = true;
        this.Monitor.Log("Couldn't read Ultimate Fertilizer's Fertilizer Mode, so only one fertilizer from an Enricher is applied per tile.", LogLevel.Warn);
        return null;
    }

    /// <summary>Get whether Ultimate Fertilizer is installed and set to <see cref="FertilizerModes.MultiStack"/>, so an Enricher can apply more than one fertilizer per tile.</summary>
    public bool AllowsMultiple()
    {
        return FertilizerModes.AllowsMultiple(this.GetMode());
    }
}
