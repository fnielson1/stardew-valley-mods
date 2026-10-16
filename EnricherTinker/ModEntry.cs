using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace EnricherTinker;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The mod instance, for the Harmony patches.</summary>
    private static ModEntry Instance = null!; // set in Entry

    /// <summary>The mod settings.</summary>
    private ModConfig Config = null!; // set in Entry

    /// <summary>Reads whether Ultimate Fertilizer lets a tile take more than one fertilizer.</summary>
    private UltimateFertilizer UltimateFertilizer = null!; // set in Entry

    /// <summary>Whether the error for an Enricher whose captured values couldn't be found has been logged.</summary>
    private bool LoggedMissingCaptures;


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        ModEntry.Instance = this;
        this.Config = helper.ReadConfig<ModConfig>();
        this.UltimateFertilizer = new UltimateFertilizer(helper, this.Monitor);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

        Harmony harmony = new(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(Chest), nameof(Chest.GetActualCapacity)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.After_GetActualCapacity))
        );

        MethodInfo? applyEnricher = ModEntry.FindApplyEnricherMethod();
        if (applyEnricher == null)
        {
            this.Monitor.Log("Couldn't find the game code which applies an Enricher's fertilizer, so Enrichers will hold more stacks but only use the first one as usual, and use it up.", LogLevel.Error);
            return;
        }
        harmony.Patch(
            original: applyEnricher,
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_ApplyEnricher))
        );
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched" />
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        new ConfigMenu(this.Helper, this.ModManifest, this.Config, this.UltimateFertilizer).Register();
    }

    /// <summary>Find the game method which applies an Enricher's fertilizer after a seed is planted in range.</summary>
    /// <remarks>It's a lambda in <see cref="SObject.placementAction"/>, run once the Enricher's chest is locked, so its compiler-generated name isn't stable. It's the only method in a nested type of <see cref="SObject"/> which both plants fertilizer and uses up a stack.</remarks>
    private static MethodInfo? FindApplyEnricherMethod()
    {
        MethodInfo consumeStack = AccessTools.Method(typeof(Item), nameof(Item.ConsumeStack));
        MethodInfo plant = AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.plant));

        foreach (Type type in typeof(SObject).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.IsAbstract || method.GetMethodBody() == null)
                    continue;

                List<object> operands = PatchProcessor.ReadMethodBody(method).Select(p => p.Value).ToList();
                if (operands.Contains(consumeStack) && operands.Contains(plant))
                    return method;
            }
        }
        return null;
    }

    /// <summary>Get the values the Enricher lambda captured, from its closure and the outer closures it links to.</summary>
    /// <param name="closure">The closure instance.</param>
    /// <param name="chest">The Enricher's chest.</param>
    /// <param name="slots">The Enricher's slots.</param>
    /// <param name="dirt">The tile which was planted.</param>
    /// <param name="who">The player who planted it.</param>
    private static bool TryGetCaptures(object closure, out Chest chest, out IInventory slots, out HoeDirt dirt, out Farmer who)
    {
        Chest? foundChest = null;
        IInventory? foundSlots = null;
        HoeDirt? foundDirt = null;
        Farmer? foundWho = null;

        // the lambda's closure links to the closures of the scopes around it, e.g. the one holding the player
        void Search(object? obj, int depth)
        {
            if (obj == null || depth > 5)
                return;
            foreach (FieldInfo field in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object? value = field.GetValue(obj);
                switch (value)
                {
                    case Chest c:
                        foundChest ??= c;
                        break;
                    case IInventory i:
                        foundSlots ??= i;
                        break;
                    case HoeDirt d:
                        foundDirt ??= d;
                        break;
                    case Farmer f:
                        foundWho ??= f;
                        break;
                    default:
                        if (field.FieldType.IsDefined(typeof(CompilerGeneratedAttribute)))
                            Search(value, depth + 1);
                        break;
                }
            }
        }
        Search(closure, 0);

        chest = foundChest!;
        slots = foundSlots ?? foundChest?.Items!;
        dirt = foundDirt!;
        who = foundWho!;
        return chest != null && slots != null && dirt != null && who != null;
    }

    /// <summary>Before the game applies an Enricher's fertilizer, apply every stack instead of only the first, and don't use them up if they're infinite.</summary>
    /// <param name="__instance">The lambda's closure.</param>
    /// <returns>Whether to run the game's code.</returns>
    private static bool Before_ApplyEnricher(object __instance)
    {
        ModEntry mod = ModEntry.Instance;
        ModConfig config = mod.Config;
        if (!config.ModEnabled)
            return true;

        if (!ModEntry.TryGetCaptures(__instance, out Chest chest, out IInventory slots, out HoeDirt dirt, out Farmer who))
        {
            if (!mod.LoggedMissingCaptures)
            {
                mod.Monitor.Log($"Couldn't read the Enricher's values from {__instance.GetType().FullName}, so Enrichers work as in the base game.", LogLevel.Error);
                mod.LoggedMissingCaptures = true;
            }
            return true;
        }

        try
        {
            FertilizerApplier.Apply(
                slots: slots,
                tryApply: item => item.Category == SObject.fertilizerCategory && dirt.plant(item.ItemId, who, isFertilizer: true),
                consumeOne: item => item.ConsumeStack(1),
                applyAll: config.ApplyAllFertilizers && mod.UltimateFertilizer.AllowsMultiple(),
                infinite: config.InfiniteFertilizer
            );

            // the game only looks for fertilizer in the first slot before running this, so keep the stacks at the front
            chest.clearNulls();
        }
        catch (Exception ex)
        {
            mod.Monitor.Log($"Failed applying an Enricher's fertilizer:\n{ex}", LogLevel.Error);
        }
        finally
        {
            chest.GetMutex().ReleaseLock();
        }
        return false;
    }

    /// <summary>After the game gets a chest's capacity, give an Enricher a 3x3 grid of slots.</summary>
    /// <param name="__instance">The chest.</param>
    /// <param name="__result">The chest's capacity.</param>
    private static void After_GetActualCapacity(Chest __instance, ref int __result)
    {
        if (__instance.SpecialChestType != Chest.SpecialChestTypes.Enricher)
            return;

        int slots = ModEntry.Instance.Config.ModEnabled ? EnricherSlots.Count : __result;
        __result = EnricherSlots.Capacity(slots, __instance.Items.CountItemStacks());
    }
}
