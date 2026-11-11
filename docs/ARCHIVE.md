# ReadAloud archive contract notes v0.1

Application baseline: v0.12. Date: 2026-11-11.

These notes derive from Technical Handoff v0.1 and the supplied deployment package. They describe the reported existing archive; they are not a new published wire schema. Exact field types and all validation rules must be checked against the C# sources before implementation.

## Layout

| Relative location | Purpose |
| --- | --- |
| history-index/ | Per-recording JSON metadata |
| history-cache/ | Owned raw AAC/MP3 frame archives, when retained |
| history/ | Exported MP3 recordings, when retained |
| cache-recovery/ | Recovery copies created by explicit cache-removal operations |
| intex-settings.json | Application configuration |
| tools/ | Locally bundled conversion tools |

A deployment ZIP may contain indexes without the corresponding media files. Presence of an index alone is not proof that an MP3 is downloadable.

## Identity and byte accounting

The handoff identifies Entry.Id as the recording identity. SourceKey and source paths are internal bookkeeping and must not be exposed through a remote catalogue.

CopiedBytes describes the committed source prefix. ExportedBytes describes source bytes covered by an MP3 revision; it is not the MP3 file length. Mp3File and SavedAtUtc identify the currently indexed export. Bridge must add an immutable revision identity, actual MP3 byte length and SHA-256.

The current index can point to a replacement export while previous revisions are removed. Remote download implementations need a lease, stable open handle with appropriate Windows sharing, or an immutable staged revision so retention/replacement cannot invalidate a transfer.

## Completeness and retention

Complete audio frames are not proof of complete utterance capture. The reported Partial flag includes an incomplete trailing frame or recent growth. Temporary inactivity must not be represented as confirmed utterance completion.

The handoff reports default retention of 30 days, with zero disabling cleanup. Pinned, locally active and exporting entries are exempt under the existing implementation. The precise interaction with a future remote download lease requires source-level verification.

## Remote catalogue projection

A future catalogue should expose recording/capture IDs, capture timestamp, known duration, verified format, capture state, available MP3 revisions, size/hash and availability. Use an explicit field allowlist. Resolve recordings by validated IDs rather than accepting client-supplied local paths.

Downloaded revisions must be verified against their exact length and SHA-256. Missing, replaced and expired revisions must produce explicit outcomes.
