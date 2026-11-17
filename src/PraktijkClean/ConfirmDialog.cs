using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PraktijkClean;

internal enum ConfirmIcon { Info, Warning }

/// <summary>
/// Vraagvenster in de stijl van Windows 11: een korte vraag als kop, daaronder de uitleg, en knoppen die zeggen
/// wat er gebeurt ("Activeren", "Map toevoegen", "Annuleren") in plaats van Ja/Nee.
/// De eerste knop is de hoofdactie (vet); Esc en het kruisje kiezen altijd de veilige knop.
/// </summary>
internal sealed class ConfirmDialog : Form
{
    public const string Cancel = "Annuleren";

    private int _choice;

    private ConfirmDialog(bool hasOwner, string heading, string body, ConfirmIcon icon, int defaultIndex, int cancelIndex, string[] buttons)
    {
        _choice = cancelIndex;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiFont.Normal;
        Text = Texts.AppTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = hasOwner ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = !hasOwner;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.White;

        // Bovenste deel: icoon, vraag, uitleg.
        var picture = new PictureBox
        {
            Image = LoadIcon(icon), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(32, 32),
            Margin = new Padding(0, 2, 14, 0),
        };
        var lblHeading = new Label
        {
            Text = heading, AutoSize = true, MaximumSize = new Size(500, 0),
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 51, 153),
            Margin = new Padding(0, 0, 0, body.Length > 0 ? 10 : 0),
        };
        var lblBody = new Label { Text = body, AutoSize = true, MaximumSize = new Size(500, 0), Margin = Padding.Empty, Visible = body.Length > 0 };

        var top = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Padding = new Padding(22, 20, 26, 22), BackColor = Color.White,
        };
        top.Controls.Add(picture, 0, 0);
        top.SetRowSpan(picture, 2);
        top.Controls.Add(lblHeading, 1, 0);
        top.Controls.Add(lblBody, 1, 1);

        // Onderste balk met knoppen, rechts uitgelijnd, in de opgegeven volgorde.
        var bar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right, Margin = Padding.Empty, WrapContents = false,
        };
        Button? defaultButton = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var b = new Button
            {
                Text = buttons[i], AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(100, 30), Padding = new Padding(10, 1, 10, 1),
                Margin = new Padding(i == 0 ? 0 : 8, 0, 0, 0), UseVisualStyleBackColor = true,
            };
            if (i == 0) b.Font = new Font(UiFont.Normal, FontStyle.Bold);
            b.Click += (_, _) => { _choice = index; DialogResult = DialogResult.OK; Close(); };
            bar.Controls.Add(b);
            if (i == defaultIndex) defaultButton = b;
            if (i == cancelIndex) CancelButton = b;
        }
        var bottom = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            Padding = new Padding(22, 12, 18, 12), BackColor = Color.FromArgb(243, 243, 243),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.Controls.Add(bar, 0, 0);

        var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty };
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(bottom, 0, 1);
        Controls.Add(root);

        if (defaultButton is not null)
        {
            AcceptButton = defaultButton;
            Shown += (_, _) => defaultButton.Focus();
        }
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// Toont de vraag en geeft het nummer van de gekozen knop (0 = eerste). Esc of het kruisje geeft <paramref name="cancelIndex"/>.
    /// </summary>
    public static int Show(IWin32Window? owner, string heading, string body, ConfirmIcon icon,
                           int defaultIndex, int cancelIndex, params string[] buttons)
    {
        using var dialog = new ConfirmDialog(owner is not null, heading, body, icon, defaultIndex, cancelIndex, buttons);
        if (owner is null) dialog.ShowDialog(); else dialog.ShowDialog(owner);
        return dialog._choice;
    }

    /// <summary>
    /// Twee knoppen: de actie en een veilige knop (standaard "Annuleren"). Enter kiest de veilige knop,
    /// tenzij <paramref name="acceptIsDefault"/>.
    /// </summary>
    public static bool Ask(IWin32Window? owner, string heading, string body, string accept,
                           ConfirmIcon icon = ConfirmIcon.Info, string cancel = Cancel, bool acceptIsDefault = false) =>
        Show(owner, heading, body, icon, acceptIsDefault ? 0 : 1, 1, accept, cancel) == 0;

    // ------------------------------------------------------------------------------------
    // Icoon van Windows zelf, scherp op elke schermschaal.

    private static Image LoadIcon(ConfirmIcon icon)
    {
        const uint SIID_WARNING = 78, SIID_INFO = 79, SHGSI_ICON = 0x100, SHGSI_LARGEICON = 0;
        try
        {
            var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf(typeof(SHSTOCKICONINFO)) };
            if (SHGetStockIconInfo(icon == ConfirmIcon.Warning ? SIID_WARNING : SIID_INFO, SHGSI_ICON | SHGSI_LARGEICON, ref info) == 0 &&
                info.hIcon != IntPtr.Zero)
            {
                try
                {
                    using var i = Icon.FromHandle(info.hIcon);
                    return i.ToBitmap();
                }
                finally { DestroyIcon(info.hIcon); }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException) { }
        return (icon == ConfirmIcon.Warning ? SystemIcons.Warning : SystemIcons.Information).ToBitmap();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
