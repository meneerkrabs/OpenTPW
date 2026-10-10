using System.Globalization;

namespace OpenTPW.PpcEvidence;

/// <summary>Explicit result for a proved scalar path, with no default-value fallback for unknown behavior.</summary>
public readonly record struct AnimationEvidence<T>(T? Value, string? UnsupportedReason) where T : struct
{
    public bool Supported => Value.HasValue && UnsupportedReason is null;
    public T RequireValue() => Supported ? Value!.Value : throw new NotSupportedException(UnsupportedReason);
    public static AnimationEvidence<T> Proved(T value) => new(value, null);
    public static AnimationEvidence<T> Unsupported(string reason) => new(null, reason);
}

/// <summary>Normalized metadata only; archive lookup/case policy remains the caller's responsibility.</summary>
public readonly record struct AnimationBinding(char Letter, int Variant, string NumberedSuffix, string? UnnumberedFallbackSuffix);
public readonly record struct AnimationClocks(uint ScaledMilliseconds, uint UnscaledMilliseconds);
public readonly record struct AnimationChannel(int CurrentId, uint Flags, uint StartMilliseconds, float Speed, float TotalFrames);
public readonly record struct AnimationFrame(uint SelectedClock, uint ElapsedMilliseconds, float ConvertedElapsed,
    float AfterMultiply30, float AfterDivide1000, float Frame);
public readonly record struct AnimationTriggerTiming(float ChannelSpeed, short StoredPerMille, int AdjustedReturn,
    int DelayMilliseconds, uint Deadline);

/// <summary>
/// Pure scalar evidence from the identified Feral Mac executable. This is not a
/// playback/state-transition engine. Model update gates, loop/end transitions,
/// deferred animations, node mixing, and exceptional FPSCR modes are unqualified.
/// </summary>
public static class OriginalAnimationState
{
    public const int DefaultChannel = 0;
    public const int ChannelStride = 56;
    public const int NoAnimationId = 12;
    public const uint UnscaledClockFlag = 0x40;
    public const uint NegativeQueryFlag = 4;
    private const float MinimumNormal = 1.17549435E-38f;
    private const string Letters = "cdilsmeuwbro";

    public static AnimationEvidence<AnimationBinding> BindingMetadata(int category, int variant)
    {
        if (category < 0 || category >= Letters.Length)
            return AnimationEvidence<AnimationBinding>.Unsupported("category has no proved loader suffix");
        if (variant < 0 || variant == int.MaxValue)
            return AnimationEvidence<AnimationBinding>.Unsupported("variant is outside the qualified numbered-member domain");
        var letter = Letters[category];
        var numbered = letter + (variant + 1).ToString(CultureInfo.InvariantCulture) + ".md2";
        // The loader retries unnumbered only when the first numbered member is absent.
        return AnimationEvidence<AnimationBinding>.Proved(new(letter, variant, numbered, variant == 0 ? letter + ".md2" : null));
    }

    public static uint SelectClock(uint channelFlags, AnimationClocks clocks) =>
        (channelFlags & UnscaledClockFlag) != 0 ? clocks.UnscaledMilliseconds : clocks.ScaledMilliseconds;

    /// <summary>Native GETANIM_CH scalar query; no inference that the result is a playing/not-playing boolean.</summary>
    public static int GetAnimChannel(int currentId, uint channelFlags) => (channelFlags & NegativeQueryFlag) != 0 ? -1 : currentId;

    /// <summary>
    /// Qualified pre-end progress only. Each intermediate is single precision,
    /// matching fsubs/fmuls/fdivs/fmuls after the exact unsigned double conversion.
    /// Before-start/wrap ambiguity and end/mixing/loop flags produce unsupported.
    /// </summary>
    public static AnimationEvidence<AnimationFrame> FrameBeforeEnd(AnimationChannel state, int channel, int channelCount, AnimationClocks clocks)
    {
        if (channel < 0 || channel >= channelCount)
            return AnimationEvidence<AnimationFrame>.Unsupported("channel index has no valid record");
        if (state.CurrentId == NoAnimationId)
            return AnimationEvidence<AnimationFrame>.Unsupported("native sentinel skips frame update; prior pose is unqualified");
        if (state.CurrentId < 0 || state.CurrentId >= NoAnimationId)
            return AnimationEvidence<AnimationFrame>.Unsupported("category/state transition is unqualified");
        if ((state.Flags & ~UnscaledClockFlag) != 0)
            return AnimationEvidence<AnimationFrame>.Unsupported("loop/end/deferred/mixing flag semantics are unqualified");
        if (!PositiveNormal(state.Speed) || !PositiveNormal(state.TotalFrames))
            return AnimationEvidence<AnimationFrame>.Unsupported("speed or frame count is outside the qualified finite domain");
        var now = SelectClock(state.Flags, clocks);
        if (now < state.StartMilliseconds)
            return AnimationEvidence<AnimationFrame>.Unsupported("before-start time cannot be distinguished from unsigned clock wrap");
        var elapsed = now - state.StartMilliseconds;
        // Native forms 2^52 + elapsed in double, then fsubs rounds its difference to float.
        var converted = (float)(double)elapsed;
        var multiplied = converted * 30f;
        var divided = multiplied / 1000f;
        var frame = divided * state.Speed;
        if (!NormalOrZero(converted) || !NormalOrZero(multiplied) || !NormalOrZero(divided) || !NormalOrZero(frame))
            return AnimationEvidence<AnimationFrame>.Unsupported("exceptional floating-point result is unqualified");
        if (frame >= state.TotalFrames)
            return AnimationEvidence<AnimationFrame>.Unsupported("end boundary, final pose and loop transition are unqualified");
        return AnimationEvidence<AnimationFrame>.Proved(new(now, elapsed, converted, multiplied, divided, frame));
    }

    public static AnimationEvidence<float> ScriptSpeed(short bias)
    {
        // Signed16 -> exact double -> fsubs single -> fdivs single -> fadds single.
        var converted = (float)(double)bias;
        var divided = converted / 100f;
        var speed = .5f + divided;
        return PositiveNormal(speed) ? AnimationEvidence<float>.Proved(speed)
            : AnimationEvidence<float>.Unsupported("nonpositive or exceptional script speed is unqualified");
    }

    /// <summary>Signed clamp exists in opcodes16/19/21/23; WAITANIM17 is a different unsigned path.</summary>
    public static AnimationEvidence<int> AdjustSignedTriggerReturn(int opcode, int returnedDuration)
    {
        if (opcode is not (16 or 19 or 21 or 23))
            return AnimationEvidence<int>.Unsupported("wait opcode has no proved signed-trigger adjustment");
        if (returnedDuration < 0)
            return AnimationEvidence<int>.Unsupported("negative native trigger return is outside the qualified domain");
        return AnimationEvidence<int>.Proved(Math.Max(returnedDuration - 300, 300));
    }

    /// <summary>Scalar timing for signed trigger cases; this does not execute TRIGWAITANIM's stateful wait protocol.</summary>
    public static AnimationEvidence<AnimationTriggerTiming> SignedTriggerTiming(int opcode, short scriptBias,
        int returnedDuration, uint now)
    {
        if (opcode is not (16 or 19 or 23))
            return AnimationEvidence<AnimationTriggerTiming>.Unsupported("opcode has no qualified ordinary signed-trigger timing path");
        var speed = ScriptSpeed(scriptBias);
        var adjusted = AdjustSignedTriggerReturn(opcode, returnedDuration);
        if (!speed.Supported || !adjusted.Supported)
            return AnimationEvidence<AnimationTriggerTiming>.Unsupported("trigger return or script speed is unqualified");
        var converted = (float)(double)adjusted.RequireValue();
        var delayFloat = converted / speed.RequireValue();
        if (!NormalOrZero(delayFloat) || (double)delayFloat >= 2147483648d)
            return AnimationEvidence<AnimationTriggerTiming>.Unsupported("fctiwz exceptional or overflow result is unqualified");
        var delay = (int)delayFloat;
        return AnimationEvidence<AnimationTriggerTiming>.Proved(new(speed.RequireValue(), 1000,
            adjusted.RequireValue(), delay, unchecked(now + (uint)delay)));
    }

    /// <summary>
    /// Opcode21 timing only, given an actual native trigger return and already-resolved int32 speed operand.
    /// This does not infer a bare clip duration or implement literal/variable decoding.
    /// </summary>
    public static AnimationEvidence<AnimationTriggerTiming> TrigAnimSpeed(short scriptBias, int perMilleOperand,
        int returnedDuration, uint now)
    {
        var baseSpeed = ScriptSpeed(scriptBias);
        var adjusted = AdjustSignedTriggerReturn(21, returnedDuration);
        if (!baseSpeed.Supported || !adjusted.Supported || perMilleOperand <= 0)
            return AnimationEvidence<AnimationTriggerTiming>.Unsupported("trigger return, script speed or per-mille input is unqualified");
        var rawConverted = (float)(double)perMilleOperand;
        var product = baseSpeed.RequireValue() * rawConverted;
        var channelSpeed = product / 1000f;
        if (!PositiveNormal(product) || !PositiveNormal(channelSpeed))
            return AnimationEvidence<AnimationTriggerTiming>.Unsupported("exceptional trigger speed is unqualified");
        var scaledDuration = (long)adjusted.RequireValue() * 1000;
        if (scaledDuration > int.MaxValue)
            return AnimationEvidence<AnimationTriggerTiming>.Unsupported("signed deadline numerator overflow is unqualified");
        // Native deadline divides by the original int32 operand, not script speed or its stored int16 copy.
        var delay = (int)scaledDuration / perMilleOperand;
        return AnimationEvidence<AnimationTriggerTiming>.Proved(new(channelSpeed, unchecked((short)perMilleOperand),
            adjusted.RequireValue(), delay, unchecked(now + (uint)delay)));
    }

    private static bool PositiveNormal(float value) => float.IsFinite(value) && value >= MinimumNormal;
    private static bool NormalOrZero(float value) => value == 0 || PositiveNormal(value);
}
