using System.Collections.Generic;
using System.Drawing;
using PraktijkClean.Core;

namespace PraktijkClean;

internal enum StatusKind { Off, Paused, Simulation, Problem, On }

/// <summary>
/// Bepaalt in één keer of PraktijkClean aan staat, zodat de balk in het venster en het icoon in het
/// systeemvak altijd hetzelfde zeggen.
/// </summary>
internal sealed record StatusInfo(StatusKind Kind, string Title, string Detail, string Short)
{
    public static StatusInfo Compute(AppConfig? config, InstallState state)
    {
        if (config is null || !state.ConfigInstalled)
            return new(StatusKind.Off, "Nog niet ingesteld",
                "Controleer hieronder de mappen en de planning en klik op 'Opslaan'. PraktijkClean begint in simulatiemodus: er wordt dan nog niets verwijderd.",
                "niet ingesteld");
        if (config.Paused)
            return new(StatusKind.Paused, "Gepauzeerd",
                "Er wordt niets automatisch gecontroleerd of opgeruimd tot u op 'Hervatten' klikt.", "GEPAUZEERD");
        bool active = !config.SimulationMode && config.AutomaticCleanupConfirmed;
        if (!active)
            return new(StatusKind.Simulation, "Simulatiemodus – er wordt niets verwijderd",
                "PraktijkClean telt alleen wat het zou opruimen (" + Moment(config.Schedule) + "). Bekijk de simulatie en zet daarna automatisch opruimen aan.",
                "SIMULATIE");
        bool taskOk = state.Task is { Enabled: true } || !TaskXmlBuilder.HasAnyTrigger(config.Schedule);
        if (!taskOk)
            return new(StatusKind.Problem, "Actie nodig",
                "Automatisch opruimen staat aan, maar de geplande taak ontbreekt of staat uit. Klik op 'Herstellen'.", "PROBLEEM – open de app");
        string what = config.DeleteMode == DeleteModes.Permanent ? "Oude bestanden worden definitief verwijderd" : "Oude bestanden gaan naar de Prullenbak";
        if (config.EmptyRecycleBin)
            what += config.RecycleBinRetentionDays == 0 ? ", die daarna wordt geleegd"
                : $", die na {config.RecycleBinRetentionDays} {(config.RecycleBinRetentionDays == 1 ? "dag" : "dagen")} wordt geleegd";
        return new(StatusKind.On, "Aan – automatisch opruimen", what + " · " + Capitalize(Moment(config.Schedule)) + ".", "AAN");
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    public static string Moment(ScheduleSettings schedule)
    {
        var parts = new List<string>();
        if (schedule.Daily) parts.Add("dagelijks om " + schedule.DailyTime);
        if (schedule.AtLogon) parts.Add("bij aanmelden");
        return parts.Count == 0 ? "alleen handmatig" : string.Join(" en ", parts);
    }

    /// <summary>Achtergrondkleur van de kop en kleur van de titel in het venster.</summary>
    public (Color Back, Color Fore) BannerColors => Kind switch
    {
        StatusKind.On => (Color.FromArgb(219, 242, 219), Color.FromArgb(0, 100, 0)),
        StatusKind.Simulation => (Color.FromArgb(221, 234, 250), Color.FromArgb(20, 70, 140)),
        StatusKind.Paused => (Color.FromArgb(255, 236, 204), Color.FromArgb(150, 75, 0)),
        StatusKind.Problem => (Color.FromArgb(250, 222, 222), Color.Firebrick),
        _ => (Color.FromArgb(235, 235, 235), Color.DimGray),
    };

    /// <summary>Kleur van het icoon in het systeemvak.</summary>
    public static Color IconColor(StatusKind kind) => kind switch
    {
        StatusKind.On => Color.FromArgb(40, 160, 60),
        StatusKind.Simulation => Color.FromArgb(40, 110, 200),
        StatusKind.Paused => Color.FromArgb(235, 140, 20),
        StatusKind.Problem => Color.FromArgb(210, 40, 40),
        _ => Color.FromArgb(140, 140, 140),
    };

    /// <summary>"Afgelopen 7 dagen: …" of "Vandaag: …" uit het logboek.</summary>
    public static string Summary(LogSummary s, string period) =>
        s.Runs == 0
            ? $"{period}: geen controles"
            : $"{period}: {s.DeletedFiles} bestand(en) opgeruimd ({Format.Bytes(Mb(s.DeletedMegabytes))})" +
              (s.BinItemsDeleted > 0 ? $", {s.BinItemsDeleted} uit de Prullenbak ({Format.Bytes(Mb(s.BinMegabytes))})" : "") +
              $", {s.Runs} controle(s)" + (s.Errors > 0 ? $", {s.Errors} fout(en)" : "");

    private static long Mb(double megabytes) => (long)(megabytes * 1024 * 1024);
}
