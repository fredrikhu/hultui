using HultUI.Media.Mpv;
using Xunit;

namespace HultUI.Media.Backends.Tests;

/// <summary>Which GPUs mpv's rgba16f pictures are kept from (GlesFboFormat): an Adreno 5xx drew them
/// in part (a Galaxy Tab S4's Adreno 540).</summary>
public class GlesFboFormatTests
{
    [Theory]
    [InlineData("Adreno (TM) 540", true)]
    [InlineData("Adreno (TM) 505", true)]
    [InlineData("Adreno (TM) 530 ", true)]
    [InlineData("Adreno (TM) 830", false)]   // a Galaxy S25: rgba16f draws whole
    [InlineData("Adreno (TM) 650", false)]
    [InlineData("Adreno (TM) 5", false)]
    [InlineData("Adreno (TM) 5000", false)]
    [InlineData("Mali-G78", false)]
    [InlineData("Android Emulator OpenGL ES Translator (NVIDIA GeForce RTX 4090/PCIe/SSE2)", false)]
    public void AnAdreno5xx_DrawsHalfFloatInPart(string renderer, bool inPart) =>
        Assert.Equal(inPart, GlesFboFormat.DrawsHalfFloatInPart(renderer));
}
