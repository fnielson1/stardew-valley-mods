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
    private const int BoxTopOffset = 80;

    /// <summary>The pixel height below the box which the game leaves out when drawing it.</summary>
    private const int BoxBottomOffset = 20;

    /// <summary>The pixel width of the box's border, within which content isn't shown when it scrolls.</summary>
    private const int BoxBorder = 20;

    /// <summary>The pixel width at the right of the title screen taken by its fullscreen button, which the scroll bar is kept clear of.</summary>
    private const int CornerButtonWidth = 36 + 16;

    /// <summary>The pixel height at the bottom of the title screen covered by its back button, which is drawn over the dialog.</summary>
    private const int BackButtonHeight = 81 + 24;

    /// <summary>The number of text lines to scroll for each mouse wheel step or arrow click.</summary>
    private const int ScrollLines = 3;

    /// <summary>The pixel scale of the scroll arrows.</summary>
    private const int ArrowScale = 4;

    /// <summary>The up scroll arrow's source rectangle in <see cref="Game1.mouseCursors"/>.</summary>
    private static readonly Rectangle UpArrowSource = new(421, 459, 11, 12);

    /// <summary>The down scroll arrow's source rectangle in <see cref="Game1.mouseCursors"/>.</summary>
    private static readonly Rectangle DownArrowSource = new(421, 472, 11, 12);

    /// <summary>The scroll bar's source rectangle in <see cref="Game1.mouseCursors"/>.</summary>
    private static readonly Rectangle ScrollBarSource = new(435, 463, 6, 10);

    /// <summary>The scroll bar track's source rectangle in <see cref="Game1.mouseCursors"/>.</summary>
    private static readonly Rectangle ScrollTrackSource = new(403, 383, 6, 6);

    /// <summary>Draws only within the scissor rectangle, to clip the scrolled content to the box.</summary>
    private static readonly RasterizerState ClipState = new() { ScissorTestEnable = true };

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
    private IReadOnlyList<ModLink> Links = Array.Empty<ModLink>();

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

    /// <summary>How many pixels the dialog is scrolled down, if it's taller than the screen.</summary>
    private int ScrollOffset;

    /// <summary>The furthest the dialog can be scrolled down, or 0 if it fits on the screen.</summary>
    private int MaxScroll;

    /// <summary>The box to draw, as passed to <see cref="Game1.drawDialogueBox(int,int,int,int,bool,bool,string,bool,bool,int,int,int)"/>. If the content doesn't fit on the screen, this is shorter than the content, which scrolls within it.</summary>
    private Rectangle Box;

    /// <summary>The area within the box where the content is shown.</summary>
    private Rectangle View;

    /// <summary>The arrow which scrolls up, if the content doesn't fit.</summary>
    private Rectangle UpArrow;

    /// <summary>The arrow which scrolls down, if the content doesn't fit.</summary>
    private Rectangle DownArrow;

    /// <summary>The track the scroll bar moves along, if the content doesn't fit.</summary>
    private Rectangle ScrollTrack;

    /// <summary>The scroll bar, if the content doesn't fit.</summary>
    private Rectangle ScrollBar;

    /// <summary>Whether the player is dragging the scroll bar.</summary>
    private bool IsDraggingScrollBar;

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

    /// <summary>Change the text and links, and lay out the dialog again.</summary>
    /// <param name="text">The text to show, unless it depends on the checkbox.</param>
    /// <param name="links">The mods whose names to show as links, in the order their lines appear in the text.</param>
    public void SetContent(string text, IReadOnlyList<ModLink> links)
    {
        this.Text = this.GetCheckboxText?.Invoke(this.IsChecked) ?? text;
        if (this.OpenLink != null)
            this.Links = links;
        this.HoveredLink = -1;

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
        if (this.MaxScroll > 0)
        {
            if (this.UpArrow.Contains(x, y) || this.DownArrow.Contains(x, y))
            {
                int step = this.Font.LineSpacing * SyncDialog.ScrollLines;
                this.ScrollTo(this.ScrollOffset + (this.UpArrow.Contains(x, y) ? -step : step));
                Game1.playSound("shwip");
                return;
            }

            if (this.ScrollTrack.Contains(x, y) || this.ScrollBar.Contains(x, y))
            {
                this.IsDraggingScrollBar = true;
                this.DragScrollBar(y);
                return;
            }

            // the content outside the box is hidden
            if (!this.View.Contains(x, y))
                return;
        }

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
    public override void leftClickHeld(int x, int y)
    {
        base.leftClickHeld(x, y);

        if (this.IsDraggingScrollBar)
            this.DragScrollBar(y);
    }

    /// <inheritdoc />
    public override void releaseLeftClick(int x, int y)
    {
        base.releaseLeftClick(x, y);

        this.IsDraggingScrollBar = false;
    }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        this.ScrollTo(this.ScrollOffset - Math.Sign(direction) * this.Font.LineSpacing * SyncDialog.ScrollLines);
    }

    /// <inheritdoc />
    public override void snapCursorToCurrentSnappedComponent()
    {
        // scroll a component selected with a controller into the box
        if (this.currentlySnappedComponent != null && this.MaxScroll > 0)
        {
            Rectangle bounds = this.currentlySnappedComponent.bounds;
            if (bounds.Top < this.View.Top)
                this.ScrollTo(this.ScrollOffset - (this.View.Top - bounds.Top));
            else if (bounds.Bottom > this.View.Bottom)
                this.ScrollTo(this.ScrollOffset + (bounds.Bottom - this.View.Bottom));
        }

        base.snapCursorToCurrentSnappedComponent();
    }

    /// <inheritdoc />
    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);

        this.HoveredLink = this.MaxScroll == 0 || this.View.Contains(x, y)
            ? this.GetLinkAt(x, y)
            : -1;
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
        Game1.drawDialogueBox(this.Box.X, this.Box.Y, this.Box.Width, this.Box.Height, speaker: false, drawOnlyBox: true);

        // only show the part of the content within the box
        Rectangle oldClip = b.GraphicsDevice.ScissorRectangle;
        if (this.MaxScroll > 0)
        {
            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, SyncDialog.ClipState);
            b.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(this.View, b.GraphicsDevice.Viewport.Bounds);
        }

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

        if (this.MaxScroll > 0)
        {
            b.End();
            b.GraphicsDevice.ScissorRectangle = oldClip;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

            // draw the scroll bar
            b.Draw(Game1.mouseCursors, this.UpArrow, SyncDialog.UpArrowSource, Color.White);
            b.Draw(Game1.mouseCursors, this.DownArrow, SyncDialog.DownArrowSource, Color.White);
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, SyncDialog.ScrollTrackSource, this.ScrollTrack.X, this.ScrollTrack.Y, this.ScrollTrack.Width, this.ScrollTrack.Height, Color.White, SyncDialog.ArrowScale, drawShadow: false);
            b.Draw(Game1.mouseCursors, this.ScrollBar, SyncDialog.ScrollBarSource, Color.White);
        }

        // show where the hovered link goes
        if (this.HoveredLink >= 0 && this.Links[this.HoveredLink].Url != "")
            IClickableMenu.drawHoverText(b, this.Links[this.HoveredLink].Url, Game1.smallFont);

        this.drawMouse(b);
    }

    /// <summary>The lowest pixel position for the box, above the title screen's back button.</summary>
    private static int MaxBoxBottom => Game1.uiViewport.Height - SyncDialog.ScreenMargin - SyncDialog.BackButtonHeight;

    /// <summary>The position of the top-left of the text.</summary>
    private Vector2 TextPosition => new(this.xPositionOnScreen + IClickableMenu.borderWidth, this.yPositionOnScreen + IClickableMenu.spaceToClearTopBorder + IClickableMenu.borderWidth / 2);

    /// <summary>Size and position the dialog and everything in it, using the smaller font if it doesn't fit on the screen with the normal one, and scrolling the content within the box if it still doesn't fit.</summary>
    private void LayOut()
    {
        this.LayOut(Game1.dialogueFont);
        if (this.MaxScroll > 0)
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

        // if the content doesn't fit above the back button, the box fills that space and the content scrolls within it
        int maxBoxHeight = SyncDialog.MaxBoxBottom - SyncDialog.ScreenMargin;
        this.MaxScroll = Math.Max(0, this.height - SyncDialog.BoxTopOffset - maxBoxHeight);
        this.ScrollOffset = Math.Clamp(this.ScrollOffset, 0, this.MaxScroll);

        // center the box the game draws, which starts below the dialog's position
        this.xPositionOnScreen = Game1.uiViewport.Width / 2 - this.width / 2;
        if (this.MaxScroll > 0)
        {
            int boxY = SyncDialog.ScreenMargin - SyncDialog.BoxTopOffset;
            this.Box = new Rectangle(this.xPositionOnScreen, boxY, this.width, maxBoxHeight + SyncDialog.BoxTopOffset + SyncDialog.BoxBottomOffset);
            this.yPositionOnScreen = boxY - this.ScrollOffset;
        }
        else
        {
            this.yPositionOnScreen = (Game1.uiViewport.Height - this.height - SyncDialog.BoxTopOffset) / 2;
            this.Box = new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height);
        }
        int boxTop = this.Box.Y + SyncDialog.BoxTopOffset;
        int boxBottom = this.Box.Bottom - SyncDialog.BoxBottomOffset;
        this.View = new Rectangle(this.Box.X + SyncDialog.BoxBorder, boxTop + SyncDialog.BoxBorder, this.Box.Width - SyncDialog.BoxBorder * 2, boxBottom - boxTop - SyncDialog.BoxBorder * 2);

        // place the scroll bar right of the box
        int arrowWidth = SyncDialog.UpArrowSource.Width * SyncDialog.ArrowScale;
        int arrowHeight = SyncDialog.UpArrowSource.Height * SyncDialog.ArrowScale;
        int arrowX = Math.Min(this.Box.Right + SyncDialog.ScreenMargin, Game1.uiViewport.Width - SyncDialog.CornerButtonWidth - SyncDialog.ScreenMargin / 2 - arrowWidth);
        this.UpArrow = new Rectangle(arrowX, boxTop, arrowWidth, arrowHeight);
        this.DownArrow = new Rectangle(arrowX, boxBottom - arrowHeight, arrowWidth, arrowHeight);
        int barWidth = SyncDialog.ScrollBarSource.Width * SyncDialog.ArrowScale;
        int barHeight = SyncDialog.ScrollBarSource.Height * SyncDialog.ArrowScale;
        int trackX = arrowX + (arrowWidth - barWidth) / 2;
        this.ScrollTrack = new Rectangle(trackX, this.UpArrow.Bottom + 4, barWidth, this.DownArrow.Top - 4 - (this.UpArrow.Bottom + 4));
        int barY = this.ScrollTrack.Y + (this.MaxScroll > 0 ? (this.ScrollTrack.Height - barHeight) * this.ScrollOffset / this.MaxScroll : 0);
        this.ScrollBar = new Rectangle(trackX, barY, barWidth, barHeight);

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

    /// <summary>Scroll so the scroll bar is centered on a position, as far as it can be.</summary>
    /// <param name="y">The Y pixel position.</param>
    private void DragScrollBar(int y)
    {
        int range = this.ScrollTrack.Height - this.ScrollBar.Height;
        if (range <= 0)
            return;

        int barTop = Math.Clamp(y - this.ScrollBar.Height / 2 - this.ScrollTrack.Y, 0, range);
        this.ScrollTo((int)Math.Round(barTop * (double)this.MaxScroll / range));
    }

    /// <summary>Scroll the content within the box.</summary>
    /// <param name="offset">How many pixels to scroll down from the top.</param>
    private void ScrollTo(int offset)
    {
        offset = Math.Clamp(offset, 0, this.MaxScroll);
        if (offset == this.ScrollOffset)
            return;

        this.ScrollOffset = offset;
        int? snappedId = this.currentlySnappedComponent?.myID;
        this.LayOut(this.Font);

        // the checkbox and links were replaced, so select the new ones
        if (Game1.options.SnappyMenus)
        {
            this.populateClickableComponentList();
            if (snappedId != null)
                this.currentlySnappedComponent = this.getComponentWithID(snappedId.Value);
        }
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
