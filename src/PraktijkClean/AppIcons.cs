using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PraktijkClean;

/// <summary>
/// De iconen van PraktijkClean (bron: map icon\ in de projectmap).
/// <list type="bullet">
/// <item>App-icoon: icon\PraktijkClean.ico, ingebakken in de exe (Startmenu, taakbalk, Instellingen &gt; Apps) en
/// als resource voor de titelbalk van de vensters.</item>
/// <item>Systeemvak: hetzelfde beeldscherm met bezem, zonder schildje, met een statusbolletje
/// linksonder (kleur + wit teken, zodat het ook zonder kleurenzien herkenbaar is).</item>
/// </list>
/// </summary>
internal static class AppIcons
{
    /// <summary>Formaten waarin het systeemvak-icoon vooraf is verkleind (Resources\tray-N.png).</summary>
    private static readonly int[] TraySizes = { 16, 20, 24, 28, 32, 40, 48, 64 };

    private static Icon? _app;

    /// <summary>Het app-icoon met alle formaten (16-256), of null als het niet geladen kan worden.</summary>
    public static Icon? App
    {
        get
        {
            if (_app is not null) return _app;
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PraktijkClean.app.ico");
                if (stream is not null) _app = new Icon(stream);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException) { }
            return _app;
        }
    }

    /// <summary>Systeemvak-icoon voor deze status, precies op <paramref name="size"/> pixels.</summary>
    public static Bitmap Tray(StatusKind kind, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var basis = LoadTrayBase(size))
            if (basis is not null) g.DrawImage(basis, new Rectangle(0, 0, size, size));

        // Statusbolletje linksonder (rechts staat de bezem, het herkenbaarste deel), met een witte rand zodat
        // het loskomt van het beeldscherm.
        float d = (float)Math.Round(size * 0.5f);
        float ring = Math.Max(1f, size / 16f);
        var outer = new RectangleF(0, size - d, d, d);
        var inner = RectangleF.Inflate(outer, -ring, -ring);
        using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, outer);
        using (var fill = new SolidBrush(StatusInfo.IconColor(kind))) g.FillEllipse(fill, inner);

        using var mark = new Pen(Color.White, Math.Max(1.2f, d / 7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float x = inner.X, y = inner.Y, s = inner.Width;
        PointF P(float fx, float fy) => new(x + s * fx, y + s * fy);
        switch (kind)
        {
            case StatusKind.On:          // vinkje
                g.DrawLines(mark, new[] { P(.27f, .52f), P(.44f, .69f), P(.74f, .34f) });
                break;
            case StatusKind.Paused:      // twee streepjes
                g.DrawLine(mark, P(.38f, .30f), P(.38f, .70f));
                g.DrawLine(mark, P(.62f, .30f), P(.62f, .70f));
                break;
            case StatusKind.Problem:     // uitroepteken
                g.DrawLine(mark, P(.5f, .24f), P(.5f, .56f));
                g.DrawLine(mark, P(.5f, .74f), P(.5f, .76f));
                break;
            case StatusKind.Simulation:  // open rondje
                g.DrawEllipse(mark, x + s * .32f, y + s * .32f, s * .36f, s * .36f);
                break;
            default:                     // streepje
                g.DrawLine(mark, P(.28f, .5f), P(.72f, .5f));
                break;
        }
        return bmp;
    }

    /// <summary>Het vooraf verkleinde beeld dat het best past (exact, anders het eerstvolgende grotere).</summary>
    private static Bitmap? LoadTrayBase(int size)
    {
        int pick = TraySizes.FirstOrDefault(n => n >= size);
        if (pick == 0) pick = TraySizes[TraySizes.Length - 1];
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"PraktijkClean.tray-{pick}.png");
            return stream is null ? null : new Bitmap(stream);
        }
        catch (ArgumentException) { return null; }
    }
}
