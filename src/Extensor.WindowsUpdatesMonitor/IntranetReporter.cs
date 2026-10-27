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
    private bool _adaptersLogged;

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

    /// <summary>
    /// The IPv4 address of the network adapter that has a default gateway (the LAN one, not Hyper-V/WSL).
    /// The first time, every adapter and the choice are logged, to see why a PC reports the address it does.
    /// </summary>
    private string? LocalIpAddress()
    {
        string? chosen = null;
        var lines = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = nic.GetIPProperties();
                var ipv4 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                                                 .Select(a => a.Address.ToString()).ToList();
                var gateways = props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                 && !g.Address.Equals(System.Net.IPAddress.Any))
                                                     .Select(g => g.Address.ToString()).ToList();
                bool candidate = nic.OperationalStatus == OperationalStatus.Up
                                 && nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                                 && gateways.Count > 0 && ipv4.Count > 0;
                if (candidate && chosen == null)
                    chosen = ipv4[0];
                lines.Add($"  {nic.Name} ({nic.Description}): {nic.NetworkInterfaceType}, {nic.OperationalStatus}, " +
                          $"IPv4 [{string.Join(", ", ipv4)}], gateway [{string.Join(", ", gateways)}]" +
                          (candidate ? (chosen == ipv4[0] ? "  <- used" : "  (also possible)") : ""));
            }
        }
        catch (NetworkInformationException ex)
        {
            lines.Add($"  could not list the network adapters: {ex.Message}");
        }

        if (!_adaptersLogged)
        {
            _logger.LogInformation("IP address for the intranet report: {Ip}. Network adapters:{NewLine}{Adapters}",
                chosen ?? "(none found; the intranet will use the connection's address)", Environment.NewLine,
                string.Join(Environment.NewLine, lines));
            _adaptersLogged = true;
        }
        return chosen;
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
        string reply = "";
        try
        {
            using var response = await _http.PostAsJsonAsync(_options.ReportUrl, report, cancellationToken);
            problem = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            if (problem == null)
                reply = await response.Content.ReadAsStringAsync(cancellationToken);
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
            // The reply says what the intranet stored, e.g. {"ok":true,"machineName":"PC01","ipAddress":"172.24.1.57"}
            _logger.LogInformation("Reported status to the intranet; it stored: {Reply}", reply);
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
