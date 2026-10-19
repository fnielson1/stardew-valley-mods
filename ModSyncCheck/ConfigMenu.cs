using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared;
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

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>This mod's manifest.</summary>
    private readonly IManifest Manifest;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;

    /// <summary>Uninstalls the mods the player chooses once the game closes.</summary>
    private readonly ModUninstaller Uninstaller;

    /// <summary>Which installed mods each installed mod needs to load.</summary>
    private readonly ModDependencies Dependencies;

    /// <summary>The values shown in the menu for each installed mod, by unique ID.</summary>
    private readonly Dictionary<string, ModRow> Rows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The mods which the mods ticked as required need to load, with the mod which needs each one directly, or <c>null</c> if they need to be found again.</summary>
    private Dictionary<string, string>? NeededBy;

    /// <summary>The unique IDs of the mods which will be uninstalled once the game closes.</summary>
    private HashSet<string> PendingUninstalls = new();

    /// <summary>Draws the mod checkboxes and applies clicks on them, once the menu is registered.</summary>
    private ConfigCheckboxes Checkboxes = null!; // set in Register

    /// <summary>The tooltip for the checkbox or column title under the cursor, if any.</summary>
    private string? HoverText;

    /// <summary>The game tick on which <see cref="HoverText"/> was set.</summary>
    private int HoverTick;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="manifest">This mod's manifest.</param>
    /// <param name="config">The mod settings.</param>
    /// <param name="uninstaller">Uninstalls the mods the player chooses once the game closes.</param>
    /// <param name="dependencies">Which installed mods each installed mod needs to load.</param>
    public ConfigMenu(IModHelper helper, IManifest manifest, ModConfig config, ModUninstaller uninstaller, ModDependencies dependencies)
    {
        this.Helper = helper;
        this.Manifest = manifest;
        this.Config = config;
        this.Uninstaller = uninstaller;
        this.Dependencies = dependencies;
    }

    /// <summary>Add the settings to Generic Mod Config Menu, if it's installed.</summary>
    public void Register()
    {
        IGenericModConfigMenuApi? menu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(ConfigMenu.GenericModConfigMenuId);
        if (menu == null)
            return;
        this.Checkboxes = new ConfigCheckboxes(this.Helper.Events);
        this.PendingUninstalls = this.Uninstaller.GetPending();
        List<RecycleRow> recycleRows = new();

        menu.Register(
            mod: this.Manifest,
            reset: this.Config.Reset,
            save: () =>
            {
                // whether a mod's settings are synced by default depends on whether it's required, which can depend on whether the mods which need it are, so those are all set first
                foreach ((string id, ModRow row) in this.Rows)
                    this.Config.SetIgnored(id, !row.Required);
                foreach ((string id, ModRow row) in this.Rows)
                    this.Config.SetConfigSynced(id, row.Synced);
                this.Helper.WriteConfig(this.Config);

                // the uninstall boxes aren't settings, so they're applied directly
                HashSet<string> uninstall = this.Rows.Where(pair => pair.Value.Uninstall).Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!uninstall.SetEquals(this.PendingUninstalls))
                {
                    this.Uninstaller.SetPending(uninstall);
                    this.PendingUninstalls = this.Uninstaller.GetPending();
                }

                // so are the restore boxes, which move the mods back straight away since the game isn't using them
                foreach (RecycleRow row in recycleRows.Where(row => row.Restore && !row.Restored))
                {
                    row.Restored = this.Uninstaller.Restore(row.Mod);
                    row.Failed = !row.Restored;
                    row.Restore = row.Restored;
                }
            }
        );

        // general
        menu.AddSectionTitle(this.Manifest, this.Text("config.downloads.title"));
        menu.AddBoolOption(
            mod: this.Manifest,
            getValue: () => this.Config.RequireThisMod,
            setValue: value => this.Config.RequireThisMod = value,
            name: this.Text("config.require-this-mod.name"),
            tooltip: this.Text("config.require-this-mod.tooltip")
        );

        // three checkboxes per installed mod: whether joining players must have it at the same version, whether they use our settings for it, and whether to uninstall it
        menu.AddSectionTitle(this.Manifest, this.Text("config.required.title"));
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
            ModRow row = new() { Id = id, CanSync = this.Config.CanSyncConfig(id), MustMatch = this.Config.MustMatch(id), IsLaunchOnly = this.Config.IsLaunchOnlyConfigMod(id), CanUninstall = this.Uninstaller.CanUninstall(mod) };
            this.Rows[id] = row;
            void Load()
            {
                // a mod which is only required because another mod needs it keeps the player's own choice, for when that mod isn't required anymore
                row.Required = row.MustMatch || !this.Config.IsListedIgnored(id);
                row.Synced = this.Config.IsConfigSynced(id);
                row.Uninstall = this.PendingUninstalls.Contains(id);
                this.NeededBy = null;
            }

            menu.AddComplexOption(
                mod: this.Manifest,
                name: () => mod.Manifest.Name,
                tooltip: () => this.Helper.Translation.Get("config.required.tooltip", new { name = mod.Manifest.Name, version = mod.Manifest.Version, author = mod.Manifest.Author, id }),
                draw: (spriteBatch, position) => this.DrawRow(spriteBatch, position, row),
                beforeMenuOpened: Load,
                afterReset: Load,
                height: () => ConfigCheckboxes.Size
            );
        }

        // one checkbox per uninstalled mod, to put it back
        menu.AddSectionTitle(this.Manifest, this.Text("config.recycle.title"));
        List<RecycledMod> recycled = this.Uninstaller.GetRecycled();
        if (recycled.Count == 0)
            menu.AddParagraph(this.Manifest, this.Text("config.recycle.empty"));
        else
        {
            menu.AddComplexOption(
                mod: this.Manifest,
                name: () => "",
                draw: (spriteBatch, position) => this.DrawColumnTitle(spriteBatch, position, "config.recycle.column", "config.recycle.explanation"),
                height: () => (int)Game1.dialogueFont.MeasureString("A").Y
            );
        }
        foreach (RecycledMod mod in recycled)
        {
            RecycleRow row = new(mod);
            recycleRows.Add(row);

            menu.AddComplexOption(
                mod: this.Manifest,
                name: () => $"{mod.Folder.Name} {mod.Folder.Version}",
                tooltip: () => this.Helper.Translation.Get("config.recycle.tooltip", new { date = mod.Uninstalled, path = this.Uninstaller.GetDisplayPath(mod.Folder.OriginalPath) }),
                draw: (spriteBatch, position) => this.DrawRecycleRow(spriteBatch, position, row),
                beforeMenuOpened: () => row.Restore = row.Restored,
                height: () => ConfigCheckboxes.Size
            );
        }

        this.Helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IDisplayEvents.RenderedActiveMenu" />
    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        // the list is clipped to the visible part of the menu, so the tooltip is drawn over the menu afterwards
        if (this.HoverText == null || Game1.ticks - this.HoverTick > 1)
            return;

        IClickableMenu.drawHoverText(e.SpriteBatch, Game1.parseText(this.HoverText, Game1.smallFont, 500), Game1.smallFont);
        Game1.activeClickableMenu?.drawMouse(e.SpriteBatch);
    }

    /// <summary>Draw the titles above the columns of checkboxes.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    private void DrawColumnTitles(SpriteBatch spriteBatch, Vector2 position)
    {
        this.DrawColumnTitle(spriteBatch, position, "config.required.column", "config.required.explanation");
        this.DrawColumnTitle(spriteBatch, position + new Vector2(this.GetColumnWidth(), 0), "config.sync-settings.column", "config.sync-settings.explanation");
        this.DrawColumnTitle(spriteBatch, position + new Vector2(this.GetColumnWidth() * 2, 0), "config.uninstall.column", "config.uninstall.explanation");
    }

    /// <summary>Draw the title above a column of checkboxes, centered on them.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position of the column's checkboxes.</param>
    /// <param name="titleKey">The translation key for the title.</param>
    /// <param name="tooltipKey">The translation key for the tooltip shown when the cursor is on the title.</param>
    private void DrawColumnTitle(SpriteBatch spriteBatch, Vector2 position, string titleKey, string tooltipKey)
    {
        string title = this.Helper.Translation.Get(titleKey);
        Vector2 size = Game1.dialogueFont.MeasureString(title);
        Vector2 titlePosition = position + new Vector2((ConfigCheckboxes.Size - size.X) / 2, 0);
        spriteBatch.DrawString(Game1.dialogueFont, title, titlePosition, Game1.textColor);
        this.SetHoverText(spriteBatch, titlePosition, size, tooltipKey);
    }

    /// <summary>Show a tooltip if the cursor is in an area.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position of the area.</param>
    /// <param name="size">The pixel size of the area.</param>
    /// <param name="tooltipKey">The translation key for the tooltip.</param>
    private void SetHoverText(SpriteBatch spriteBatch, Vector2 position, Vector2 size, string tooltipKey)
    {
        this.SetHoverText(spriteBatch, position, size, () => this.Helper.Translation.Get(tooltipKey));
    }

    /// <summary>Show a tooltip if the cursor is in an area.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position of the area.</param>
    /// <param name="size">The pixel size of the area.</param>
    /// <param name="getTooltip">Get the tooltip text.</param>
    private void SetHoverText(SpriteBatch spriteBatch, Vector2 position, Vector2 size, Func<string> getTooltip)
    {
        Point cursor = new(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
        Rectangle bounds = new((int)position.X, (int)position.Y, (int)size.X, (int)size.Y);
        if (bounds.Contains(cursor) && spriteBatch.GraphicsDevice.ScissorRectangle.Contains(cursor))
        {
            this.HoverText = getTooltip();
            this.HoverTick = Game1.ticks;
        }
    }

    /// <summary>Draw the checkboxes for a mod, and apply a click on them.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    /// <param name="row">The values shown for the mod.</param>
    private void DrawRow(SpriteBatch spriteBatch, Vector2 position, ModRow row)
    {
        Vector2 syncedPosition = position + new Vector2(this.GetColumnWidth(), 0);
        Vector2 uninstallPosition = position + new Vector2(this.GetColumnWidth() * 2, 0);

        // our settings are used for a required mod unless we choose otherwise, so that box follows this one until it's changed
        string? neededBy = this.GetNeededBy().GetValueOrDefault(row.Id);
        if (!row.MustMatch && neededBy == null && this.Checkboxes.TryClick(spriteBatch, position))
            this.ToggleRequired(row);
        else if (row.CanSync && !row.MustMatch && this.Checkboxes.TryClick(spriteBatch, syncedPosition))
            row.Synced = !row.Synced;
        else if (row.CanUninstall && this.Checkboxes.TryClick(spriteBatch, uninstallPosition))
        {
            if (row.Uninstall)
                row.Uninstall = false;
            else
                this.ConfirmUninstall(row);
        }

        // a mod which only reads its settings when the game starts must always match, so both boxes are locked on; a mod which a required mod needs is locked on as required
        this.Checkboxes.Draw(spriteBatch, position, row.MustMatch || row.Required || neededBy != null, enabled: !row.MustMatch && neededBy == null);
        this.Checkboxes.Draw(spriteBatch, syncedPosition, row.MustMatch || (row.CanSync && row.Synced), enabled: row.CanSync && !row.MustMatch);
        this.Checkboxes.Draw(spriteBatch, uninstallPosition, row.Uninstall, enabled: row.CanUninstall);

        Vector2 size = new(ConfigCheckboxes.Size);
        string? mustMatchKey = row.MustMatch ? (row.IsLaunchOnly ? "config.must-match.launch-only" : "config.must-match.listed") : null;
        if (mustMatchKey == null && neededBy != null)
            this.SetHoverText(spriteBatch, position, size, () => this.Helper.Translation.Get("config.required.needed-by", new { name = this.GetModName(neededBy) }));
        else
            this.SetHoverText(spriteBatch, position, size, mustMatchKey ?? "config.required.explanation");
        this.SetHoverText(spriteBatch, syncedPosition, size, mustMatchKey ?? (row.CanSync ? "config.sync-settings.explanation" : "config.sync-settings.never-synced"));
        this.SetHoverText(spriteBatch, uninstallPosition, size, "config.uninstall.explanation");
        Game1.activeClickableMenu?.drawMouse(spriteBatch);
    }

    /// <summary>Draw the restore checkbox for an uninstalled mod, and apply a click on it.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    /// <param name="row">The values shown for the mod.</param>
    private void DrawRecycleRow(SpriteBatch spriteBatch, Vector2 position, RecycleRow row)
    {
        // a restored mod can't be uninstalled again until the game restarts and loads it
        if (!row.Restored && this.Checkboxes.TryClick(spriteBatch, position))
            row.Restore = !row.Restore;

        this.Checkboxes.Draw(spriteBatch, position, row.Restore, enabled: !row.Restored);
        string tooltipKey = row.Restored ? "config.recycle.restored" : row.Failed ? "config.recycle.restore-failed" : "config.recycle.explanation";
        this.SetHoverText(spriteBatch, position, new Vector2(ConfigCheckboxes.Size), tooltipKey);
        Game1.activeClickableMenu?.drawMouse(spriteBatch);
    }

    /// <summary>Get the mods which the mods ticked as required need to load, with the mod which needs each one directly.</summary>
    private Dictionary<string, string> GetNeededBy()
    {
        return this.NeededBy ??= this.Dependencies.GetNeededBy(id => this.Rows.TryGetValue(id, out ModRow? row) && (row.MustMatch || row.Required));
    }

    /// <summary>Tick or untick a mod as required, along with whether our settings are used for it and for the mods it needs which that makes required or not.</summary>
    /// <param name="row">The values shown for the mod.</param>
    private void ToggleRequired(ModRow row)
    {
        Dictionary<string, string> before = this.GetNeededBy();
        row.Required = row.Synced = !row.Required;
        this.NeededBy = null;
        Dictionary<string, string> after = this.GetNeededBy();

        foreach (string id in before.Keys.Concat(after.Keys).Where(id => before.ContainsKey(id) != after.ContainsKey(id)))
        {
            if (this.Rows.TryGetValue(id, out ModRow? needed) && !needed.MustMatch && !needed.Required)
                needed.Synced = after.ContainsKey(id);
        }
    }

    /// <summary>Tick a mod to uninstall. If other mods need it, first ask whether to uninstall it anyway and whether to uninstall those mods too.</summary>
    /// <param name="row">The values shown for the mod.</param>
    private void ConfirmUninstall(ModRow row)
    {
        List<string> dependents = this.Dependencies.GetDependents(new[] { row.Id }).Keys
            .Where(id => !this.Rows.TryGetValue(id, out ModRow? dependent) || !dependent.Uninstall)
            .OrderBy(this.GetModName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (dependents.Count == 0)
        {
            row.Uninstall = true;
            return;
        }

        string name = this.GetModName(row.Id);
        string names = string.Join("\n", dependents.Select(id => $"- {this.GetModName(id)}"));
        Action close = null!; // set once the dialog is shown
        SyncDialog dialog = null!;
        dialog = new SyncDialog(
            this.Helper.Translation.Get("config.uninstall.dependents", new { name, names }),
            onConfirm: () =>
            {
                close();
                row.Uninstall = true;
                if (dialog.Checked[0])
                {
                    foreach (string id in dependents)
                    {
                        if (this.Rows.TryGetValue(id, out ModRow? dependent) && dependent.CanUninstall)
                            dependent.Uninstall = true;
                    }
                }
            },
            onCancel: () => close(),
            checkboxLabels: new[] { this.Helper.Translation.Get("config.uninstall.dependents-too").ToString() },
            initialChecks: new[] { true },
            canConfirm: _ => true
        );
        close = this.ShowDialog(dialog);
    }

    /// <summary>Show a dialog in place of the settings menu, which comes back when the dialog closes.</summary>
    /// <param name="dialog">The dialog to show.</param>
    /// <returns>Close the dialog and show the settings menu again.</returns>
    private Action ShowDialog(IClickableMenu dialog)
    {
        // the settings menu is a submenu on the title screen, and may be a child of another menu in game
        IClickableMenu? parent = null;
        IClickableMenu? menu = Game1.activeClickableMenu is TitleMenu ? TitleMenu.subMenu : Game1.activeClickableMenu;
        while (menu?.GetChildMenu() is { } child)
        {
            parent = menu;
            menu = child;
        }

        void SetMenu(IClickableMenu? value)
        {
            if (parent != null)
                parent.SetChildMenu(value);
            else if (Game1.activeClickableMenu is TitleMenu)
                TitleMenu.subMenu = value;
            else
                Game1.activeClickableMenu = value;
        }

        // the game disposes a replaced menu unless something depends on it
        menu?.AddDependency();
        SetMenu(dialog);
        return () =>
        {
            SetMenu(menu);
            menu?.RemoveDependency();
        };
    }

    /// <summary>Get the display name of an installed mod.</summary>
    /// <param name="modId">The unique mod ID.</param>
    private string GetModName(string modId)
    {
        return this.Helper.ModRegistry.Get(modId)?.Manifest.Name ?? modId;
    }

    /// <summary>Get the pixel distance between two columns of checkboxes.</summary>
    private int GetColumnWidth()
    {
        // each title is centered on its checkboxes, so two next to each other need half of each one's width between them
        float[] widths = new[] { "config.required.column", "config.sync-settings.column", "config.uninstall.column" }
            .Select(key => Game1.dialogueFont.MeasureString(this.Helper.Translation.Get(key)).X)
            .ToArray();
        float minWidth = widths.Zip(widths.Skip(1), (left, right) => (left + right) / 2).Max() + 16;
        return Math.Max(180, (int)minWidth);
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
        /// <summary>The mod's unique ID.</summary>
        public string Id { get; init; } = "";

        /// <summary>Whether joining players must have the mod at the same version, as the player chose it. The mod is also required if a required mod needs it.</summary>
        public bool Required { get; set; }

        /// <summary>Whether the host's settings for the mod are allowed to be sent to players.</summary>
        public bool CanSync { get; init; }

        /// <summary>Whether players must have the mod and use the host's settings for it whatever the host chooses, since it only reads its settings when the game starts. Both checkboxes are then always checked and can't be changed.</summary>
        public bool MustMatch { get; init; }

        /// <summary>Whether the mod only reads its settings when the game starts, which is why it must match if <see cref="MustMatch"/> is set.</summary>
        public bool IsLaunchOnly { get; init; }

        /// <summary>Whether joining players who have the mod use the host's settings for it.</summary>
        public bool Synced { get; set; }

        /// <summary>Whether the mod's folder is known, so it can be uninstalled.</summary>
        public bool CanUninstall { get; init; }

        /// <summary>Whether to uninstall the mod once the game closes.</summary>
        public bool Uninstall { get; set; }
    }

    /// <summary>The values shown in the menu for an uninstalled mod.</summary>
    /// <param name="Mod">The uninstalled mod.</param>
    private record RecycleRow(RecycledMod Mod)
    {
        /// <summary>Whether to restore the mod when the player saves.</summary>
        public bool Restore { get; set; }

        /// <summary>Whether the mod was restored.</summary>
        public bool Restored { get; set; }

        /// <summary>Whether the mod couldn't be restored the last time the player saved.</summary>
        public bool Failed { get; set; }
    }
}
