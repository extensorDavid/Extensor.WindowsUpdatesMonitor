using System.ServiceProcess;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using UpdateNag;

bool nagNow = args.Contains("--nag-now", StringComparer.OrdinalIgnoreCase);
bool isService = WindowsServiceHelpers.IsWindowsService();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(a => !a.StartsWith("--console", StringComparison.OrdinalIgnoreCase)
                        && !a.StartsWith("--nag-now", StringComparison.OrdinalIgnoreCase)).ToArray(),
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.Configure<NagOptions>(builder.Configuration.GetSection("UpdateNag"));

builder.Logging.ClearProviders();
builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(NagState.DataDirectory, "logs")));
if (isService)
{
    builder.Logging.AddEventLog(s => { s.SourceName = "UpdateNag"; s.LogName = "Application"; });
}
else
{
    builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
}

builder.Services.AddWindowsService(o => o.ServiceName = "UpdateNag");
builder.Services.AddSingleton<UpdateChecker>();
builder.Services.AddSingleton<SessionMessenger>();
builder.Services.AddSingleton<IntranetReporter>();
builder.Services.AddSingleton<NagService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NagService>());
if (isService)
{
    // Replaces the lifetime registered by AddWindowsService so we receive logon notifications.
    builder.Services.AddSingleton<IHostLifetime, SessionAwareLifetime>();
}

using var host = builder.Build();

if (nagNow)
{
    return await host.Services.GetRequiredService<NagService>().NagNowAsync();
}

await host.RunAsync();
return 0;

/// <summary>Windows service lifetime that forwards session logon events to <see cref="NagService"/>.</summary>
sealed class SessionAwareLifetime : WindowsServiceLifetime
{
    private readonly NagService _nag;

    public SessionAwareLifetime(IHostEnvironment env, IHostApplicationLifetime appLifetime, ILoggerFactory loggerFactory,
        IOptions<HostOptions> hostOptions, IOptions<WindowsServiceLifetimeOptions> serviceOptions, NagService nag)
        : base(env, appLifetime, loggerFactory, hostOptions, serviceOptions)
    {
        _nag = nag;
        CanHandleSessionChangeEvent = true;
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        if (changeDescription.Reason == SessionChangeReason.SessionLogon)
        {
            _nag.OnSessionLogon(changeDescription.SessionId);
        }
        base.OnSessionChange(changeDescription);
    }
}
