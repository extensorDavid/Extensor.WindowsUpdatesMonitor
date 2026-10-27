# UpdateNag

A small Windows service that checks every 2 hours for pending Windows Updates (or a pending restart), reports the result to the Extensor intranet, and, if anything is pending, pops up a message box in every logged-in user's session telling them to open **Settings → Windows Update**.

- Runs as LocalSystem, Automatic (Delayed Start).
- Uses the Windows Update Agent COM API (`IsInstalled=0 and IsHidden=0 and Type='Software'`) and the `RebootRequired` / `RebootPending` registry keys.
- Messages are sent with `WTSSendMessage` (OK button only, never blocks, auto-closes after 1 hour), in English and then Norwegian.
- Users who log on later the same day are nagged ~2 minutes after logon, using the cached result of the last check.
- Each user is nagged at most once per day; state survives restarts in `%ProgramData%\UpdateNag\state.json`.
- If a check fails (offline, WU busy, COM error) it is logged, reported, and retried an hour later; nobody is nagged.
- After every check it posts a Norwegian status line ("Ingen oppdateringer nødvendig", "Oppdateringer kreves (3)", "Omstart kreves for å fullføre oppdateringer", "Feil: ...") to the intranet (`https://intranet.extensor.no/WindowsUpdatesNagger/Api/Report`, built in). The intranet shows the machines under *Windows-oppdateringer* on the dashboard.

## Build

Requires the .NET 8 SDK.

```powershell
dotnet publish src/UpdateNag -c Release -o publish
```

This produces a self-contained single-file `publish\UpdateNag.exe` (win-x64) plus `appsettings.json`.

## Configuration

`appsettings.json` (next to the exe):

| Setting          | Default      | Meaning                                                    |
|------------------|--------------|------------------------------------------------------------|
| `CheckInterval`  | `02:00:00`   | How often to check                                         |
| `StartupDelay`   | `00:05:00`   | Wait after service start before the first check            |
| `RetryDelay`     | `01:00:00`   | Wait before retrying a failed check                        |
| `LogonDelay`     | `00:02:00`   | Wait after a user logs on before nagging them              |
| `MessageTimeout` | `01:00:00`   | Message box closes itself after this long                  |
| `MaxTitles`      | `5`          | Update titles listed in the message                        |
| `DryRun`         | `false`      | Log the message instead of sending it                      |

Restart the service after changing settings.

## Intranet reporting

Nothing to configure: the service posts to `https://intranet.extensor.no/WindowsUpdatesNagger/Api/Report`. (Only to use another address: add `"ReportUrl"` to the `UpdateNag` section of `appsettings.json`.) The service runs as LocalSystem, so the call is made with Windows authentication as the PC's own domain account (`DOMAIN\PC01$`): no password or key is stored on the PC. The intranet only accepts computer accounts there, takes the machine name from the account and the IP address from the connection, and keeps one row per machine in the `WindowsUpdatesNaggerStatus` table. This needs domain-joined PCs.

If the intranet can't be reached (off-site, not on the domain, intranet down), the service logs that once and carries on with message boxes only; it tries again at every check and logs once when contact is back.

`--nag-now` does not report.

## Logs

- Windows Event Log: *Application* log, source `UpdateNag`.
- Text files: `%ProgramData%\UpdateNag\logs\updatenag-yyyyMMdd.log` (14 days kept).

## Install

From an elevated PowerShell prompt in the repository root, after publishing:

```powershell
.\install.ps1
```

This copies the files to `C:\Program Files\UpdateNag`, registers the `UpdateNag` Event Log source, creates the service (LocalSystem, Automatic (Delayed Start), restart on failure) and starts it. Re-running it upgrades the exe and keeps an existing `appsettings.json`.

## Testing

Run in a console instead of as a service:

```powershell
# Normal schedule, logging to the console
.\publish\UpdateNag.exe --console

# Check right now and message every active session (ignores the schedule and saved state)
.\publish\UpdateNag.exe --console --nag-now
```

Run from an elevated prompt so it can read/write `%ProgramData%\UpdateNag` and message other users' sessions; unelevated, it can still message your own session. Set `"DryRun": true` in `appsettings.json` to see the message in the log without popping anything up.

To force the service to check again, stop it, delete `%ProgramData%\UpdateNag\state.json` and start it; the check runs after the startup delay.

## Uninstall

From an elevated PowerShell prompt:

```powershell
.\uninstall.ps1              # stop and delete the service, remove Event Log source and program files
.\uninstall.ps1 -RemoveData  # also delete %ProgramData%\UpdateNag (state and logs)
```
