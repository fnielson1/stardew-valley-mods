using System;
using Shared;
using StardewModdingAPI;

namespace NoCollisionMultiplayer;

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

        this.AddOption(menu, "pets", () => this.Config.PassThroughPets, value => this.Config.PassThroughPets = value);
        this.AddOption(menu, "horses", () => this.Config.PassThroughHorses, value => this.Config.PassThroughHorses = value);
        this.AddOption(menu, "junimos", () => this.Config.PassThroughJunimos, value => this.Config.PassThroughJunimos = value);
        this.AddOption(menu, "villagers", () => this.Config.PassThroughVillagers, value => this.Config.PassThroughVillagers = value);
        this.AddOption(menu, "farm-animals", () => this.Config.PassThroughFarmAnimals, value => this.Config.PassThroughFarmAnimals = value);
        this.AddOption(menu, "monsters", () => this.Config.PassThroughMonsters, value => this.Config.PassThroughMonsters = value);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Add a checkbox to the menu.</summary>
    /// <param name="menu">The Generic Mod Config Menu API.</param>
    /// <param name="key">The translation key between <c>config.</c> and <c>.name</c> or <c>.desc</c>.</param>
    /// <param name="getValue">Get the setting.</param>
    /// <param name="setValue">Set the setting.</param>
    private void AddOption(IGenericModConfigMenuApi menu, string key, Func<bool> getValue, Action<bool> setValue)
    {
        menu.AddBoolOption(
            mod: this.Manifest,
            getValue: getValue,
            setValue: setValue,
            name: () => this.Helper.Translation.Get($"config.{key}.name"),
            tooltip: () => this.Helper.Translation.Get($"config.{key}.desc")
        );
    }
}
