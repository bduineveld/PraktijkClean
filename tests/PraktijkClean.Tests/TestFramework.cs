using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace PraktijkClean.Tests;

/// <summary>
/// Een heel klein testraamwerk, zodat er geen externe pakketten (xUnit/NUnit) nodig zijn.
/// Elke methode met [Test] in een klasse in deze assembly wordt uitgevoerd.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public sealed class SkipException : Exception
{
    public SkipException(string reason) : base(reason) { }
}

public static class Assert
{
    public static void Equal<T>(T expected, T actual, string what = "waarde")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: verwacht '{expected}', maar was '{actual}'");
    }

    public static void True(bool condition, string what)
    {
        if (!condition) throw new Exception("Verwacht waar: " + what);
    }

    public static void False(bool condition, string what) => True(!condition, "niet " + what);

    public static T Throws<T>(Action action, string what) where T : Exception
    {
        try { action(); }
        catch (T ex) { return ex; }
        catch (Exception ex) { throw new Exception($"{what}: verwacht {typeof(T).Name}, kreeg {ex.GetType().Name}: {ex.Message}"); }
        throw new Exception($"{what}: verwacht {typeof(T).Name}, maar er werd geen fout gegooid");
    }

    public static void Skip(string reason) => throw new SkipException(reason);
}

/// <summary>Hulpmiddelen voor het besturingssysteem (vervangen OperatingSystem en Directory.CreateSymbolicLink van nieuw .NET).</summary>
public static class Platform
{
    public static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <summary>Maakt een map-koppeling (junction) via mklink /J. Geeft false als dat niet lukt.</summary>
    public static bool CreateJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 && Directory.Exists(link);
    }
}

/// <summary>Tijdelijke testmap die na gebruik wordt opgeruimd.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PraktijkCleanTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try
        {
            // Koppelingen eerst losmaken zodat nooit een doel buiten de testmap wordt doorlopen.
            foreach (var e in new DirectoryInfo(Path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                         .Where(e => (e.Attributes & FileAttributes.ReparsePoint) != 0).ToList())
                e.Delete();
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class Repo
{
    /// <summary>Zoekt de projectmap (waar script\cleanup.ps1 staat) vanaf de testuitvoer omhoog.</summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "script", "cleanup.ps1"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Projectmap niet gevonden.");
        }
    }

    public static string ScriptPath => System.IO.Path.Combine(Root, "script", "cleanup.ps1");
    public static string ExampleConfigPath => System.IO.Path.Combine(Root, "script", "config.example.json");
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string? filter = args.Length > 0 ? args[0] : null;
        int passed = 0, failed = 0, skipped = 0;
        var failures = new List<string>();

        var tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
                .Select(m => (Type: t, Method: m)))
            .OrderBy(x => x.Type.Name).ThenBy(x => x.Method.MetadataToken);

        string? currentClass = null;
        foreach (var (type, method) in tests)
        {
            string name = $"{type.Name}.{method.Name}";
            if (filter is not null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (currentClass != type.Name) { Console.WriteLine($"\n{type.Name}"); currentClass = type.Name; }
            try
            {
                object? instance = method.IsStatic ? null : Activator.CreateInstance(type);
                object? result = method.Invoke(instance, null);
                if (result is Task task) await task;
                passed++;
                Write(ConsoleColor.Green, $"  [OK]    {method.Name}");
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: Exception i } ? i : ex;
                if (inner is SkipException)
                {
                    skipped++;
                    Write(ConsoleColor.Yellow, $"  [SKIP]  {method.Name} ({inner.Message})");
                }
                else
                {
                    failed++;
                    failures.Add($"{name}: {inner.Message}");
                    Write(ConsoleColor.Red, $"  [FOUT]  {method.Name}: {inner.Message}");
                }
            }
        }

        Console.WriteLine($"\nResultaat: {passed} geslaagd, {failed} mislukt, {skipped} overgeslagen.");
        foreach (var f in failures) Write(ConsoleColor.Red, " - " + f);
        return failed == 0 ? 0 : 1;
    }

    private static void Write(ConsoleColor color, string text)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
    }
}
