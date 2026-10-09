using System.Text.Json;
using OpenTPW.PpcEvidence;

var tests = new (string Name, Action Run)[]
{
    ("category binding and first-member fallback", Binding),
    ("default and explicit channel bounds", Channels),
    ("scaled and unscaled clock flag", Clocks),
    ("first frame and pre-start ambiguity", FirstFrame),
    ("end and unknown playback boundaries", EndBoundary),
    ("single precision after long gaps", Rounding),
    ("GETANIM_CH ID and flag4 query", Query),
    ("per-mille speed and independent deadline divisor", Speed),
    ("signed trigger clamp boundaries", Clamp),
    ("ordinary signed trigger deadline", OrdinaryTiming),
    ("unsupported numeric input domains", Unsupported),
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"{tests.Length} standalone animation evidence tests passed.");
if (args.Length == 1)
    File.WriteAllText(args[0], JsonSerializer.Serialize(new { schema = 1, scope = "synthetic scalar evidence; no original assets or game execution",
        tests = tests.Select(t => t.Name), defaultChannel = OriginalAnimationState.DefaultChannel,
        channelStride = OriginalAnimationState.ChannelStride, unscaledClockFlag = OriginalAnimationState.UnscaledClockFlag,
        unsupported = new[] { "before-start/clock wrap", "at/after end", "loop/deferred/mixing flags", "WAITANIM unsigned path", "exceptional FP or deadline overflow" } },
        new JsonSerializerOptions { WriteIndented = true }));
else if (args.Length > 1) throw new ArgumentException("Usage: AnimationWitness [metadata-output.json]");

static void Equal<T>(T expected, T actual) where T : IEquatable<T>
{
    if (!expected.Equals(actual)) throw new Exception($"Expected {expected}; observed {actual}.");
}
static void IsUnsupported<T>(AnimationEvidence<T> result) where T : struct
{
    if (result.Supported || string.IsNullOrWhiteSpace(result.UnsupportedReason)) throw new Exception("Expected explicit unsupported result.");
    try { result.RequireValue(); }
    catch (NotSupportedException) { return; }
    throw new Exception("Unsupported results must not silently yield a default value.");
}
static AnimationChannel State(uint flags = 0, uint start = 0, float speed = 1, float frames = 430) => new(5, flags, start, speed, frames);

static void Binding()
{
    var letters = "cdilsmeuwbro";
    for (var category = 0; category < letters.Length; ++category)
    {
        var first = OriginalAnimationState.BindingMetadata(category, 0).RequireValue();
        Equal(letters[category], first.Letter);
        Equal(letters[category] + "1.md2", first.NumberedSuffix);
        Equal(letters[category] + ".md2", first.UnnumberedFallbackSuffix!);
    }
    var later = OriginalAnimationState.BindingMetadata(5, 9).RequireValue();
    Equal("m10.md2", later.NumberedSuffix);
    if (later.UnnumberedFallbackSuffix is not null) throw new Exception("An unnumbered fallback cannot stand in for a missing later numbered variant.");
    IsUnsupported(OriginalAnimationState.BindingMetadata(12, 0));
    IsUnsupported(OriginalAnimationState.BindingMetadata(5, -1));
}
static void Channels()
{
    Equal(0, OriginalAnimationState.DefaultChannel);
    Equal(56, OriginalAnimationState.ChannelStride);
    var clocks = new AnimationClocks(1000, 2000);
    var first = OriginalAnimationState.FrameBeforeEnd(State(), OriginalAnimationState.DefaultChannel, 4, clocks).RequireValue();
    var explicitZero = OriginalAnimationState.FrameBeforeEnd(State(), 0, 4, clocks).RequireValue();
    Equal(first, explicitZero);
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(), -1, 4, clocks));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(), 4, 4, clocks));
}
static void Clocks()
{
    var clocks = new AnimationClocks(1000, 3000);
    Equal(1000u, OriginalAnimationState.SelectClock(0, clocks));
    Equal(3000u, OriginalAnimationState.SelectClock(0x40, clocks));
    Equal(30f, OriginalAnimationState.FrameBeforeEnd(State(), 0, 1, clocks).RequireValue().Frame);
    Equal(90f, OriginalAnimationState.FrameBeforeEnd(State(0x40), 0, 1, clocks).RequireValue().Frame);
}
static void FirstFrame()
{
    Equal(0f, OriginalAnimationState.FrameBeforeEnd(State(start: 100), 0, 1, new(100, 200)).RequireValue().Frame);
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(start: 100), 0, 1, new(99, 200)));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(start: uint.MaxValue - 5), 0, 1, new(5, 5)));
}
static void EndBoundary()
{
    var state = State(frames: 60);
    if (!OriginalAnimationState.FrameBeforeEnd(state, 0, 1, new(1999, 0)).Supported) throw new Exception("Pre-end progress is supported.");
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(state, 0, 1, new(2000, 0)));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(state, 0, 1, new(2500, 0)));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(flags: 1), 0, 1, new(1000, 0)));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(flags: 4), 0, 1, new(1000, 0)));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(new(12, 0, 0, 1, 60), 0, 1, new(1000, 0)));
}
static void Rounding()
{
    var gap = OriginalAnimationState.FrameBeforeEnd(State(frames: 1e9f), 0, 1, new(16_777_217, 0)).RequireValue();
    Equal(16_777_216f, gap.ConvertedElapsed);
    Equal(503_316_480f, gap.AfterMultiply30);
    Equal(0x48f5c28f, BitConverter.SingleToInt32Bits(gap.AfterDivide1000));
    var fractional = OriginalAnimationState.FrameBeforeEnd(State(speed: 1.3f), 0, 1, new(1001, 0)).RequireValue();
    Equal(0x41f03d71, BitConverter.SingleToInt32Bits(fractional.AfterDivide1000));
    Equal(0x421c27ef, BitConverter.SingleToInt32Bits(fractional.Frame));
}
static void Query()
{
    Equal(0, OriginalAnimationState.GetAnimChannel(0, 0));
    Equal(5, OriginalAnimationState.GetAnimChannel(5, 0));
    Equal(12, OriginalAnimationState.GetAnimChannel(12, 0x40));
    Equal(-1, OriginalAnimationState.GetAnimChannel(5, 4));
    Equal(-1, OriginalAnimationState.GetAnimChannel(12, 0x44));
}
static void Speed()
{
    Equal(.5f, OriginalAnimationState.ScriptSpeed(0).RequireValue());
    Equal(1f, OriginalAnimationState.ScriptSpeed(50).RequireValue());
    Equal(1.5f, OriginalAnimationState.ScriptSpeed(100).RequireValue());
    var timing = OriginalAnimationState.TrigAnimSpeed(100, 4000, 900, 1000).RequireValue();
    Equal(6f, timing.ChannelSpeed);
    Equal((short)4000, timing.StoredPerMille);
    Equal(600, timing.AdjustedReturn);
    Equal(150, timing.DelayMilliseconds);
    Equal(1150u, timing.Deadline);
    var truncatedStore = OriginalAnimationState.TrigAnimSpeed(100, 40000, 900, 0).RequireValue();
    Equal(unchecked((short)40000), truncatedStore.StoredPerMille);
    Equal(15, truncatedStore.DelayMilliseconds);
}
static void Clamp()
{
    foreach (var opcode in new[] { 16, 19, 21, 23 })
        foreach (var pair in new[] { (0, 300), (299, 300), (300, 300), (599, 300), (600, 300), (601, 301), (1000, 700) })
            Equal(pair.Item2, OriginalAnimationState.AdjustSignedTriggerReturn(opcode, pair.Item1).RequireValue());
    IsUnsupported(OriginalAnimationState.AdjustSignedTriggerReturn(17, 600));
    IsUnsupported(OriginalAnimationState.AdjustSignedTriggerReturn(16, -1));
}
static void OrdinaryTiming()
{
    var timing = OriginalAnimationState.SignedTriggerTiming(16, 100, 600, 1000).RequireValue();
    Equal(300, timing.AdjustedReturn);
    Equal(200, timing.DelayMilliseconds);
    Equal(1200u, timing.Deadline);
    Equal(600, OriginalAnimationState.SignedTriggerTiming(23, 0, 0, 0).RequireValue().DelayMilliseconds);
    Equal(299u, OriginalAnimationState.SignedTriggerTiming(19, 100, 900, uint.MaxValue - 100).RequireValue().Deadline);
    IsUnsupported(OriginalAnimationState.SignedTriggerTiming(17, 100, 600, 0));
    IsUnsupported(OriginalAnimationState.SignedTriggerTiming(21, 100, 600, 0));
}
static void Unsupported()
{
    IsUnsupported(OriginalAnimationState.ScriptSpeed(-50));
    IsUnsupported(OriginalAnimationState.TrigAnimSpeed(50, 0, 900, 0));
    IsUnsupported(OriginalAnimationState.TrigAnimSpeed(50, -1, 900, 0));
    IsUnsupported(OriginalAnimationState.TrigAnimSpeed(50, 4000, int.MaxValue, 0));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(speed: float.NaN), 0, 1, new(1000, 0)));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(speed: float.PositiveInfinity), 0, 1, new(1000, 0)));
    IsUnsupported(OriginalAnimationState.FrameBeforeEnd(State(speed: float.Epsilon), 0, 1, new(1000, 0)));
}
