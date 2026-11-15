using System;
using Shared;
using StardewModdingAPI;

namespace CustomCaskAging;

/// <summary>Adds the mod settings to Generic Mod Config Menu, if it's installed.</summary>
internal class ConfigMenu
{
    /*********
    ** Fields
    *********/
    /// <summary>The unique ID of the Generic Mod Config Menu mod.</summary>
    private const string GenericModConfigMenuId = "spacechase0.GenericModConfigMenu";

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>This mod's manifest.</summary>
    private readonly IManifest Manifest;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;

    /// <summary>Save the settings and apply them in the game.</summary>
    private readonly Action Save;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="manifest">This mod's manifest.</param>
    /// <param name="config">The mod settings.</param>
    /// <param name="save">Save the settings and apply them in the game.</param>
    public ConfigMenu(IModHelper helper, IManifest manifest, ModConfig config, Action save)
    {
        this.Helper = helper;
        this.Manifest = manifest;
        this.Config = config;
        this.Save = save;
    }

    /// <summary>Add the settings to Generic Mod Config Menu, if it's installed.</summary>
    public void Register()
    {
        IGenericModConfigMenuApi? menu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(ConfigMenu.GenericModConfigMenuId);
        if (menu == null)
            return;

        menu.Register(
            mod: this.Manifest,
            reset: this.Config.Reset,
            save: this.Save
        );

        menu.AddNumberOption(
            mod: this.Manifest,
            getValue: () => this.Config.AgingMultiplier,
            setValue: value => this.Config.AgingMultiplier = value,
            name: () => this.Helper.Translation.Get("config.aging-multiplier.name"),
            tooltip: () => this.Helper.Translation.Get("config.aging-multiplier.desc"),
            min: ModConfig.MinMultiplier,
            max: ModConfig.MaxMultiplier,
            interval: 0.25f,
            formatValue: value => $"{value:0.##}x"
        );
    }
}
