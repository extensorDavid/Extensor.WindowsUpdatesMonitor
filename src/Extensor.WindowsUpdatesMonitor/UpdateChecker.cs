using Microsoft.Win32;

namespace Extensor.WindowsUpdatesMonitor;

/// <summary>Asks the Windows Update Agent (late-bound COM) for pending updates and checks for a pending reboot.</summary>
public sealed class UpdateChecker
{
    private const string Criteria = "IsInstalled=0 and IsHidden=0 and Type='Software'";

    private static readonly string[] RebootKeys =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
    };

    private readonly ILogger<UpdateChecker> _logger;

    public UpdateChecker(ILogger<UpdateChecker> logger) => _logger = logger;

    /// <summary>Runs the search on a dedicated thread, since it can take minutes. Throws on failure.</summary>
    public Task<CheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<CheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(Check()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true,
            Name = "Windows Update search",
        };
        thread.Start();

        // The COM call itself can't be cancelled; on shutdown we just stop waiting for it.
        return tcs.Task.WaitAsync(cancellationToken);
    }

    private CheckResult Check()
    {
        _logger.LogInformation("Searching for updates: {Criteria}", Criteria);

        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session", throwOnError: true)!;
        dynamic session = Activator.CreateInstance(sessionType)!;
        session.ClientApplicationID = "Extensor.WindowsUpdatesMonitor";
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic result = searcher.Search(Criteria);

        // OperationResultCode: 2 = Succeeded, 3 = SucceededWithErrors
        int resultCode = (int)result.ResultCode;
        if (resultCode != 2 && resultCode != 3)
            throw new InvalidOperationException($"Windows Update search failed with result code {resultCode}.");

        dynamic updates = result.Updates;
        int count = (int)updates.Count;
        var titles = new List<string>(count);
        for (int i = 0; i < count; i++)
            titles.Add((string)updates[i].Title);

        bool rebootPending = IsRebootPending();
        _logger.LogInformation("Found {Count} pending update(s); reboot pending: {RebootPending}", count, rebootPending);
        return new CheckResult(count, titles, rebootPending);
    }

    private static bool IsRebootPending()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return RebootKeys.Any(path =>
        {
            using var key = hklm.OpenSubKey(path);
            return key != null;
        });
    }
}
