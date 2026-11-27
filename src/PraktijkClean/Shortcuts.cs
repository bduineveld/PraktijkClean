using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using PraktijkClean.Core;

namespace PraktijkClean;

/// <summary>
/// Snelkoppelingen (Startmenu en bureaublad) en vastmaken aan de taakbalk.
/// <para>
/// Alle snelkoppelingen en het proces zelf krijgen dezelfde AppUserModelID (<see cref="AppId"/>). Daardoor horen het
/// venster, de snelkoppeling in het Startmenu en een vastgemaakt icoon in de taakbalk bij elkaar. Dat is ook nodig om
/// Windows te kunnen vragen de app vast te maken.
/// </para>
/// <para>
/// Vastmaken gebeurt via de officiële Windows-functie (Windows.UI.Shell.TaskbarManager). Windows toont daarbij zelf
/// een vraag die de gebruiker moet bevestigen. Op oudere Windows-versies werkt dat niet; dan legt de app uit hoe het
/// met de rechtermuisknop kan.
/// </para>
/// </summary>
internal static class Shortcuts
{
    public const string AppId = "PraktijkClean.App";
    private const string Description = "Bureaublad en Downloads automatisch opruimen";
    private const string FileName = "PraktijkClean.lnk";

    public static string StartMenuPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), FileName);
    public static string DesktopPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName);

    /// <summary>De map waarin Windows de vastgemaakte taakbalk-iconen als snelkoppeling bewaart.</summary>
    private static string PinnedFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    /// <summary>Zo vroeg mogelijk aanroepen, voordat er een venster is.</summary>
    public static void SetProcessAppId()
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppId); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or COMException) { }
    }

    // ------------------------------------------------------------------------------------ snelkoppelingen

    /// <summary>Startmenu-snelkoppeling: aanmaken of bijwerken (alleen schrijven als er iets afwijkt).</summary>
    public static void EnsureStartMenu(InstallLayout layout) => Ensure(StartMenuPath, layout);

    public static bool HasDesktop(InstallLayout layout) => PointsTo(DesktopPath, layout.AppExePath);

    public static void CreateDesktop(InstallLayout layout) => Ensure(DesktopPath, layout);

    /// <summary>Haalt de snelkoppeling op het bureaublad weg, alleen als die naar PraktijkClean wijst.</summary>
    public static void RemoveDesktop(InstallLayout layout) => DeleteIfOurs(DesktopPath, layout.AppExePath);

    /// <summary>
    /// Bij verwijderen: Startmenu, bureaublad en een vastgemaakt taakbalk-icoon weg (alleen bestanden die naar
    /// de geïnstalleerde PraktijkClean wijzen).
    /// </summary>
    public static void RemoveAll(InstallLayout layout)
    {
        DeleteIfOurs(StartMenuPath, layout.AppExePath);
        DeleteIfOurs(DesktopPath, layout.AppExePath);
        foreach (string lnk in PinnedLinks(layout)) DeleteIfOurs(lnk, layout.AppExePath);
    }

    /// <summary>
    /// Een icoon dat ooit met de hand is vastgemaakt (vóór versie 1.4.2, zonder AppUserModelID) krijgt de nieuwe ID,
    /// zodat het geopende venster niet als tweede icoon naast het vastgemaakte verschijnt.
    /// </summary>
    public static void UpdatePinnedLinks(InstallLayout layout)
    {
        foreach (string lnk in PinnedLinks(layout))
        {
            try { if (ReadAppId(lnk) != AppId) Write(lnk, layout, onlyAppId: true); }
            catch (Exception ex) when (IsExpected(ex)) { }
        }
    }

    private static IEnumerable<string> PinnedLinks(InstallLayout layout)
    {
        string[] files;
        try { files = Directory.Exists(PinnedFolder) ? Directory.GetFiles(PinnedFolder, "*.lnk") : Array.Empty<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (string f in files)
            if (PointsTo(f, layout.AppExePath)) yield return f;
    }

    private static void Ensure(string path, InstallLayout layout)
    {
        try
        {
            if (File.Exists(path) && PointsTo(path, layout.AppExePath) && ReadAppId(path) == AppId) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Write(path, layout, onlyAppId: false);
        }
        catch (Exception ex) when (IsExpected(ex)) { }
    }

    private static void DeleteIfOurs(string path, string exe)
    {
        try { if (File.Exists(path) && PointsTo(path, exe)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsExpected(Exception ex) =>
        ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException or ArgumentException;

    private static bool PointsTo(string lnk, string exe)
    {
        if (!File.Exists(lnk)) return false;
        object? link = null;
        try
        {
            link = new CShellLink();
            ((IPersistFile)link).Load(lnk, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.Length > 0 && AppRegistration.SamePath(sb.ToString(), exe);
        }
        catch (Exception ex) when (IsExpected(ex)) { return false; }
        finally { if (link is not null) Marshal.ReleaseComObject(link); }
    }

    private static string? ReadAppId(string lnk)
    {
        object? link = null;
        try
        {
            link = new CShellLink();
            ((IPersistFile)link).Load(lnk, 0);
            var key = AppUserModelIdKey;
            ((IPropertyStore)link).GetValue(ref key, out var pv);
            try { return pv.vt == VtLpwstr && pv.p1 != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p1) : null; }
            finally { PropVariantClear(ref pv); }
        }
        catch (Exception ex) when (IsExpected(ex)) { return null; }
        finally { if (link is not null) Marshal.ReleaseComObject(link); }
    }

    private static void Write(string path, InstallLayout layout, bool onlyAppId)
    {
        object link = new CShellLink();
        try
        {
            var sl = (IShellLinkW)link;
            if (onlyAppId) ((IPersistFile)link).Load(path, 2 /* STGM_READWRITE */);
            else
            {
                sl.SetPath(layout.AppExePath);
                sl.SetWorkingDirectory(layout.BaseDirectory);
                sl.SetDescription(Description);
                sl.SetIconLocation(layout.AppExePath, 0);
            }
            var store = (IPropertyStore)link;
            var key = AppUserModelIdKey;
            var pv = new PropVariant { vt = VtLpwstr, p1 = Marshal.StringToCoTaskMemUni(AppId) };
            try { store.SetValue(ref key, ref pv); }
            finally { PropVariantClear(ref pv); }
            store.Commit();
            ((IPersistFile)link).Save(onlyAppId ? null : path, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    // ------------------------------------------------------------------------------------ taakbalk

    public enum PinResult { Pinned, AlreadyPinned, Declined, NotSupported }

    /// <summary>Of PraktijkClean al aan de taakbalk vastzit (null = onbekend).</summary>
    public static async Task<bool?> IsPinnedAsync(InstallLayout layout)
    {
        foreach (var _ in PinnedLinks(layout)) return true;
        try
        {
            using var manager = TaskbarManager.TryGet();
            if (manager is null) return null;
            return await manager.CallAsync(TaskbarManager.SlotIsCurrentAppPinned);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Vraagt Windows om PraktijkClean vast te maken; Windows laat de gebruiker dat bevestigen. Alleen aanroepen vanuit
    /// het venster (UI-thread), terwijl het venster voorop staat, en na een keuze van de gebruiker.
    /// </summary>
    public static async Task<PinResult> RequestPinAsync(InstallLayout layout)
    {
        if (await IsPinnedAsync(layout) == true) return PinResult.AlreadyPinned;
        try
        {
            using var manager = TaskbarManager.TryGet();
            if (manager is null) return PinResult.NotSupported;
            bool? ok = await manager.CallAsync(TaskbarManager.SlotRequestPinCurrentApp);
            return ok switch { true => PinResult.Pinned, false => PinResult.Declined, null => PinResult.NotSupported };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return PinResult.NotSupported; }
    }

    /// <summary>
    /// Kleine laag om Windows.UI.Shell.TaskbarManager (WinRT) zonder extra bibliotheken: de functies worden direct via
    /// hun plaats in de COM-functietabel aangeroepen. De interface-ID's en de volgorde komen uit Windows.UI.winmd.
    /// </summary>
    private sealed class TaskbarManager : IDisposable
    {
        private const string ClassName = "Windows.UI.Shell.TaskbarManager";
        private static readonly Guid IidStatics = new("db32ab74-de52-4fe6-b7b6-95ff9f8395df");
        private static readonly Guid IidDesktopSupport = new("cdfefd63-e879-4134-b9a7-8283f05f9480");
        private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-c000-000000000046");

        // ITaskbarManager: 0-2 IUnknown, 3-5 IInspectable, 6 IsSupported, 7 IsPinningAllowed, 8 IsCurrentAppPinnedAsync,
        // 9 IsAppListEntryPinnedAsync, 10 RequestPinCurrentAppAsync, 11 RequestPinAppListEntryAsync.
        public const int SlotIsCurrentAppPinned = 8, SlotRequestPinCurrentApp = 10;

        private IntPtr _manager;
        private TaskbarManager(IntPtr manager) => _manager = manager;

        public static TaskbarManager? TryGet()
        {
            if (NeedsUnlockToken()) return null;
            IntPtr cls = IntPtr.Zero, statics = IntPtr.Zero, support = IntPtr.Zero;
            try
            {
                if (WindowsCreateString(ClassName, ClassName.Length, out cls) < 0) return null;
                // Zonder deze 'markering' kan een gewone (niet-Store) app TaskbarManager niet gebruiken.
                var iid = IidDesktopSupport;
                if (RoGetActivationFactory(cls, ref iid, out support) < 0) return null;
                iid = IidStatics;
                if (RoGetActivationFactory(cls, ref iid, out statics) < 0) return null;
                if (Slot<GetPtr>(statics, 6)(statics, out IntPtr manager) < 0 || manager == IntPtr.Zero) return null;
                if (Slot<GetBool>(manager, 6)(manager, out byte supported) < 0 || supported == 0) { Marshal.Release(manager); return null; }
                return new TaskbarManager(manager);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
            finally
            {
                if (support != IntPtr.Zero) Marshal.Release(support);
                if (statics != IntPtr.Zero) Marshal.Release(statics);
                if (cls != IntPtr.Zero) WindowsDeleteString(cls);
            }
        }

        /// <summary>
        /// Op Windows-versies van vóór begin 2026 mag alleen software met een sleutel van Microsoft deze functie
        /// gebruiken (een 'Limited Access Feature'); daar is het antwoord altijd nee. Microsoft beschrijft deze controle.
        /// </summary>
        private static bool NeedsUnlockToken()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\LimitedAccessFeatures\com.microsoft.windows.taskbar.pin");
                return key?.GetValue("4096B239A7295B635C090E647E867B5707DA6AB6CB78340B01FE4E0C8F4953D4") is int v && v != 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
        }

        /// <summary>Roept een functie aan die IAsyncOperation&lt;bool&gt; teruggeeft en wacht op het antwoord (null = mislukt).</summary>
        public async Task<bool?> CallAsync(int slot)
        {
            if (Slot<GetPtr>(_manager, slot)(_manager, out IntPtr op) < 0 || op == IntPtr.Zero) return null;
            IntPtr info = IntPtr.Zero;
            try
            {
                var iid = IidAsyncInfo;
                if (Marshal.QueryInterface(op, ref iid, out info) < 0) return null;
                // IAsyncInfo: 6 Id, 7 Status (0 bezig, 1 klaar, 2 geannuleerd, 3 fout).
                var status = Slot<GetInt>(info, 7);
                var until = DateTime.UtcNow.AddMinutes(5);   // de vraag van Windows mag even openstaan
                int s;
                while (status(info, out s) >= 0 && s == 0 && DateTime.UtcNow < until) await Task.Delay(100);
                if (s != 1) return null;
                // IAsyncOperation<bool>: 6 put_Completed, 7 get_Completed, 8 GetResults.
                if (Slot<GetBool>(op, 8)(op, out byte result) < 0) return null;
                return result != 0;
            }
            finally
            {
                if (info != IntPtr.Zero) Marshal.Release(info);
                Marshal.Release(op);
            }
        }

        public void Dispose()
        {
            if (_manager != IntPtr.Zero) Marshal.Release(_manager);
            _manager = IntPtr.Zero;
        }

        private static T Slot<T>(IntPtr obj, int index) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size));

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPtr(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBool(IntPtr self, out byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInt(IntPtr self, out int value);

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    }

    // ------------------------------------------------------------------------------------ COM-definities

    private const ushort VtLpwstr = 31;
    private static PropertyKey AppUserModelIdKey => new() { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = 5 };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pv);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant { public ushort vt, r1, r2, r3; public IntPtr p1, p2; }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
