using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>A receiver lifetime, not a receiver category. Reopening a panel requires a new token.</summary>
internal sealed class UiPointerOwner( object target, bool capturesMouse = true )
{
	public object Target { get; } = target;
	/// <summary>
	/// Whether an event this owner takes counts as the interface capturing the mouse (<c>Level.UiCapturesMouse</c>, which switches off the camera's
	/// wheel, right-drag and edge scrolling). The pause backdrop takes the click without that: the camera keeps working under P, as before routing.
	/// </summary>
	public bool CapturesMouse { get; } = capturesMouse;
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

	private readonly UiPointerOwner unavailableOwner = new( new object(), capturesMouse: false );
	private long lastTimestamp;

	/// <summary>
	/// Missing order is not an empty batch. Preserve an established movement/release owner, but quarantine new
	/// presses until a trustworthy new gesture arrives. No guessed ordering can turn an unknown press into a park edit.
	/// </summary>
	public IReadOnlyList<UiRoutedEvent> AdmitUnavailable( UiInput input )
	{
		var result = new List<UiRoutedEvent>();
		var point = input.Mouse;
		// No platform time exists for this frame, and a wall clock would mix bases with the event times: reuse the last event time for a motion or a quarantined press,
		// and stamp a release past the click time, so a release whose press time is unknown is explicitly not a click.
		var now = lastTimestamp;
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
			result.Add( new UiRoutedEvent( new( 0, OrderedInputEventKind.ButtonUp, point.X, point.Y, left, TimestampMilliseconds: now + UiInputSource.ClickMilliseconds + 1 ), owner, false ) );
		}
		return result;
	}

	/// <summary>
	/// Hand-built input (smoke tests, fixtures) has no raw batch but states its pointer edges on purpose: a move to the pointer when it
	/// differs from the last position, a press for <see cref="UiInput.LeftPressed"/> or for a release with nothing held (a lone
	/// <see cref="UiInput.Click"/> is a whole quick click), then the release. A <see cref="UiInput.NotClick"/> release is stamped
	/// past the click time. Unlike <see cref="AdmitUnavailable"/> this never quarantines a press.
	/// </summary>
	public IReadOnlyList<UiRoutedEvent> AdmitInjected( UiInput input, Func<NVector2, UiPointerOwner?> hitTest )
	{
		var events = new List<OrderedInputEvent>();
		var point = input.Mouse;
		if ( point.X < 0 )
			return Array.Empty<UiRoutedEvent>();
		OrderedInputEvent At( OrderedInputEventKind kind, int button = -1, long time = 0 ) =>
			new( events.Count + 1, kind, point.X, point.Y, button, TimestampMilliseconds: time, PositionIsExact: true );
		var left = (int)Veldrid.MouseButton.Left;
		if ( !positioned || point != position )
			events.Add( At( OrderedInputEventKind.Motion ) );
		if ( input.LeftPressed || input.LeftReleased && !LeftDown )
			events.Add( At( OrderedInputEventKind.ButtonDown, left ) );
		if ( input.LeftReleased )
			events.Add( At( OrderedInputEventKind.ButtonUp, left, input.NotClick ? UiInputSource.ClickMilliseconds + 1 : 0 ) );
		if ( input.RightPressed )
		{
			var right = (int)Veldrid.MouseButton.Right;
			events.Add( At( OrderedInputEventKind.ButtonDown, right ) );
			events.Add( At( OrderedInputEventKind.ButtonUp, right ) );
		}
		return Admit( events, hitTest );
	}

	public IReadOnlyList<UiRoutedEvent> Admit( IReadOnlyList<OrderedInputEvent> events, Func<NVector2, UiPointerOwner?> hitTest )
	{
		var result = new List<UiRoutedEvent>( events.Count );
		foreach ( var item in events )
		{
			UiPointerOwner? owner = null;
			lastTimestamp = item.TimestampMilliseconds;
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
