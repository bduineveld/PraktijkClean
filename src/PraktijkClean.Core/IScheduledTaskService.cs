using System;

namespace PraktijkClean.Core;

/// <summary>Status van een geplande taak zoals Windows Taakplanner die rapporteert.</summary>
public sealed record ScheduledTaskInfo(bool Enabled, DateTime? NextRunTime, DateTime? LastRunTime, int? LastResult);

/// <summary>
/// Abstractie van Windows Taakplanner, zodat de installatielogica ook zonder Windows getest kan worden.
/// </summary>
public interface IScheduledTaskService
{
    /// <summary>Maakt de taak aan of werkt hem bij. Gooit <see cref="ScheduledTaskException"/> bij een fout.</summary>
    void Register(string taskName, string xml);

    /// <summary>Verwijdert de taak; geen fout als die niet bestaat.</summary>
    void Delete(string taskName);

    /// <summary>Null als de taak niet bestaat.</summary>
    ScheduledTaskInfo? GetInfo(string taskName);
}

public sealed class ScheduledTaskException : Exception
{
    public ScheduledTaskException(string message, Exception? inner = null) : base(message, inner) { }
}
