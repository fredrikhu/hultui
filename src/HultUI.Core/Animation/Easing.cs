namespace HultUI.Core.Animation;

public static class Easing
{
    public static float Linear(float t) => t;

    // Quadratic
    public static float EaseInQuad(float t) => t * t;
    public static float EaseOutQuad(float t) => t * (2 - t);
    public static float EaseInOutQuad(float t) =>
        t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;

    // Cubic
    public static float EaseInCubic(float t) => t * t * t;
    public static float EaseOutCubic(float t) => 1 - MathF.Pow(1 - t, 3);
    public static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;

    // Exponential
    public static float EaseInExpo(float t) =>
        t == 0 ? 0 : MathF.Pow(2, 10 * (t - 1));
    public static float EaseOutExpo(float t) =>
        t == 1 ? 1 : 1 - MathF.Pow(2, -10 * t);

    // Back (slight overshoot)
    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }

    // Elastic
    public static float EaseOutElastic(float t)
    {
        if (t is 0 or 1) return t;
        return MathF.Pow(2, -10 * t) * MathF.Sin((t * 10 - 0.75f) * (2 * MathF.PI / 3)) + 1;
    }

    /// <summary>
    /// Creates a cubic-bezier easing function matching CSS cubic-bezier() semantics.
    /// </summary>
    public static Func<float, float> CubicBezier(float x1, float y1, float x2, float y2)
    {
        return (float t) =>
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            float guessT = t;
            for (int i = 0; i < 8; i++)
            {
                float x = SampleCurve(guessT, x1, x2) - t;
                float dx = SampleCurveDerivative(guessT, x1, x2);
                if (MathF.Abs(dx) < 1e-6f) break;
                guessT -= x / dx;
            }
            return SampleCurve(guessT, y1, y2);
        };

        static float SampleCurve(float t, float a, float b) =>
            ((1 - 3 * b + 3 * a) * t + (3 * b - 6 * a)) * t * t + 3 * a * t;

        static float SampleCurveDerivative(float t, float a, float b) =>
            3 * (1 - 3 * b + 3 * a) * t * t + 2 * (3 * b - 6 * a) * t + 3 * a;
    }
}
