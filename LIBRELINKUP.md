# LibreLinkUp setup and trial-build notes

This adds direct LibreLinkUp readings to GlucoseTray's existing numeric tray icon, colour thresholds, stale-reading display, and Windows notifications. It does not require Nightscout.

## Connect your account

1. Keep the official Libre app collecting sensor readings on your iPhone and uploading them to Abbott.
2. In the Libre app, open **Connected Apps > LibreLinkUp > Manage > Add connection** and invite an email address you control for a follower account.
3. Install the official **LibreLinkUp** phone app, register/sign in to that follower account, verify the email address, accept the invitation and pending terms, and confirm that a current reading appears.
4. Extract the trial-build ZIP into its own folder. Keep it separate from any existing GlucoseTray installation. Only run one instance at a time.
5. Edit `appsettings.json` locally, replacing the empty `LibreLinkUpUsername` and `LibreLinkUpPassword` with the follower account credentials. Do not send credentials in chat.
6. Confirm `DataSource` is `LibreLinkUp`. Use `DisplayUnitType: Mmol` for mmol/L or `Mg` for mg/dL. The trial settings use a one-minute refresh. Select your own high/low thresholds with your care team; the bundled numbers are unchanged upstream defaults.
7. Launch `GlucoseTray.exe`. If Windows hides the icon, open the **^** tray overflow beside the clock and drag it into the visible tray. Compare the number and reading time with the official Libre app. Double-click the tray number for details.

The password is encrypted with Windows DPAPI on startup and when saved settings are processed. Session tokens remain in memory. To replace the saved password, replace the entire `ENC:...` value with the new password locally and save. Uninstall by exiting the app, disabling its Run on startup setting if enabled, and removing its extracted folder.

## Settings

Merge these fields into existing settings if using a source build:

```json
{
  "DataSource": "LibreLinkUp",
  "LibreLinkUpUsername": "your-follower-email@example.com",
  "LibreLinkUpPassword": "your-follower-password",
  "LibreLinkUpRegion": "EU",
  "LibreLinkUpPatientId": "",
  "LibreLinkUpApiVersion": "4.16.0",
  "DisplayUnitType": "Mmol",
  "RefreshIntervalInMinutes": 1,
  "EnableAlerts": true
}
```

`EU` is the starting region for this Swiss trial; the client follows up to three known regions returned by Abbott. Supported codes: `AE`, `AP`, `AU`, `CA`, `DE`, `EU`, `EU2`, `FR`, `JP`, `US`, `LA`, `RU`, `CN`. Unknown regions, HTTP redirects, and redirect loops are rejected.

With exactly one shared connection, leave `LibreLinkUpPatientId` empty. With multiple connections, set the exact patient ID returned by `/llu/connections`, or use a dedicated follower account with only your own connection. The app rejects ambiguous or unmatched selections instead of choosing the first patient.

`ServerUnitType` is ignored for LibreLinkUp: the API's `ValueInMgPerDl` is converted independently of the account's display units. `FactoryTimestamp` is parsed as UTC, rather than using the patient's local `Timestamp`. If the cloud stops updating, the last successful reading ages into the existing stale display; refreshing does not make it fresh.

Cached readings are scoped to the configured follower account, region, and patient selection. Changing these clears the previous reading, including when settings change during an in-flight request. Missing, ambiguous, or unmatched connections also clear the cache. A successful automatic patient change resets the glucose alert state.

Sign-in and setup failures appear in the tray's hover status and double-click details, with one Windows notification per distinct error. Repeated failures do not notify on every poll. A same-patient network outage may retain the last reading with its original timestamp and an error status; no glucose alerts are emitted from failed refreshes. A successful refresh clears the connection error.

## Alerts and limits

GlucoseTray's existing alerts are **Windows notification balloons when entering an alert level**, not a repeating audible alarm. Windows notification settings can affect their visibility and sound. Stale readings do not produce glucose alerts. Keep official Libre alarms enabled.

This build has automated tests with simulated API responses. **A live Libre 3 Plus / Swiss follower account has not yet been tested**, and the Windows tray UI has not yet been visually tested for this build. The ZIP is a local, unsigned trial build, not an upstream release.

LibreLinkUp uses an unofficial API whose compatibility can change. This implementation follows the protocol documented in the [Nightscout LibreLinkUp uploader](https://github.com/timoschlueter/nightscout-librelink-up): login, API version/product headers, SHA-256 account ID, connections, UTC timestamp, and trend codes. It does not accept terms for you or change sensor settings. Authentication tokens are reused until expiry and renewed once on authentication rejection.

If no reading appears, first check the follower account in the official LibreLinkUp app, accept pending terms, and confirm your credentials and sharing connection. Keep personal readings, passwords, tokens, and account IDs out of any shared logs.
