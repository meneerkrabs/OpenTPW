using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	private readonly UiPointerRouter pointerRouter = new();
	private UiPointerOwner? parkPointerOwner;
	private bool pointerRoutingStarted;
	private bool pointerRoutingActive;
	private UiPointerOwner? pausePointerOverlay;
	private UiScreen[] pauseOverlayBelow = Array.Empty<UiScreen>();
	private readonly Dictionary<(UiScreen Screen, UiElement? Element, int Generation, int Membership), UiPointerOwner> screenPointerOwners = new();
	private NVector2 routedPressPoint;
	private long routedPressTime;
	private bool routedPressMoved;

	/// <summary>Unit-test seam: the level this HUD was built for (the fixtures build the HUD around a hand-made level).</summary>
	internal Level LevelForTests => level;

	private UiPointerOwner ParkPointerOwner => parkPointerOwner ??= new UiPointerOwner( this );

	private UiPointerOwner ScreenPointerOwner( UiScreen screen, UiElement? element )
	{
		var key = (screen, element, screen.PointerGeneration, screen.ElementGeneration( element ));
		if ( !screenPointerOwners.TryGetValue( key, out var owner ) )
			screenPointerOwners.Add( key, owner = new UiPointerOwner( key ) );
		return owner;
	}

	/// <summary>
	/// [BIN:TPW-EXE:0x47ed80 P window] the P window's 'lolight' backdrop (0x65dd7b, rect 0,0,0x7ff,0x5ff, flags 1, hidden byte 0) is an eligible receiver of
	/// the hit test 0x65db25 (flag bit 0x2 clear, byte +4 clear, bit 0x1 set), so it takes every pointer event over the windows that existed when P created it.
	/// Only the P key creates it, and only offline: the owner dies with the pause (a co-op or read-only visit never gets one).
	/// </summary>
	private void CreatePausePointerOverlay()
	{
		if ( level == null || level.CoopRole != CoopRole.None || level.IsReadOnlyVisit )
			return;
		RetirePausePointerOverlay();
		pausePointerOverlay = new UiPointerOwner( new object() );
		pauseOverlayBelow = Stack.Screens.ToArray();
	}

	private void RetirePausePointerOverlay()
	{
		pausePointerOverlay?.Retire();
		pausePointerOverlay = null;
		pauseOverlayBelow = Array.Empty<UiScreen>();
	}

	private UiPointerOwner? HitPointer( UiContext context, NVector2 point )
	{
		if ( point.X < 0 || point.Y < 0 || point.X >= context.Canvas.Width || point.Y >= context.Canvas.Height ) return null;
		// [BIN:TPW-EXE:0x65db25 topmost eligible window] the last created sibling wins, so a window opened after the P window (the Escape menu,
		// a confirm or message box) lies above its backdrop; the backdrop covers only what existed when it was created.
		if ( pausePointerOverlay != null && (Stack.Top is not { } above || Array.IndexOf( pauseOverlayBelow, above ) >= 0) ) return pausePointerOverlay;
		if ( Stack.Top is { } screen && (screen.Covers( context.Canvas, point ) || Stack.Screens.Count > 1) )
			return ScreenPointerOwner( screen, screen.HitTest( context.Canvas, point ) );
		return ParkPointerOwner;
	}

	private void SyncPointerCapture()
	{
		// Explicit movement capture is separate from each button's release recipient.
		if ( level.CoasterPanel is { IsDragging: true } )
			pointerRouter.Capture = ParkPointerOwner;
		else if ( Stack.Screens.LastOrDefault( screen => screen.PointerDragCapture != null ) is { } screen )
			pointerRouter.Capture = ScreenPointerOwner( screen, screen.PointerDragCapture );
		else
			pointerRouter.Capture = null;
	}

	/// <summary>Host safety policy, not native mode-switch parity: settle an existing coaster edit and cancel UI gestures.
	/// Height settlement can charge an already-started edit; this never synthesizes a new park click.</summary>
	private void SuspendParkPointer()
	{
		pointerRoutingActive = false;
		if ( level.CoopRole != CoopRole.None || level.IsReadOnlyVisit )
			RetirePausePointerOverlay();
		pointerRouter.Suspend();
		foreach ( var screen in Stack.Screens ) screen.CancelPointerGesture();
		if ( level.CoasterPanel is { IsDragging: true } panel ) panel.Release( null, null );
		toolPress = coasterPress = false;
		refusalClickState = null;
	}

	private IReadOnlyList<UiRoutedEvent>? AdmitPointerEvents( UiContext context, UiInput input )
	{
		// This is local offline pointer ownership, never a shared-session speed policy.
		if ( level.CoopRole != CoopRole.None || level.IsReadOnlyVisit )
			return null;
		foreach ( var screen in Stack.Screens ) screen.PreparePointerGeometry( context );
		foreach ( var pair in screenPointerOwners.ToArray() )
		{
			var (screen, element, generation, membership) = pair.Key;
			if ( screen.IsRemoved || screen.PointerGeneration != generation || screen.ElementGeneration( element ) != membership || !Stack.Screens.Contains( screen ) || element != null && !screen.Elements.Contains( element ) )
			{
				pair.Value.Retire();
				screenPointerOwners.Remove( pair.Key );
			}
		}
		if ( input.OrderedEvents is { } batch && !batch.Any( item => item.Kind is OrderedInputEventKind.Motion or OrderedInputEventKind.ButtonDown or OrderedInputEventKind.ButtonUp ) )
			pointerRouter.SeedPosition( input.Mouse );
		SyncPointerCapture();
		pointerRouter.RefreshHover( point => HitPointer( context, point ) );
		level.WorldPointerRouted = true;
		pointerRoutingStarted = true;
		pointerRoutingActive = true;
		return input.OrderedEvents == null ? pointerRouter.AdmitUnavailable( input )
			: pointerRouter.Admit( input.OrderedEvents, point => HitPointer( context, point ) );
	}

	private bool DrainPointerEvents( UiContext context, IReadOnlyList<UiRoutedEvent> events, UiInput frameInput, bool rideOwnsInput, bool mouseCaptured, bool keyboardCaptured, bool textCaptured )
	{
		if ( frameInput.PointerHostBlocked || mouseCaptured )
		{
			SuspendParkPointer();
			return true;
		}
		mouseCaptured |= frameInput.PointerHostBlocked;
		var consumed = frameInput.PointerHostBlocked;
		var modeSuspended = rideOwnsInput || IsRiding || CamcorderArmed || level.FreeWalk != null;
		foreach ( var delivery in events )
		{
			modeSuspended |= IsRiding || CamcorderArmed || level.FreeWalk != null || level.CoopRole != CoopRole.None || level.IsReadOnlyVisit;
			if ( modeSuspended )
			{
				SuspendParkPointer();
				consumed = true;
				continue;
			}
			var item = delivery.Event;
			if ( item.Kind is OrderedInputEventKind.KeyDown or OrderedInputEventKind.KeyUp )
			{
				if ( item.Kind == OrderedInputEventKind.KeyDown && !item.Repeat && item.Key == (int)Input.ChordKey( InputButton.Pause )
					&& ((ModifierKeys)item.Modifiers & ModifierKeys.Control) == 0 && !frameInput.PointerHostBlocked && !Paused && !IsRiding && !Input.TextEntryActive && !keyboardCaptured && !textCaptured )
				{
					ToggleUserPause();
					pointerRouter.RefreshHover( point => HitPointer( context, point ) );
				}
				continue;
			}
			var point = new NVector2( item.X, item.Y );
			var leftPress = item.Kind == OrderedInputEventKind.ButtonDown && item.Button == (int)MouseButton.Left;
			var leftRelease = item.Kind == OrderedInputEventKind.ButtonUp && item.Button == (int)MouseButton.Left;
			if ( leftPress )
			{
				routedPressPoint = point;
				routedPressTime = item.TimestampMilliseconds;
				routedPressMoved = false;
			}
			if ( Math.Abs( point.X - routedPressPoint.X ) > UiInputSource.ClickSlop * frameInput.PointerScaleX || Math.Abs( point.Y - routedPressPoint.Y ) > UiInputSource.ClickSlop * frameInput.PointerScaleY )
				routedPressMoved = true;
			var notClick = routedPressMoved || item.TimestampMilliseconds - routedPressTime > UiInputSource.ClickMilliseconds
				|| leftRelease && item.Source == OrderedInputEventSource.Browser && !item.PositionIsExact;
			var input = new UiInput( point, delivery.LeftDown, leftPress, leftRelease,
				item.Kind == OrderedInputEventKind.ButtonDown && item.Button == (int)MouseButton.Right, UiKeys.None, NotClick: notClick );
			using ( Input.UsePointerModifiers( item.Modifiers ) )
				consumed |= DispatchPointerOwner( context, delivery.Owner, input, rideOwnsInput, mouseCaptured, keyboardCaptured, textCaptured );
			SyncPointerCapture();
		}
		if ( modeSuspended || IsRiding || CamcorderArmed || level.FreeWalk != null )
		{
			SuspendParkPointer();
			return true;
		}
		// Preserve the existing once-per-frame hover/held-repeat work, without inventing a second button edge.
		var resting = frameInput with { Mouse = pointerRouter.Position, LeftDown = pointerRouter.LeftDown, LeftPressed = false, LeftReleased = false,
			RightPressed = false, Pressed = UiKeys.None, Text = "", Wheel = 0, OrderedEvents = null };
		consumed |= DispatchPointerOwner( context, pointerRouter.Capture ?? HitPointer( context, resting.Mouse ), resting,
			rideOwnsInput, mouseCaptured, keyboardCaptured, textCaptured );
		// Level's separate WorldPointerRouted latch prevents replay, without claiming the park captures the camera every frame.
		return consumed;
	}

	private bool DispatchPointerOwner( UiContext context, UiPointerOwner? owner, UiInput input, bool rideOwnsInput, bool mouseCaptured, bool keyboardCaptured, bool textCaptured )
	{
		if ( owner == null || owner.Retired ) return true;
		UpdateRideUpgradeButton( input );
		if ( mouseCaptured ) return true;
		var isPark = ReferenceEquals( owner, ParkPointerOwner );
		var isScreen = owner.Target is ValueTuple<UiScreen, UiElement?, int, int>;
		if ( (isPark || isScreen) && InfoArmOpen && Stack.Screens.Count == 1 && !IsRiding
			&& input.RightPressed && GameOptions.Current.RmbCancel && !Input.TextEntryActive )
		{
			SetInfoArm( false );
			return true;
		}
		if ( isPark )
		{
			if ( input.LeftPressed && Stack.Top?.Focused is UiTextField )
			{
				Stack.Top.Focus( null );
				Input.TextEntryActive = false;
			}
			var result = DispatchParkPointer( context, input, false, false, rideOwnsInput, Paused, false, keyboardCaptured, textCaptured );
			if ( input.LeftPressed && !result.Consumed )
				level.DispatchRoutedWorldPress( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), Input.ModifierMaskDown );
			return result.Consumed;
		}
		else if ( owner.Target is ValueTuple<UiScreen, UiElement?, int, int> target )
		{
			var (screen, element, generation, membership) = target;
			if ( !screen.IsRemoved && screen.PointerGeneration == generation && screen.ElementGeneration( element ) == membership && Stack.Screens.Contains( screen ) && (element == null || screen.Elements.Contains( element )) )
			{
				screen.UpdatePointer( context, input, element );
				Input.TextEntryActive = Stack.Top?.Focused is UiTextField { Visible: true, Enabled: true };
			}
			else
				owner.Retire();
		}
		// A local pause backdrop owns its entire down/up pair, even when its generation is now retired.
		return true;
	}
}
