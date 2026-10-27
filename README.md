# Extensor.WindowsUpdatesMonitor

A small Windows service that checks every 2 hours for pending Windows Updates (or a pending restart), reports the result to the Extensor intranet, and, if anything is pending, pops up a message box in every logged-in user's session telling them to open **Settings → Windows Update**.

- Runs as LocalSystem, Automatic (Delayed Start). Every start (or restart) of the service checks within 10 seconds, then every 2 hours.
- Uses the Windows Update Agent COM API (`IsInstalled=0 and IsHidden=0 and Type='Software'`) and the `RebootRequired` / `RebootPending` registry keys.
- Messages are sent with `WTSSendMessage` (OK button only, never blocks, auto-closes after 1 hour), in English and then Norwegian.
- Users who log on later the same day are reminded ~2 minutes after logon, using the cached result of the last check.
- Each user is reminded at most once per day; state survives restarts in `%ProgramData%\Extensor.WindowsUpdatesMonitor\state.json`.
- If a check fails (offline, WU busy, COM error) it is logged, reported, and retried an hour later; nobody is notified.
- After every check it posts a Norwegian status line ("Ingen oppdateringer nødvendig", "Oppdateringer kreves (3)", "Omstart kreves for å fullføre oppdateringer", "Feil: ...") to the intranet (`https://intranet.extensor.no/WindowsUpdatesMonitor/Api/Report`, built in). The intranet shows the machines under *Windows-oppdateringer* on the dashboard.

## Build

Requires the .NET 8 SDK.

```powershell
dotnet publish src/Extensor.WindowsUpdatesMonitor -c Release -o publish
```

This produces a self-contained single-file `publish\Extensor.WindowsUpdatesMonitor.exe` (win-x64) plus an optional `appsettings.json` (the defaults are built in).

## Configuration

`appsettings.json` (next to the exe):

| Setting          | Default      | Meaning                                                    |
|------------------|--------------|------------------------------------------------------------|
| `CheckInterval`  | `02:00:00`   | How often to check                                         |
| `StartupDelay`   | `00:00:10`   | Wait after service start before the first check            |
| `RetryDelay`     | `01:00:00`   | Wait before retrying a failed check                        |
| `LogonDelay`     | `00:02:00`   | Wait after a user logs on before reminding them            |
| `MessageTimeout` | `01:00:00`   | Message box closes itself after this long                  |
| `MaxTitles`      | `5`          | Update titles listed in the message                        |
| `DryRun`         | `false`      | Log the message instead of sending it                      |

Restart the service after changing settings.

## Intranet reporting

Nothing to configure: the service posts to `https://intranet.extensor.no/WindowsUpdatesMonitor/Api/Report`. (Only to use another address: add `"ReportUrl"` to the `WindowsUpdatesMonitor` section of `appsettings.json`.) The report carries the machine name; the intranet takes the IP address from the connection and keeps one row per machine in the `WindowsUpdatesMonitorStatus` table. The service runs as LocalSystem, so the call signs in with the PC's own domain account (Windows authentication, as for the rest of the intranet); this needs domain-joined PCs. On the intranet server itself, Windows refuses that login (the loopback check, HTTP 401) unless `intranet.extensor.no` is listed in `BackConnectionHostNames` under `HKLM\SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0`. Don't allow anonymous access for the report path in IIS: that made IIS answer 404 for it.

If the intranet can't be reached (off-site, not on the domain, intranet down), the service logs that once and carries on with message boxes only; it tries again at every check and logs once when contact is back.

`--notify-now` does not report.

## Logs

- Windows Event Log: *Application* log, source `Extensor.WindowsUpdatesMonitor`.
- Text files: `%ProgramData%\Extensor.WindowsUpdatesMonitor\logs\windowsupdatesmonitor-yyyyMMdd.log` (14 days kept).

## Install

Put `install.ps1` next to `Extensor.WindowsUpdatesMonitor.exe` (or run it from the repository root after publishing; it also looks in `.\publish`), then from an elevated PowerShell prompt:

```powershell
.\install.ps1
```

This copies the files to `C:\Program Files\Extensor.WindowsUpdatesMonitor`, registers the `Extensor.WindowsUpdatesMonitor` Event Log source, creates the `Extensor.WindowsUpdatesMonitor` service ("Extensor Windows Updates Monitor": LocalSystem, Automatic (Delayed Start), restart on failure), starts it and checks it is running. Re-running it upgrades the exe and keeps an existing `appsettings.json`.

If scripts are blocked on the PC, run `Set-ExecutionPolicy -Scope Process Bypass` first in the same prompt.

## Testing

Run in a console instead of as a service:

```powershell
# Normal schedule, logging to the console
.\publish\Extensor.WindowsUpdatesMonitor.exe --console

# Check right now and message every active session (ignores the schedule and saved state)
.\publish\Extensor.WindowsUpdatesMonitor.exe --console --notify-now
```

Run from an elevated prompt so it can read/write `%ProgramData%\Extensor.WindowsUpdatesMonitor` and message other users' sessions; unelevated, it can still message your own session. Set `"DryRun": true` in `appsettings.json` to see the message in the log without popping anything up.

To force a check (and a report to the intranet), restart the service: it checks within 10 seconds of starting.

## Uninstall

From an elevated PowerShell prompt:

```powershell
.\uninstall.ps1              # stop and delete the service, remove Event Log source and program files
.\uninstall.ps1 -RemoveData  # also delete %ProgramData%\Extensor.WindowsUpdatesMonitor (state and logs)
```
