using System.Collections.Generic;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Monsters;

namespace NoCollisionMultiplayer;

/// <summary>The mod entry point.</summary>
/// <remarks>
/// Whether you bump into a character is decided on your own computer, from fields which aren't sent over the network. A character created on the host, like a Junimo from a hut, has them set there, but each farmhand's copy starts with the defaults. So this mod works on every player's own computer, instead of leaving it to the host.
/// </remarks>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The number the base game's push timer must pass for a farm animal to get out of the player's way.</summary>
    private const int AnimalPushDone = 61;

    /// <summary>The mod settings.</summary>
    private static ModConfig Config = new();

    /// <summary>The characters whose pass-through flag this mod turned on, so it can turn it off again if the setting changes.</summary>
    private readonly HashSet<Character> Changed = new();

    /// <summary>The characters to stop tracking, found while checking <see cref="Changed"/>.</summary>
    private readonly List<Character> Stale = new();

    /// <summary>Whether this mod turned on <see cref="Farmer.ignoreCollisions"/>, so it only turns it off again in that case.</summary>
    private bool SetIgnoreCollisions;


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        ModEntry.Config = helper.ReadConfig<ModConfig>();

        new Harmony(this.ModManifest.UniqueID).Patch(
            original: AccessTools.Method(typeof(Farmer), nameof(Farmer.takeDamage), new[] { typeof(int), typeof(bool), typeof(Monster) }),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_TakeDamage))
        );

        helper.Events.GameLoop.GameLaunched += (_, _) => new ConfigMenu(helper, this.ModManifest, ModEntry.Config).Register();
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.Changed.Clear();
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IGameLoopEvents.UpdateTicked" />
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        GameLocation? location = Game1.player.currentLocation;
        if (location != null)
        {
            foreach (NPC npc in location.characters)
                this.Update(npc);

            if (ModEntry.Config.PassThroughFarmAnimals)
            {
                foreach (FarmAnimal animal in location.getAllFarmAnimals())
                    animal.pushAccumulator = ModEntry.AnimalPushDone;
            }
        }

        // turn it back off for characters we changed which are gone, or whose kind is no longer allowed
        this.Stale.Clear();
        foreach (Character character in this.Changed)
        {
            PassThroughKind? kind = ModEntry.GetKind(character);
            if (character.currentLocation != location || kind == null || !ModEntry.Config.Allows(kind.Value))
            {
                character.farmerPassesThrough = false;
                this.Stale.Add(character);
            }
        }
        foreach (Character character in this.Stale)
            this.Changed.Remove(character);

        if (ModEntry.Config.PassThroughEverything)
        {
            Game1.player.ignoreCollisions = true;
            this.SetIgnoreCollisions = true;
        }
        else if (this.SetIgnoreCollisions)
        {
            Game1.player.ignoreCollisions = false;
            this.SetIgnoreCollisions = false;
        }
    }

    /// <summary>Let the player walk through a character if the settings allow it.</summary>
    /// <param name="character">The character.</param>
    private void Update(NPC character)
    {
        if (character.farmerPassesThrough)
            return;

        PassThroughKind? kind = ModEntry.GetKind(character);
        if (kind != null && ModEntry.Config.Allows(kind.Value))
        {
            character.farmerPassesThrough = true;
            this.Changed.Add(character);
        }
    }

    /// <summary>Get what kind of character this is, if it's one this mod handles.</summary>
    /// <param name="character">The character.</param>
    private static PassThroughKind? GetKind(Character character)
    {
        return character switch
        {
            Horse => PassThroughKind.Horse,
            Pet => PassThroughKind.Pet,
            Junimo or JunimoHarvester => PassThroughKind.Junimo, // a hut's Junimos are JunimoHarvester, not Junimo
            Child => null,
            { IsMonster: true } => PassThroughKind.Monster,
            { IsVillager: true } => PassThroughKind.Villager,
            _ => null
        };
    }

    /// <summary>Before the player takes damage, skip it if a monster they can walk through caused it.</summary>
    /// <param name="damager">The monster which hit the player, if any.</param>
    /// <returns>Whether to run the base game's code.</returns>
    private static bool Before_TakeDamage(Monster? damager)
    {
        return damager == null || !ModEntry.Config.PassThroughMonsters;
    }
}
