namespace HultUI.Text;

public interface ITextShaper
{
    ShapedRun Shape(string text, IResolvedFont font, float fontSize);
}
