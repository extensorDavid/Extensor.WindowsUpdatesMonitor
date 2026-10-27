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

    public IntranetReporter(IOptions<NagOptions> options, ILogger<IntranetReporter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Reports a successful check. Never throws: a failed report is logged and the next check reports again.</summary>
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

    public static string StatusText(CheckResult result) => (result.UpdateCount, result.RebootPending) switch
    {
        (0, false) => "Ingen oppdateringer nødvendig",
        (0, true) => "Omstart kreves for å fullføre oppdateringer",
        (var n, false) => $"Oppdateringer kreves ({n})",
        (var n, true) => $"Oppdateringer kreves ({n}), og omstart kreves for å fullføre tidligere oppdateringer",
    };

    private async Task PostAsync(object report, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ReportUrl))
            return;

        try
        {
            using var response = await _http.PostAsJsonAsync(_options.ReportUrl, report, cancellationToken);
            if (response.IsSuccessStatusCode)
                _logger.LogInformation("Reported status to {Url}", _options.ReportUrl);
            else
                _logger.LogWarning("Intranet report failed: {Status} {Body}", (int)response.StatusCode,
                    await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Intranet report to {Url} failed", _options.ReportUrl);
        }
    }

    public void Dispose() => _http.Dispose();
}
