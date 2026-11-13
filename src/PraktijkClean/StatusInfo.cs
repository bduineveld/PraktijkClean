using System.Collections.Generic;
using System.Drawing;
using PraktijkClean.Core;

namespace PraktijkClean;

internal enum StatusKind { Off, Paused, Simulation, Problem, On }

/// <summary>
/// Bepaalt in één keer of PraktijkClean aan staat, zodat de balk in het venster en het icoon in het
/// systeemvak altijd hetzelfde zeggen.
/// </summary>
internal sealed record StatusInfo(StatusKind Kind, string Banner, string Short)
{
    public static StatusInfo Compute(AppConfig? config, InstallState state)
    {
        if (config is null || !state.ConfigInstalled)
            return new(StatusKind.Off, "○  NIET INGESTELD – controleer de instellingen en klik op 'Instellingen opslaan'.", "niet ingesteld");
        if (config.Paused)
            return new(StatusKind.Paused, "●  GEPAUZEERD – er wordt niets automatisch gecontroleerd of opgeruimd. Klik op 'Hervatten'.", "GEPAUZEERD");
        bool active = !config.SimulationMode && config.AutomaticCleanupConfirmed;
        if (!active)
            return new(StatusKind.Simulation, "●  SIMULATIE – er wordt alleen gecontroleerd, niets verwijderd. Aanzetten: 'Automatische opschoning activeren…'.", "SIMULATIE");
        bool taskOk = state.Task is { Enabled: true } || !TaskXmlBuilder.HasAnyTrigger(config.Schedule);
        if (!taskOk)
            return new(StatusKind.Problem, "⚠  AAN, MAAR DE GEPLANDE TAAK ONTBREEKT OF STAAT UIT – klik op 'Installatie herstellen'.", "PROBLEEM – open de app");
        return new(StatusKind.On,
            "●  AAN – oude bestanden worden automatisch " +
            (config.DeleteMode == DeleteModes.Permanent ? "definitief verwijderd" : "naar de Prullenbak verplaatst") +
            (config.EmptyRecycleBin ? " en de Prullenbak wordt geleegd" : "") + " (" + Moment(config.Schedule) + ").",
            "AAN");
    }

    public static string Moment(ScheduleSettings schedule)
    {
        var parts = new List<string>();
        if (schedule.Daily) parts.Add("dagelijks om " + schedule.DailyTime);
        if (schedule.AtLogon) parts.Add("bij aanmelden");
        return parts.Count == 0 ? "alleen handmatig" : string.Join(" en ", parts);
    }

    /// <summary>Achtergrond- en tekstkleur voor de balk in het venster.</summary>
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
