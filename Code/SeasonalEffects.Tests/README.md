# Seasonal effects regression checks

This small executable tests the actual application assembly without launching the
desktop application. It needs Windows, the .NET 8 SDK, and Visual Studio Build
Tools with .NET desktop support (the application uses COM references).

From a Visual Studio Developer PowerShell:

```powershell
msbuild Code/SeasonalEffects.Tests/SeasonalEffects.Tests.csproj /restore /p:Configuration=Release
dotnet Code/SeasonalEffects.Tests/bin/Release/net8.0-windows7.0/SeasonalEffects.Tests.dll
```

Run from its own build output, not an installed application's directory. The
runner temporarily creates settings under its output directory and removes them
afterward. It refuses to overwrite an existing `MasterOptions.json`.

Checks cover fresh defaults, missing/empty/legacy/malformed settings, true/false
round trips, global configuration precedence, every date in 2026 with effects
enabled and disabled, timer allocation, repeated initialization before restart,
the Halloween suspension from release 2.7.8.377 even when opted in, and localized
Appearance controls. WPF controls are constructed and laid out;
no animation frames or desktop interaction are rendered by this runner.

Manual checks before release:

- In Options > Appearance, toggle **Enable seasonal effects**, save, reopen
  Options, and restart. Check both directions on a seasonal date.
- Cancel/close Options without saving and confirm the stored setting is unchanged.
- Confirm the restart hint is visible at common display scales and translated
  text fits. Verify old external language packs fall back to bundled resources.
- With effects disabled at startup, check existing and newly created frames have
  no seasonal overlay, including after reloading frames or switching profiles.
- With effects enabled, verify Halloween remains inactive (Oct 29–Nov 2), matching
  the 2.7.8.377 release package. Compare winter (Dec 22–29) and New Year (Jan 1–2)
  scenes with that release. Use an isolated Windows VM for date changes. The
  underlying frame expansion issue is out of scope.

The setting follows the existing profile/MasterOptions configuration rules. Its
runtime value is captured on first initialization and requires an application
restart, including after switching to a profile with a different saved value.
