namespace UpdateNag;

/// <summary>Settings bound from the "UpdateNag" section of appsettings.json.</summary>
public sealed class NagOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan LogonDelay { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromHours(1);
    public int MaxTitles { get; set; } = 5;

    /// <summary>Log the message instead of sending it.</summary>
    public bool DryRun { get; set; }
}
