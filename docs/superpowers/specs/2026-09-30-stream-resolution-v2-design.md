# Emergency Stream Resolution V2 Design

## Goal
Reduce perceived stutter on restricted HTTPS/SignalR LongPolling links by reducing encoded pixel density before JPEG transport, while preserving native desktop coordinates and unattended-control behavior.

## Confirmed protocol behavior
The browser keeps its canvas in native desktop dimensions. CaptureProcessor draws each received bitmap into the destination rectangle carried in the binary header, and browser input is sent as normalized coordinates against the viewer canvas. Therefore the Desktop client can send a lower-resolution bitmap while keeping the header rectangle in native desktop coordinates. The browser will stretch the bitmap into that native rectangle and mouse/touch semantics remain unchanged.

## Stream pipeline
1. Capture native desktop frame.
2. Calculate a stream size bounded by the active profile, preserving aspect ratio and never upscaling.
3. Resize the full frame before diff detection.
4. Diff against the previous stream-space frame.
5. Crop the changed stream-space region.
6. JPEG encode the low-resolution crop.
7. Map the stream-space diff rectangle back to a native destination rectangle using floor for left/top and ceiling for right/bottom.
8. Send the existing binary header with the native destination rectangle followed by the low-resolution JPEG.

No Server, SignalR, LongPolling, or browser input protocol changes are required.

## Profiles
- Original: native resolution, upstream FPS/audio behavior, JPEG quality 80 ceiling behavior already used by the feature.
- Balanced: max 1600x900, JPEG 55, 20 FPS, audio off.
- Emergency: max 1280x720, JPEG 35, 20 FPS, audio off.
- Ultra Low: max 960x540, JPEG 20, 20 FPS, audio off.
- Custom: Native / 1600x900 / 1280x720 / 960x540 / 640x360, JPEG 20-90, FPS 2-30.

Existing V1 JSON remains readable: missing maxStreamWidth/maxStreamHeight means native resolution. Version remains 1.

## State changes
Changing stream resolution or captured source dimensions invalidates the previous stream-space frame and forces the next transmitted frame to be full-screen. Static desktops need not synthesize frames; the new setting takes effect on the next captured change.

## Diagnostics
Every approximately five seconds of transmitted frames, log source dimensions, stream dimensions, actual encoded FPS, encoded KB/s, average encoded frame size, and average JPEG encode time. Diagnostics are local logging only in V2; no server/UI protocol is added.

## Safety and rollback
Original remains a zero-scaling path. Existing service, authentication, LongPolling, audio policy, and Manager lifecycle are unchanged. The feature stays on `feature/emergency-manager-low-bandwidth`; master is untouched.