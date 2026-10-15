# LibreLinkUp setup

GlucoseTray reads shared Libre readings directly from Abbott's cloud, without a Nightscout server.

1. Keep the official Libre app collecting and uploading sensor readings on your phone.
2. In that app, open **Connected Apps > LibreLinkUp > Manage > Add connection** and invite an email address you control for a follower account.
3. In the official LibreLinkUp phone app, sign in to that follower account, verify the email, accept the sharing invitation and any pending terms. Confirm a current reading appears.
4. Extract the Windows trial ZIP into its own folder and run `GlucoseTray.exe`. Only run one instance at a time.
5. When **GlucoseTray — LibreLinkUp sign in** opens, enter your follower email and password and click **Save and connect**. If you cancel, reopen it by right-clicking the tray icon and choosing **LibreLinkUp sign in...**.
6. Compare the tray reading and time with the official Libre app. The **^** menu beside the clock may hide the icon. Double-click it for full status details.

## Password storage

The dialog masks the password and saves directly to **Windows Credential Manager**, under `GlucoseTray/LibreLinkUp/<follower email>`. It never writes the password to the settings file. Windows protects the saved credential for your Windows user; GlucoseTray reads it to authenticate with Abbott. Cloud session tokens stay in memory.

Only the follower email remains in `appsettings.json`, to select the saved credential. To replace the password, use **LibreLinkUp sign in...** again. **LibreLinkUp sign out** deletes the active account's saved password, clears its account setting, and stops using its cached reading/session. Other accounts' saved credentials are not deleted.

If an older settings file contains `LibreLinkUpPassword`, startup migrates its plaintext or DPAPI-encrypted value into Credential Manager, then removes the field. A failed vault write leaves the original file intact. Migration does not erase copies already retained by editors or backups.

## Diagnostic privacy

Application logs contain only fixed operation/failure categories, GET/POST/OTHER, and numeric HTTP status codes. Request URLs, headers, bodies, response reason phrases/bodies, account settings, and raw exceptions (including inner exceptions) are excluded. Factory HTTP logging is removed for both clients, including at Trace level. Regression tests inject synthetic credentials into those paths and inspect both rendered logs and structured fields.

All glucose requests require HTTPS without credentials embedded in the URL, and automatic HTTP redirects are disabled. LibreLinkUp's bounded region selection still uses its fixed HTTPS host allowlist. For Nightscout, configure the final HTTPS service address; plain HTTP and endpoints requiring redirects fail rather than sending credentials onward.

The LibreLinkUp password remains in Windows Credential Manager and is sent only in the required HTTPS authentication request to an allowed Abbott host. Authentication necessarily places passwords/tokens in process memory. These controls do not protect against a compromised Windows account, a debugger/crash dump, or logging performed by the remote service. Earlier versions could log Nightscout access tokens or Dexcom session IDs in request URLs on failure; do not share logs from older builds without inspecting them.

## Connection and display settings

Only non-password settings belong in `appsettings.json`:

```json
{
  "DataSource": "LibreLinkUp",
  "LibreLinkUpUsername": "your-follower-email@example.com",
  "LibreLinkUpRegion": "EU",
  "LibreLinkUpPatientId": "",
  "LibreLinkUpApiVersion": "4.16.0",
  "DisplayUnitType": "Mmol",
  "RefreshIntervalInMinutes": 1,
  "EnableAlerts": true
}
```

Merge fields into existing settings rather than replacing thresholds. Use the high/low thresholds agreed with your diabetes team; bundled numbers are upstream defaults. Use `Mmol` for mmol/L or `Mg` for mg/dL. `ServerUnitType` is ignored for LibreLinkUp: the API's `ValueInMgPerDl` is converted independently of account units.

`EU` is the initial region for the Swiss trial. The client follows bounded region redirects. Supported codes: `AE`, `AP`, `AU`, `CA`, `DE`, `EU`, `EU2`, `FR`, `JP`, `US`, `LA`, `RU`, `CN`. Unknown regions, HTTP redirects, and redirect loops are rejected.

With one connection, leave `LibreLinkUpPatientId` empty. With multiple connections, use the exact patient ID from `/llu/connections`, or a dedicated follower account with only your own connection. Ambiguous, missing, and unmatched selections clear cached readings. Changing accounts or patients clears old readings, including results arriving after settings changed during a request.

## Status, alerts, and limits

Right-click the tray number and select **Show glucose widget** for a draggable, always-on-top reading with units, trend, and age. Use its **×** to hide it; the tray menu brings it back. Visibility is saved as `ShowGlucoseWidget`; the initial setting is off. On first use the widget starts near the bottom-right of the primary screen. Its position is saved after dragging, hiding, or exiting in `widget-position.json` beside the executable. That file contains only X/Y coordinates, is separate from account settings, and is excluded from Git and packaged defaults. At startup or when reopening, positions are kept within an available monitor's working area, including monitors with negative coordinates. Missing or corrupt preferences use the default position; write failures leave the widget usable for the session.

The widget and tray use green for **In range**, amber for **Low/High**, red for **Critical low/high**, and gray for stale, disconnected, or missing data. The widget's text labels distinguish low from high. These states use the existing configured thresholds. Light-mode tray colors use darker green and amber for contrast. Reading age updates every second; the widget turns stale without needing another successful poll and hides the trend arrow while disconnected or stale.

Source UTC measurement time is preserved. An outage for the same patient can retain the last reading with its original timestamp and an error status. It ages into the stale display; polling does not make it fresh. Failed refreshes do not emit glucose alerts. Setup errors appear on hover and double-click, with one Windows notification per distinct error; recovery clears the error.

Glucose alerts are **Windows notification balloons on alert-level transitions**, not repeating audible alarms. Windows settings affect visibility and sound. Keep official Libre alarms enabled.

The ZIP is an unsigned local trial, not an upstream release. The 192-test suite includes position persistence/recovery, secret-disclosure regressions, production-DI cloud scenarios, a synthetic Windows Credential Manager round-trip, actual native icon colors and handle lifetime, background-thread display refresh, widget reopen behavior, and stale-data status checks. Live Swiss account login and shared-connection polling have been verified locally. Native widget states were rendered and inspected; Windows notification sound, sleep/resume, and mixed-DPI/multiple-monitor behavior still need manual validation.

The unofficial API follows the protocol in the [Nightscout LibreLinkUp uploader](https://github.com/timoschlueter/nightscout-librelink-up); compatibility can change. Complete consent and terms in the official app. GlucoseTray does not accept them or change sensor settings. Keep passwords, tokens, patient identifiers, and personal readings out of shared logs.
