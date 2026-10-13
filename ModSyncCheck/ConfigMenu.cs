using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace ModSyncCheck;

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

    /// <summary>The position of a click which hasn't been handled by a checkbox yet, if any.</summary>
    private Point? Click;

    /// <summary>The game tick on which <see cref="Click"/> happened.</summary>
    private int ClickTick;

    /// <summary>The game tick on which the mod checkboxes were last drawn.</summary>
    private int LastDrawTick = -100;


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

        // general
        menu.AddSectionTitle(this.Manifest, this.Text("config.downloads.title"));
        menu.AddBoolOption(
            mod: this.Manifest,
            getValue: () => this.Config.AllowModDownloads,
            setValue: value => this.Config.AllowModDownloads = value,
            name: this.Text("config.allow-downloads.name"),
            tooltip: this.Text("config.allow-downloads.tooltip")
        );
        menu.AddNumberOption(
            mod: this.Manifest,
            getValue: () => this.Config.MaxModsInPopup,
            setValue: value => this.Config.MaxModsInPopup = value,
            name: this.Text("config.max-mods.name"),
            tooltip: this.Text("config.max-mods.tooltip"),
            min: 1,
            max: 30
        );

        // two checkboxes per installed mod: whether joining players must have it at the same version, and whether they use our settings for it
        menu.AddSectionTitle(this.Manifest, this.Text("config.required.title"));
        menu.AddParagraph(this.Manifest, this.Text("config.required.explanation"));
        menu.AddParagraph(this.Manifest, this.Text("config.sync-settings.explanation"));
        menu.AddComplexOption(
            mod: this.Manifest,
            name: () => "",
            draw: this.DrawColumnTitles,
            height: () => (int)Game1.dialogueFont.MeasureString("A").Y
        );
        foreach (IModInfo mod in this.Helper.ModRegistry.GetAll().OrderBy(mod => mod.Manifest.Name, StringComparer.OrdinalIgnoreCase))
        {
            // this mod is handled separately, since the host can always tell whether a player has it
            string id = mod.Manifest.UniqueID;
            if (string.Equals(id, this.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase))
                continue;

            // the values shown in the menu are only copied to the settings when the player saves
            ModRow row = new() { CanSync = this.Config.CanSyncConfig(id) };
            void Load()
            {
                row.Required = !this.Config.IsIgnored(id);
                row.Synced = this.Config.IsConfigSynced(id);
            }

            menu.AddComplexOption(
                mod: this.Manifest,
                name: () => mod.Manifest.Name,
                tooltip: () => this.Helper.Translation.Get(row.CanSync ? "config.required.tooltip" : "config.required.tooltip-never-synced", new { name = mod.Manifest.Name, version = mod.Manifest.Version, author = mod.Manifest.Author, id }),
                draw: (spriteBatch, position) => this.DrawRow(spriteBatch, position, row),
                beforeMenuOpened: Load,
                beforeSave: () =>
                {
                    this.Config.SetIgnored(id, !row.Required);
                    this.Config.SetConfigSynced(id, row.Synced);
                },
                afterReset: Load,
                height: () => ConfigMenu.CheckboxSize
            );
        }

        this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IInputEvents.ButtonPressed" />
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        // ignore the click which opened the menu, since it may be where a checkbox then appears
        if (e.Button is SButton.MouseLeft or SButton.ControllerA && Game1.ticks - this.LastDrawTick <= 2)
        {
            this.Click = new Point(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
            this.ClickTick = Game1.ticks;
        }
    }

    /// <summary>Draw the titles above the two columns of checkboxes.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    private void DrawColumnTitles(SpriteBatch spriteBatch, Vector2 position)
    {
        spriteBatch.DrawString(Game1.dialogueFont, this.Helper.Translation.Get("config.required.column"), position, Game1.textColor);
        spriteBatch.DrawString(Game1.dialogueFont, this.Helper.Translation.Get("config.sync-settings.column"), position + new Vector2(this.GetColumnWidth(), 0), Game1.textColor);
    }

    /// <summary>Draw the checkboxes for a mod, and apply a click on them.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    /// <param name="row">The values shown for the mod.</param>
    private void DrawRow(SpriteBatch spriteBatch, Vector2 position, ModRow row)
    {
        this.LastDrawTick = Game1.ticks;
        Vector2 syncedPosition = position + new Vector2(this.GetColumnWidth(), 0);

        // our settings are used for a required mod unless we choose otherwise, so that box follows this one until it's changed
        if (this.TryClick(spriteBatch, position))
            row.Required = row.Synced = !row.Required;
        else if (row.CanSync && this.TryClick(spriteBatch, syncedPosition))
            row.Synced = !row.Synced;

        this.DrawCheckbox(spriteBatch, position, row.Required);
        this.DrawCheckbox(spriteBatch, syncedPosition, row.CanSync && row.Synced, enabled: row.CanSync);
        Game1.activeClickableMenu?.drawMouse(spriteBatch);
    }

    /// <summary>Draw a checkbox.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    /// <param name="isChecked">Whether the box is checked.</param>
    /// <param name="enabled">Whether the box can be changed.</param>
    private void DrawCheckbox(SpriteBatch spriteBatch, Vector2 position, bool isChecked, bool enabled = true)
    {
        Rectangle source = isChecked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked;
        spriteBatch.Draw(Game1.mouseCursors, position, source, Color.White * (enabled ? 1f : 0.35f), 0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 0f);
    }

    /// <summary>Get whether a checkbox was just clicked, and mark the click handled if so.</summary>
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

    /// <summary>Get the pixel distance between the two columns of checkboxes.</summary>
    private int GetColumnWidth()
    {
        return Math.Max(180, (int)Game1.dialogueFont.MeasureString(this.Helper.Translation.Get("config.required.column")).X + 48);
    }

    /// <summary>Get a function which returns a translation, so the text follows the game language.</summary>
    /// <param name="key">The translation key.</param>
    private Func<string> Text(string key)
    {
        return () => this.Helper.Translation.Get(key);
    }

    /// <summary>The values shown in the menu for a mod.</summary>
    private class ModRow
    {
        /// <summary>Whether joining players must have the mod at the same version.</summary>
        public bool Required { get; set; }

        /// <summary>Whether the host's settings for the mod are allowed to be sent to players.</summary>
        public bool CanSync { get; init; }

        /// <summary>Whether joining players who have the mod use the host's settings for it.</summary>
        public bool Synced { get; set; }
    }
}
