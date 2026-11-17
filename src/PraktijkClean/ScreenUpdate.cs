using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PraktijkClean;

/// <summary>
/// Schermupdates in één keer: zonder dit herberekent Windows Forms bij elk gewijzigd tekstje (AutoSize) de indeling
/// van het hele venster en tekent het tussenstanden, wat op een trage pc of bij 200 % schermschaal seconden kost.
/// </summary>
internal static class ScreenUpdate
{
    private const int WM_SETREDRAW = 0x000B;
    private const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100, RDW_FRAME = 0x400;

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);

    /// <summary>Tekenen van <paramref name="control"/> (en alles erin) tijdelijk uitzetten.</summary>
    public static void StopDrawing(Control control)
    {
        if (control.IsHandleCreated) SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Tekenen weer aanzetten en alles in één keer opnieuw tekenen.</summary>
    public static void ResumeDrawing(Control control)
    {
        if (!control.IsHandleCreated) return;
        SendMessage(control.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
        RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_FRAME | RDW_UPDATENOW);
    }

    /// <summary>
    /// Voert <paramref name="action"/> uit met de indeling van alle panelen opgeschort en het tekenen uit; daarna één
    /// indelingsronde (binnenste panelen eerst) en één keer tekenen. Geneste aanroepen doen gewoon mee.
    /// </summary>
    public static void Batch(Control root, Action action)
    {
        if (_depth > 0 || !root.IsHandleCreated) { action(); return; }
        _depth++;
        var containers = new List<Control>();
        Collect(root, containers);
        StopDrawing(root);
        foreach (var c in containers) c.SuspendLayout();
        try { action(); }
        finally
        {
            for (int i = containers.Count - 1; i >= 0; i--) containers[i].ResumeLayout(true);
            _depth--;
            ResumeDrawing(root);
        }
    }

    [ThreadStatic] private static int _depth;

    private static void Collect(Control c, List<Control> into)
    {
        if (c.Controls.Count == 0) return;
        into.Add(c);
        foreach (Control child in c.Controls) Collect(child, into);
    }
}
