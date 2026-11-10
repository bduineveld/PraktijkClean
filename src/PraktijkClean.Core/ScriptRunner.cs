using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PraktijkClean.Core;

/// <summary>
/// Start cleanup.ps1 vanuit de beheerapp (simulatie of "Nu opruimen") en leest het resultaat.
/// Het script wordt altijd met dezelfde vaste argumenten gestart; instellingen gaan via config.json.
/// </summary>
public sealed class ScriptRunner
{
    private readonly string _powerShellPath;
    private readonly InstallLayout _layout;
    private readonly TimeSpan _timeout;

    public ScriptRunner(string powerShellPath, InstallLayout layout, TimeSpan? timeout = null)
    {
        _powerShellPath = powerShellPath;
        _layout = layout;
        _timeout = timeout ?? TimeSpan.FromMinutes(10);
    }

    /// <summary>Simulatie met bestandsnamen (alleen in het geheugen, niet in logbestanden).</summary>
    public Task<RunReport> SimulateAsync(CancellationToken ct = default) =>
        RunAsync(new[] { "-Mode", "Simulate", "-Trigger", "Handmatig", "-Json", "-ShowFileNames" }, ct);

    /// <summary>Daadwerkelijk opruimen. Alleen aanroepen na expliciete bevestiging door de gebruiker.</summary>
    public Task<RunReport> CleanNowAsync(CancellationToken ct = default) =>
        RunAsync(new[] { "-Mode", "CleanNow", "-Trigger", "Handmatig", "-ConfirmedByUser", "-Json" }, ct);

    private async Task<RunReport> RunAsync(string[] modeArgs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_powerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = _layout.BaseDirectory,
        };
        // Vaste argumenten, elk volgens de Windows-regels tussen aanhalingstekens gezet (zie QuoteArgument).
        var args = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned",
                                      "-File", _layout.ScriptPath, "-ConfigPath", _layout.ConfigPath };
        args.AddRange(modeArgs);
        psi.Arguments = BuildCommandLine(args);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ScriptRunException("PowerShell kon niet worden gestart.", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        int timeoutMs = (int)Math.Min(int.MaxValue, _timeout.TotalMilliseconds);
        bool exited = await Task.Run(() => process.WaitForExit(timeoutMs), ct).ConfigureAwait(false);
        if (!exited)
        {
            try { process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            throw new ScriptRunException("Het opschoonscript reageerde niet binnen de verwachte tijd en is gestopt.");
        }
        process.WaitForExit(); // wacht ook tot de uitvoer volledig is ingelezen

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        string json = ExtractJson(stdout);
        if (json.Length == 0)
        {
            string hint = stderr.Contains("ExecutionPolicy", StringComparison.OrdinalIgnoreCase) ||
                          stderr.Contains("execution policy", StringComparison.OrdinalIgnoreCase) ||
                          stderr.Contains("uitvoeringsbeleid", StringComparison.OrdinalIgnoreCase)
                ? " Het PowerShell-uitvoeringsbeleid van deze computer staat het script niet toe (zie README, 'Uitvoeringsbeleid')."
                : "";
            throw new ScriptRunException($"Het opschoonscript gaf geen resultaat (exitcode {process.ExitCode}).{hint}", details: stderr);
        }
        try
        {
            return RunReport.Parse(json);
        }
        catch (JsonFormatException ex)
        {
            throw new ScriptRunException("Het resultaat van het opschoonscript kon niet worden gelezen.", ex);
        }
    }

    /// <summary>Neemt de laatste regel die met '{' begint (het script schrijft één JSON-regel).</summary>
    public static string ExtractJson(string stdout)
    {
        var lines = stdout.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i].Trim();
            if (line.StartsWith('{')) return line;
        }
        return "";
    }

    /// <summary>Zet argumenten om naar één opdrachtregel volgens de regels van CommandLineToArgvW.</summary>
    public static string BuildCommandLine(IEnumerable<string> args) => string.Join(" ", args.Select(QuoteArgument));

    /// <summary>
    /// Eén argument veilig tussen aanhalingstekens: spaties blijven één argument, aanhalingstekens en
    /// backslashes ervoor worden ge-escaped. Zo kan een pad nooit als extra argument worden gelezen.
    /// </summary>
    public static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}

public sealed class ScriptRunException : Exception
{
    public ScriptRunException(string message, Exception? inner = null, string? details = null) : base(message, inner)
    {
        Details = details;
    }

    /// <summary>Technische details (stderr) voor de systeembeheerder.</summary>
    public string? Details { get; }
}
