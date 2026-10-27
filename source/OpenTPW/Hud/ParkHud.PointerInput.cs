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
	private NVector2 routedRightPressPoint;
	private long routedRightPressTime;
	private bool routedRightPressMoved;

	/// <summary>Unit-test seam: the level this HUD was built for (the fixtures build the HUD around a hand-made level).</summary>
	internal Level LevelForTests => level;

	// Unit-test seams for the private screens and state the routed-pointer tests need; they build nothing the game would not.
	internal static UiScreen NewPriceScreenForTests( string id ) => new ShopPriceScreen( id );
	internal static UiScreen NewUpgradeScreenForTests() => new RideUpgradeScreen( null! );
	internal static UiScreen NewAttractionsScreenForTests( ParkHud owner ) => new AttractionsScreen( owner );
	internal static UiElement NewAttractionHeaderForTests( ParkHud owner, BuildCategory category, int column, UIStrings header ) => new AttractionHeader( owner, category, column, header );
	internal void SetAttractionStateForTests( BuildCategory category, float valueAge ) { attractionCategory = category; attractionValueAge = valueAge; }
	internal float AttractionValueAgeForTests => attractionValueAge;

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
	/// [BIN:TPW-EXE:0x409350 / 0x4092a0 creators] only the P key (0x4a27f0) and the offline focus-loss pause (0x4a2790) create it, and every resume destroys it (0x4a27c0).
	/// OpenTPW makes it offline only (a co-op or read-only visit never gets one).
	/// </summary>
	private void CreatePausePointerOverlay()
	{
		// [BIN:TPW-EXE:0x4a2790 create] the window is made only while [0x7cb2cc] is still empty
		if ( pausePointerOverlay != null || level == null || level.CoopRole != CoopRole.None || level.IsReadOnlyVisit )
			return;
		// [BIN:TPW-EXE:0x42a760 camera input] the camera takes its buttons, wheel, pointer position and keys only from messages that the procedures of the HUD (0x488290, 0x48845c, 0x488b6a), the hidden-HUD overlay (0x4a287d) and the chat mode (0x4bd5cc) pass on to it;
		// the backdrop is the hit window of every pointer message and the focus window of every key (0x47ed80 ends in 0x65e59b), and its procedure 0x4a2680 hands all it does not use to the default procedure 0x65f6d1, which forwards nothing: the camera takes no input
		// under it, and the HUD, which gets the leave message (0x10002: [0x790ab0] |= 0x100) when the layer appears, no pointer either
		pausePointerOverlay = new UiPointerOwner( new object() );
		pauseOverlayBelow = Stack.Screens.ToArray();
	}

	/// <summary>The P window's backdrop is up and nothing was opened after it: it holds the focus and the pointer (0x47ee67, 0x65db25).</summary>
	private bool PauseBackdropHoldsFocus => pausePointerOverlay != null && (Stack.Top is not { } top || Array.IndexOf( pauseOverlayBelow, top ) >= 0);

	// created on first use: fixtures build a HUD without its constructor
	private HashSet<UiScreen>? seenPausingBoxes, actuallyPausingBoxes;

	/// <summary>
	/// A generic message box that actually paused (0x47F26C: the enable word was 1, so no pause menu, map or postcard held the pause) resumes the game when it closes (0x47EED0 message 0x14 ->
	/// 0x409300), which also clears a P pause and destroys the P window (0x4a27c0): a box opened under P and closed again leaves the game running.
	/// </summary>
	// [BIN:TPW-EXE:0x47efac box close resume] the box's procedure 0x47EED0 resumes on its destruction (0x47EFAC-0x47EFDA) when its own property 5 says it paused; the P key does not clear the enable word, so a box opened during P pauses again and its close ends the P pause
	private void UpdatePausingBoxes()
	{
		var holder = false;
		seenPausingBoxes ??= new(); actuallyPausingBoxes ??= new();
		foreach ( var screen in Stack.Screens )
		{
			if ( screen.Name is PauseMenuScreen or MapScreenName or PostcardScreen or "texturePackDownload" )
				holder = true;
			else if ( IsPausingMessageBox( screen.Name ) && seenPausingBoxes.Add( screen ) && !holder && !OnlineSession )
				actuallyPausingBoxes.Add( screen );
		}
		if ( seenPausingBoxes.Count == 0 )
			return;
		foreach ( var closed in seenPausingBoxes.Where( box => !Stack.Screens.Contains( box ) ).ToArray() )
		{
			seenPausingBoxes.Remove( closed );
			if ( actuallyPausingBoxes.Remove( closed ) && (pausePointerOverlay != null || applicationPaused) )
			{
				if ( Status.Speed == GameSpeed.Paused ) SetUserGameSpeed( GameSpeed.Normal );
				else ClearApplicationPause();
				RetirePausePointerOverlay();
			}
		}
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
		coasterPress = pendingPathEntry = false;
		pressHostStarts = null;
		pendingQueueEdit = null;
		legacyLeftPress = legacyRightPress = null;
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
		return input.OrderedEvents != null ? pointerRouter.Admit( input.OrderedEvents, point => HitPointer( context, point ) )
			: input.OrderedUnavailable ? pointerRouter.AdmitUnavailable( input )
			: pointerRouter.AdmitInjected( input, point => HitPointer( context, point ) );
	}

	private bool IsPlainPauseKey( OrderedInputEvent item, UiInput frameInput, bool keyboardCaptured, bool textCaptured ) =>
		item.Kind == OrderedInputEventKind.KeyDown && !item.Repeat && item.Key == (int)Input.ChordKey( InputButton.Pause )
		&& ((ModifierKeys)item.Modifiers & ModifierKeys.Control) == 0 && !frameInput.PointerHostBlocked && !Paused && !IsRiding && !Input.TextEntryActive && !keyboardCaptured && !textCaptured;

	private bool DrainPointerEvents( UiContext context, IReadOnlyList<UiRoutedEvent> events, UiInput frameInput, bool rideOwnsInput, bool mouseCaptured, bool keyboardCaptured, bool textCaptured )
	{
		if ( frameInput.PointerHostBlocked || mouseCaptured )
		{
			// A P key that arrived with the batch still toggles the pause while ImGui has the mouse (the key is not a pointer fact);
			// only a host block (a park being built) swallows it.
			if ( !frameInput.PointerHostBlocked && level.CoopRole == CoopRole.None && !level.IsReadOnlyVisit )
				foreach ( var delivery in events )
					if ( IsPlainPauseKey( delivery.Event, frameInput, keyboardCaptured, textCaptured ) )
						ToggleUserPause();
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
				if ( IsPlainPauseKey( item, frameInput, keyboardCaptured, textCaptured ) )
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
			// the right button: a release that follows its press within 200 ms and 8 px is the quick click the park view cancels on; anything longer or wider was the camera's drag
			var rightPress = item.Kind == OrderedInputEventKind.ButtonDown && item.Button == (int)MouseButton.Right;
			var rightRelease = item.Kind == OrderedInputEventKind.ButtonUp && item.Button == (int)MouseButton.Right;
			if ( rightPress )
			{
				routedRightPressPoint = point;
				routedRightPressTime = item.TimestampMilliseconds;
				routedRightPressMoved = false;
			}
			if ( Math.Abs( point.X - routedRightPressPoint.X ) > UiInputSource.RightClickSlop * frameInput.PointerScaleX || Math.Abs( point.Y - routedRightPressPoint.Y ) > UiInputSource.RightClickSlop * frameInput.PointerScaleY )
				routedRightPressMoved = true;
			var rightClicked = rightRelease && !routedRightPressMoved && item.TimestampMilliseconds - routedRightPressTime <= UiInputSource.RightClickMilliseconds
				&& !(item.Source == OrderedInputEventSource.Browser && !item.PositionIsExact);
			var input = new UiInput( point, delivery.LeftDown, leftPress, leftRelease, rightPress, UiKeys.None, NotClick: notClick, RightReleased: rightRelease, RightClicked: rightClicked,
				LeftReleaseLost: leftRelease && item.Source == OrderedInputEventSource.Browser && !item.PositionIsExact );
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
			RightPressed = false, RightReleased = false, RightClicked = false, Pressed = UiKeys.None, Text = "", Wheel = 0, OrderedEvents = null };
		var restingOwner = pointerRouter.Capture ?? HitPointer( context, resting.Mouse );
		// Hover help belongs to the screen element under the pointer; once the pointer rests on the park or a backdrop the last help is stale.
		if ( restingOwner?.Target is not ValueTuple<UiScreen, UiElement?, int, int> )
			context.HoverHelp = null;
		consumed |= DispatchPointerOwner( context, restingOwner, resting,
			rideOwnsInput, mouseCaptured, keyboardCaptured, textCaptured );
		// Level's separate WorldPointerRouted latch prevents replay, without claiming the park captures the camera every frame.
		return consumed;
	}

	private bool DispatchPointerOwner( UiContext context, UiPointerOwner? owner, UiInput input, bool rideOwnsInput, bool mouseCaptured, bool keyboardCaptured, bool textCaptured )
	{
		if ( owner == null ) return true;
		if ( owner.Retired ) return owner.CapturesMouse;
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
			// [BIN:TPW-EXE:0x46C610 release slot] the level's tools (a placement, a removal) act on the release the park took, at the cell under the pointer there
			if ( input.LeftReleased && !input.LeftReleaseLost && !result.Consumed )
				level.DispatchRoutedWorldClick( input.Mouse, new NVector2( context.Canvas.Width, context.Canvas.Height ), Input.ModifierMaskDown );
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
		// A local pause backdrop owns its entire down/up pair, even when its generation is now retired. It takes the click without capturing the camera.
		return owner.CapturesMouse;
	}
}
