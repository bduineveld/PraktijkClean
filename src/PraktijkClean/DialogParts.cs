using System.Drawing;
using System.Windows.Forms;

namespace PraktijkClean;

/// <summary>Bouwstenen voor vensters in dezelfde stijl als <see cref="ConfirmDialog"/>: witte inhoud, grijze knoppenbalk.</summary>
internal static class DialogParts
{
    public static readonly Color BarColor = Color.FromArgb(243, 243, 243);
    public static readonly Color HeadingColor = Color.FromArgb(0, 51, 153);

    public static void SetupForm(Form f, string title)
    {
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.Font = UiFont.Normal;
        f.Text = title;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.StartPosition = FormStartPosition.CenterParent;
        f.MinimizeBox = false;
        f.MaximizeBox = false;
        f.ShowInTaskbar = false;
        f.BackColor = Color.White;
        if (AppIcons.App is { } icon) f.Icon = icon;
    }

    public static Label Heading(string text, float size = 12F) => new()
    {
        Text = text, AutoSize = true, Font = new Font("Segoe UI Semibold", size, FontStyle.Bold), ForeColor = HeadingColor,
        Margin = new Padding(0, 0, 0, 6),
    };

    public static Label Text(string text, int maxWidth, bool gray = false, int top = 0, int bottom = 6) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(maxWidth, 0), Margin = new Padding(0, top, 0, bottom),
        ForeColor = gray ? SystemColors.GrayText : SystemColors.WindowText,
    };

    public static Label Section(string text, int top = 12) => new()
    {
        Text = text, AutoSize = true, Font = new Font(UiFont.Normal, FontStyle.Bold), Margin = new Padding(0, top, 0, 4),
    };

    public static Button Button(string text, bool main = false) => new()
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(100, 30),
        Padding = new Padding(10, 1, 10, 1), Margin = new Padding(8, 0, 0, 0), UseVisualStyleBackColor = true,
        Font = main ? new Font(UiFont.Normal, FontStyle.Bold) : UiFont.Normal,
    };

    /// <summary>Kolom met regels onder elkaar (één kolom, automatische hoogte).</summary>
    public static TableLayoutPanel Column(Padding padding)
    {
        var t = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = padding, Margin = Padding.Empty,
            BackColor = Color.White,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    public static void Add(TableLayoutPanel t, Control c, bool stretch = false)
    {
        t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
        t.Controls.Add(c, 0, t.RowCount - 1);
    }

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, BackColor = Color.White };
        f.Controls.AddRange(controls);
        return f;
    }

    /// <summary>Grijze balk onderaan met links optionele knoppen en rechts de hoofdknoppen.</summary>
    public static TableLayoutPanel ButtonBar(Control[] left, Control[] right)
    {
        var bar = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1, Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(22, 12, 18, 12), BackColor = BarColor,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var l = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        foreach (var c in left) { c.Margin = new Padding(0, 0, 8, 0); l.Controls.Add(c); }
        var r = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
        r.Controls.AddRange(right);
        bar.Controls.Add(l, 0, 0);
        bar.Controls.Add(r, 1, 0);
        return bar;
    }
}
