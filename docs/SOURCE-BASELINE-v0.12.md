# Source baseline v0.12

Recorded: 2026-11-11. This is a source-preservation import, not a Bridge release.

Original project path within the supplied source ZIP: ChatGPT Synergy Projekt/apps/INTEX.ReadAloudEnhancer.

Imported unchanged: nine C# files, build.ps1, app.manifest and the required ICO asset. Personal runtime data, generated reports, binaries and unrelated project files are not part of this import.

The executable supplied under the source package's bin directory and the executable in the earlier deployment package have identical SHA-256:

```text
25fab3c804c7a447b6fb07870a3bc6a5385e1a9e0c10ec8c57f83acd261d5006
```

This proves equality of the two supplied binaries. It does not prove a reproducible build from the supplied source or identify the currently running process. Program.cs declares AssemblyVersion and AssemblyFileVersion 0.12.0.0.

Direct source checks:
- HistoryStore.Capture appends complete bytes and calls Save after metadata changes.
- Engine.FinishExport publishes an MP3 and saves the index before attempting removal of older unpinned/inactive revisions.
- Cleanup removes expired files and index entries; future remote downloads must coordinate their reads with this lifecycle.
- Program.cs contains self, UI, tray, watch and automatic-trigger test entry points.
- build.ps1 requires Windows .NET Framework/WPF and does not run tests automatically.

The available Integration Server v0.3.0 source baseline passed all 17 tests on the latest rerun. The preceding run passed 16 and failed the file-transfer test's immediate temporary-file cleanup assertion (one .part file remained). Source inspection shows the rejection response is sent before asynchronous cleanup in finally; this is a likely timing race, not yet corrected. The successful rerun does not resolve that intermittent failure. Windows-specific Enhancer tests cannot run in the current Linux environment. Running/deployed server identity remains unverified. The implementation bootstrap requires these baseline checks before functional modifications.

Next gate: confirm the deployed server version and execute the original Enhancer baseline tests on Windows. Then implement Phase A catalogue and immutable MP3 downloads with authentication and independent client playback.
