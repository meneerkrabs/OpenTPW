using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
public class OrderedInputIngressTests
{
	[TestMethod]
	public void KeyMappingRetainsCrossKindOrderCoordinatesAndModifiers()
	{
		var raw = new[]
		{
			new OrderedInputEvent( 1, OrderedInputEventKind.KeyDown, 10, 20 ),
			new OrderedInputEvent( 2, OrderedInputEventKind.Motion, 30, 40 ),
			new OrderedInputEvent( 3, OrderedInputEventKind.ButtonDown, 31, 41, (int)MouseButton.Left ),
			new OrderedInputEvent( 4, OrderedInputEventKind.KeyUp, 31, 41 ),
			new OrderedInputEvent( 5, OrderedInputEventKind.ButtonUp, 32, 42, (int)MouseButton.Left )
		};
		var snapshot = new Snapshot
		{
			KeyEvents = new[] { new KeyEvent( Key.F8, true, ModifierKeys.Control ), new KeyEvent( Key.F8, false, ModifierKeys.None ) },
			MouseEvents = new[] { new MouseEvent( MouseButton.Left, true ), new MouseEvent( MouseButton.Left, false ) }
		};
		var ordered = Renderer.ResolveOrderedInput( raw, snapshot );
		Assert.AreSame( raw, ordered );
		CollectionAssert.AreEqual( new long[] { 1, 2, 3, 4, 5 }, ordered!.Select( item => item.Sequence ).ToArray() );
		Assert.AreEqual( (int)Key.F8, ordered[0].Key, "the configurable pause key is not hard-coded to P" );
		Assert.AreEqual( (int)ModifierKeys.Control, ordered[0].Modifiers );
		Assert.AreEqual( (int)Key.F8, ordered[3].Key );
		Assert.AreEqual( 32f, ordered[4].X );
		Assert.AreEqual( 42f, ordered[4].Y );
	}

	[TestMethod]
	public void EveryShortClickRemainsPresentWithinOneBatch()
	{
		var raw = new[]
		{
			new OrderedInputEvent( 11, OrderedInputEventKind.ButtonDown, 1, 2, (int)MouseButton.Right ),
			new OrderedInputEvent( 12, OrderedInputEventKind.ButtonUp, 3, 4, (int)MouseButton.Right ),
			new OrderedInputEvent( 13, OrderedInputEventKind.ButtonDown, 5, 6, (int)MouseButton.Right ),
			new OrderedInputEvent( 14, OrderedInputEventKind.ButtonUp, 7, 8, (int)MouseButton.Right )
		};
		var snapshot = new Snapshot
		{
			MouseEvents = new[] { new MouseEvent( MouseButton.Right, true ), new MouseEvent( MouseButton.Right, false ),
				new MouseEvent( MouseButton.Right, true ), new MouseEvent( MouseButton.Right, false ) }
		};
		Assert.AreSame( raw, Renderer.ResolveOrderedInput( raw, snapshot ) );
		Assert.AreEqual( 4, raw.Length );
		Assert.AreEqual( 7f, raw[3].X );
	}

	[TestMethod]
	public void MissingExtraOrMismatchedKeyboardFactsDoNotClaimAnAvailableBatch()
	{
		var down = new OrderedInputEvent( 1, OrderedInputEventKind.KeyDown, 0, 0 );
		Assert.IsNull( Renderer.ResolveOrderedInput( new[] { down }, new Snapshot() ) );
		Assert.IsNull( Renderer.ResolveOrderedInput( Array.Empty<OrderedInputEvent>(), new Snapshot
			{ KeyEvents = new[] { new KeyEvent( Key.P, true, ModifierKeys.None ) } } ) );
		Assert.IsNull( Renderer.ResolveOrderedInput( new[] { down }, new Snapshot
			{ KeyEvents = new[] { new KeyEvent( Key.P, false, ModifierKeys.None ) } } ) );
		Assert.IsNull( Renderer.ResolveOrderedInput( new[] { down }, new Snapshot
			{ KeyEvents = new[] { new KeyEvent( Key.P, true, ModifierKeys.None, true ) } } ) );
	}

	[TestMethod]
	public void MissingExtraOrMismatchedMouseFactsDoNotClaimAnAvailableBatch()
	{
		var down = new OrderedInputEvent( 1, OrderedInputEventKind.ButtonDown, 1, 2, (int)MouseButton.Left );
		Assert.IsNull( Renderer.ResolveOrderedInput( new[] { down }, new Snapshot() ) );
		Assert.IsNull( Renderer.ResolveOrderedInput( Array.Empty<OrderedInputEvent>(), new Snapshot
			{ MouseEvents = new[] { new MouseEvent( MouseButton.Left, true ) } } ) );
		Assert.IsNull( Renderer.ResolveOrderedInput( new[] { down }, new Snapshot
			{ MouseEvents = new[] { new MouseEvent( MouseButton.Left, false ) } } ) );
		Assert.IsNull( Renderer.ResolveOrderedInput( new[] { down }, new Snapshot
			{ MouseEvents = new[] { new MouseEvent( MouseButton.Right, true ) } } ) );
	}

	[TestMethod]
	public void UnmappedSdlButtonIsSkippedWithoutInvalidatingTheRestOfTheBatch()
	{
		// Veldrid files an SDL button it cannot map (a 6th button) as a Left event; the observer records it with Button -1.
		var raw = new[]
		{
			new OrderedInputEvent( 1, OrderedInputEventKind.ButtonDown, 1, 2, (int)MouseButton.Left ),
			new OrderedInputEvent( 2, OrderedInputEventKind.ButtonDown, 1, 2, -1 ),
			new OrderedInputEvent( 3, OrderedInputEventKind.ButtonUp, 1, 2, -1 ),
			new OrderedInputEvent( 4, OrderedInputEventKind.ButtonUp, 1, 2, (int)MouseButton.Left )
		};
		var snapshot = new Snapshot { MouseEvents = new[] { new MouseEvent( MouseButton.Left, true ), new MouseEvent( MouseButton.Left, true ),
			new MouseEvent( MouseButton.Left, false ), new MouseEvent( MouseButton.Left, false ) } };
		var resolved = Renderer.ResolveOrderedInput( raw, snapshot );
		CollectionAssert.AreEqual( new long[] { 1, 4 }, resolved!.Select( item => item.Sequence ).ToArray() );
		// The skipped slot still has to agree on direction, or the batch is not trustworthy.
		Assert.IsNull( Renderer.ResolveOrderedInput( raw, new Snapshot { MouseEvents = new[] { new MouseEvent( MouseButton.Left, true ),
			new MouseEvent( MouseButton.Left, false ), new MouseEvent( MouseButton.Left, false ), new MouseEvent( MouseButton.Left, false ) } } ) );
	}

	[TestMethod]
	public void SdlTimestampsStayMonotonicAcrossThe32BitWrap()
	{
		var clock = new Renderer.SdlTimestampWidener();
		Assert.AreEqual( 10L, clock.Widen( 10 ) );
		Assert.AreEqual( 5L, clock.Widen( 5 ), "A small step back is event reordering, not a wrap." );
		Assert.AreEqual( uint.MaxValue - 4L, clock.Widen( uint.MaxValue - 4 ) );
		Assert.AreEqual( (1L << 32) + 3, clock.Widen( 3 ) );
		Assert.AreEqual( (1L << 32) + 700, clock.Widen( 700 ) );
	}

	[TestMethod]
	public void EmptyObservationIsAvailableAndRepeatFlagIsPreserved()
	{
		Assert.IsNotNull( Renderer.ResolveOrderedInput( Array.Empty<OrderedInputEvent>(), new Snapshot() ) );
		var repeated = new[] { new OrderedInputEvent( 5, OrderedInputEventKind.KeyDown, 0, 0, Repeat: true ) };
		var resolved = Renderer.ResolveOrderedInput( repeated, new Snapshot
			{ KeyEvents = new[] { new KeyEvent( Key.P, true, ModifierKeys.Shift, true ) } } );
		Assert.IsNotNull( resolved );
		Assert.IsTrue( resolved![0].Repeat );
		Assert.AreEqual( (int)ModifierKeys.Shift, resolved[0].Modifiers );
	}

#if !OPENTPW_BROWSER
	[TestMethod]
	public void BrowserObservesBeforeDeferralsAndSharesExactlyOneContractIdentity()
	{
		var root = RepositoryFolder();
		var input = File.ReadAllText( Path.Combine( root, "source", "OpenTPW.Web.Veldrid", "Input.cs" ) );
		StringAssert.Contains( input, "CanvasInputSnapshot : InputSnapshot, IOrderedInputSnapshot" );
		Assert.IsTrue( input.IndexOf( "Record( keyEvent.Down", StringComparison.Ordinal ) < input.IndexOf( "AddKey( keyEvent );", StringComparison.Ordinal ) );
		Assert.IsTrue( input.IndexOf( "Record( down ?", StringComparison.Ordinal ) < input.IndexOf( "SetButton( button, down );", StringComparison.Ordinal ) );
		StringAssert.Contains( input, "orderedEvents.Clear();" );
		StringAssert.Contains( input, "exactPosition = false" );
		var script = File.ReadAllText( Path.Combine( root, "source", "OpenTPW.Web", "wwwroot", "opentpw-gl.js" ) );
		StringAssert.Contains( script, "`b 1 ${button(event)} ${event.offsetX} ${event.offsetY} ${event.timeStamp}`" );
		StringAssert.Contains( script, "${x} ${y} ${event.timeStamp}`" );
		var web = File.ReadAllText( Path.Combine( root, "source", "OpenTPW.Web", "OpenTPW.Web.csproj" ) );
		StringAssert.Contains( web, ";OPENTPW_BROWSER</DefineConstants>" );
		StringAssert.Contains( web, ";$(CommonSources)/Client/OrderedInputEvent.cs\"" );
		var shim = File.ReadAllText( Path.Combine( root, "source", "OpenTPW.Web.Veldrid", "OpenTPW.Web.Veldrid.csproj" ) );
		StringAssert.Contains( shim, "Compile Include=\"../OpenTPW.Common/Client/OrderedInputEvent.cs\"" );
	}

	[TestMethod]
	public void DesktopObserverFiltersTheWindowAndRetainsTheNormalSnapshotPump()
	{
		var source = File.ReadAllText( Path.Combine( RepositoryFolder(), "source", "OpenTPW", "Client", "Renderer.cs" ) );
		StringAssert.Contains( source, "var snapshot = Window.SdlWindow.PumpEvents();" );
		StringAssert.Contains( source, "if ( ev.windowID != orderedInputWindowId )" );
		StringAssert.Contains( source, "SDL_GetWindowID( orderedInputWindow.SdlWindowHandle )" );
		StringAssert.Contains( source, "Sdl2Events.Subscribe( orderedInputHandler )" );
		StringAssert.Contains( source, "Sdl2Events.Unsubscribe( orderedInputHandler )" );
		StringAssert.Contains( source, "orderedInputWindow.Closed += StopOrderedInputObserver" );
		StringAssert.Contains( source, "orderedInputWindow.Closed -= StopOrderedInputObserver" );
		StringAssert.Contains( source, "orderedClock.Widen( ev.timestamp )" );
		Assert.IsFalse( source.Contains( "var now = Environment.TickCount64;" ), "Event times come from SDL, not from when the frame reads the batch." );
	}

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW.Web", "OpenTPW.Web.csproj" ) ) )
				return directory.FullName;
		throw new FileNotFoundException( "source/OpenTPW.Web not found above the test binaries." );
	}
#endif

	private sealed class Snapshot : InputSnapshot
	{
		public IReadOnlyList<KeyEvent> KeyEvents { get; set; } = Array.Empty<KeyEvent>();
		public IReadOnlyList<MouseEvent> MouseEvents { get; set; } = Array.Empty<MouseEvent>();
		public IReadOnlyList<char> KeyCharPresses => Array.Empty<char>();
		public System.Numerics.Vector2 MousePosition => default;
		public float WheelDelta => 0;
		public bool IsMouseDown( MouseButton button ) => false;
	}
}
