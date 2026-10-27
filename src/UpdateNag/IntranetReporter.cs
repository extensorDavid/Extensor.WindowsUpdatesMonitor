using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace UpdateNag;

/// <summary>
/// Posts each check's result, with a Norwegian status line, to the intranet (table WindowsUpdatesNaggerStatus).
/// The service runs as LocalSystem, so Windows authentication sends the PC's own domain account (DOMAIN\PC01$);
/// the intranet takes the machine name and IP address from the call itself.
/// </summary>
public sealed class IntranetReporter : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseDefaultCredentials = true })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly NagOptions _options;
    private readonly ILogger<IntranetReporter> _logger;
    private bool _unreachable;

    public IntranetReporter(IOptions<NagOptions> options, ILogger<IntranetReporter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Reports a successful check. Never throws: if the intranet can't be reached the next check tries again.</summary>
    public Task ReportAsync(CheckResult result, CancellationToken cancellationToken) =>
        PostAsync(new
        {
            CheckedAtUtc = DateTime.UtcNow,
            Status = StatusText(result),
            UpdateCount = (int?)result.UpdateCount,
            RebootPending = (bool?)result.RebootPending,
            UpdateTitles = result.Titles,
        }, cancellationToken);

    /// <summary>Reports a failed check.</summary>
    public Task ReportErrorAsync(Exception error, CancellationToken cancellationToken) =>
        PostAsync(new
        {
            CheckedAtUtc = DateTime.UtcNow,
            Status = $"Feil: {error.Message}",
            UpdateCount = (int?)null,
            RebootPending = (bool?)null,
            UpdateTitles = new List<string>(),
        }, cancellationToken);

    // An empty setting (as written by older versions of appsettings.json) means the built-in address.
    private string ReportUrl => string.IsNullOrWhiteSpace(_options.ReportUrl) ? NagOptions.DefaultReportUrl : _options.ReportUrl;

    public static string StatusText(CheckResult result) => (result.UpdateCount, result.RebootPending) switch
    {
        (0, false) => "Ingen oppdateringer nødvendig",
        (0, true) => "Omstart kreves for å fullføre oppdateringer",
        (var n, false) => $"Oppdateringer kreves ({n})",
        (var n, true) => $"Oppdateringer kreves ({n}), og omstart kreves for å fullføre tidligere oppdateringer",
    };

    private async Task PostAsync(object report, CancellationToken cancellationToken)
    {
        string? problem;
        try
        {
            using var response = await _http.PostAsJsonAsync(ReportUrl, report, cancellationToken);
            problem = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            problem = ex is OperationCanceledException ? "timed out" : ex.Message;
        }

        // Not being able to reach the intranet is normal for some PCs (off-site, not on the domain): say so once,
        // carry on with the message boxes, and try again at the next check.
        if (problem == null)
        {
            if (_unreachable)
                _logger.LogInformation("The intranet is reachable again; status reports resumed");
            else
                _logger.LogDebug("Reported status to {Url}", ReportUrl);
            _unreachable = false;
        }
        else if (!_unreachable)
        {
            _logger.LogInformation("Could not report status to the intranet ({Problem}). Message boxes work as usual; " +
                                   "will try again at the next check", problem);
            _unreachable = true;
        }
        else
        {
            _logger.LogDebug("Intranet still not reachable ({Problem})", problem);
        }
    }

    public void Dispose() => _http.Dispose();
}
