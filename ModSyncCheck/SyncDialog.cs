using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace ModSyncCheck;

/// <summary>A dialog shown on the title screen to a player who was blocked from joining.</summary>
internal class SyncDialog : ConfirmationDialog
{
    /// <summary>The maximum pixel width of the dialog text.</summary>
    private const int MaxTextWidth = 1000;

    /// <summary>The pixel size of the checkbox.</summary>
    private const int CheckboxSize = 36;

    /// <summary>The pixel gap between the checkbox and its label, and above and below the checkbox row.</summary>
    private const int CheckboxMargin = 16;

    /// <summary>Get the text to show, if it changes while the dialog is open.</summary>
    private readonly Func<string>? GetText;

    /// <summary>Whether the dialog has an OK button.</summary>
    private readonly bool CanConfirm;

    /// <summary>The checkbox the player must tick before they can click OK, if any. Its bounds cover the label too.</summary>
    public ClickableComponent? Checkbox;

    /// <summary>The wrapped checkbox label.</summary>
    private readonly string CheckboxLabel = "";

    /// <summary>Whether the player ticked the checkbox.</summary>
    private bool IsChecked;

    /// <summary>Construct an instance.</summary>
    /// <param name="text">The text to show, which also sets the dialog size.</param>
    /// <param name="onConfirm">The action to perform when the player clicks OK, or <c>null</c> to hide that button.</param>
    /// <param name="onCancel">The action to perform when the player clicks cancel, or <c>null</c> to close the dialog.</param>
    /// <param name="getText">Get the text to show, if it changes while the dialog is open.</param>
    /// <param name="checkboxLabel">The label for a checkbox the player must tick before they can click OK, or <c>null</c> for no checkbox.</param>
    public SyncDialog(string text, Action? onConfirm = null, Action? onCancel = null, Func<string>? getText = null, string? checkboxLabel = null)
        : base(
            Game1.parseText(text, Game1.dialogueFont, SyncDialog.MaxTextWidth),
            onConfirm != null ? _ => onConfirm() : null,
            onCancel != null ? _ => onCancel() : null
        )
    {
        this.GetText = getText;
        this.CanConfirm = onConfirm != null;

        if (checkboxLabel != null && this.CanConfirm)
        {
            int labelOffset = SyncDialog.CheckboxSize + SyncDialog.CheckboxMargin;
            int rowWidth = this.width - IClickableMenu.borderWidth * 2;
            this.CheckboxLabel = Game1.parseText(checkboxLabel, Game1.dialogueFont, rowWidth - labelOffset);
            Vector2 labelSize = Game1.dialogueFont.MeasureString(this.CheckboxLabel);
            int rowHeight = Math.Max(SyncDialog.CheckboxSize, (int)labelSize.Y);

            // make room for the checkbox row between the text and the buttons
            int extraHeight = rowHeight + SyncDialog.CheckboxMargin * 2;
            this.height += extraHeight;
            this.yPositionOnScreen -= extraHeight / 2;
            this.okButton.bounds.Y += extraHeight - extraHeight / 2;
            this.cancelButton.bounds.Y += extraHeight - extraHeight / 2;

            this.Checkbox = new ClickableComponent(
                new Rectangle(
                    this.xPositionOnScreen + IClickableMenu.borderWidth,
                    this.okButton.bounds.Y - SyncDialog.CheckboxMargin - rowHeight,
                    Math.Min(rowWidth, labelOffset + (int)labelSize.X),
                    rowHeight
                ),
                "Checkbox"
            )
            {
                myID = 103,
                downNeighborID = ConfirmationDialog.region_cancelButton
            };
            this.okButton.upNeighborID = this.Checkbox.myID;
            this.cancelButton.upNeighborID = this.Checkbox.myID;

            if (Game1.options.SnappyMenus)
            {
                this.populateClickableComponentList();
                this.snapToDefaultClickableComponent();
            }
        }

        this.UpdateOkButton();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.Checkbox != null)
        {
            if (this.Checkbox.containsPoint(x, y))
            {
                this.IsChecked = !this.IsChecked;
                this.UpdateOkButton();
                Game1.playSound("drumkit6");
                return;
            }

            if (!this.IsChecked && this.okButton.bounds.Contains(x, y))
                return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        base.update(time);

        if (this.GetText != null)
            this.message = Game1.parseText(this.GetText(), Game1.dialogueFont, SyncDialog.MaxTextWidth);
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        base.draw(b);
        if (this.Checkbox == null)
            return;

        Rectangle row = this.Checkbox.bounds;
        b.Draw(
            Game1.mouseCursors,
            new Vector2(row.X, row.Y + Math.Min(row.Height - SyncDialog.CheckboxSize, 8)),
            this.IsChecked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked,
            Color.White,
            0f,
            Vector2.Zero,
            SyncDialog.CheckboxSize / 9f,
            SpriteEffects.None,
            0.4f
        );
        b.DrawString(Game1.dialogueFont, this.CheckboxLabel, new Vector2(row.X + SyncDialog.CheckboxSize + SyncDialog.CheckboxMargin, row.Y), Game1.textColor);

        // the OK button is hidden until the checkbox is ticked, so show it greyed out
        if (!this.IsChecked)
            b.Draw(this.okButton.texture, this.okButton.bounds, this.okButton.sourceRect, Color.White * 0.35f);

        this.drawMouse(b);
    }

    /// <summary>Show the OK button only if it can be clicked.</summary>
    private void UpdateOkButton()
    {
        this.okButton.visible = this.CanConfirm && (this.Checkbox == null || this.IsChecked);
    }
}
