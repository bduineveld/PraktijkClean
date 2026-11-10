using System;
using System.Drawing;

namespace PraktijkClean;

/// <summary>Lettertype en schermschaal voor alle vensters.</summary>
internal static class UiFont
{
    /// <summary>Lettertype van Windows 10/11 (.NET Framework gebruikt anders het oude "Microsoft Sans Serif").</summary>
    public static readonly Font Normal = new("Segoe UI", 9F);

    /// <summary>Schermschaal van Windows (1 = 100%, 2 = 200%), voor maten die WinForms niet zelf meeschaalt.</summary>
    public static readonly float Scale = GetScale();

    public static int Px(int logical) => (int)Math.Round(logical * Scale);

    private static float GetScale()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96F;
    }
}
