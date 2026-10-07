namespace WavefrontErrorCalculator.Core;

/// <summary>
/// A point or direction in the image space's frame: the image surface's own vertex frame, x
/// sagittal, y meridional and z along the axis, which is where AberrationCalculator reports a
/// ray's landing.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(double s, Vec3 a) => new(s * a.X, s * a.Y, s * a.Z);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public double Length => Math.Sqrt(Dot(this));

    public override string ToString() => $"({X:G9}, {Y:G9}, {Z:G9})";
}
