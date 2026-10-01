using Remotely.Desktop.Shared.Services;
using SkiaSharp;

namespace Remotely.Desktop.Win.Tests;

[TestClass]
public class StreamResolutionPolicyTests
{
    [DataTestMethod]
    [DataRow(2560, 1440, 1600, 900, 1600, 900)]
    [DataRow(2560, 1440, 1280, 720, 1280, 720)]
    [DataRow(2560, 1440, 960, 540, 960, 540)]
    [DataRow(2560, 1440, 640, 360, 640, 360)]
    [DataRow(1920, 1200, 1280, 720, 1152, 720)]
    public void CalculateSize_PreservesAspectAndHonorsCaps(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight, int expectedWidth, int expectedHeight)
    {
        var actual = StreamResolutionPolicy.CalculateSize(sourceWidth, sourceHeight, maxWidth, maxHeight);
        Assert.AreEqual(new SKSizeI(expectedWidth, expectedHeight), actual);
    }

    [TestMethod]
    public void CalculateSize_NeverUpscales()
    {
        Assert.AreEqual(new SKSizeI(800, 600), StreamResolutionPolicy.CalculateSize(800, 600, 1280, 720));
        Assert.AreEqual(new SKSizeI(800, 600), StreamResolutionPolicy.CalculateSize(800, 600, null, null));
    }

    [TestMethod]
    public void MapToSource_UsesConservativeEdges()
    {
        var mapped = StreamResolutionPolicy.MapToSource(
            new SKRectI(10, 10, 101, 51),
            sourceWidth: 2560,
            sourceHeight: 1440,
            streamWidth: 960,
            streamHeight: 540);

        Assert.AreEqual(new SKRectI(26, 26, 270, 136), mapped);
    }

    [TestMethod]
    public void MapToSource_FullStreamCoversFullSource()
    {
        Assert.AreEqual(
            new SKRectI(0, 0, 2560, 1440),
            StreamResolutionPolicy.MapToSource(new SKRectI(0, 0, 960, 540), 2560, 1440, 960, 540));
    }
}
