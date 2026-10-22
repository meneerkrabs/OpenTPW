using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

[TestClass]
public class UiPointerRouterTests
{
	private static OrderedInputEvent Event( OrderedInputEventKind kind, int button = 0, float x = 10 ) =>
		new( 1, kind, x, 10, button );

	[TestMethod]
	public void CaptureMovesAWhileHoveredBStillReceivesDownAndUp()
	{
		var router = new UiPointerRouter();
		var a = new UiPointerOwner( new object() );
		var b = new UiPointerOwner( new object() );
		router.Admit( new[] { Event( OrderedInputEventKind.Motion ) }, _ => b );
		router.Capture = a;
		var records = router.Admit( new[] { Event( OrderedInputEventKind.Motion, x: 20 ), Event( OrderedInputEventKind.ButtonDown ), Event( OrderedInputEventKind.ButtonUp ) }, _ => a );
		Assert.AreSame( a, records[0].Owner );
		Assert.AreSame( b, records[1].Owner );
		Assert.AreSame( b, records[2].Owner );
	}

	[TestMethod]
	public void AdmissionFixesBothPairsBeforeLaterOverlayChange()
	{
		var router = new UiPointerRouter();
		var park = new UiPointerOwner( new object() );
		var overlay = new UiPointerOwner( new object() );
		var records = router.Admit( new[] { Event( OrderedInputEventKind.ButtonDown ), Event( OrderedInputEventKind.ButtonUp ), Event( OrderedInputEventKind.ButtonDown ), Event( OrderedInputEventKind.ButtonUp ) }, _ => park );
		router.RefreshHover( _ => overlay );
		Assert.IsTrue( records.All( record => ReferenceEquals( record.Owner, park ) ) );
		CollectionAssert.AreEqual( new[] { true, false, true, false }, records.Select( record => record.LeftDown ).ToArray() );
		Assert.AreSame( overlay, router.Admit( new[] { Event( OrderedInputEventKind.ButtonDown ) }, _ => overlay )[0].Owner );
	}

	[TestMethod]
	public void OverlayReleaseRemainsRetiredOwnerAfterResume()
	{
		var router = new UiPointerRouter();
		var overlay = new UiPointerOwner( new object() );
		var park = new UiPointerOwner( new object() );
		router.Admit( new[] { Event( OrderedInputEventKind.ButtonDown ) }, _ => overlay );
		overlay.Retire();
		router.RefreshHover( _ => park );
		var release = router.Admit( new[] { Event( OrderedInputEventKind.ButtonUp ) }, _ => park )[0];
		Assert.AreSame( overlay, release.Owner );
		Assert.IsTrue( release.Owner!.Retired );
		Assert.IsFalse( release.LeftDown );
	}

	[TestMethod]
	public void MissingBatchPreservesOldReleaseButQuarantinesNewPress()
	{
		var router = new UiPointerRouter();
		var park = new UiPointerOwner( new object() );
		router.Admit( new[] { Event( OrderedInputEventKind.ButtonDown ) }, _ => park );
		var release = router.AdmitUnavailable( UiInput.Idle( new NVector2( 10, 10 ) ) ).Single();
		Assert.AreSame( park, release.Owner );
		var down = router.AdmitUnavailable( new UiInput( new NVector2( 10, 10 ), true, true, false, false, UiKeys.None ) ).Single();
		Assert.AreNotSame( park, down.Owner );
		var quarantinedRelease = router.Admit( new[] { Event( OrderedInputEventKind.ButtonUp ) }, _ => park ).Single();
		Assert.AreSame( down.Owner, quarantinedRelease.Owner );
	}

	[TestMethod]
	public void SuspensionDropsOwnerWithoutReplayingRelease()
	{
		var router = new UiPointerRouter();
		var park = new UiPointerOwner( new object() );
		router.Admit( new[] { Event( OrderedInputEventKind.ButtonDown ) }, _ => park );
		router.Capture = park;
		router.Suspend();
		Assert.IsNull( router.Capture );
		Assert.IsFalse( router.LeftDown );
		Assert.IsNull( router.Admit( new[] { Event( OrderedInputEventKind.ButtonUp ) }, _ => park ).Single().Owner );
	}

	[TestMethod]
	public void RemovedElementsLeaveNoGenerationAndAReaddedOneIsNewlyIdentified()
	{
		var screen = new UiScreen( "membership" );
		var button = screen.Add( new UiButton() );
		var other = screen.Add( new UiButton() );
		var first = screen.ElementGeneration( button );
		Assert.AreNotEqual( 0, first );
		Assert.AreNotEqual( first, screen.ElementGeneration( other ) );
		screen.Remove( button );
		Assert.AreEqual( 0, screen.ElementGeneration( button ), "A removed element keeps no entry." );
		screen.Add( button );
		Assert.AreNotEqual( 0, screen.ElementGeneration( button ) );
		Assert.AreNotEqual( first, screen.ElementGeneration( button ), "Re-adding must never reuse the earlier recipient identity." );
		screen.Remove( button ); screen.Remove( other );
		Assert.AreEqual( 0, screen.ElementGeneration( other ) );
	}

	[TestMethod]
	public void InjectedEdgesBecomeOrderedEventsAndAReleaseNeverAddsAPressWhileOneIsHeld()
	{
		var router = new UiPointerRouter();
		var owner = new UiPointerOwner( new object() );
		var click = router.AdmitInjected( UiInput.Click( new NVector2( 5, 6 ) ), _ => owner );
		CollectionAssert.AreEqual( new[] { OrderedInputEventKind.Motion, OrderedInputEventKind.ButtonDown, OrderedInputEventKind.ButtonUp },
			click.Select( item => item.Event.Kind ).ToArray() );
		Assert.IsTrue( click.All( item => ReferenceEquals( item.Owner, owner ) ) );
		var press = router.AdmitInjected( new UiInput( new NVector2( 5, 6 ), true, true, false, false, UiKeys.None ), _ => owner );
		CollectionAssert.AreEqual( new[] { OrderedInputEventKind.ButtonDown }, press.Select( item => item.Event.Kind ).ToArray(), "No move: the pointer did not move." );
		var release = router.AdmitInjected( new UiInput( new NVector2( 5, 6 ), false, false, true, false, UiKeys.None ), _ => null );
		CollectionAssert.AreEqual( new[] { OrderedInputEventKind.ButtonUp }, release.Select( item => item.Event.Kind ).ToArray() );
		Assert.AreSame( owner, release[0].Owner, "The release belongs to its original press." );
		var right = router.AdmitInjected( UiInput.Idle( new NVector2( 5, 6 ) ) with { RightPressed = true }, _ => owner );
		CollectionAssert.AreEqual( new[] { (int)MouseButton.Right, (int)MouseButton.Right }, right.Select( item => item.Event.Button ).ToArray() );
		var slow = router.AdmitInjected( UiInput.Click( new NVector2( 5, 6 ) ) with { NotClick = true }, _ => owner );
		Assert.IsTrue( slow[^1].Event.TimestampMilliseconds - slow[^2].Event.TimestampMilliseconds > UiInputSource.ClickMilliseconds );
		Assert.AreEqual( 0, router.AdmitInjected( UiInput.Key( UiKeys.Accept ), _ => owner ).Count, "Input without a pointer position carries no pointer event." );
	}
}
