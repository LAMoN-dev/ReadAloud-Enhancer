# Validation and provenance v0.2

Date: 2026-11-11. Application baseline: v0.12.

## Evidence classes

| Evidence | Status |
| --- | --- |
| GitHub tree before the first documentation commit | Independently checked: only README.md |
| Supplied deployment ZIP | Inspected: application binaries, release/test notes, history index, dependencies and legacy scripts; no .cs, .csproj or .sln files |
| Technical Handoff v0.1 | Reviewed as an earlier source-inspection report |
| v0.12 release and test notes | Reviewed; original tests were not rerun in this documentation update |
| Current C# source | Imported unchanged from the newly supplied source ZIP; critical archive code inspected |
| Actual Enhancer or server process on the user's Windows host | Not inspected in this update |
| Bridge end-to-end operation | Not implemented or verified by this update |

## Historical v0.12 validation

The supplied November 1 report describes local Windows 11 x64 testing against the ChatGPT Windows executable, file version 156.0.8078.48. Reported coverage includes persisted Watch/WatchAndPlay/Off state, fallback for old settings, pending settings writes at shutdown, themes, slider input, tray behavior, parser/conversion/retention cases and local playback queues.

It reports passing synthetic Auto Read aloud coordinator scenarios and a read-only UI Automation probe. It explicitly did not activate Read aloud during those tests, so it does not prove automatic triggering through a complete newly generated real response. Multi-monitor/DPI coverage was limited.

The November 11 Technical Handoff reports source inspection, matching deployed/workspace binary hashes and limited archive header/timestamp samples. It reports incremental capture, but does not establish full-utterance completeness or one-way playback latency.

## This documentation update

Environment: ChatGPT Work, cloud Linux workspace. User-facing client context: desktop web. Reasoning effort/Think harder selection for this turn: not independently recorded; the implementation bootstrap requests HIGH.

The available Integration Server v0.3.0 baseline passed 17 automated tests in the preceding inspection, including backup, file transfer, messages and PCM. During source import, a fresh run passed 16/17: tests/files.test.mjs failed the immediate .part-file cleanup assertion. A subsequent full rerun passed 17/17. The response-before-cleanup ordering in server/src/files.ts suggests a timing race; no runtime fix was made in this source import. Those server tests do not validate the Enhancer or Bridge.

Documentation checks cover relative link resolution, explicit provenance and exclusion of personal runtime content. No new claim of Windows hardware acceptance is made.

## Required implementation acceptance

After source import, rerun the original Enhancer tests, establish the deployed server build and preserve a recoverable baseline. Start with authenticated MP3 catalogue/download, verify file size and SHA-256 on both client platforms, and test retention during an active download.

Live streaming follows archive acceptance. Test independent play/pause/seek/volume, reconnect, source-generation changes, slow clients, restarts, expired ranges and interrupted downloads. Report synthetic, cloud and physical-device results separately.

## Source import follow-up

A second ZIP supplied the original C# project. See [source baseline](SOURCE-BASELINE-v0.12.md) for binary hashes and checks. Earlier deployment-ZIP findings remain correct: that earlier package contained no C# sources. The new source import changes availability, not historical test outcomes. No fresh Windows build or Enhancer runtime test is claimed.
