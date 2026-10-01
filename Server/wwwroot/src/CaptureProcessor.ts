import { ViewerApp } from "./App.js";
import { FindFreshestSafeFrameStart, FrameGeometry } from "./FrameBufferPolicy.js";
import { StreamingState } from "./Models/StreamingState.js";
import { Screen2DContext } from "./UI.js";
import { GetUint64 } from "./Utilities.js";

const FrameHeaderSize = 28;

interface CompleteFrame extends FrameGeometry {
    imageBlob: Blob;
    timestamp: number;
}


export async function ProcessStream(streamingState: StreamingState): Promise<void> {
    if (streamingState.StreamEnded) {
        return;
    }

    try {
        const chunks = streamingState.ReceivedChunks.splice(0);
        streamingState.Buffer = new Blob([streamingState.Buffer, ...chunks]);

        const bufferSize = streamingState.Buffer.size;
        if (bufferSize < FrameHeaderSize) {
            return;
        }

        const completeFrames: CompleteFrame[] = [];
        let consumedBytes = 0;

        while (bufferSize - consumedBytes >= FrameHeaderSize) {
            const headerStart = consumedBytes;
            const headerEnd = headerStart + FrameHeaderSize;
            const headerBlob = streamingState.Buffer.slice(headerStart, headerEnd);
            const headerBuffer = await headerBlob.arrayBuffer();
            const dataView = new DataView(headerBuffer);
            const imageSize = dataView.getInt32(0, true);

            if (imageSize <= 0) {
                throw new Error(`Invalid desktop frame size: ${imageSize}.`);
            }

            const frameEnd = headerEnd + imageSize;
            if (frameEnd > bufferSize) {
                break;
            }

            completeFrames.push({
                imageX: dataView.getFloat32(4, true),
                imageY: dataView.getFloat32(8, true),
                imageWidth: dataView.getFloat32(12, true),
                imageHeight: dataView.getFloat32(16, true),
                timestamp: GetUint64(dataView, 20, true),
                imageBlob: streamingState.Buffer.slice(headerEnd, frameEnd)
            });

            consumedBytes = frameEnd;
        }

        if (completeFrames.length === 0) {
            return;
        }

        // Preserve only the incomplete tail. Newly arriving chunks are kept in
        // ReceivedChunks and will be appended on the next animation frame.
        streamingState.Buffer = streamingState.Buffer.slice(consumedBytes);

        const renderStart = FindFreshestSafeFrameStart(
            completeFrames,
            Screen2DContext.canvas.width,
            Screen2DContext.canvas.height);

        let newestRenderedTimestamp: number = null;
        for (let i = renderStart; i < completeFrames.length; i++) {
            const frame = completeFrames[i];
            const bitmap = await createImageBitmap(frame.imageBlob);
            try {
                Screen2DContext.drawImage(
                    bitmap,
                    frame.imageX,
                    frame.imageY,
                    frame.imageWidth,
                    frame.imageHeight);
                newestRenderedTimestamp = frame.timestamp;
            }
            finally {
                bitmap.close();
            }
        }

        if (newestRenderedTimestamp !== null) {
            // Frame acknowledgements are themselves coalesced by MessageSender,
            // so a slow LongPolling uplink never queues one ACK per stale frame.
            void ViewerApp.MessageSender.SendFrameReceived(newestRenderedTimestamp);
        }
    }
    catch (ex) {
        console.error("Capture processing error.  Resetting stream buffer.", ex);
        streamingState.Buffer = new Blob();
    }
    finally {
        requestAnimationFrame(() => {
            ProcessStream(streamingState);
        });
    }
}
