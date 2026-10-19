using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared;
using StardewModdingAPI;
using StardewValley;

namespace EnricherTinker;

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

    /// <summary>Reads whether Ultimate Fertilizer lets a tile take more than one fertilizer.</summary>
    private readonly UltimateFertilizer UltimateFertilizer;

    /// <summary>The "Apply every fertilizer" value shown in the menu, which is only copied to the settings when the player saves.</summary>
    private bool ApplyAll;

    /// <summary>The "Fertilize planted crops" value shown in the menu, which is only copied to the settings when the player saves.</summary>
    private bool FertilizePlanted;

    /// <summary>Draws the "Apply every fertilizer" and "Fertilize planted crops" checkboxes and applies clicks on it, once the menu is registered.</summary>
    private ConfigCheckboxes Checkboxes = null!; // set in Register


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="manifest">This mod's manifest.</param>
    /// <param name="config">The mod settings.</param>
    /// <param name="ultimateFertilizer">Reads whether Ultimate Fertilizer lets a tile take more than one fertilizer.</param>
    public ConfigMenu(IModHelper helper, IManifest manifest, ModConfig config, UltimateFertilizer ultimateFertilizer)
    {
        this.Helper = helper;
        this.Manifest = manifest;
        this.Config = config;
        this.UltimateFertilizer = ultimateFertilizer;
    }

    /// <summary>Add the settings to Generic Mod Config Menu, if it's installed.</summary>
    public void Register()
    {
        IGenericModConfigMenuApi? menu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(ConfigMenu.GenericModConfigMenuId);
        if (menu == null)
            return;
        this.Checkboxes = new ConfigCheckboxes(this.Helper.Events);

        menu.Register(
            mod: this.Manifest,
            reset: this.Config.Reset,
            save: () => this.Helper.WriteConfig(this.Config)
        );

        menu.AddBoolOption(
            mod: this.Manifest,
            getValue: () => this.Config.ModEnabled,
            setValue: value => this.Config.ModEnabled = value,
            name: this.Text("config.mod-enabled.name"),
            tooltip: this.Text("config.mod-enabled.desc")
        );

        // drawn by hand so it can be greyed out when Ultimate Fertilizer doesn't allow more than one fertilizer per tile
        menu.AddComplexOption(
            mod: this.Manifest,
            name: this.Text("config.apply-all.name"),
            tooltip: this.GetApplyAllTooltip,
            draw: this.DrawApplyAll,
            beforeMenuOpened: () => this.ApplyAll = this.Config.ApplyAllFertilizers,
            beforeSave: () => this.Config.ApplyAllFertilizers = this.ApplyAll,
            afterReset: () => this.ApplyAll = this.Config.ApplyAllFertilizers,
            height: () => ConfigCheckboxes.Size
        );

        // drawn by hand so it can be greyed out when Ultimate Fertilizer isn't installed
        menu.AddComplexOption(
            mod: this.Manifest,
            name: this.Text("config.fertilize-planted.name"),
            tooltip: this.GetFertilizePlantedTooltip,
            draw: this.DrawFertilizePlanted,
            beforeMenuOpened: () => this.FertilizePlanted = this.Config.FertilizePlantedCrops,
            beforeSave: () => this.Config.FertilizePlantedCrops = this.FertilizePlanted,
            afterReset: () => this.FertilizePlanted = this.Config.FertilizePlantedCrops,
            height: () => ConfigCheckboxes.Size
        );

        menu.AddBoolOption(
            mod: this.Manifest,
            getValue: () => this.Config.InfiniteFertilizer,
            setValue: value => this.Config.InfiniteFertilizer = value,
            name: this.Text("config.infinite.name"),
            tooltip: this.Text("config.infinite.desc")
        );
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the tooltip for "Apply every fertilizer", which explains why it's greyed out if it is.</summary>
    private string GetApplyAllTooltip()
    {
        string tooltip = this.Helper.Translation.Get("config.apply-all.desc");
        string required = FertilizerModes.MultiStack;

        string? unavailable = null;
        if (!this.UltimateFertilizer.IsInstalled())
            unavailable = this.Helper.Translation.Get("config.apply-all.needs-ultimate-fertilizer", new { required });
        else if (!this.UltimateFertilizer.AllowsMultiple())
            unavailable = this.Helper.Translation.Get("config.apply-all.needs-multiple-mode", new { mode = this.UltimateFertilizer.GetMode() ?? "?", required });
        if (unavailable == null)
            return tooltip;

        // Generic Mod Config Menu doesn't wrap a tooltip which has line breaks, so each paragraph is wrapped here
        return ConfigMenu.Wrap(tooltip) + "\n\n" + ConfigMenu.Wrap(unavailable);
    }

    /// <summary>Get the tooltip for "Fertilize planted crops", which explains why it's greyed out if it is.</summary>
    private string GetFertilizePlantedTooltip()
    {
        string tooltip = this.Helper.Translation.Get("config.fertilize-planted.desc");
        if (this.UltimateFertilizer.IsInstalled())
            return tooltip;

        return ConfigMenu.Wrap(tooltip) + "\n\n" + ConfigMenu.Wrap(this.Helper.Translation.Get("config.fertilize-planted.needs-ultimate-fertilizer"));
    }

    /// <summary>Wrap a paragraph to the width of a tooltip.</summary>
    /// <param name="text">The paragraph.</param>
    private static string Wrap(string text)
    {
        return Game1.parseText(text, Game1.smallFont, 800);
    }

    /// <summary>Draw the "Apply every fertilizer" checkbox, and apply a click on it.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    private void DrawApplyAll(SpriteBatch spriteBatch, Vector2 position)
    {
        bool enabled = this.UltimateFertilizer.AllowsMultiple();

        if (enabled && this.Checkboxes.TryClick(spriteBatch, position))
            this.ApplyAll = !this.ApplyAll;

        this.Checkboxes.Draw(spriteBatch, position, isChecked: enabled && this.ApplyAll, enabled: enabled);
    }

    /// <summary>Draw the "Fertilize planted crops" checkbox, and apply a click on it.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    private void DrawFertilizePlanted(SpriteBatch spriteBatch, Vector2 position)
    {
        bool enabled = this.UltimateFertilizer.IsInstalled();

        if (enabled && this.Checkboxes.TryClick(spriteBatch, position))
            this.FertilizePlanted = !this.FertilizePlanted;

        this.Checkboxes.Draw(spriteBatch, position, isChecked: enabled && this.FertilizePlanted, enabled: enabled);
    }

    /// <summary>Get a translated text getter.</summary>
    /// <param name="key">The translation key.</param>
    private Func<string> Text(string key)
    {
        return () => this.Helper.Translation.Get(key);
    }
}
