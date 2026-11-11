# Build and run guide v0.2

Application source baseline: v0.12. Date: 2026-11-11.

## Build

Original sources are in src/INTEX.ReadAloudEnhancer. The imported build.ps1 is unchanged. It uses the Windows .NET Framework 64-bit csc.exe, emits an x64 WinForms executable and references WPF, UI Automation, System.Drawing and System.Web.Extensions.

Run in Windows PowerShell on a host with the required .NET Framework and reference assemblies:

```powershell
Set-Location .\src\INTEX.ReadAloudEnhancer
.\build.ps1
```

Output: bin/INTEX ReadAloud Enhancer v0.12.exe. The manifest and icon are included. The script declares a Tests switch but does not execute tests; invoke the test modes explicitly.

The previously inspected host had .NET Framework 4.8. The minimum supported framework version has not been established. A fresh Windows build was not executed during this cloud source import.

## Dependencies and data root

Normal startup defaults the data root to the EXE directory; --data-root overrides it. The root must be writable. Existing deployment dependencies include tools/ffmpeg.exe and tools/ffprobe.exe with their distributor license/build notices. Preserve those from the existing deployment or obtain the appropriate upstream distribution. This repository does not include the dependency binaries.

The build alone does not populate bin/tools. Prepare a separate test root with the needed tools before running conversion/playback tests. The test fixture generator uses tools/ffmpeg.exe under the supplied data root.

## Baseline tests on Windows

Example using a separately prepared test root and an existing output directory:

```powershell
$exe = Join-Path $PWD 'bin\INTEX ReadAloud Enhancer v0.12.exe'
$testRoot = 'C:\INTEX-Test-Root'
$reportDir = Join-Path $testRoot 'reports'
New-Item -ItemType Directory -Force $reportDir | Out-Null
foreach ($mode in @('self-test','ui-test','tray-test','watch-test','auto-trigger-test')) {
    $report = Join-Path $reportDir ($mode + '.json')
    $arguments = '--' + $mode + ' --data-root "' + $testRoot + '" --report "' + $report + '"'
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw ('Failed: ' + $mode) }
}
```

UI/playback modes require an interactive Windows desktop. Read each JSON report; process launch alone is not a pass. The activation-host test requires a second instance and is not covered by the loop above. Diagnostic --probe, --compatibility and --auto-probe modes have different purposes; they are not substitutes for functional tests.

## Running and rollback

Close the previous instance through its tray Exit action, then launch the selected executable using the established writable data root. Retain the prior executable and back up the complete data directory before any future archive-format migration.

For Auto Read aloud, explicitly select the ChatGPT conversation window in Options, then generate a new response. The selected window is session-specific. Enabling the option alone does not choose a window.

The imported source has no functional modifications. The exact running server build on the user's Windows host still needs confirmation before Bridge integration proceeds. See VALIDATION.md.
