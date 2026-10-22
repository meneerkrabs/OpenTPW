#if !OPENTPW_BROWSER
extern alias WebInputShim;
using CanvasSnapshot = WebInputShim::Veldrid.CanvasInputSnapshot;
using CanvasButton = WebInputShim::Veldrid.MouseButton;
using CanvasKey = WebInputShim::Veldrid.Key;
using CanvasModifiers = WebInputShim::Veldrid.ModifierKeys;
using RawKind = WebInputShim::OpenTPW.OrderedInputEventKind;
using RawSource = WebInputShim::OpenTPW.OrderedInputEventSource;
#else
using CanvasSnapshot = Veldrid.CanvasInputSnapshot;
using CanvasButton = Veldrid.MouseButton;
using CanvasKey = Veldrid.Key;
using CanvasModifiers = Veldrid.ModifierKeys;
using RawKind = OpenTPW.OrderedInputEventKind;
using RawSource = OpenTPW.OrderedInputEventSource;
#endif
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Executes the actual browser parser without constructing a window, DOM, graphics device or native SDL.</summary>
[TestClass]
public class CanvasInputSnapshotTests
{
	[TestMethod]
	public void ApplyPreservesCrossKindOrderBeforeShortReleaseDeferrals()
	{
		var snapshot = new CanvasSnapshot();
		Assert.IsFalse( snapshot.Apply( "k 1 F8 2 0\nm 10 20\nb 1 0\nb 0 0 31 41\nk 0 F8 0 0\nb 1 0\nb 0 0 32 42" ) );
		var raw = snapshot.OrderedEvents;
		CollectionAssert.AreEqual( new[] { RawKind.KeyDown, RawKind.Motion, RawKind.ButtonDown, RawKind.ButtonUp,
			RawKind.KeyUp, RawKind.ButtonDown, RawKind.ButtonUp }, raw.Select( item => item.Kind ).ToArray() );
		CollectionAssert.AreEqual( new long[] { 1, 2, 3, 4, 5, 6, 7 }, raw.Select( item => item.Sequence ).ToArray() );
		Assert.AreEqual( (int)CanvasKey.F8, raw[0].Key );
		Assert.AreEqual( (int)CanvasModifiers.Control, raw[0].Modifiers );
		Assert.AreEqual( 31f, raw[3].X );
		Assert.AreEqual( 42f, raw[6].Y );
		Assert.IsTrue( raw.All( item => item.Source == RawSource.Browser ) );
		Assert.IsTrue( snapshot.IsMouseDown( CanvasButton.Left ), "the legacy short release is still deferred" );
		Assert.AreEqual( 1, snapshot.KeyEvents.Count, "the key-up is deferred only in the legacy list" );
		Assert.IsTrue( snapshot.KeyEvents[0].Down );
		CollectionAssert.AreEqual( new[] { true, false, true }, snapshot.MouseEvents.Select( item => item.Down ).ToArray(),
			"a second press flushes the first held-back legacy release" );
	}

	[TestMethod]
	public void ClearAppliesDeferredLegacyReleasesWithoutReplayingRawEvents()
	{
		var snapshot = new CanvasSnapshot();
		snapshot.Apply( "k 1 P 0 0\nb 1 0\nb 0 0 4 5\nk 0 P 0 0" );
		Assert.AreEqual( 4, snapshot.OrderedEvents.Count );
		var lastSequence = snapshot.OrderedEvents[^1].Sequence;
		snapshot.Clear();
		Assert.AreEqual( 0, snapshot.OrderedEvents.Count, "deferred releases are not newly received platform facts" );
		Assert.IsFalse( snapshot.IsMouseDown( CanvasButton.Left ) );
		Assert.AreEqual( 1, snapshot.MouseEvents.Count );
		Assert.IsFalse( snapshot.MouseEvents[0].Down );
		Assert.AreEqual( 1, snapshot.KeyEvents.Count );
		Assert.IsFalse( snapshot.KeyEvents[0].Down );
		Assert.IsFalse( snapshot.Apply( "" ) );
		Assert.AreEqual( 0, snapshot.OrderedEvents.Count );
		snapshot.Clear();
		Assert.AreEqual( 0, snapshot.MouseEvents.Count );
		Assert.AreEqual( 0, snapshot.KeyEvents.Count );
		Assert.AreEqual( 0, snapshot.OrderedEvents.Count );
		snapshot.Apply( "m 6 7" );
		Assert.AreEqual( lastSequence + 1, snapshot.OrderedEvents[0].Sequence, "batch reset does not reuse sequence identities" );
	}

	[TestMethod]
	public void SeveralShortClicksInOnePumpRemainCompleteWhileLegacyStateStaysHeld()
	{
		var snapshot = new CanvasSnapshot();
		snapshot.Apply( "b 1 2\nb 0 2 1 2\nb 1 2\nb 0 2 3 4\nb 1 2\nb 0 2 5 6" );
		CollectionAssert.AreEqual( new[] { RawKind.ButtonDown, RawKind.ButtonUp, RawKind.ButtonDown,
			RawKind.ButtonUp, RawKind.ButtonDown, RawKind.ButtonUp }, snapshot.OrderedEvents.Select( item => item.Kind ).ToArray() );
		Assert.AreEqual( 6, snapshot.OrderedEvents.Count );
		Assert.IsTrue( snapshot.IsMouseDown( CanvasButton.Right ) );
		snapshot.Clear();
		Assert.IsFalse( snapshot.IsMouseDown( CanvasButton.Right ) );
		Assert.AreEqual( 1, snapshot.MouseEvents.Count );
		Assert.IsFalse( snapshot.MouseEvents[0].Down );
		Assert.AreEqual( 0, snapshot.OrderedEvents.Count );
	}

	[TestMethod]
	public void PhysicalReleaseCoordinatesAreExactWithoutInventingMotionOrChangingLegacyPosition()
	{
		var snapshot = new CanvasSnapshot();
		snapshot.Apply( "m 12 13\nb 1 0\nm -1 -1\nb 0 0 900.25 -42.5" );
		var release = snapshot.OrderedEvents[^1];
		Assert.AreEqual( RawKind.ButtonUp, release.Kind );
		Assert.IsTrue( release.PositionIsExact );
		Assert.AreEqual( 900.25f, release.X );
		Assert.AreEqual( -42.5f, release.Y );
		Assert.AreEqual( 4, snapshot.OrderedEvents.Count, "a release coordinate is not an extra motion event" );
		Assert.AreEqual( -1f, snapshot.MousePosition.X, "legacy position still reflects the last motion record" );
		Assert.AreEqual( -1f, snapshot.MousePosition.Y );
	}

	[TestMethod]
	public void LegacyAndBlurReleasesRetainExplicitlyInexactLastKnownCoordinates()
	{
		var snapshot = new CanvasSnapshot();
		snapshot.Apply( "m 8 9\nb 1 0\nb 0 0" );
		var release = snapshot.OrderedEvents[^1];
		Assert.AreEqual( RawKind.ButtonUp, release.Kind );
		Assert.IsFalse( release.PositionIsExact );
		Assert.AreEqual( 8f, release.X );
		Assert.AreEqual( 9f, release.Y );
	}

	[TestMethod]
	public void MultipleAppliesKeepSequenceAndRepeatFactsUntilClear()
	{
		var snapshot = new CanvasSnapshot();
		snapshot.Apply( "k 1 F8 2 0\nm 1 2" );
		snapshot.Apply( "k 1 F8 2 1\nb 1 1\nb 0 1 3 4\nk 0 F8 0 0" );
		CollectionAssert.AreEqual( new long[] { 1, 2, 3, 4, 5, 6 }, snapshot.OrderedEvents.Select( item => item.Sequence ).ToArray() );
		Assert.IsTrue( snapshot.OrderedEvents[2].Repeat );
		Assert.IsFalse( snapshot.OrderedEvents[0].Repeat );
		Assert.AreEqual( (int)CanvasModifiers.Control, snapshot.OrderedEvents[2].Modifiers );
		Assert.IsTrue( snapshot.OrderedEvents.Zip( snapshot.OrderedEvents.Skip( 1 ), (a, b) => a.TimestampMilliseconds <= b.TimestampMilliseconds ).All( value => value ) );
	}

	[TestMethod]
	public void PressesCarryCoordinatesAndEveryRecordUsesTheDomEventTime()
	{
		var snapshot = new CanvasSnapshot();
		snapshot.Apply( "m 1 2 1000.4\nb 1 0 10 20 1000.6\nb 0 0 11 21 1100.2\nk 1 F8 0 0 1200.5\nb 1 0\nb 0 0 12 22" );
		var raw = snapshot.OrderedEvents;
		CollectionAssert.AreEqual( new long[] { 1000, 1001, 1100, 1200 }, raw.Take( 4 ).Select( item => item.TimestampMilliseconds ).ToArray() );
		Assert.IsTrue( raw[1].PositionIsExact );
		Assert.AreEqual( 10f, raw[1].X );
		Assert.AreEqual( 20f, raw[1].Y );
		Assert.IsFalse( raw[4].PositionIsExact, "A press written without coordinates stays explicitly inexact." );
		Assert.AreEqual( 1200L, raw[4].TimestampMilliseconds, "A record without a time reuses the last event time, not the frame's clock." );
		Assert.AreEqual( 1200L, raw[5].TimestampMilliseconds );
	}
}
