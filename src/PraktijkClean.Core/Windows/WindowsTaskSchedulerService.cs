using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PraktijkClean.Core.Windows;

/// <summary>
/// Windows Taakplanner via de officiële COM-interface "Schedule.Service".
/// Taken worden in de hoofdmap van Taakplanner aangemaakt voor de huidige gebruiker;
/// daarvoor zijn geen administratorrechten nodig.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTaskSchedulerService : IScheduledTaskService
{
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int HResultFileNotFound = unchecked((int)0x80070002);
    private const int HResultPathNotFound = unchecked((int)0x80070003);

    public void Register(string taskName, string xml)
    {
        Run(root =>
        {
            root.RegisterTask(taskName, xml, TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
            return 0;
        }, "De geplande taak kon niet worden aangemaakt of bijgewerkt");
    }

    public void Delete(string taskName)
    {
        Run(root =>
        {
            try { root.DeleteTask(taskName, 0); }
            catch (Exception ex) when (IsNotFound(ex)) { }
            return 0;
        }, "De geplande taak kon niet worden verwijderd");
    }

    public ScheduledTaskInfo? GetInfo(string taskName)
    {
        return Run<ScheduledTaskInfo?>(root =>
        {
            dynamic task;
            try { task = root.GetTask(taskName); }
            catch (Exception ex) when (IsNotFound(ex)) { return null; }
            try
            {
                return new ScheduledTaskInfo(
                    Enabled: (bool)task.Enabled,
                    NextRunTime: ValidDate((DateTime)task.NextRunTime),
                    LastRunTime: ValidDate((DateTime)task.LastRunTime),
                    LastResult: (int)task.LastTaskResult);
            }
            finally { Release(task); }
        }, "De status van de geplande taak kon niet worden gelezen");
    }

    private static T Run<T>(Func<dynamic, T> action, string errorMessage)
    {
        dynamic? service = null;
        dynamic? root = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
            service = Activator.CreateInstance(type)!;
            service.Connect();
            root = service.GetFolder("\\");
            return action(root);
        }
        catch (Exception ex) when (Unwrap(ex) is COMException com)
        {
            throw new ScheduledTaskException($"{errorMessage} (Windows-foutcode 0x{com.HResult:X8}).", ex);
        }
        catch (Exception ex) when (Unwrap(ex) is UnauthorizedAccessException)
        {
            throw new ScheduledTaskException($"{errorMessage}: geen toestemming. Mogelijk blokkeert beleid van de organisatie het aanmaken van taken.", ex);
        }
        catch (Exception ex) when (ex is not ScheduledTaskException)
        {
            throw new ScheduledTaskException($"{errorMessage} ({ex.GetType().Name}).", ex);
        }
        finally
        {
            Release(root);
            Release(service);
        }
    }

    /// <summary>"Taak bestaat niet" (ook als de fout is ingepakt door de COM-binder).</summary>
    private static bool IsNotFound(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e.HResult is HResultFileNotFound or HResultPathNotFound) return true;
        return false;
    }

    private static Exception Unwrap(Exception ex) =>
        ex is System.Reflection.TargetInvocationException { InnerException: Exception inner } ? inner : ex;

    private static DateTime? ValidDate(DateTime d) => d.Year < 2000 ? null : d;

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
    }
}
