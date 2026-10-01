import { FindFreshestSafeFrameStart, IsFullCanvasFrame } from "../../Server/wwwroot/src/FrameBufferPolicy.js";

function assert(condition: boolean, message: string): void {
    if (!condition) throw new Error(message);
}

const full = { imageX: 0, imageY: 0, imageWidth: 1920, imageHeight: 1080 };
const diffA = { imageX: 10, imageY: 20, imageWidth: 300, imageHeight: 100 };
const diffB = { imageX: 400, imageY: 500, imageWidth: 200, imageHeight: 150 };

assert(IsFullCanvasFrame(full, 1920, 1080), "A native full-screen frame must be recognized as self-contained.");
assert(!IsFullCanvasFrame(diffA, 1920, 1080), "A partial diff must not be treated as self-contained.");
assert(
    FindFreshestSafeFrameStart([diffA, diffB], 1920, 1080) === 0,
    "Diff-only batches must retain every frame in order.");
assert(
    FindFreshestSafeFrameStart([diffA, full, diffB], 1920, 1080) === 1,
    "Frames before a full-screen reset may be discarded.");
assert(
    FindFreshestSafeFrameStart([full, diffA, full, diffB], 1920, 1080) === 2,
    "The newest full-screen reset must win.");

console.log("FrameBufferPolicy tests passed.");
