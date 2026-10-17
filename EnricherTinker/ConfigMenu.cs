using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace EnricherTinker;

/// <summary>Adds the mod settings to Generic Mod Config Menu, if it's installed.</summary>
internal class ConfigMenu
{
    /*********
    ** Fields
    *********/
    /// <summary>The unique ID of the Generic Mod Config Menu mod.</summary>
    private const string GenericModConfigMenuId = "spacechase0.GenericModConfigMenu";

    /// <summary>The pixel size of a checkbox.</summary>
    private const int CheckboxSize = 36;

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

    /// <summary>The position of a click which hasn't been handled by the checkbox yet, if any.</summary>
    private Point? Click;

    /// <summary>The game tick on which <see cref="Click"/> happened.</summary>
    private int ClickTick;

    /// <summary>The game tick on which the checkbox was last drawn.</summary>
    private int LastDrawTick = -100;


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
            height: () => ConfigMenu.CheckboxSize
        );

        menu.AddBoolOption(
            mod: this.Manifest,
            getValue: () => this.Config.InfiniteFertilizer,
            setValue: value => this.Config.InfiniteFertilizer = value,
            name: this.Text("config.infinite.name"),
            tooltip: this.Text("config.infinite.desc")
        );

        this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IInputEvents.ButtonPressed" />
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.Button is SButton.MouseLeft or SButton.ControllerA && Game1.ticks - this.LastDrawTick <= 2)
        {
            this.Click = new Point(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
            this.ClickTick = Game1.ticks;
        }
    }

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
        this.LastDrawTick = Game1.ticks;
        bool enabled = this.UltimateFertilizer.AllowsMultiple();

        if (enabled && this.TryClick(spriteBatch, position))
            this.ApplyAll = !this.ApplyAll;

        bool isChecked = enabled && this.ApplyAll;
        Rectangle source = isChecked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked;
        spriteBatch.Draw(Game1.mouseCursors, position, source, Color.White * (enabled ? 1f : 0.35f), 0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 0f);
    }

    /// <summary>Get whether the checkbox was just clicked, and mark the click handled if so.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position of the checkbox.</param>
    private bool TryClick(SpriteBatch spriteBatch, Vector2 position)
    {
        if (this.Click is not { } click || Game1.ticks - this.ClickTick > 1)
            return false;

        // the list is clipped to the visible part of the menu, so a box which is scrolled out of view can't be clicked
        Rectangle bounds = new((int)position.X, (int)position.Y, ConfigMenu.CheckboxSize, ConfigMenu.CheckboxSize);
        if (!bounds.Contains(click) || !spriteBatch.GraphicsDevice.ScissorRectangle.Contains(click))
            return false;

        this.Click = null;
        Game1.playSound("drumkit6");
        return true;
    }

    /// <summary>Get a translated text getter.</summary>
    /// <param name="key">The translation key.</param>
    private Func<string> Text(string key)
    {
        return () => this.Helper.Translation.Get(key);
    }
}
