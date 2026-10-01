export interface FrameGeometry {
    imageX: number;
    imageY: number;
    imageWidth: number;
    imageHeight: number;
}

function IsNear(left: number, right: number): boolean {
    return Math.abs(left - right) <= 0.5;
}

export function IsFullCanvasFrame(
    frame: FrameGeometry,
    canvasWidth: number,
    canvasHeight: number): boolean {
    return IsNear(frame.imageX, 0) &&
        IsNear(frame.imageY, 0) &&
        IsNear(frame.imageWidth, canvasWidth) &&
        IsNear(frame.imageHeight, canvasHeight);
}

/**
 * Returns the first frame that must be rendered to reconstruct the freshest
 * valid canvas.  A full-canvas frame is self-contained, so any complete
 * frames before the newest full-canvas frame are stale and can be dropped.
 * If no full-canvas frame is present, return 0 so dependent diff frames remain
 * ordered and lossless.
 */
export function FindFreshestSafeFrameStart(
    frames: FrameGeometry[],
    canvasWidth: number,
    canvasHeight: number): number {
    let startIndex = 0;

    for (let i = 0; i < frames.length; i++) {
        if (IsFullCanvasFrame(frames[i], canvasWidth, canvasHeight)) {
            startIndex = i;
        }
    }

    return startIndex;
}
