using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Slider
{
    public static SliderElement New(double value = 0, double min = 0, double max = 100) =>
        SliderElement.New(value, min, max);
}
