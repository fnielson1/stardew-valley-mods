using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using SObject = StardewValley.Object;

namespace SmallerJunimoHut;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The Junimo Hut's ID in <c>Data/Buildings</c>.</summary>
    private const string HutId = "Junimo Hut";

    /// <summary>The Junimo Hut's size in tiles.</summary>
    private static readonly Point HutSize = new(2, 2);

    /// <summary>Where the base game's lit window sprite is in the hut texture.</summary>
    private static readonly Rectangle LightSourceRect = new(195, 0, 18, 17);

    /// <summary>Where the base game's coin bag sprite is in the hut texture.</summary>
    private static readonly Rectangle BagSourceRect = new(208, 51, 15, 13);

    /// <summary>Where the base game's raisin decoration sprite is in the hut texture.</summary>
    private static readonly Rectangle RaisinSourceRect = new(246, 46, 10, 18);

    /// <summary>The mod instance, for the Harmony patches.</summary>
    private static ModEntry Instance = null!; // set in Entry

    /// <summary>The hut texture last checked for art 2 tiles wide, if any.</summary>
    private static Texture2D? CheckedTexture;

    /// <summary>Whether <see cref="CheckedTexture"/> has art 2 tiles wide.</summary>
    private static bool CheckedTextureIsNarrow;

    /// <summary>Whether drawing a hut has failed, so the error is only logged once.</summary>
    private static bool LoggedDrawError;


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        ModEntry.Instance = this;

        Harmony harmony = new(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(JunimoHut), nameof(JunimoHut.draw)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_Draw))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(JunimoHut), nameof(JunimoHut.drawInMenu)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_DrawInMenu))
        );

        helper.Events.Content.AssetRequested += this.OnAssetRequested;
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IContentEvents.AssetRequested" />
    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
            return;

        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, BuildingData>().Data;
            if (data.TryGetValue(ModEntry.HutId, out BuildingData? hut))
                hut.Size = ModEntry.HutSize;
            else
                this.Monitor.Log($"Couldn't find the {ModEntry.HutId} in Data/Buildings, so it keeps its size.", LogLevel.Warn);
        });
    }

    /// <summary>Get how much to scale a hut's sprites, checking its texture for art 2 tiles wide the first time it's seen.</summary>
    /// <param name="texture">The hut's texture.</param>
    private static float GetScale(Texture2D texture)
    {
        if (!object.ReferenceEquals(texture, ModEntry.CheckedTexture))
        {
            Color[] pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
            ModEntry.CheckedTextureIsNarrow = HutLayout.IsNarrowArt(texture.Width, texture.Height, (x, y) => pixels[y * texture.Width + x].A > 0);
            ModEntry.CheckedTexture = texture;
            ModEntry.Instance.Monitor.Log(ModEntry.CheckedTextureIsNarrow
                ? "The Junimo Hut texture is already 2 tiles wide, so it's drawn at full size."
                : "The Junimo Hut texture is 3 tiles wide, so it's shrunk to fit 2 tiles.");
        }

        return HutLayout.GetScale(ModEntry.CheckedTextureIsNarrow);
    }

    /// <summary>Get a screen position from a hut's bottom-left corner plus an offset.</summary>
    /// <param name="hut">The Junimo Hut.</param>
    /// <param name="offset">The offset in screen pixels at zoom 1 (see <see cref="HutLayout"/>).</param>
    private static Vector2 GetScreenPosition(JunimoHut hut, (float X, float Y) offset)
    {
        Vector2 corner = new(hut.tileX.Value * 64, (hut.tileY.Value + hut.tilesHigh.Value) * 64);
        return Game1.GlobalToLocal(Game1.viewport, corner + new Vector2(offset.X, offset.Y));
    }

    /// <summary>Before a Junimo Hut is drawn in the world, draw it scaled to fit 2 tiles. The rest is what the base game does.</summary>
    /// <param name="__instance">The Junimo Hut.</param>
    /// <param name="b">The sprite batch to draw to.</param>
    /// <returns>Whether to run the base game's code.</returns>
    private static bool Before_Draw(JunimoHut __instance, SpriteBatch b)
    {
        if (__instance.isMoving || __instance.daysOfConstructionLeft.Value > 0)
            return true;

        try
        {
            JunimoHut hut = __instance;
            Texture2D texture = hut.texture.Value;
            float scale = ModEntry.GetScale(texture);
            Color color = hut.color * hut.alpha;
            float depth = (hut.tileY.Value + hut.tilesHigh.Value - 1) * 64 / 10000f;

            hut.drawShadow(b);
            Rectangle sourceRect = hut.getSourceRectForMenu() ?? hut.getSourceRect();
            b.Draw(texture, ModEntry.GetScreenPosition(hut, (0, 0)), sourceRect, color, 0f, new Vector2(0f, texture.Bounds.Height), 4f * scale, SpriteEffects.None, depth);

            if (hut.raisinDays.Value > 0 && !Game1.IsWinter)
                b.Draw(texture, ModEntry.GetScreenPosition(hut, HutLayout.Scale(HutLayout.BaseRaisinOffset, scale)), ModEntry.RaisinSourceRect, color, 0f, new Vector2(0f, 18f), 4f * scale, SpriteEffects.None, depth + 2 / 10000f);

            if (hut.GetOutputChest()?.Items.Any(item => item != null && item.Category != SObject.mineralsCategory && item.Category != SObject.GemCategory) == true)
                b.Draw(texture, ModEntry.GetScreenPosition(hut, HutLayout.GetBagOffset(scale)), ModEntry.BagSourceRect, color, 0f, Vector2.Zero, 4f * scale, SpriteEffects.None, depth + 1 / 10000f);

            if (Game1.timeOfDay >= 2000 && Game1.timeOfDay < 2400 && hut.wasLit.Value && !hut.GetParentLocation().IsWinterHere())
                b.Draw(texture, ModEntry.GetScreenPosition(hut, HutLayout.Scale(HutLayout.BaseLightOffset, scale)), ModEntry.LightSourceRect, color, 0f, Vector2.Zero, 4f * scale, SpriteEffects.None, depth + 1 / 10000f);

            return false;
        }
        catch (Exception ex)
        {
            if (!ModEntry.LoggedDrawError)
            {
                ModEntry.LoggedDrawError = true;
                ModEntry.Instance.Monitor.Log($"Couldn't draw the Junimo Hut shrunk, so it's drawn at full size.\n{ex}", LogLevel.Error);
            }
            return true;
        }
    }

    /// <summary>Before a Junimo Hut is drawn in a menu, like Robin's or the Wizard's building menu, draw it scaled to fit 2 tiles.</summary>
    /// <param name="__instance">The Junimo Hut.</param>
    /// <param name="b">The sprite batch to draw to.</param>
    /// <param name="x">The X screen position of the hut's top-left corner.</param>
    /// <param name="y">The Y screen position of the hut's top-left corner.</param>
    /// <returns>Whether to run the base game's code.</returns>
    private static bool Before_DrawInMenu(JunimoHut __instance, SpriteBatch b, int x, int y)
    {
        try
        {
            Texture2D texture = __instance.texture.Value;
            float scale = ModEntry.GetScale(texture);
            Rectangle sourceRect = new(0, 0, 48, 64);

            __instance.drawShadow(b, x, y);
            b.Draw(texture, new Vector2(x, y + HutLayout.GetMenuOffsetY(sourceRect.Height, scale)), sourceRect, __instance.color, 0f, Vector2.Zero, 4f * scale, SpriteEffects.None, 0.89f);
            return false;
        }
        catch (Exception ex)
        {
            if (!ModEntry.LoggedDrawError)
            {
                ModEntry.LoggedDrawError = true;
                ModEntry.Instance.Monitor.Log($"Couldn't draw the Junimo Hut shrunk, so it's drawn at full size.\n{ex}", LogLevel.Error);
            }
            return true;
        }
    }
}
