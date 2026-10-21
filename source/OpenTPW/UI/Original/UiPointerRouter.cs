using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>A receiver lifetime, not a receiver category. Reopening a panel requires a new token.</summary>
internal sealed class UiPointerOwner( object target )
{
	public object Target { get; } = target;
	public bool Retired { get; private set; }
	public void Retire() => Retired = true;
}

internal readonly record struct UiRoutedEvent( OrderedInputEvent Event, UiPointerOwner? Owner, bool LeftDown );

/// <summary>
/// Ordinary Windows-message input baseline: admit the entire pump batch before dispatching any handler.
/// [BIN:TPW-EXE:0x45ab13 pump] [BIN:TPW-EXE:0x48a5be drain] Pointer recipients are fixed at admission;
/// a P handler cannot retarget a mouse event already queued. The alternate buffered-input mode is not modeled here.
/// </summary>
internal sealed class UiPointerRouter
{
	private readonly Dictionary<int, UiPointerOwner?> presses = new();
	private readonly HashSet<int> down = new();
	private UiPointerOwner? hover;
	private NVector2 position;
	private bool positioned;
	public UiPointerOwner? Capture { get; set; }
	public bool LeftDown => down.Contains( (int)Veldrid.MouseButton.Left );
	public NVector2 Position => position;

	/// <summary>Seed a stationary first frame without inventing a mouse event or button edge.</summary>
	public void SeedPosition( NVector2 point )
	{
		if ( positioned ) return;
		position = point;
		positioned = true;
	}

	/// <summary>Window creation/removal refreshes hover, except while an explicit capture suppresses hit testing.</summary>
	public void RefreshHover( Func<NVector2, UiPointerOwner?> hitTest )
	{
		if ( Capture is { Retired: true } ) Capture = null;
		if ( positioned && Capture == null ) hover = hitTest( position );
	}

	/// <summary>Leaving the supported park view cancels ownership; a later up cannot revive an old gesture.</summary>
	public void Suspend()
	{
		presses.Clear();
		down.Clear();
		Capture = null;
		hover = null;
		positioned = false;
	}

	private readonly UiPointerOwner unavailableOwner = new( new object() );

	/// <summary>
	/// Missing order is not an empty batch. Preserve an established movement/release owner, but quarantine new
	/// presses until a trustworthy new gesture arrives. No guessed ordering can turn an unknown press into a park edit.
	/// </summary>
	public IReadOnlyList<UiRoutedEvent> AdmitUnavailable( UiInput input )
	{
		var result = new List<UiRoutedEvent>();
		var point = input.Mouse;
		var now = Environment.TickCount64;
		if ( Capture != null && (!positioned || point != position) )
			result.Add( new UiRoutedEvent( new( 0, OrderedInputEventKind.Motion, point.X, point.Y, TimestampMilliseconds: now ), Capture, LeftDown ) );
		position = point;
		positioned = true;
		var left = (int)Veldrid.MouseButton.Left;
		if ( input.LeftDown && !LeftDown )
		{
			down.Add( left );
			presses[left] = unavailableOwner;
			result.Add( new UiRoutedEvent( new( 0, OrderedInputEventKind.ButtonDown, point.X, point.Y, left, TimestampMilliseconds: now ), unavailableOwner, true ) );
		}
		else if ( !input.LeftDown && LeftDown )
		{
			presses.Remove( left, out var owner );
			down.Remove( left );
			result.Add( new UiRoutedEvent( new( 0, OrderedInputEventKind.ButtonUp, point.X, point.Y, left, TimestampMilliseconds: now ), owner, false ) );
		}
		return result;
	}

	public IReadOnlyList<UiRoutedEvent> Admit( IReadOnlyList<OrderedInputEvent> events, Func<NVector2, UiPointerOwner?> hitTest )
	{
		var result = new List<UiRoutedEvent>( events.Count );
		foreach ( var item in events )
		{
			UiPointerOwner? owner = null;
			if ( item.Kind is OrderedInputEventKind.Motion or OrderedInputEventKind.ButtonDown or OrderedInputEventKind.ButtonUp )
			{
				position = new NVector2( item.X, item.Y );
				if ( !positioned )
				{
					positioned = true;
					RefreshHover( hitTest );
				}
			}
			switch ( item.Kind )
			{
				case OrderedInputEventKind.Motion:
					RefreshHover( hitTest );
					owner = Capture ?? hover;
					break;
				case OrderedInputEventKind.ButtonDown:
					// [BIN:TPW-EXE:0x658b1a] Down uses hover, NOT the explicit movement capture.
					owner = hover;
					presses[item.Button] = owner;
					down.Add( item.Button );
					break;
				case OrderedInputEventKind.ButtonUp:
					// [BIN:TPW-EXE:0x658b7f] Up belongs to its original down, even after overlay removal.
					presses.Remove( item.Button, out owner );
					down.Remove( item.Button );
					break;
			}
			result.Add( new UiRoutedEvent( item, owner, LeftDown ) );
		}
		return result;
	}
}
