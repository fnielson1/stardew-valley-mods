using System;
using Shared;
using StardewModdingAPI;

namespace SkipLongAnimations;

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


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="manifest">This mod's manifest.</param>
    /// <param name="config">The mod settings.</param>
    public ConfigMenu(IModHelper helper, IManifest manifest, ModConfig config)
    {
        this.Helper = helper;
        this.Manifest = manifest;
        this.Config = config;
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
            save: () => this.Helper.WriteConfig(this.Config)
        );

        menu.AddBoolOption(
            mod: this.Manifest,
            getValue: () => this.Config.SkipBusRide,
            setValue: value => this.Config.SkipBusRide = value,
            name: this.Text("config.skip-bus-ride.name"),
            tooltip: this.Text("config.skip-bus-ride.desc")
        );

        menu.AddNumberOption(
            mod: this.Manifest,
            getValue: this.Config.GetGeodeSpeed,
            setValue: value => this.Config.GeodeSpeed = value,
            name: this.Text("config.geode-speed.name"),
            tooltip: this.Text("config.geode-speed.desc"),
            min: ModConfig.MinGeodeSpeed,
            max: ModConfig.MaxGeodeSpeed,
            formatValue: value => this.Helper.Translation.Get("config.geode-speed.value", new { speed = value })
        );
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get a translated text getter.</summary>
    /// <param name="key">The translation key.</param>
    private Func<string> Text(string key)
    {
        return () => this.Helper.Translation.Get(key);
    }
}
