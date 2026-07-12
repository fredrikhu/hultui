using System.Numerics;

namespace HultUI.Core.Primitives;

/// <summary>
/// A 2D affine transform applied to an element and its subtree when rendering and
/// hit-testing. Rotation and scale pivot on the element's centre; translation is
/// applied afterwards. Rotation is in degrees, clockwise (screen Y points down).
/// </summary>
public readonly record struct Transform2D(
    float RotationDegrees,
    float ScaleX,
    float ScaleY,
    float TranslateX,
    float TranslateY)
{
    public static Transform2D Identity => new(0f, 1f, 1f, 0f, 0f);

    public bool IsIdentity =>
        RotationDegrees == 0f && ScaleX == 1f && ScaleY == 1f && TranslateX == 0f && TranslateY == 0f;

    public static Transform2D Rotate(float degrees) => new(degrees, 1f, 1f, 0f, 0f);
    public static Transform2D Scale(float scale) => new(0f, scale, scale, 0f, 0f);
    public static Transform2D Scale(float scaleX, float scaleY) => new(0f, scaleX, scaleY, 0f, 0f);
    public static Transform2D Translate(float dx, float dy) => new(0f, 1f, 1f, dx, dy);

    /// <summary>
    /// Builds the affine matrix that pivots rotation/scale on (centerX, centerY),
    /// then translates. Shared by the renderer (to concat onto the canvas) and the
    /// hit-tester (inverted, to map a screen point into local space).
    /// </summary>
    public Matrix3x2 ToMatrix(float centerX, float centerY)
    {
        float rad = RotationDegrees * (MathF.PI / 180f);
        return Matrix3x2.CreateTranslation(-centerX, -centerY)
             * Matrix3x2.CreateScale(ScaleX, ScaleY)
             * Matrix3x2.CreateRotation(rad)
             * Matrix3x2.CreateTranslation(centerX, centerY)
             * Matrix3x2.CreateTranslation(TranslateX, TranslateY);
    }
}
