using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Shared;

/// <summary>Draws checkboxes inside a Generic Mod Config Menu complex option, and applies clicks on them.</summary>
/// <remarks>A complex option only gets a draw callback, so clicks are caught from SMAPI's input events and matched to a checkbox when it's next drawn.</remarks>
internal sealed class ConfigCheckboxes
{
    /*********
    ** Fields
    *********/
    /// <summary>The position of a click which hasn't been handled by a checkbox yet, if any.</summary>
    private Point? Click;

    /// <summary>The game tick on which <see cref="Click"/> happened.</summary>
    private int ClickTick;

    /// <summary>The game tick on which a checkbox was last drawn.</summary>
    private int LastDrawTick = -100;


    /*********
    ** Accessors
    *********/
    /// <summary>The pixel size of a checkbox.</summary>
    public const int Size = 36;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="events">The SMAPI events, to catch clicks.</param>
    public ConfigCheckboxes(IModEvents events)
    {
        events.Input.ButtonPressed += this.OnButtonPressed;
    }

    /// <summary>Get whether a checkbox was just clicked, and mark the click handled if so.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position of the checkbox.</param>
    public bool TryClick(SpriteBatch spriteBatch, Vector2 position)
    {
        this.LastDrawTick = Game1.ticks;
        if (this.Click is not { } click || Game1.ticks - this.ClickTick > 1)
            return false;

        // the list is clipped to the visible part of the menu, so a box which is scrolled out of view can't be clicked
        Rectangle bounds = new((int)position.X, (int)position.Y, ConfigCheckboxes.Size, ConfigCheckboxes.Size);
        if (!bounds.Contains(click) || !spriteBatch.GraphicsDevice.ScissorRectangle.Contains(click))
            return false;

        this.Click = null;
        Game1.playSound("drumkit6");
        return true;
    }

    /// <summary>Draw a checkbox.</summary>
    /// <param name="spriteBatch">The sprite batch being drawn.</param>
    /// <param name="position">The pixel position at which to draw.</param>
    /// <param name="isChecked">Whether the box is checked.</param>
    /// <param name="enabled">Whether the box can be changed. Otherwise it's greyed out.</param>
    public void Draw(SpriteBatch spriteBatch, Vector2 position, bool isChecked, bool enabled = true)
    {
        this.LastDrawTick = Game1.ticks;
        Rectangle source = isChecked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked;
        spriteBatch.Draw(Game1.mouseCursors, position, source, Color.White * (enabled ? 1f : 0.35f), 0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 0f);
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IInputEvents.ButtonPressed" />
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        // only while checkboxes are being drawn, so the click which opened the menu isn't applied to a checkbox which then appears where it was
        if (e.Button is SButton.MouseLeft or SButton.ControllerA && Game1.ticks - this.LastDrawTick <= 2)
        {
            this.Click = new Point(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
            this.ClickTick = Game1.ticks;
        }
    }
}
