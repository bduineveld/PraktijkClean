using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PraktijkClean;

/// <summary>
/// Plaatje van de rechteronderhoek van het scherm (taakbalk met systeemvak en klok), zodat duidelijk is waar het
/// icoon van PraktijkClean komt te staan en wat de kleuren betekenen. Met <see cref="IconVisible"/> uit staat het
/// icoon er niet in, zodat het plaatje meteen laat zien wat het vinkje doet.
/// </summary>
internal sealed class TrayPreview : Control
{
    private const int LogicalWidth = 480, LogicalHeight = 156;
    private bool _iconVisible = true;

    public TrayPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "Voorbeeld: het icoon van PraktijkClean rechtsonder in het systeemvak, naast de klok";
        Size = new Size(UiFont.Px(LogicalWidth), UiFont.Px(LogicalHeight));
        Margin = new Padding(3, 4, 3, 6);
    }

    /// <summary>Of het icoon van PraktijkClean in het plaatje staat.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IconVisible
    {
        get => _iconVisible;
        set { if (_iconVisible == value) return; _iconVisible = value; Invalidate(); }
    }

    // Vaste maat in schermpixels (UiFont.Px): niet laten meeschalen of krimpen door het venster of de tabel.
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, UiFont.Px(LogicalWidth), UiFont.Px(LogicalHeight), specified);

    public override Size GetPreferredSize(Size proposedSize) => new(UiFont.Px(LogicalWidth), UiFont.Px(LogicalHeight));

    private static int P(int v) => UiFont.Px(v);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = Parent?.BackColor ?? SystemColors.Control;
        g.Clear(bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // ---- Taakbalk (lichte Windows 11-stijl), rechts uitgelijnd, links vervagend
        var bar = new Rectangle(0, P(62), Width - 1, P(46));
        using (var path = RoundRect(bar, P(8)))
        {
            using var fill = new SolidBrush(Color.FromArgb(238, 241, 246));
            g.FillPath(fill, path);
            using var pen = new Pen(Color.FromArgb(214, 218, 224), 1);
            g.DrawPath(pen, path);
        }
        // Een paar app-knoppen links, om het als taakbalk herkenbaar te maken.
        for (int i = 0; i < 4; i++)
        {
            var app = new Rectangle(P(14 + i * 40), bar.Top + P(9), P(28), P(28));
            using var ap = RoundRect(app, P(6));
            using var ab = new SolidBrush(Color.FromArgb(i == 1 ? 196 : 212, i == 1 ? 214 : 218, i == 1 ? 236 : 226));
            g.FillPath(ab, ap);
        }
        using (var fade = new LinearGradientBrush(new Rectangle(-1, bar.Top - 1, P(190), bar.Height + 2), bg, Color.FromArgb(0, bg), LinearGradientMode.Horizontal))
            g.FillRectangle(fade, new Rectangle(-1, bar.Top - 1, P(190), bar.Height + 2));

        var dark = Color.FromArgb(32, 32, 32);
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        // Klok (helemaal rechts)
        int right = bar.Right - P(12);
        using (var small = new Font("Segoe UI", 8.25F))
        {
            var clock = new Rectangle(right - P(76), bar.Top + P(6), P(76), bar.Height - P(12));
            TextRenderer.DrawText(g, "12:30", small, new Rectangle(clock.X, clock.Y + P(2), clock.Width, clock.Height / 2), dark, flags | TextFormatFlags.Right);
            TextRenderer.DrawText(g, DateTime.Today.ToString("dd-MM-yyyy"), small,
                new Rectangle(clock.X, clock.Y + clock.Height / 2, clock.Width, clock.Height / 2), dark, flags | TextFormatFlags.Right);
            right = clock.Left - P(10);
        }

        // Wifi, geluid, batterij (in één knop, zoals in Windows 11)
        using (var glyphs = new Font("Segoe MDL2 Assets", 10.5F))
        {
            int gw = P(26);
            string[] icons = { "", "", "" };
            int x = right - icons.Length * gw;
            for (int i = 0; i < icons.Length; i++)
                TextRenderer.DrawText(g, icons[i], glyphs, new Rectangle(x + i * gw, bar.Top, gw, bar.Height), dark,
                    flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            right = x - P(8);
        }

        // Het icoon van PraktijkClean, met een accentkader eromheen
        int slot = P(34);
        var iconSlot = new Rectangle(right - slot, bar.Top + (bar.Height - slot) / 2, slot, slot);
        if (_iconVisible)
        {
            using (var sp = RoundRect(iconSlot, P(6)))
            {
                using var sf = new SolidBrush(Color.FromArgb(222, 234, 250));
                g.FillPath(sf, sp);
                using var spen = new Pen(SystemColors.Highlight, Math.Max(1, P(2))) { Alignment = PenAlignment.Inset };
                g.DrawPath(spen, sp);
            }
            int isz = P(20);
            using var icon = AppIcons.Tray(StatusKind.On, isz);
            g.DrawImage(icon, iconSlot.Left + (slot - isz) / 2, iconSlot.Top + (slot - isz) / 2, isz, isz);
            right = iconSlot.Left - P(4);
        }

        // Pijltje omhoog (verborgen icoontjes)
        using (var glyphs = new Font("Segoe MDL2 Assets", 8F))
            TextRenderer.DrawText(g, "", glyphs, new Rectangle(right - P(26), bar.Top, P(26), bar.Height), dark,
                flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // ---- Ballonnetje boven het icoon (wat u ziet als u met de muis over het icoon gaat)
        if (_iconVisible)
        {
            int bw = P(250), bh = P(46);
            int cx = iconSlot.Left + iconSlot.Width / 2;
            int bx = Math.Max(0, Math.Min(Width - bw - 1, cx - bw + P(60)));
            var bubble = new Rectangle(bx, P(4), bw, bh);
            using (var bp = BubblePath(bubble, cx, P(10), P(6)))
            {
                using var bf = new SolidBrush(Color.White);
                g.FillPath(bf, bp);
                using var pen = new Pen(Color.FromArgb(200, 204, 210), 1);
                g.DrawPath(pen, bp);
            }
            int tx = bubble.Left + P(10), tw = bubble.Width - P(20), lh = (bh - P(8)) / 2;
            using (var bold = new Font(UiFont.Normal, FontStyle.Bold))
                TextRenderer.DrawText(g, "PraktijkClean: aan", bold, new Rectangle(tx, bubble.Top + P(4), tw, lh), dark, flags | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "Vandaag 4 bestanden opgeruimd", UiFont.Normal, new Rectangle(tx, bubble.Top + P(4) + lh, tw, lh),
                Color.FromArgb(90, 90, 90), flags | TextFormatFlags.VerticalCenter);
        }
        else
        {
            TextRenderer.DrawText(g, "Het icoon staat uit", UiFont.Normal, new Rectangle(0, P(20), Width, P(24)), SystemColors.GrayText,
                flags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        // ---- Wat de kleuren betekenen
        var legend = new (StatusKind Kind, string Text)[]
        {
            (StatusKind.On, "Aan"), (StatusKind.Simulation, "Simulatie"), (StatusKind.Paused, "Gepauzeerd"), (StatusKind.Problem, "Probleem"),
        };
        int lx = 0, ly = bar.Bottom + P(14), lsz = P(18);
        foreach (var (kind, text) in legend)
        {
            using (var icon = AppIcons.Tray(kind, lsz))
            {
                if (_iconVisible) g.DrawImage(icon, lx, ly, lsz, lsz);
                else ControlPaint.DrawImageDisabled(g, icon, lx, ly, bg);
            }
            var size = TextRenderer.MeasureText(g, text, UiFont.Normal, Size.Empty, flags);
            TextRenderer.DrawText(g, text, UiFont.Normal, new Rectangle(lx + lsz + P(6), ly, size.Width + 2, lsz),
                _iconVisible ? SystemColors.ControlText : SystemColors.GrayText, flags | TextFormatFlags.VerticalCenter);
            lx += lsz + P(6) + size.Width + P(22);
        }
    }

    private static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Afgerond rechthoekje met onderaan een puntje naar <paramref name="tipX"/>.</summary>
    private static GraphicsPath BubblePath(Rectangle r, int tipX, int tip, int radius)
    {
        int d = radius * 2;
        int tx = Math.Max(r.Left + d + tip, Math.Min(r.Right - d - tip, tipX));
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddLine(tx + tip, r.Bottom, tx, r.Bottom + tip);
        p.AddLine(tx, r.Bottom + tip, tx - tip, r.Bottom);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
