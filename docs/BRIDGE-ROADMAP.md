# INTEX ReadAloud Bridge roadmap v0.2

Date: 2026-11-11. Capability identifier: readaloud-v1.
Status: authorized design; implementation blocked pending original Enhancer source and deployment-baseline confirmation.

## Goal

Aborym IV captures and stores the audio through ReadAloud Enhancer. The existing INTEX Integration Server distributes recordings and retained compressed audio to independently controlled Android and Windows clients. Reuse generated audio without a second TTS engine, paid API or cloud relay.

## Incremental milestones

1. Archive catalogue and immutable MP3 retrieval. Authenticate remote access, project approved metadata, verify size/hash and coordinate active downloads with retention.
2. Enhancer publisher. Use a dedicated authenticated loopback WebSocket, bounded asynchronous queues, archive-change notifications and reconnect recovery.
3. One live subscriber. Preserve compressed frames with source generations, ordering, byte offsets, media timestamps, duplicate detection, bounded retention and explicit gap/expiry responses.
4. Android playback. Validate the actual AAC/MP3 decoder path and implement independent local controls and offline MP3 playback.
5. Windows playback. Provide equivalent independent controls and queues.
6. Multi-client reliability. Test Enhancer, Android and laptop together, including pause/seek isolation, reconnect, restarts, revision replacement and slow receivers.

Archive retrieval must pass acceptance before live fan-out.

## Publisher integration points

The source-inspection handoff identifies HistoryStore.Capture after successful index save as the committed-range publication boundary. Engine.FinishExport after MP3/index publication is the recording-revision notification boundary. Verify both in the original code before editing.

No network send should block scanning or UI work. A failed publication must not corrupt the archive or interrupt local playback.

## Provisional protocol operations

- readaloud.source.started
- readaloud.audio.segment
- readaloud.source.completed
- readaloud.recording.available
- readaloud.history.list
- readaloud.recording.get
- readaloud.stream.subscribe
- readaloud.stream.unsubscribe

These names are design proposals, not available endpoints. Keep readaloud-v1 separate from legacy fixed PCM framing and preserve messaging, files, backup and existing connections.

## Acceptance gate

Play the same recording in Enhancer, Android and Windows. Pause Android while Windows continues; seek Windows while Android retains its cursor; resume Android without affecting Enhancer. Verify downloaded bytes, interruption/recovery and explicit errors when required retained data has expired.

Measure capture observation, committed publication, delivery and audible playback separately. Do not infer one-way latency from unsynchronized timestamps or assume temporary source inactivity means utterance completion.
