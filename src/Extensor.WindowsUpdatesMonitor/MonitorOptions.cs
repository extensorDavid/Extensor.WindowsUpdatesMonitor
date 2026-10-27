namespace Extensor.WindowsUpdatesMonitor;

/// <summary>Settings bound from the "WindowsUpdatesMonitor" section of appsettings.json.</summary>
public sealed class MonitorOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan LogonDelay { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromHours(1);
    public int MaxTitles { get; set; } = 5;

    /// <summary>Intranet endpoint each check's result is posted to. Built in; only set it to use another address.</summary>
    public string ReportUrl { get; set; } = "https://intranet.extensor.no/WindowsUpdatesMonitor/Api/Report";

    /// <summary>
    /// Updates that never count as pending: an update whose title contains one of these (case insensitive) is left
    /// out. KB5007651 (Windows Security platform) installs and is then offered again, in one version after another.
    /// To ignore more, give the whole list in appsettings.json, KB5007651 included.
    /// </summary>
    public string[] IgnoreUpdates { get; set; } = { "KB5007651" };

    /// <summary>Log the message instead of sending it.</summary>
    public bool DryRun { get; set; }
}
