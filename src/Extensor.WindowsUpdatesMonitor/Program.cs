using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Extensor.WindowsUpdatesMonitor;

bool notifyNow = args.Contains("--notify-now", StringComparer.OrdinalIgnoreCase);
bool isService = WindowsServiceHelpers.IsWindowsService();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(a => !a.StartsWith("--console", StringComparison.OrdinalIgnoreCase)
                        && !a.StartsWith("--notify-now", StringComparison.OrdinalIgnoreCase)).ToArray(),
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection("WindowsUpdatesMonitor"));

builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(MonitorState.DataDirectory, "logs")));
if (isService)
{
    builder.Logging.AddEventLog(s => { s.SourceName = "Extensor.WindowsUpdatesMonitor"; s.LogName = "Application"; });
}
else
{
    builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
}

builder.Services.AddWindowsService(o => o.ServiceName = "Extensor.WindowsUpdatesMonitor");
builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddSingleton<SessionMessenger>();
builder.Services.AddSingleton<IntranetReporter>();
builder.Services.AddSingleton<MonitorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MonitorService>());
if (isService)
{
    // Replaces the lifetime registered by AddWindowsService so we receive logon notifications.
    builder.Services.AddSingleton<IHostLifetime, SessionAwareLifetime>();
}

using var host = builder.Build();

if (notifyNow)
{
    return await host.Services.GetRequiredService<MonitorService>().NotifyNowAsync();
}

await host.RunAsync();
return 0;

/// <summary>Windows service lifetime that forwards session logon events to <see cref="MonitorService"/>.</summary>
sealed class SessionAwareLifetime : WindowsServiceLifetime
{
    private readonly MonitorService _monitor;

    public SessionAwareLifetime(IHostEnvironment env, IHostApplicationLifetime appLifetime, ILoggerFactory loggerFactory,
        IOptions<HostOptions> hostOptions, IOptions<WindowsServiceLifetimeOptions> serviceOptions, MonitorService monitor)
        : base(env, appLifetime, loggerFactory, hostOptions, serviceOptions)
    {
        _monitor = monitor;
        CanHandleSessionChangeEvent = true;
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        if (changeDescription.Reason == SessionChangeReason.SessionLogon)
        {
            _monitor.OnSessionLogon(changeDescription.SessionId);
        }
        base.OnSessionChange(changeDescription);
    }
}
