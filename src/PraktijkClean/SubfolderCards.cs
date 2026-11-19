using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Keuze voor submappen als drie kaarten naast elkaar (Licht, Midden, Zwaar). Elke kaart laat met een klein
/// voorbeeld zien wat er gebeurt: wat rood doorgestreept is, wordt opgeruimd. Een sterktemeter (1-3 streepjes)
/// laat zien hoe grondig. Werkt met muis en toetsenbord (pijltjes, spatie), als een groep keuzerondjes.
/// </summary>
internal sealed class SubfolderCards : Control
{
    private static readonly string[] Titles = { "Alleen deze map", "Ook submappen", "Ook lege submappen" };
    private static readonly string[] Tags = { "Licht", "Midden", "Zwaar" };

    private int _selected;
    private int _hover = -1;
    private int _focusIndex;

    /// <summary>Voordat de keuze verandert; zet Cancel om de oude keuze te houden.</summary>
    public event EventHandler<LevelChangingEventArgs>? LevelChanging;
    public event EventHandler? LevelChanged;

    public SubfolderCards()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.Grouping;
        AccessibleName = "Submappen";
        Margin = new Padding(3, 3, 3, 3);
        UpdateHeight();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Level
    {
        get => SubfolderLevels.All[_selected].Level;
        set
        {
            int i = SubfolderLevels.IndexOf(value);
            if (i == _selected) return;
            _selected = _focusIndex = i;
            Invalidate();
            AccessibleDescription = Titles[i];
        }
    }

    private string _rootName = "Bureaublad";

    /// <summary>Naam van de map bovenaan het voorbeeldboompje (de gekozen map zelf).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RootName
    {
        get => _rootName;
        set { value = string.IsNullOrWhiteSpace(value) ? "Bureaublad" : value; if (value == _rootName) return; _rootName = value; Invalidate(); }
    }

    /// <summary>Kiest een kaart zoals de gebruiker dat doet (met LevelChanging/LevelChanged).</summary>
    private void Choose(int index)
    {
        _focusIndex = index;
        if (index == _selected || !Enabled) { Invalidate(); return; }
        var args = new LevelChangingEventArgs(SubfolderLevels.All[_selected].Level, SubfolderLevels.All[index].Level);
        LevelChanging?.Invoke(this, args);
        if (args.Cancel) { Invalidate(); return; }
        _selected = index;
        AccessibleDescription = Titles[index];
        Invalidate();
        LevelChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------------------------ maten

    private int Gap => UiFont.Px(10);
    private int Pad => UiFont.Px(12);
    private int LineH => (int)Math.Ceiling(Font.GetHeight()) + UiFont.Px(3);

    private int CardHeight => Pad + LineH + LineH + UiFont.Px(6) + 4 * LineH + Pad;

    private void UpdateHeight()
    {
        int h = CardHeight + UiFont.Px(4);
        if (Height != h) Height = h;
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateHeight(); Invalidate(); }

    // De hoogte volgt uit de letter (al op schermschaal); niet nog eens laten meeschalen door het venster.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified) =>
        base.ScaleControl(factor, specified & ~BoundsSpecified.Height);

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateHeight(); }

    public override Size GetPreferredSize(Size proposedSize) => new(UiFont.Px(3 * 175 + 2 * 10), CardHeight + UiFont.Px(4));

    private Rectangle CardRect(int i)
    {
        int w = (Width - 2 * Gap - 1) / 3;
        return new Rectangle(i * (w + Gap), UiFont.Px(2), w, CardHeight);
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < 3; i++) if (CardRect(i).Contains(p)) return i;
        return -1;
    }

    // ------------------------------------------------------------------------------------ muis en toetsen

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Cursor = h >= 0 && Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        int h = HitTest(e.Location);
        if (h >= 0) Choose(h);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Up && _selected > 0) { Choose(_selected - 1); e.Handled = true; }
        else if (e.KeyCode is Keys.Right or Keys.Down && _selected < 2) { Choose(_selected + 1); e.Handled = true; }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    // ------------------------------------------------------------------------------------ tekenen

    private static readonly Color Removed = Color.FromArgb(196, 43, 28);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 0; i < 3; i++) DrawCard(g, i);
    }

    private void DrawCard(Graphics g, int i)
    {
        var r = CardRect(i);
        bool selected = i == _selected, hover = i == _hover && Enabled;
        var accent = SystemColors.Highlight;
        Color text = Enabled ? SystemColors.WindowText : SystemColors.GrayText;
        Color muted = SystemColors.GrayText;

        // Kaart: wit, afgeronde hoeken; gekozen kaart met een dikke rand in de accentkleur.
        int radius = UiFont.Px(8);
        using (var path = RoundRect(r, radius))
        {
            using var fill = new SolidBrush(SystemColors.Window);
            g.FillPath(fill, path);
            float bw = selected ? UiFont.Px(2) : Math.Max(1, UiFont.Px(1));
            var border = selected ? (Enabled ? accent : SystemColors.GrayText)
                : hover ? Color.FromArgb(140, 140, 140) : Color.FromArgb(214, 214, 214);
            using var pen = new Pen(border, bw) { Alignment = PenAlignment.Inset };
            g.DrawPath(pen, path);
        }
        if (Focused && i == _focusIndex && ShowFocusCues)
        {
            var fr = Rectangle.Inflate(r, -UiFont.Px(4), -UiFont.Px(4));
            ControlPaint.DrawFocusRectangle(g, fr);
        }

        int x = r.Left + Pad, y = r.Top + Pad, w = r.Width - 2 * Pad;
        var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        // Kop: titel over de volle breedte; daaronder Licht/Midden/Zwaar met rechts de sterktemeter.
        int barW = UiFont.Px(12), barH = UiFont.Px(5), barGap = UiFont.Px(3);
        int barsW = 3 * barW + 2 * barGap;
        using (var bold = new Font(Font, FontStyle.Bold))
            TextRenderer.DrawText(g, Titles[i], bold, new Rectangle(x, y, w, LineH), text, flags);
        y += LineH;
        for (int b = 0; b < 3; b++)
        {
            var br = new Rectangle(x + w - barsW + b * (barW + barGap), y + (LineH - barH) / 2, barW, barH);
            using var brush = new SolidBrush(b <= i ? (Enabled ? accent : SystemColors.GrayText) : Color.FromArgb(218, 218, 218));
            using var bp = RoundRect(br, UiFont.Px(2));
            g.FillPath(brush, bp);
        }
        TextRenderer.DrawText(g, Tags[i], Font, new Rectangle(x, y, w - barsW - UiFont.Px(6), LineH), muted, flags);
        y += LineH + UiFont.Px(6);

        // Voorbeeld: een map met een oud bestand en een submap Scans met een oud bestand.
        int indent = UiFont.Px(16);
        DrawItem(g, x, y, w, folder: true, _rootName, text, strike: false);
        y += LineH;
        DrawItem(g, x + indent, y, w - indent, folder: false, "oud.pdf", Removed, strike: true);
        y += LineH;
        // Licht: de submap en alles erin blijft staan (grijs); Midden: oud bestand erin weg; Zwaar: daarna ook de lege submap.
        DrawItem(g, x + indent, y, w - indent, folder: true, "Scans", i == 2 ? Removed : text, strike: i == 2);
        y += LineH;
        DrawItem(g, x + 2 * indent, y, w - 2 * indent, folder: false, "oud.jpg", i == 0 ? muted : Removed, strike: i > 0);
    }

    private void DrawItem(Graphics g, int x, int y, int w, bool folder, string name, Color color, bool strike)
    {
        int s = UiFont.Px(12);
        var icon = new Rectangle(x, y + (LineH - s) / 2, s, s);
        var iconColor = strike ? color : SystemColors.GrayText;
        using (var pen = new Pen(iconColor, Math.Max(1f, UiFont.Scale)))
        {
            if (folder)
            {
                // Mapje: tabje linksboven en een rechthoek.
                int tab = s / 3;
                var pts = new[]
                {
                    new Point(icon.Left, icon.Top + tab / 2), new Point(icon.Left + s * 2 / 5, icon.Top + tab / 2),
                    new Point(icon.Left + s / 2, icon.Top + tab), new Point(icon.Right, icon.Top + tab),
                    new Point(icon.Right, icon.Bottom - 1), new Point(icon.Left, icon.Bottom - 1),
                };
                g.DrawPolygon(pen, pts);
            }
            else
            {
                // Bestand: rechthoek met omgevouwen hoekje.
                int l = icon.Left + s / 6, r = icon.Right - s / 6, fold = s / 3;
                var pts = new[]
                {
                    new Point(l, icon.Top), new Point(r - fold, icon.Top), new Point(r, icon.Top + fold),
                    new Point(r, icon.Bottom - 1), new Point(l, icon.Bottom - 1),
                };
                g.DrawPolygon(pen, pts);
                g.DrawLine(pen, r - fold, icon.Top, r - fold, icon.Top + fold);
                g.DrawLine(pen, r - fold, icon.Top + fold, r, icon.Top + fold);
            }
        }
        using var font = strike ? new Font(Font, FontStyle.Strikeout) : null;
        TextRenderer.DrawText(g, name, font ?? Font, new Rectangle(x + s + UiFont.Px(6), y, Math.Max(0, w - s - UiFont.Px(6)), LineH),
            color, TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(2 * radius, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

internal sealed class LevelChangingEventArgs : CancelEventArgs
{
    public LevelChangingEventArgs(string from, string to) { From = from; To = to; }
    public string From { get; }
    public string To { get; }
}
