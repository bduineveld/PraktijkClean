using System.Drawing;
using System.Windows.Forms;

namespace PraktijkClean;

/// <summary>Eenvoudig venster met uitleg en één invoerveld (bijv. voor een extensie of patroon).</summary>
internal sealed class InputDialog : Form
{
    private readonly TextBox _text = new() { Width = 420, Anchor = AnchorStyles.Left | AnchorStyles.Right };

    public string Value => _text.Text;

    private InputDialog(string title, string explanation)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiFont.Normal;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var label = new Label { Text = explanation, AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(3, 3, 3, 8) };
        var ok = new Button { Text = "Toevoegen", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annuleren", AutoSize = true, DialogResult = DialogResult.Cancel };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12) };
        root.Controls.Add(label, 0, 0);
        root.Controls.Add(_text, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>Toont het venster; geeft de ingevoerde tekst of null bij Annuleren.</summary>
    public static string? Ask(IWin32Window owner, string title, string explanation)
    {
        using var dialog = new InputDialog(title, explanation);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Value : null;
    }
}
