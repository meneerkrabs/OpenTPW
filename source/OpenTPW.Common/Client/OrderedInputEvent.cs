namespace OpenTPW;

/// <summary>Only the ordered input subset used by the park pointer-owner router.</summary>
public enum OrderedInputEventKind { Motion, ButtonDown, ButtonUp, KeyDown, KeyUp }

/// <summary>Where a raw input record was observed; synthetic batches are not native evidence.</summary>
public enum OrderedInputEventSource { Synthetic, Sdl, Browser }

/// <summary>
/// Copied platform facts, before snapshot aggregation or release deferral. Coordinates are logical
/// window units. Button, Key and Modifiers carry the corresponding Veldrid enum's integer value,
/// without a dependency on a particular Veldrid assembly. A non-pointer event has Button = -1.
/// Sequence preserves observation order, not timestamp sorting. TimestampMilliseconds is the platform's own
/// event time (the SDL event timestamp, widened over its 32-bit wrap, or the DOM event.timeStamp), so a slow frame does not stretch a click.
/// Only differences between times of one source are meaningful; a record without a platform time reuses the last one seen.
/// PositionIsExact is false when the adapter only knows the last observed pointer position (such as
/// a browser blur-generated release or a legacy button record without coordinates). ModifiersAreExact marks
/// a platform key mask or a pointer mask synchronized by an earlier key in its batch; before that, the
/// router uses observed physical snapshot state, which cannot prove modifiers held across an unobserved focus transition.
/// No receiver is assigned here.
/// </summary>
public readonly record struct OrderedInputEvent(
	long Sequence, OrderedInputEventKind Kind, float X, float Y, int Button = -1, int Key = 0,
	int Modifiers = 0, bool Repeat = false, long TimestampMilliseconds = 0,
	OrderedInputEventSource Source = OrderedInputEventSource.Synthetic, bool PositionIsExact = false, bool ModifiersAreExact = false );

/// <summary>An optional raw side channel; an empty batch means available but no matching events.</summary>
public interface IOrderedInputSnapshot
{
	IReadOnlyList<OrderedInputEvent> OrderedEvents { get; }
}
