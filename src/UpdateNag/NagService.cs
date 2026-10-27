using System.Text;
using Microsoft.Extensions.Options;

namespace UpdateNag;

/// <summary>
/// Checks for pending Windows Updates every 2 hours, reports the result to the intranet, and nags logged-in users
/// about pending updates (each user at most once a day).
/// </summary>
public sealed class NagService : BackgroundService
{
    private const string Title = "Windows Updates pending";

    // Even when the next check is far away, wake up hourly: timers don't advance while the machine sleeps.
    private static readonly TimeSpan MaxSleep = TimeSpan.FromHours(1);

    // Users logging on are nagged from the last check's result while it is less than a day old.
    private static readonly TimeSpan ResultMaxAge = TimeSpan.FromHours(24);

    private readonly UpdateChecker _checker;
    private readonly SessionMessenger _messenger;
    private readonly IntranetReporter _reporter;
    private readonly NagOptions _options;
    private readonly ILogger<NagService> _logger;
    private readonly CancellationToken _stopping;
    private readonly object _lock = new();
    private readonly NagState _state;

    public NagService(UpdateChecker checker, SessionMessenger messenger, IntranetReporter reporter, IOptions<NagOptions> options,
        IHostApplicationLifetime lifetime, ILogger<NagService> logger)
    {
        _checker = checker;
        _messenger = messenger;
        _reporter = reporter;
        _options = options.Value;
        _logger = logger;
        _stopping = lifetime.ApplicationStopping;
        _state = LoadState();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UpdateNag started (DryRun={DryRun}); first check in {Delay}", _options.DryRun, _options.StartupDelay);
        await Task.Delay(_options.StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset? lastCheck;
            lock (_lock)
                lastCheck = _state.LastCheckUtc;

            var untilDue = lastCheck is null ? TimeSpan.Zero : lastCheck.Value + _options.CheckInterval - DateTimeOffset.UtcNow;
            if (untilDue > TimeSpan.Zero)
            {
                await Task.Delay(untilDue < MaxSleep ? untilDue : MaxSleep, stoppingToken);
                continue;
            }

            CheckResult result;
            try
            {
                result = await _checker.CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Update check failed; retrying in {Delay}", _options.RetryDelay);
                await _reporter.ReportErrorAsync(ex, stoppingToken);
                await Task.Delay(_options.RetryDelay, stoppingToken);
                continue;
            }

            lock (_lock)
            {
                _state.LastCheckUtc = DateTimeOffset.UtcNow;
                _state.LastResult = result;
                SaveState();
            }

            await _reporter.ReportAsync(result, stoppingToken);
            if (result.AnythingPending)
                NagSessions(result, _messenger.GetActiveSessions(), skipAlreadyNagged: true);
        }
    }

    /// <summary>Called from the service lifetime when a user logs on.</summary>
    public void OnSessionLogon(int sessionId)
    {
        _logger.LogInformation("Session {SessionId} logged on", sessionId);
        _ = Task.Run(async () =>
        {
            try
            {
                // Let the desktop finish loading before popping anything up.
                await Task.Delay(_options.LogonDelay, _stopping);

                CheckResult? result;
                lock (_lock)
                {
                    bool fresh = _state.LastCheckUtc is { } last && DateTimeOffset.UtcNow - last < ResultMaxAge;
                    result = fresh ? _state.LastResult : null;
                }
                if (result is not { AnythingPending: true })
                    return;

                var session = _messenger.GetActiveSessions().Where(s => s.SessionId == sessionId).ToList();
                NagSessions(result, session, skipAlreadyNagged: true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle logon for session {SessionId}", sessionId);
            }
        });
    }

    /// <summary>--nag-now: check immediately and message every active session, ignoring schedule and saved state.</summary>
    public async Task<int> NagNowAsync()
    {
        CheckResult result;
        try
        {
            result = await _checker.CheckAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            return 1;
        }

        if (!result.AnythingPending)
        {
            _logger.LogInformation("Nothing pending, no messages sent");
            return 0;
        }

        NagSessions(result, _messenger.GetActiveSessions(), skipAlreadyNagged: false);
        return 0;
    }

    private void NagSessions(CheckResult result, List<UserSession> sessions, bool skipAlreadyNagged)
    {
        string body = BuildMessage(result);
        foreach (var session in sessions)
        {
            if (skipAlreadyNagged)
            {
                lock (_lock)
                {
                    if (_state.WasNaggedToday(session.UserName))
                    {
                        _logger.LogInformation("{User} was already nagged today, skipping", session.UserName);
                        continue;
                    }
                }
            }

            if (_options.DryRun)
            {
                _logger.LogInformation("[DryRun] Would message {User} (session {SessionId}):\n{Body}", session.UserName, session.SessionId, body);
            }
            else if (_messenger.Send(session.SessionId, Title, body, _options.MessageTimeout))
            {
                _logger.LogInformation("Messaged {User} (session {SessionId})", session.UserName, session.SessionId);
            }
            else
            {
                continue;
            }

            if (skipAlreadyNagged)
            {
                lock (_lock)
                {
                    _state.MarkNagged(session.UserName);
                    SaveState();
                }
            }
        }
    }

    private string BuildMessage(CheckResult result)
    {
        var sb = new StringBuilder();
        if (result.UpdateCount > 0)
        {
            sb.AppendLine($"{result.UpdateCount} Windows update(s) are waiting to be installed:");
            foreach (var title in result.Titles.Take(_options.MaxTitles))
                sb.AppendLine($"  • {title}");
            if (result.UpdateCount > _options.MaxTitles)
                sb.AppendLine($"  …and {result.UpdateCount - _options.MaxTitles} more");
            sb.AppendLine();
        }
        if (result.RebootPending)
        {
            sb.AppendLine("A restart is required to finish installing updates.");
            sb.AppendLine();
        }
        sb.Append("Please open Settings → Windows Update to install them.");
        return sb.ToString();
    }

    private NagState LoadState()
    {
        try
        {
            return NagState.Load();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read state file; starting fresh");
            return new NagState();
        }
    }

    private void SaveState()
    {
        try
        {
            _state.Save();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write state file");
        }
    }
}
