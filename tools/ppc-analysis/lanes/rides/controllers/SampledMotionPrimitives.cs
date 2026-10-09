namespace OpenTPW.PpcEvidence;

public readonly record struct SampledPosition(float X, float Y, float Z)
{
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
}
public readonly record struct HeightAcceleration(float PerHeight, float UphillModifier, float DownhillModifier);

/// <summary>
/// Scalar reference for already sampled, supplied memory values. No track decoding or train tick.
/// MathF/Math fused operations retain the native single/double operation boundaries.
/// Finite-domain checks are evidence-tool policy; original FPSCR modes remain unqualified.
/// </summary>
public static class SampledMotionPrimitives
{
    public static PrimitiveResult<SampledPosition> Interpolate(SampledPosition current, SampledPosition next, float fraction)
    {
        if (!current.IsFinite || !next.IsFinite || !float.IsFinite(fraction) || fraction < 0 || fraction > 1)
            return PrimitiveResult<SampledPosition>.Reject(PrimitiveStatus.InvalidSnapshot, "requires finite adjacent supplied points and fraction0..1");
        var complement = 1f - fraction;
        static float Component(float current, float next, float fraction, float complement)
        {
            var product = next * fraction;
            return MathF.FusedMultiplyAdd(current, complement, product);
        }
        var position = new SampledPosition(Component(current.X, next.X, fraction, complement),
            Component(current.Y, next.Y, fraction, complement), Component(current.Z, next.Z, fraction, complement));
        return position.IsFinite ? PrimitiveResult<SampledPosition>.Applied(position)
            : PrimitiveResult<SampledPosition>.Reject(PrimitiveStatus.InvalidSnapshot, "interpolation leaves qualified finite domain");
    }

    public static PrimitiveResult<float> HeightTarget(float currentSpeed, float currentY, float projectedY, float suppliedFloor, HeightAcceleration settings)
    {
        if (!float.IsFinite(currentSpeed) || currentSpeed < 0 || !float.IsFinite(currentY) || !float.IsFinite(projectedY)
            || !float.IsFinite(suppliedFloor) || suppliedFloor < 0 || !float.IsFinite(settings.PerHeight)
            || !float.IsFinite(settings.UphillModifier) || !float.IsFinite(settings.DownhillModifier))
            return PrimitiveResult<float>.Reject(PrimitiveStatus.InvalidSnapshot, "height target requires finite supplied scalars and nonnegative speed/floor");
        var delta = currentY - projectedY;
        var acceleration = settings.PerHeight * delta;
        var modifier = projectedY > currentY ? settings.UphillModifier : settings.DownhillModifier;
        var target = MathF.FusedMultiplyAdd(modifier, acceleration, currentSpeed);
        if (!float.IsFinite(delta) || !float.IsFinite(acceleration) || !float.IsFinite(target))
            return PrimitiveResult<float>.Reject(PrimitiveStatus.InvalidSnapshot, "height target leaves qualified finite domain");
        return PrimitiveResult<float>.Applied(target < suppliedFloor ? suppliedFloor : target);
    }

    public static PrimitiveResult<float> Distance(SampledPosition a, SampledPosition b)
    {
        if (!a.IsFinite || !b.IsFinite)
            return PrimitiveResult<float>.Reject(PrimitiveStatus.InvalidSnapshot, "distance requires finite supplied points");
        var dy = a.Y - b.Y; var dx = a.X - b.X; var dz = a.Z - b.Z;
        var ySquare = dy * dy;
        var xy = Math.FusedMultiplyAdd((double)dx, dx, ySquare);
        var zSquare = dz * dz;
        var squared = (double)zSquare + xy;
        var distance = (float)Math.Sqrt(squared);
        if (!float.IsFinite(dx) || !float.IsFinite(dy) || !float.IsFinite(dz)
            || !float.IsFinite(ySquare) || !float.IsFinite(zSquare) || !float.IsFinite(distance))
            return PrimitiveResult<float>.Reject(PrimitiveStatus.InvalidSnapshot, "distance leaves qualified finite domain");
        return PrimitiveResult<float>.Applied(distance);
    }

    public static PrimitiveResult<float> AdvanceTrain() => PrimitiveResult<float>.Reject(PrimitiveStatus.UnsupportedSchema,
        "full tick requires native station flags, adaptive substeps, path wrap, speed limits, collision and track schema");
}
