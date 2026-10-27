using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Extensor.WindowsUpdatesMonitor;

/// <summary>
/// Posts each check's result, with a Norwegian status line, to the intranet (table WindowsUpdatesMonitorStatus).
/// The report carries the machine name and IP address (the intranet only sees the router's address on the
/// connection). The call signs in
/// with Windows authentication as the PC's domain account (the service runs as LocalSystem).
/// </summary>
public sealed class IntranetReporter : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseDefaultCredentials = true })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly MonitorOptions _options;
    private readonly ILogger<IntranetReporter> _logger;
    private bool _unreachable;

    public IntranetReporter(IOptions<MonitorOptions> options, ILogger<IntranetReporter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Reports a successful check. Never throws: if the intranet can't be reached the next check tries again.</summary>
    public Task ReportAsync(CheckResult result, CancellationToken cancellationToken) =>
        PostAsync(new
        {
            Environment.MachineName,
            IpAddress = LocalIpAddress(),
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
            Environment.MachineName,
            IpAddress = LocalIpAddress(),
            CheckedAtUtc = DateTime.UtcNow,
            Status = $"Feil: {error.Message}",
            UpdateCount = (int?)null,
            RebootPending = (bool?)null,
            UpdateTitles = new List<string>(),
        }, cancellationToken);

    /// <summary>The IPv4 address of the network adapter that has a default gateway (the LAN one, not Hyper-V/WSL).</summary>
    private static string? LocalIpAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var props = nic.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                     && !g.Address.Equals(System.Net.IPAddress.Any)))
                    continue;
                var address = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (address != null)
                    return address.Address.ToString();
            }
        }
        catch (NetworkInformationException) { }
        return null;
    }

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
            using var response = await _http.PostAsJsonAsync(_options.ReportUrl, report, cancellationToken);
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
                _logger.LogDebug("Reported status to {Url}", _options.ReportUrl);
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
