using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Toont het resultaat van een handmatige simulatie, inclusief bestandsnamen.
/// De namen bestaan alleen in dit venster; ze worden nergens opgeslagen.
/// </summary>
internal sealed class SimulationResultForm : Form
{
    public SimulationResultForm(RunReport report, AppConfig config)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiFont.Normal;
        Text = "Simulatie – er is niets verwijderd";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(700, 450);
        MinimizeBox = false;
        ShowInTaskbar = false;

        string NameOf(string id) => config.Folders.FirstOrDefault(f => f.Id == id)?.DisplayName ?? id;

        var summary = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            Padding = new Padding(0, 0, 0, 8),
            Text =
                $"In aanmerking voor verwijdering: {report.Candidates} bestand(en), samen {Format.Bytes(report.CandidateBytes)}.\n" +
                $"Overgeslagen: {report.Skipped}. Nog te recent: {report.TooYoung}. Fouten: {report.Errors}.\n" +
                (report.Candidates > config.MaxFilesPerRun
                    ? $"Let op: dit is meer dan het maximum van {config.MaxFilesPerRun} per keer; bij echt opruimen wordt dan uit voorzorg niets verwijderd.\n"
                    : "") +
                (report.RecycleBin is { } bin
                    ? $"Prullenbak (items ouder dan {bin.RetentionDays} dagen): {bin.Candidates} item(s), samen {Format.Bytes(bin.CandidateBytes)}. Nog te recent: {bin.TooYoung}.\n"
                    : "") +
                (report.Messages.Count > 0 ? string.Join("\n", report.Messages) + "\n" : "") +
                "Bestandsnamen worden alleen in dit venster getoond en niet opgeslagen in logbestanden.",
        };

        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        list.Columns.Add("Map", 140);
        list.Columns.Add("Bestand", 360);
        list.Columns.Add("Grootte", 90, HorizontalAlignment.Right);
        list.Columns.Add("Wat gebeurt er", 260);
        // Kolombreedtes staan in pixels bij 100% en schalen niet automatisch mee met de Windows-schaal (DPI).
        Load += (_, _) => { foreach (ColumnHeader col in list.Columns) col.Width = UiFont.Px(col.Width); };

        list.BeginUpdate();
        foreach (var folder in report.Folders)
        {
            string name = NameOf(folder.Id);
            if (folder.Status is "Geblokkeerd" or "Fout" or "Uitgeschakeld")
            {
                var item = new ListViewItem(new[] { name, "(hele map)", "", folder.Status == "Uitgeschakeld" ? "Map staat uit" : folder.Message })
                {
                    ForeColor = folder.Status == "Uitgeschakeld" ? SystemColors.GrayText : Color.Firebrick,
                };
                list.Items.Add(item);
                continue;
            }
            var details = report.FileDetails?.FirstOrDefault(d => d.Id == folder.Id);
            if (details is null) continue;
            foreach (var c in details.Candidates)
                list.Items.Add(new ListViewItem(new[] { name, c.Name, Format.Bytes(c.Size), "Komt in aanmerking voor verwijdering" }));
            foreach (var s in details.Skipped)
                list.Items.Add(new ListViewItem(new[] { name, s.Name, "", "Overgeslagen: " + s.Reason }) { ForeColor = SystemColors.GrayText });
            if (folder.TooYoung > 0)
                list.Items.Add(new ListViewItem(new[] { name, $"({folder.TooYoung} recente bestanden)", "", "Blijven staan: jonger dan de bewaartermijn" })
                    { ForeColor = SystemColors.GrayText });
            if (folder.Message.Length > 0)
                list.Items.Add(new ListViewItem(new[] { name, "", "", folder.Message }) { ForeColor = Color.DarkOrange });
        }
        if (report.RecycleBin is { } rb)
        {
            foreach (var c in report.RecycleBinDetails ?? new())
                list.Items.Add(new ListViewItem(new[] { "Prullenbak", c.Name, Format.Bytes(c.Size), "Wordt definitief uit de Prullenbak verwijderd" }));
            if (rb.TooYoung > 0)
                list.Items.Add(new ListViewItem(new[] { "Prullenbak", $"({rb.TooYoung} recente items)", "", "Blijven staan: korter in de Prullenbak dan de termijn" })
                    { ForeColor = SystemColors.GrayText });
            if (rb.Message.Length > 0)
                list.Items.Add(new ListViewItem(new[] { "Prullenbak", "", "", rb.Message }) { ForeColor = Color.DarkOrange });
        }
        list.EndUpdate();

        var close = new Button { Text = "Sluiten", AutoSize = true, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        AcceptButton = close;
        CancelButton = close;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(list, 0, 1);
        root.Controls.Add(close, 0, 2);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();
    }
}
