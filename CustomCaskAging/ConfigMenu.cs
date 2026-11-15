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

        this.AddMultiplier(menu, "cheese", () => this.Config.CheeseMultiplier, value => this.Config.CheeseMultiplier = value);
        this.AddMultiplier(menu, "goat-cheese", () => this.Config.GoatCheeseMultiplier, value => this.Config.GoatCheeseMultiplier = value);
        this.AddMultiplier(menu, "wine", () => this.Config.WineMultiplier, value => this.Config.WineMultiplier = value);
        this.AddMultiplier(menu, "mead", () => this.Config.MeadMultiplier, value => this.Config.MeadMultiplier = value);
        this.AddMultiplier(menu, "pale-ale", () => this.Config.PaleAleMultiplier, value => this.Config.PaleAleMultiplier = value);
        this.AddMultiplier(menu, "beer", () => this.Config.BeerMultiplier, value => this.Config.BeerMultiplier = value);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Add the aging speed setting for one of the base game's cask rules.</summary>
    /// <param name="menu">The Generic Mod Config Menu API.</param>
    /// <param name="key">The rule's key in the translation file, like <c>wine</c> for <c>config.wine.name</c>.</param>
    /// <param name="getValue">Get the setting.</param>
    /// <param name="setValue">Set the setting.</param>
    private void AddMultiplier(IGenericModConfigMenuApi menu, string key, Func<float> getValue, Action<float> setValue)
    {
        menu.AddNumberOption(
            mod: this.Manifest,
            getValue: getValue,
            setValue: setValue,
            name: () => this.Helper.Translation.Get($"config.{key}.name"),
            tooltip: () => this.Helper.Translation.Get($"config.{key}.desc"),
            min: ModConfig.MinMultiplier,
            max: ModConfig.MaxMultiplier,
            interval: 0.25f,
            formatValue: value => $"{value:0.##}x"
        );
    }
}
