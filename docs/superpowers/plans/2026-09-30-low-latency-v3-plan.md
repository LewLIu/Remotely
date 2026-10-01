# Low Latency V3 Implementation Plan

Context: the enterprise browser is restricted to ordinary HTTPS/SignalR LongPolling. SSE, WebSocket, WebRTC, STUN/TURN are already ruled out by prior PoC. V2 proved that lower JPEG quality and lower stream resolution reduce visual fidelity/bandwidth but do not remove the stop-start input/output latency.

1. Add `PreferLatestFrame` to `RemoteStreamSettings` as an optional V1-compatible setting. Original/Balanced/Emergency remain reliable-diff modes; Ultra Low becomes 960x540, JPEG 20, 20 FPS, audio off, `PreferLatestFrame=true`. Custom can toggle it.
2. Add deterministic tests for presets, backward-compatible JSON, Manager editing, and a one-slot latest-frame queue.
3. Refactor the Desktop stream so capture/encode is decoupled from SignalR consumption by a one-slot queue. Reliable mode waits when the slot is occupied. Latest-frame mode replaces the pending frame instead of waiting.
4. In latest-frame mode encode complete low-resolution JPEG frames, not dependent diffs. The currently-transmitting frame may finish, but at most one pending frame is retained; intermediate pending frames are dropped safely.
5. Keep normal V2 differential behavior unchanged when `PreferLatestFrame=false`, and force a full refresh whenever transport mode or geometry changes.
6. Move sent-frame accounting to the consumer side so frames dropped before transport do not count as sent or acknowledged.
7. Browser viewer: when multiple complete frames have accumulated, discard everything before the last full-canvas frame, render only the freshest valid suffix, and acknowledge the newest rendered timestamp. This prevents old full frames from being replayed after the browser catches up.
8. Browser input: coalesce MouseMove so at most one invoke is in flight and only the latest pending coordinates are retained. MouseDown/MouseUp/keyboard/tap remain reliable and are never intentionally dropped.
9. Validate TypeScript/browser source separately from the existing client package CI. Browser changes require a custom server/static asset deployment; do not claim they are active on CloudBase until that deployment is completed.
10. Run Shared/Desktop/Manager/Installer tests, Windows x64 validation publish, official client package publish, then perform real enterprise-network A/B: Emergency (reliable diff) vs Ultra Low (latest full frame). Physical E2E remains pending until the user reports results.

TDD checkpoint: the first commit intentionally contains tests that reference the not-yet-implemented latency flag and latest-frame queue. The native CI run must fail before implementation is added.
