# Build and run guide v0.1

Application baseline: v0.12. Date: 2026-11-11.

## Current repository status

This repository currently contains documentation. The original C# sources, build.ps1, application manifest and test harness still need to be imported from the development project. A clone cannot yet build the v0.12 application.

The supplied deployment package contains v0.12 and older executables, release/test documentation, older PowerShell implementations and runtime data. The older PowerShell files are not the C# source of v0.12.

## Reported build environment

Technical Handoff v0.1 describes a C# x64 Windows executable built with the Windows .NET Framework csc.exe through build.ps1. Dependencies include WinForms, System.Drawing, WPF PresentationCore/WindowsBase, UI Automation and System.Web.Extensions.

The inspected host had .NET Framework 4.8. The minimum supported framework version has not been established. The build has no explicit target-framework moniker according to the handoff.

Obtain the complete source project and inspect build.ps1 before documenting an exact reproducible build command. Preserve all original test modes and manifest settings.

## Running an existing v0.12 distribution

Close the previous application through its tray Exit action, then launch the v0.12 executable using the supplied launcher or EXE. Retain the existing data directory and older executable for rollback. The original program supports a data-root override according to the handoff; check the source before changing deployment layout.

For Auto Read aloud, select the intended ChatGPT window in Options and switch to that window during the selection countdown. Then generate a new response. Enabling the option alone does not select a conversation window. Window selection is session-specific and must be repeated after restarting.

## Dependencies

The supplied dependency note identifies a Gyan FFmpeg essentials distribution, with ffmpeg.exe and ffprobe.exe under tools/. The original distributor license and build information accompany the deployment. Its stated version/checksum verification is historical documentation, not a new download verification by this update.

Consult the distributor's license and corresponding source/build information when preparing redistributable releases. Application source licensing has not been established by this documentation update.

## Source import and rollback

Import original C# files, build scripts, manifest, required assets, source-side documentation and tests. Keep personal recording histories, settings, diagnostic logs and generated outputs outside the source repository.

Tag the verified source/binary baseline before changing capture, export or retention. Record the build hash and test environment. Close the application before switching executables, and preserve a copy of its data directory before any archive-format migration.
