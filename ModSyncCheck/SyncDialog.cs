using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>The pixel gap between the checkbox and its label, and above and below each row added below the text.</summary>
    private const int CheckboxMargin = 16;

    /// <summary>The pixel gap between two links on the same line.</summary>
    private const int LinkGap = 32;

    /// <summary>The ID of the first link for controller navigation. The others follow it.</summary>
    private const int FirstLinkId = 200;

    /// <summary>The color of a link.</summary>
    private static readonly Color LinkColor = new(40, 90, 200);

    /// <summary>The color of the link under the cursor.</summary>
    private static readonly Color HoveredLinkColor = new(20, 50, 150);

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

    /// <summary>The links the player can click, in the same order as <see cref="Links"/>. Their bounds cover the link text.</summary>
    public List<ClickableComponent> LinkComponents = new();

    /// <summary>The links shown below the text.</summary>
    private readonly IReadOnlyList<ModLink> Links = Array.Empty<ModLink>();

    /// <summary>The wrapped text shown above the links.</summary>
    private readonly string LinksLabel = "";

    /// <summary>The position of <see cref="LinksLabel"/>.</summary>
    private readonly Vector2 LinksLabelPosition;

    /// <summary>Open a link the player clicked.</summary>
    private readonly Action<ModLink>? OpenLink;

    /// <summary>The index of the link under the cursor, if any.</summary>
    private int HoveredLink = -1;

    /// <summary>Construct an instance.</summary>
    /// <param name="text">The text to show, which also sets the dialog size.</param>
    /// <param name="onConfirm">The action to perform when the player clicks OK, or <c>null</c> to hide that button.</param>
    /// <param name="onCancel">The action to perform when the player clicks cancel, or <c>null</c> to close the dialog.</param>
    /// <param name="getText">Get the text to show, if it changes while the dialog is open.</param>
    /// <param name="checkboxLabel">The label for a checkbox the player must tick before they can click OK, or <c>null</c> for no checkbox.</param>
    /// <param name="links">The links to show below the text, if any.</param>
    /// <param name="linksLabel">The text to show above the links, if any.</param>
    /// <param name="openLink">Open a link the player clicked. The links are only shown if this is set.</param>
    public SyncDialog(string text, Action? onConfirm = null, Action? onCancel = null, Func<string>? getText = null, string? checkboxLabel = null, IReadOnlyList<ModLink>? links = null, string? linksLabel = null, Action<ModLink>? openLink = null)
        : base(
            Game1.parseText(text, Game1.dialogueFont, SyncDialog.MaxTextWidth),
            onConfirm != null ? _ => onConfirm() : null,
            onCancel != null ? _ => onCancel() : null
        )
    {
        this.GetText = getText;
        this.CanConfirm = onConfirm != null;
        this.OpenLink = openLink;

        int rowWidth = this.width - IClickableMenu.borderWidth * 2;

        // measure the links, laid out left to right and wrapped onto more lines as needed
        int linksHeight = 0;
        List<Rectangle> linkBounds = new(); // relative to the top-left of the links area
        if (links?.Count > 0 && openLink != null)
        {
            this.Links = links;
            if (linksLabel != null)
            {
                this.LinksLabel = Game1.parseText(linksLabel, Game1.dialogueFont, rowWidth);
                linksHeight = (int)Game1.dialogueFont.MeasureString(this.LinksLabel).Y;
            }

            int lineHeight = Game1.dialogueFont.LineSpacing;
            int x = 0;
            int y = linksHeight;
            foreach (ModLink link in links)
            {
                int linkWidth = Math.Min(rowWidth, (int)Game1.dialogueFont.MeasureString(link.Name).X);
                if (x > 0 && x + linkWidth > rowWidth)
                {
                    x = 0;
                    y += lineHeight;
                }
                linkBounds.Add(new Rectangle(x, y, linkWidth, lineHeight));
                x += linkWidth + SyncDialog.LinkGap;
            }
            linksHeight = y + lineHeight;
        }

        // measure the checkbox row
        int checkboxHeight = 0;
        int checkboxWidth = 0;
        int labelOffset = SyncDialog.CheckboxSize + SyncDialog.CheckboxMargin;
        if (checkboxLabel != null && this.CanConfirm)
        {
            this.CheckboxLabel = Game1.parseText(checkboxLabel, Game1.dialogueFont, rowWidth - labelOffset);
            Vector2 labelSize = Game1.dialogueFont.MeasureString(this.CheckboxLabel);
            checkboxHeight = Math.Max(SyncDialog.CheckboxSize, (int)labelSize.Y);
            checkboxWidth = Math.Min(rowWidth, labelOffset + (int)labelSize.X);
        }

        if (linksHeight == 0 && checkboxHeight == 0)
        {
            this.UpdateOkButton();
            return;
        }

        // make room for the rows between the text and the buttons
        int extraHeight = SyncDialog.CheckboxMargin;
        foreach (int rowHeight in new[] { linksHeight, checkboxHeight })
        {
            if (rowHeight > 0)
                extraHeight += rowHeight + SyncDialog.CheckboxMargin;
        }
        this.height += extraHeight;
        this.yPositionOnScreen -= extraHeight / 2;
        this.okButton.bounds.Y += extraHeight - extraHeight / 2;
        this.cancelButton.bounds.Y += extraHeight - extraHeight / 2;

        // place the rows upwards from the buttons
        int left = this.xPositionOnScreen + IClickableMenu.borderWidth;
        int bottom = this.okButton.bounds.Y;
        if (checkboxHeight > 0)
        {
            bottom -= SyncDialog.CheckboxMargin + checkboxHeight;
            this.Checkbox = new ClickableComponent(new Rectangle(left, bottom, checkboxWidth, checkboxHeight), "Checkbox")
            {
                myID = 103,
                downNeighborID = ConfirmationDialog.region_cancelButton
            };
        }
        if (linksHeight > 0)
        {
            bottom -= SyncDialog.CheckboxMargin + linksHeight;
            this.LinksLabelPosition = new Vector2(left, bottom);
            for (int i = 0; i < linkBounds.Count; i++)
            {
                Rectangle bounds = linkBounds[i];
                this.LinkComponents.Add(
                    new ClickableComponent(new Rectangle(left + bounds.X, bottom + bounds.Y, bounds.Width, bounds.Height), $"Link{i}")
                    {
                        myID = SyncDialog.FirstLinkId + i,
                        leftNeighborID = i > 0 ? SyncDialog.FirstLinkId + i - 1 : -1,
                        rightNeighborID = i < linkBounds.Count - 1 ? SyncDialog.FirstLinkId + i + 1 : -1,
                        downNeighborID = this.Checkbox?.myID ?? ConfirmationDialog.region_cancelButton
                    }
                );
            }
        }

        // connect the rows for controller navigation
        int? buttonsUpId = this.Checkbox?.myID ?? this.LinkComponents.FirstOrDefault()?.myID;
        if (buttonsUpId != null)
        {
            this.okButton.upNeighborID = buttonsUpId.Value;
            this.cancelButton.upNeighborID = buttonsUpId.Value;
        }
        if (this.Checkbox != null && this.LinkComponents.Count > 0)
            this.Checkbox.upNeighborID = this.LinkComponents[0].myID;

        if (Game1.options.SnappyMenus)
        {
            this.populateClickableComponentList();
            this.snapToDefaultClickableComponent();
        }

        this.UpdateOkButton();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        for (int i = 0; i < this.LinkComponents.Count; i++)
        {
            if (this.LinkComponents[i].containsPoint(x, y))
            {
                Game1.playSound("smallSelect");
                this.OpenLink?.Invoke(this.Links[i]);
                return;
            }
        }

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
    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);

        this.HoveredLink = this.LinkComponents.FindIndex(link => link.containsPoint(x, y));
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
        if (this.Checkbox == null && this.LinkComponents.Count == 0)
            return;

        if (this.LinkComponents.Count > 0)
            this.DrawLinks(b);

        if (this.Checkbox != null)
        {
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
        }

        // show where the hovered link goes
        if (this.HoveredLink >= 0)
            IClickableMenu.drawHoverText(b, this.Links[this.HoveredLink].Url, Game1.smallFont);

        this.drawMouse(b);
    }

    /// <summary>Draw the links and the text above them.</summary>
    /// <param name="b">The sprite batch being drawn.</param>
    private void DrawLinks(SpriteBatch b)
    {
        if (this.LinksLabel.Length > 0)
            b.DrawString(Game1.dialogueFont, this.LinksLabel, this.LinksLabelPosition, Game1.textColor);

        for (int i = 0; i < this.LinkComponents.Count; i++)
        {
            Rectangle bounds = this.LinkComponents[i].bounds;
            bool hovered = i == this.HoveredLink || (Game1.options.SnappyMenus && this.currentlySnappedComponent == this.LinkComponents[i]);
            Color color = hovered ? SyncDialog.HoveredLinkColor : SyncDialog.LinkColor;

            b.DrawString(Game1.dialogueFont, this.Links[i].Name, new Vector2(bounds.X, bounds.Y), color);
            b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Bottom - 10, bounds.Width, hovered ? 3 : 2), color);
        }
    }

    /// <summary>Show the OK button only if it can be clicked.</summary>
    private void UpdateOkButton()
    {
        this.okButton.visible = this.CanConfirm && (this.Checkbox == null || this.IsChecked);
    }
}
