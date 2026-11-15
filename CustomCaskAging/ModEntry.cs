using System.Collections.Generic;
using System.Globalization;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Objects;

namespace CustomCaskAging;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The mod settings.</summary>
    private ModConfig Config = new();


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        if (this.Config.GetEffectiveMultiplier() != this.Config.AgingMultiplier)
            this.Monitor.Log($"The aging multiplier {this.Config.AgingMultiplier} isn't between {ModConfig.MinMultiplier} and {ModConfig.MaxMultiplier}, so {this.Config.GetEffectiveMultiplier()} is used instead.", LogLevel.Warn);

        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += (_, _) => new ConfigMenu(helper, this.ModManifest, this.Config, this.OnConfigSaved).Register();

        // a cask keeps the rate it got when something was put in it, so bring the casks already in use up to date
        // before they age: when a save loads, and before each night's aging (which happens after DayEnding)
        helper.Events.GameLoop.SaveLoaded += (_, _) => this.UpdateCasksInUse();
        helper.Events.GameLoop.DayEnding += (_, _) => this.UpdateCasksInUse();
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IContentEvents.AssetRequested" />
    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // late, so every mod's cask rules are already in place and this mod only has to skip the ones it doesn't own
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines"))
            e.Edit(asset => this.EditMachines(asset.AsDictionary<string, MachineData>().Data), AssetEditPriority.Late);
    }

    /// <summary>Speed up or slow down the aging of the base game's cask rules.</summary>
    /// <param name="machines">The machine data to edit.</param>
    private void EditMachines(IDictionary<string, MachineData> machines)
    {
        if (!machines.TryGetValue(CaskRules.CaskMachineId, out MachineData? cask) || cask.OutputRules == null)
        {
            this.Monitor.Log("Couldn't find the cask in the game's machine data, so aging rates are unchanged.", LogLevel.Warn);
            return;
        }

        float multiplier = this.Config.GetEffectiveMultiplier();
        foreach (MachineOutputRule rule in cask.OutputRules)
        {
            if (!CaskRules.IsBaseGameRule(rule.Id) || rule.OutputItem == null)
                continue;

            foreach (MachineItemOutput output in rule.OutputItem)
            {
                output.CustomData ??= new Dictionary<string, string>();
                output.CustomData.TryGetValue(CaskRules.AgingMultiplierKey, out string? current);
                output.CustomData[CaskRules.AgingMultiplierKey] = CaskRules.Scale(current, multiplier, CultureInfo.CurrentCulture);
            }
        }
    }

    /// <summary>Give the casks which already have something in them the rate the settings now call for, so a change applies to wine that's already aging.</summary>
    /// <remarks>It asks the game which rule the item in the cask matches, the same way it does when you put something in, so an item that matches one of another mod's rules, like Custom Cask Mod's, is left alone.</remarks>
    private void UpdateCasksInUse()
    {
        // casks are synced to the other players, so only the host changes them
        if (!Context.IsMainPlayer)
            return;

        int updated = 0;
        Utility.ForEachLocation(location =>
        {
            foreach (StardewValley.Object obj in location.objects.Values)
            {
                if (obj is Cask cask && this.UpdateCask(cask, location))
                    updated++;
            }
            return true;
        });

        if (updated > 0)
            this.Monitor.Log($"Updated the aging rate of {updated} {(updated == 1 ? "cask" : "casks")}.", LogLevel.Debug);
    }

    /// <summary>Give a cask the rate the settings now call for.</summary>
    /// <param name="cask">The cask.</param>
    /// <param name="location">The location containing the cask.</param>
    /// <returns>Returns whether the cask's rate changed.</returns>
    private bool UpdateCask(Cask cask, GameLocation location)
    {
        StardewValley.Object? held = cask.heldObject.Value;
        if (held == null || held.Quality >= 4)
            return false;

        MachineData? data = cask.GetMachineData();
        if (data == null || !MachineDataUtility.TryGetMachineOutputRule(cask, data, MachineOutputTrigger.ItemPlacedInMachine, held, Game1.player, location, out MachineOutputRule? rule, out _, out _, out _) || !CaskRules.IsBaseGameRule(rule.Id))
            return false;

        MachineItemOutput? output = MachineDataUtility.GetOutputData(cask, data, rule, held, Game1.player, location);
        string? text = null;
        output?.CustomData?.TryGetValue(CaskRules.AgingMultiplierKey, out text);
        if (!CaskRules.TryParseRate(text, CultureInfo.CurrentCulture, out float rate) || cask.agingRate.Value == rate)
            return false;

        cask.agingRate.Value = rate;
        return true;
    }

    /// <summary>Apply the settings after they're saved in Generic Mod Config Menu.</summary>
    private void OnConfigSaved()
    {
        this.Helper.WriteConfig(this.Config);
        this.Helper.GameContent.InvalidateCache("Data/Machines");
        if (Context.IsWorldReady)
            this.UpdateCasksInUse();
    }
}
