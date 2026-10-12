using System;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace ModSyncCheck;

/// <summary>A dialog shown on the title screen to a player who was blocked from joining.</summary>
internal class SyncDialog : ConfirmationDialog
{
    /// <summary>The maximum pixel width of the dialog text.</summary>
    private const int MaxTextWidth = 1000;

    /// <summary>Get the text to show, if it changes while the dialog is open.</summary>
    private readonly Func<string>? GetText;

    /// <summary>Construct an instance.</summary>
    /// <param name="text">The text to show, which also sets the dialog size.</param>
    /// <param name="onConfirm">The action to perform when the player clicks OK, or <c>null</c> to hide that button.</param>
    /// <param name="onCancel">The action to perform when the player clicks cancel, or <c>null</c> to close the dialog.</param>
    /// <param name="getText">Get the text to show, if it changes while the dialog is open.</param>
    public SyncDialog(string text, Action? onConfirm = null, Action? onCancel = null, Func<string>? getText = null)
        : base(
            Game1.parseText(text, Game1.dialogueFont, SyncDialog.MaxTextWidth),
            onConfirm != null ? _ => onConfirm() : null,
            onCancel != null ? _ => onCancel() : null
        )
    {
        this.GetText = getText;
        this.okButton.visible = onConfirm != null;
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        base.update(time);

        if (this.GetText != null)
            this.message = Game1.parseText(this.GetText(), Game1.dialogueFont, SyncDialog.MaxTextWidth);
    }
}
