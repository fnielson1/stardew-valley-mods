using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
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

    /// <summary>The ID of the link for the first mod for controller navigation. Each link's ID is this plus its index in <see cref="Links"/>.</summary>
    private const int FirstLinkId = 200;

    /// <summary>The minimum pixel gap between the dialog and the top or bottom of the screen.</summary>
    private const int ScreenMargin = 16;

    /// <summary>The pixel height above the dialog's position which the game leaves out when drawing the box.</summary>
    private const int BoxTopOffset = 64;

    /// <summary>Get whether the dialog is still active, which the base dialog keeps private. It stops being active once the player clicks OK.</summary>
    private static readonly AccessTools.FieldRef<ConfirmationDialog, bool> GetActive = AccessTools.FieldRefAccess<ConfirmationDialog, bool>("active");

    /// <summary>The color of a link.</summary>
    private static readonly Color LinkColor = new(40, 90, 200);

    /// <summary>The color of the link under the cursor.</summary>
    private static readonly Color HoveredLinkColor = new(20, 50, 150);

    /// <summary>The unwrapped text to show.</summary>
    private string Text;

    /// <summary>Get the text to show, if it changes while the dialog is open.</summary>
    private readonly Func<string>? GetText;

    /// <summary>Whether the dialog has an OK button.</summary>
    private readonly bool CanConfirm;

    /// <summary>The font for all the dialog's text. This is the game's smaller font if the dialog doesn't fit on the screen with the normal one.</summary>
    private SpriteFont Font = Game1.dialogueFont;

    /// <summary>The unwrapped checkbox label, if the dialog has a checkbox.</summary>
    private readonly string? RawCheckboxLabel;

    /// <summary>The dialog's checkbox, if any. Its bounds cover the label too.</summary>
    public ClickableComponent? Checkbox;

    /// <summary>Whether the player must tick the checkbox before they can click OK.</summary>
    private readonly bool RequireCheckbox;

    /// <summary>Get the text for whether the checkbox is ticked, if it depends on that.</summary>
    private readonly Func<bool, string>? GetCheckboxText;

    /// <summary>The wrapped checkbox label.</summary>
    private string CheckboxLabel = "";

    /// <summary>Whether the player ticked the checkbox.</summary>
    public bool IsChecked { get; private set; }

    /// <summary>The mods whose names are shown as links in the text.</summary>
    private readonly IReadOnlyList<ModLink> Links = Array.Empty<ModLink>();

    /// <summary>The pieces of the wrapped text to draw, with each link in a piece of its own.</summary>
    private List<TextRun> Runs = new();

    /// <summary>The areas the player can click to open a link, with the link's index in <see cref="Links"/>. A name wrapped onto two lines has an area on each.</summary>
    private List<(Rectangle Bounds, int Link)> LinkAreas = new();

    /// <summary>The links the player can select with a controller, in the order they're shown.</summary>
    public List<ClickableComponent> LinkComponents = new();

    /// <summary>Open a link the player clicked.</summary>
    private readonly Action<ModLink>? OpenLink;

    /// <summary>The index of the link under the cursor, if any.</summary>
    private int HoveredLink = -1;

    /// <summary>Construct an instance.</summary>
    /// <param name="text">The text to show, which also sets the dialog size.</param>
    /// <param name="onConfirm">The action to perform when the player clicks OK, or <c>null</c> to hide that button.</param>
    /// <param name="onCancel">The action to perform when the player clicks cancel, or <c>null</c> to close the dialog.</param>
    /// <param name="getText">Get the text to show, if it changes while the dialog is open.</param>
    /// <param name="checkboxLabel">The label for a checkbox, or <c>null</c> for none.</param>
    /// <param name="links">The mods whose names to show as links, in the order their lines appear in the text.</param>
    /// <param name="openLink">Open a link the player clicked. The names are only shown as links if this is set.</param>
    /// <param name="requireCheckbox">Whether the checkbox must be ticked before OK can be clicked. If not, the caller reads <see cref="IsChecked"/> to choose what OK does.</param>
    /// <param name="getCheckboxText">Get the text for whether the checkbox is ticked, replacing <paramref name="text"/>.</param>
    public SyncDialog(string text, Action? onConfirm = null, Action? onCancel = null, Func<string>? getText = null, string? checkboxLabel = null, IReadOnlyList<ModLink>? links = null, Action<ModLink>? openLink = null, bool requireCheckbox = true, Func<bool, string>? getCheckboxText = null)
        : base(
            Game1.parseText(getCheckboxText?.Invoke(false) ?? text, Game1.dialogueFont, SyncDialog.MaxTextWidth),
            onConfirm != null ? _ => onConfirm() : null,
            onCancel != null ? _ => onCancel() : null
        )
    {
        this.Text = getCheckboxText?.Invoke(false) ?? text;
        this.GetText = getText;
        this.CanConfirm = onConfirm != null;
        this.RequireCheckbox = requireCheckbox;
        this.GetCheckboxText = getCheckboxText;
        this.OpenLink = openLink;
        if (checkboxLabel != null && this.CanConfirm)
            this.RawCheckboxLabel = checkboxLabel;
        if (links?.Count > 0 && openLink != null)
            this.Links = links;

        this.LayOut();
        this.UpdateOkButton();
    }

    /// <inheritdoc />
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        this.LayOut();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        int link = this.GetLinkAt(x, y);
        if (link >= 0)
        {
            Game1.playSound("smallSelect");
            this.OpenLink?.Invoke(this.Links[link]);
            return;
        }

        if (this.Checkbox != null)
        {
            if (this.Checkbox.containsPoint(x, y))
            {
                this.IsChecked = !this.IsChecked;
                this.UpdateOkButton();
                if (this.GetCheckboxText != null)
                {
                    this.Text = this.GetCheckboxText(this.IsChecked);
                    this.SetMessage();
                    this.PlaceLinks();
                }
                Game1.playSound("drumkit6");
                return;
            }

            if (this.RequireCheckbox && !this.IsChecked && this.okButton.bounds.Contains(x, y))
                return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    /// <inheritdoc />
    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);

        this.HoveredLink = this.GetLinkAt(x, y);
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        base.update(time);

        if (this.GetText != null)
        {
            this.Text = this.GetText();
            this.SetMessage();
        }
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        if (!SyncDialog.GetActive(this))
            return;

        // same as the base dialog, but in this dialog's font and with the links in color
        b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.5f);
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, speaker: false, drawOnlyBox: true);
        this.DrawText(b);
        this.okButton.draw(b);
        this.cancelButton.draw(b);

        if (this.Checkbox != null)
        {
            Rectangle row = this.Checkbox.bounds;
            b.Draw(
                Game1.mouseCursors,
                new Vector2(row.X, row.Y + Math.Max(0, Math.Min(row.Height - SyncDialog.CheckboxSize, 8))),
                this.IsChecked ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked,
                Color.White,
                0f,
                Vector2.Zero,
                SyncDialog.CheckboxSize / 9f,
                SpriteEffects.None,
                0.4f
            );
            b.DrawString(this.Font, this.CheckboxLabel, new Vector2(row.X + SyncDialog.CheckboxSize + SyncDialog.CheckboxMargin, row.Y), Game1.textColor);

            // the OK button is hidden until the checkbox is ticked, so show it greyed out
            if (this.RequireCheckbox && !this.IsChecked)
                b.Draw(this.okButton.texture, this.okButton.bounds, this.okButton.sourceRect, Color.White * 0.35f);
        }

        // show where the hovered link goes
        if (this.HoveredLink >= 0)
            IClickableMenu.drawHoverText(b, this.Links[this.HoveredLink].Url, Game1.smallFont);

        this.drawMouse(b);
    }

    /// <summary>The position of the top-left of the text.</summary>
    private Vector2 TextPosition => new(this.xPositionOnScreen + IClickableMenu.borderWidth, this.yPositionOnScreen + IClickableMenu.spaceToClearTopBorder + IClickableMenu.borderWidth / 2);

    /// <summary>Size and position the dialog and everything in it, using the smaller font if it doesn't fit on the screen with the normal one.</summary>
    private void LayOut()
    {
        int maxHeight = Game1.uiViewport.Height - SyncDialog.ScreenMargin * 2;
        this.LayOut(Game1.dialogueFont);
        if (this.height - SyncDialog.BoxTopOffset > maxHeight)
            this.LayOut(Game1.smallFont);

        if (Game1.options.SnappyMenus)
        {
            this.populateClickableComponentList();
            this.snapToDefaultClickableComponent();
        }
    }

    /// <summary>Size and position the dialog and everything in it.</summary>
    /// <param name="font">The font for the dialog text.</param>
    private void LayOut(SpriteFont font)
    {
        this.Font = font;
        Vector2 textSize = this.MeasureText();
        int rowWidth = (int)textSize.X;

        // measure the checkbox row
        int checkboxHeight = 0;
        int checkboxWidth = 0;
        int labelOffset = SyncDialog.CheckboxSize + SyncDialog.CheckboxMargin;
        if (this.RawCheckboxLabel != null)
        {
            this.CheckboxLabel = Game1.parseText(this.RawCheckboxLabel, font, rowWidth - labelOffset);
            Vector2 labelSize = font.MeasureString(this.CheckboxLabel);
            checkboxHeight = Math.Max(SyncDialog.CheckboxSize, (int)labelSize.Y);
            checkboxWidth = Math.Min(rowWidth, labelOffset + (int)labelSize.X);
        }

        // size the dialog like the base one, with room for the checkbox between the text and the buttons
        int extraHeight = checkboxHeight > 0
            ? checkboxHeight + SyncDialog.CheckboxMargin * 2
            : 0;
        this.width = rowWidth + IClickableMenu.borderWidth * 2;
        this.height = (int)textSize.Y + IClickableMenu.borderWidth * 2 + 160 + extraHeight;

        // center the box the game draws, which starts below the dialog's position
        this.xPositionOnScreen = Game1.uiViewport.Width / 2 - this.width / 2;
        this.yPositionOnScreen = Math.Max(SyncDialog.ScreenMargin - SyncDialog.BoxTopOffset, (Game1.uiViewport.Height - this.height - SyncDialog.BoxTopOffset) / 2);

        int buttonsY = this.yPositionOnScreen + this.height - IClickableMenu.borderWidth - IClickableMenu.spaceToClearTopBorder + 21;
        this.okButton.setPosition(this.xPositionOnScreen + this.width - IClickableMenu.borderWidth - IClickableMenu.spaceToClearSideBorder - 128 - 4, buttonsY);
        this.cancelButton.setPosition(this.xPositionOnScreen + this.width - IClickableMenu.borderWidth - IClickableMenu.spaceToClearSideBorder - 64, buttonsY);

        // place the checkbox above the buttons
        this.Checkbox = null;
        if (checkboxHeight > 0)
        {
            this.Checkbox = new ClickableComponent(new Rectangle(this.xPositionOnScreen + IClickableMenu.borderWidth, buttonsY - SyncDialog.CheckboxMargin - checkboxHeight, checkboxWidth, checkboxHeight), "Checkbox")
            {
                myID = 103,
                downNeighborID = ConfirmationDialog.region_cancelButton
            };
        }

        this.PlaceLinks();

        // connect the links, checkbox, and buttons from top to bottom for controller navigation
        int lastLinkId = this.LinkComponents.LastOrDefault()?.myID ?? -1;
        int buttonsUpId = this.Checkbox?.myID ?? lastLinkId;
        this.okButton.upNeighborID = buttonsUpId;
        this.cancelButton.upNeighborID = buttonsUpId;
        if (this.Checkbox != null)
            this.Checkbox.upNeighborID = lastLinkId;
    }

    /// <summary>Wrap the text in the current font and get its size. Text which depends on the checkbox is measured both ways, so the dialog doesn't resize when it's ticked.</summary>
    private Vector2 MeasureText()
    {
        if (this.GetCheckboxText == null)
        {
            this.SetMessage();
            return this.Font.MeasureString(this.message);
        }

        Vector2 size = Vector2.Zero;
        foreach (bool isChecked in new[] { !this.IsChecked, this.IsChecked })
        {
            this.Text = this.GetCheckboxText(isChecked);
            this.SetMessage();
            Vector2 textSize = this.Font.MeasureString(this.message);
            size = new Vector2(Math.Max(size.X, textSize.X), Math.Max(size.Y, textSize.Y));
        }
        return size;
    }

    /// <summary>Wrap the text in the current font, and split it into the pieces to draw with each mod name in <see cref="Links"/> as a piece of its own.</summary>
    private void SetMessage()
    {
        SpriteFont font = this.Font;

        // wrap each line separately, to find the mod names in the lines which list them
        StringBuilder message = new();
        List<(int Start, int Length, int Link)> linkRanges = new();
        int nextLink = 0;
        foreach (string line in this.Text.Split('\n'))
        {
            if (message.Length > 0)
                message.Append('\n');
            string wrapped = line.Length > 0 ? Game1.parseText(line, font, SyncDialog.MaxTextWidth) : "";

            for (int i = nextLink; i < this.Links.Count; i++)
            {
                if (line != this.Links[i].Entry)
                    continue;

                // wrapping replaces a space with a line break, so the name may be split over two lines
                string[] words = this.Links[i].Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Match match = words.Length > 0 ? Regex.Match(wrapped, string.Join(@"\s+", words.Select(Regex.Escape))) : Match.Empty;
                if (match.Success)
                    linkRanges.Add((message.Length + match.Index, match.Length, i));
                nextLink = i + 1;
                break;
            }

            message.Append(wrapped);
        }
        this.message = message.ToString();

        // split each line where a link starts or ends
        int[] owners = Enumerable.Repeat(-1, this.message.Length).ToArray();
        foreach ((int start, int length, int link) in linkRanges)
            Array.Fill(owners, link, start, length);

        List<TextRun> runs = new();
        string[] lines = this.message.Split('\n');
        int lineStart = 0;
        for (int row = 0; row < lines.Length; row++)
        {
            string line = lines[row];
            for (int start = 0; start < line.Length;)
            {
                int owner = owners[lineStart + start];
                int end = start + 1;
                while (end < line.Length && owners[lineStart + end] == owner)
                    end++;

                string piece = line[start..end];
                runs.Add(new TextRun(piece, new Vector2(font.MeasureString(line[..start]).X, row * font.LineSpacing), font.MeasureString(piece).X, owner));
                start = end;
            }
            lineStart += line.Length + 1;
        }
        this.Runs = runs;
    }

    /// <summary>Set the areas the player can click to open each link, now that the text has been positioned.</summary>
    private void PlaceLinks()
    {
        Vector2 origin = this.TextPosition;
        this.LinkAreas = this.Runs
            .Where(run => run.Link >= 0)
            .Select(run => (new Rectangle((int)(origin.X + run.Position.X), (int)(origin.Y + run.Position.Y), (int)run.Width, this.Font.LineSpacing), run.Link))
            .ToList();

        // a controller selects the first line of each name
        this.LinkComponents = this.LinkAreas
            .GroupBy(area => area.Link)
            .Select(group => new ClickableComponent(group.First().Bounds, $"Link{group.Key}") { myID = SyncDialog.FirstLinkId + group.Key })
            .ToList();
        for (int i = 0; i < this.LinkComponents.Count; i++)
        {
            ClickableComponent component = this.LinkComponents[i];
            component.upNeighborID = i > 0 ? this.LinkComponents[i - 1].myID : -1;
            component.downNeighborID = i < this.LinkComponents.Count - 1
                ? this.LinkComponents[i + 1].myID
                : this.Checkbox?.myID ?? ConfirmationDialog.region_cancelButton;
        }
    }

    /// <summary>Get the index in <see cref="Links"/> of the link at a screen position, or -1 if there's none.</summary>
    /// <param name="x">The X pixel position.</param>
    /// <param name="y">The Y pixel position.</param>
    private int GetLinkAt(int x, int y)
    {
        foreach ((Rectangle bounds, int link) in this.LinkAreas)
        {
            if (bounds.Contains(x, y))
                return link;
        }
        return -1;
    }

    /// <summary>Draw the text, with the links in color and underlined.</summary>
    /// <param name="b">The sprite batch being drawn.</param>
    private void DrawText(SpriteBatch b)
    {
        Vector2 origin = this.TextPosition;
        foreach (TextRun run in this.Runs)
        {
            Vector2 position = origin + run.Position;
            if (run.Link < 0)
            {
                b.DrawString(this.Font, run.Text, position, Game1.textColor);
                continue;
            }

            bool hovered = run.Link == this.HoveredLink || (Game1.options.SnappyMenus && this.currentlySnappedComponent?.myID == SyncDialog.FirstLinkId + run.Link);
            Color color = hovered ? SyncDialog.HoveredLinkColor : SyncDialog.LinkColor;
            b.DrawString(this.Font, run.Text, position, color);
            b.Draw(Game1.staminaRect, new Rectangle((int)position.X, (int)position.Y + this.Font.LineSpacing - this.Font.LineSpacing / 4, (int)run.Width, hovered ? 3 : 2), color);
        }
    }

    /// <summary>Show the OK button only if it can be clicked.</summary>
    private void UpdateOkButton()
    {
        this.okButton.visible = this.CanConfirm && (this.Checkbox == null || !this.RequireCheckbox || this.IsChecked);
    }

    /// <summary>A piece of the wrapped text, within a single line.</summary>
    /// <param name="Text">The text to draw.</param>
    /// <param name="Position">The pixel position relative to the top-left of the text.</param>
    /// <param name="Width">The pixel width of the text.</param>
    /// <param name="Link">The index in <see cref="Links"/> of the link this piece shows, or -1 if it isn't a link.</param>
    private record TextRun(string Text, Vector2 Position, float Width, int Link);
}
