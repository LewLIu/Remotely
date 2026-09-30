using SkiaSharp;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Enums;
using Remotely.Shared.Models;
using Microsoft.Extensions.Logging;
using Remotely.Shared.Helpers;
using Remotely.Shared.Models.Dtos;
using Remotely.Shared.Services;
using Microsoft.IO;
using Bitbound.SimpleMessenger;
using Remotely.Desktop.Shared.Messages;
using System.Diagnostics;

namespace Remotely.Desktop.Shared.Services;

public interface IScreenCaster : IDisposable
{
    Task BeginScreenCasting(ScreenCastRequest screenCastRequest);
}

internal class ScreenCaster : IScreenCaster
{
    private readonly IAppState _appState;
    private readonly ICursorIconWatcher _cursorIconWatcher;
    private readonly FrameRateGate _frameRateGate;
    private readonly IImageHelper _imageHelper;
    private readonly ILogger<ScreenCaster> _logger;
    private readonly CancellationTokenSource _metricsCts = new();
    private readonly RecyclableMemoryStreamManager _recycleStreams = new();
    private readonly IRemoteStreamSettingsProvider _streamSettingsProvider;
    private readonly ISessionIndicator _sessionIndicator;
    private readonly IShutdownService _shutdownService;
    private readonly ISystemTime _systemTime;
    private readonly IViewerFactory _viewerFactory;
    private readonly IDisposable[] _messengerRegistrations;
    private bool _isWindowsSessionEnding;

    public ScreenCaster(
        IAppState appState,
        IViewerFactory viewerFactory,
        ICursorIconWatcher cursorIconWatcher,
        ISessionIndicator sessionIndicator,
        IShutdownService shutdownService,
        IImageHelper imageHelper,
        ISystemTime systemTime,
        IRemoteStreamSettingsProvider streamSettingsProvider,
        FrameRateGate frameRateGate,
        IMessenger messenger,
        ILogger<ScreenCaster> logger)
    {
        _appState = appState;
        _cursorIconWatcher = cursorIconWatcher;
        _sessionIndicator = sessionIndicator;
        _shutdownService = shutdownService;
        _imageHelper = imageHelper;
        _systemTime = systemTime;
        _streamSettingsProvider = streamSettingsProvider;
        _frameRateGate = frameRateGate;
        _viewerFactory = viewerFactory;
        _logger = logger;

        _messengerRegistrations =
        [
            messenger.Register<WindowsSessionSwitchedMessage>(this, HandleWindowsSessionSwitchedMessage),
            messenger.Register<WindowsSessionEndingMessage>(this, HandleWindowsSessionEndingMessage)
        ];
    }

    public async Task BeginScreenCasting(ScreenCastRequest screenCastRequest)
    {
        await BeginScreenCastingImpl(screenCastRequest).ConfigureAwait(false);
    }

    public void Dispose()
    {
        foreach (var registration in _messengerRegistrations)
        {
            try { registration.Dispose(); } catch { }
        }
        _metricsCts.Cancel();
        _metricsCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task BeginScreenCastingImpl(ScreenCastRequest screenCastRequest)
    {
        using var viewer = _viewerFactory.CreateViewer(screenCastRequest.RequesterName, screenCastRequest.ViewerId);

        try
        {
            viewer.Name = screenCastRequest.RequesterName;
            viewer.ViewerConnectionId = screenCastRequest.ViewerId;
            var screenBounds = viewer.Capturer.CurrentScreenBounds;

            _logger.LogInformation(
                "Starting screen cast.  Requester: {viewerName}. Viewer ID: {viewerViewerConnectionID}.  App Mode: {mode}",
                viewer.Name,
                viewer.ViewerConnectionId,
                _appState.Mode);

            _appState.Viewers.AddOrUpdate(viewer.ViewerConnectionId, viewer, (id, v) => viewer);

            if (_appState.Mode == AppMode.Attended) _appState.InvokeViewerAdded(viewer);
            if (_appState.Mode == AppMode.Unattended && screenCastRequest.NotifyUser) _sessionIndicator.Show();

            await viewer.SendScreenData(
                viewer.Capturer.SelectedScreen,
                viewer.Capturer.GetDisplayNames(),
                screenBounds.Width,
                screenBounds.Height);

            await viewer.SendCursorChange(_cursorIconWatcher.GetCurrentCursor());
            await viewer.SendWindowsSessions();

            viewer.Capturer.ScreenChanged += async (sender, bounds) =>
            {
                await viewer.SendScreenSize(bounds.Width, bounds.Height);
            };

            _ = Task.Run(() => LogMetrics(viewer, _metricsCts.Token));
            using var sessionEndSignal = new SemaphoreSlim(0, 1);
            await viewer.SendDesktopStream(GetDesktopStream(viewer, sessionEndSignal), screenCastRequest.StreamId);
            if (!await sessionEndSignal.WaitAsync(TimeSpan.FromHours(8)))
            {
                _logger.LogWarning("Timed out while waiting for session to end.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while starting screen casting.");
        }
        finally
        {
            _logger.LogInformation(
                "Ended desktop stream.  Requester: {viewerName}. Viewer ID: {viewerConnectionID}. Viewer Responsive: {isResponsive}.  Viewer Disconnected Requested: {viewerDisconnectRequested}. Windows Session Ending: {windowsSessionEnding}",
                viewer.Name,
                viewer.ViewerConnectionId,
                viewer.IsResponsive,
                viewer.DisconnectRequested,
                _isWindowsSessionEnding);

            _appState.Viewers.TryRemove(viewer.ViewerConnectionId, out _);
            Disposer.TryDisposeAll(viewer);

            if (_appState.Viewers.IsEmpty && _appState.Mode == AppMode.Unattended)
            {
                _logger.LogInformation("No more viewers.  Calling shutdown service.");
                await _shutdownService.Shutdown();
            }
        }
    }

    private async IAsyncEnumerable<byte[]> GetDesktopStream(IViewer viewer, SemaphoreSlim sessionEndedSignal)
    {
        await Task.Yield();

        SKBitmap? previousScaledFrame = null;
        var previousSourceSize = SKSizeI.Empty;
        var previousStreamSize = SKSizeI.Empty;
        var hasPreviousGeometry = false;
        var diagnostics = new StreamDiagnosticsTracker(TimeProvider.System);

        try
        {
            while (!viewer.DisconnectRequested && viewer.IsResponsive && !_isWindowsSessionEnding)
            {
                await viewer.ApplyAutoQuality();

                if (!await viewer.WaitForViewer())
                {
                    _logger.LogWarning("Viewer is behind on frames and did not catch up in time.");
                }

                var settings = _streamSettingsProvider.Current;
                await _frameRateGate.WaitAsync(settings.MaxFps);

                var result = viewer.Capturer.GetNextFrame();
                if (!result.IsSuccess)
                {
                    await Task.Yield();
                    continue;
                }

                var sourceFrame = result.Value;
                var sourceSize = new SKSizeI(sourceFrame.Width, sourceFrame.Height);
                var streamSize = StreamResolutionPolicy.CalculateSize(
                    sourceFrame.Width,
                    sourceFrame.Height,
                    settings.MaxStreamWidth,
                    settings.MaxStreamHeight);
                var scaled = streamSize.Width != sourceFrame.Width || streamSize.Height != sourceFrame.Height;
                var geometryChanged = !hasPreviousGeometry ||
                    sourceSize != previousSourceSize ||
                    streamSize != previousStreamSize;

                SKBitmap? scaledFrame = null;
                SKBitmap frameForTransport = sourceFrame;
                SKRect diffArea;
                SKRect destinationArea;

                try
                {
                    if (scaled)
                    {
                        scaledFrame = ResizeFrame(sourceFrame, streamSize);
                        frameForTransport = scaledFrame;
                        diffArea = _imageHelper.GetDiffArea(frameForTransport, previousScaledFrame, geometryChanged);

                        previousScaledFrame?.Dispose();
                        previousScaledFrame = frameForTransport.Copy();

                        if (diffArea.IsEmpty)
                        {
                            viewer.Capturer.CaptureFullscreen = false;
                            previousSourceSize = sourceSize;
                            previousStreamSize = streamSize;
                            hasPreviousGeometry = true;
                            await Task.Yield();
                            continue;
                        }

                        var mapped = StreamResolutionPolicy.MapToSource(
                            ToRectI(diffArea),
                            sourceFrame.Width,
                            sourceFrame.Height,
                            streamSize.Width,
                            streamSize.Height);
                        destinationArea = new SKRect(mapped.Left, mapped.Top, mapped.Right, mapped.Bottom);
                    }
                    else
                    {
                        previousScaledFrame?.Dispose();
                        previousScaledFrame = null;

                        diffArea = geometryChanged && hasPreviousGeometry
                            ? new SKRect(0, 0, sourceFrame.Width, sourceFrame.Height)
                            : viewer.Capturer.GetFrameDiffArea();
                        destinationArea = diffArea;

                        if (diffArea.IsEmpty)
                        {
                            viewer.Capturer.CaptureFullscreen = false;
                            previousSourceSize = sourceSize;
                            previousStreamSize = streamSize;
                            hasPreviousGeometry = true;
                            await Task.Yield();
                            continue;
                        }
                    }

                    viewer.Capturer.CaptureFullscreen = false;
                    previousSourceSize = sourceSize;
                    previousStreamSize = streamSize;
                    hasPreviousGeometry = true;

                    using var croppedFrame = _imageHelper.CropBitmap(frameForTransport, diffArea);
                    var encodeStarted = Stopwatch.GetTimestamp();
                    var encodedImageBytes = _imageHelper.EncodeBitmap(croppedFrame, SKEncodedImageFormat.Jpeg, viewer.ImageQuality);
                    var encodeDuration = Stopwatch.GetElapsedTime(encodeStarted);

                    if (encodedImageBytes.Length == 0) continue;

                    viewer.IncrementFpsCount();
                    viewer.AppendSentFrame(new SentFrame(encodedImageBytes.Length, _systemTime.Now));

                    var snapshot = diagnostics.RecordFrame(
                        sourceFrame.Width,
                        sourceFrame.Height,
                        streamSize.Width,
                        streamSize.Height,
                        encodedImageBytes.Length,
                        encodeDuration);
                    if (snapshot is not null)
                    {
                        _logger.LogInformation(
                            "Stream diagnostics. Source: {sourceWidth}x{sourceHeight}. Stream: {streamWidth}x{streamHeight}. Actual FPS: {fps:F1}. Encoded: {kbps:F1} KB/s. Avg frame: {avgFrame:F1} KB. Avg JPEG encode: {encodeMs:F1} ms.",
                            snapshot.SourceWidth,
                            snapshot.SourceHeight,
                            snapshot.StreamWidth,
                            snapshot.StreamHeight,
                            snapshot.ActualFps,
                            snapshot.EncodedKBytesPerSecond,
                            snapshot.AverageFrameKBytes,
                            snapshot.AverageEncodeMilliseconds);
                    }

                    using var frameStream = _recycleStreams.GetStream();
                    using var writer = new BinaryWriter(frameStream);
                    writer.Write(encodedImageBytes.Length);
                    writer.Write(destinationArea.Left);
                    writer.Write(destinationArea.Top);
                    writer.Write(destinationArea.Width);
                    writer.Write(destinationArea.Height);
                    writer.Write(DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    writer.Write(encodedImageBytes);
                    frameStream.Seek(0, SeekOrigin.Begin);

                    foreach (var chunk in frameStream.ToArray().Chunk(50_000))
                    {
                        yield return chunk;
                    }
                }
                finally
                {
                    scaledFrame?.Dispose();
                }
            }
        }
        finally
        {
            previousScaledFrame?.Dispose();
            sessionEndedSignal.Release();
        }
    }

    private static SKBitmap ResizeFrame(SKBitmap source, SKSizeI targetSize)
    {
        var resized = source.Resize(
            new SKImageInfo(targetSize.Width, targetSize.Height, source.ColorType, source.AlphaType),
            SKFilterQuality.Medium);
        return resized ?? throw new InvalidOperationException("Unable to resize desktop frame.");
    }

    private static SKRectI ToRectI(SKRect rect)
        => new(
            (int)Math.Floor(rect.Left),
            (int)Math.Floor(rect.Top),
            (int)Math.Ceiling(rect.Right),
            (int)Math.Ceiling(rect.Bottom));

    private Task HandleWindowsSessionEndingMessage(object subscriber, WindowsSessionEndingMessage arg)
    {
        _logger.LogInformation("Windows session ending.  Stopping screen cast.");
        _isWindowsSessionEnding = true;
        return Task.CompletedTask;
    }

    private Task HandleWindowsSessionSwitchedMessage(object subscriber, WindowsSessionSwitchedMessage arg)
    {
        _logger.LogInformation("Windows session switched.  Stopping screen cast.");
        _isWindowsSessionEnding = true;
        return Task.CompletedTask;
    }

    private async Task LogMetrics(IViewer viewer, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await viewer.CalculateMetrics();

            var metrics = new SessionMetricsDto(
                Math.Round(viewer.CurrentMbps, 2),
                viewer.CurrentFps,
                viewer.RoundTripLatency.TotalMilliseconds,
                viewer.Capturer.IsGpuAccelerated);

            _logger.LogDebug(
                "Current Mbps: {currentMbps}. Current FPS: {currentFps}. Roundtrip Latency: {roundTripLatency}ms. Image Quality: {imageQuality}",
                metrics.Mbps,
                metrics.Fps,
                metrics.RoundTripLatency,
                viewer.ImageQuality);

            await viewer.SendSessionMetrics(metrics);
        }
    }
}
