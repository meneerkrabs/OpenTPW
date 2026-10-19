using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Tests;

/// <summary>The research knob's opt-in captured-click route leaves ordinary widgets unchanged.</summary>
[TestClass]
public class UiCapturedClickTests
{
	private sealed class Probe : UiElement, IUiDragStart, IUiDragTarget, IUiCapturedClickTarget
	{
		public List<string> Calls { get; } = new();
		public NVector2 ClickPoint;
		public override bool Focusable => Visible && Enabled;
		public void DragStart( UiCanvas canvas, NVector2 point ) => Calls.Add( "start" );
		public void DragBegin( UiCanvas canvas, NVector2 point ) => Calls.Add( "begin" );
		public void DragTo( UiCanvas canvas, NVector2 point ) => Calls.Add( "move" );
		public void DragEnd( UiCanvas canvas, NVector2 point ) => Calls.Add( "end" );
		public void ClickCaptured( UiCanvas canvas, NVector2 point ) { ClickPoint = point; Calls.Add( "click" ); }
		public void CancelCapturedClick() => Calls.Add( "cancel" );
		public override void Activate() => Calls.Add( "activate" );
		public override void Draw( UiContext context, bool focused, bool pressed ) { }
	}

	private static UiInput Down( float x = 99 ) => new( new NVector2( x, 50 ), true, true, false, false, UiKeys.None );
	private static UiInput Up( float x = 101 ) => UiInput.Click( new NVector2( x, 50 ) );
	private static Probe AddProbe( UiScreen screen ) => screen.Add( new Probe { Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft } );

	[TestMethod]
	public void CapturedReleaseUsesSavedPointAfterDragEndWithoutAnotherGenericDragOrActivation()
	{
		var screen = new UiScreen( "capture" );
		var probe = AddProbe( screen );
		var context = OriginalUiTests.FakeContext( 2048, 1536 );
		screen.Update( context, Down() );
		screen.Update( context, Up() );
		CollectionAssert.AreEqual( new[] { "start", "begin", "move", "move", "end", "click" }, probe.Calls );
		Assert.AreEqual( new NVector2( 99, 50 ), probe.ClickPoint );
	}

	[TestMethod]
	public void RemovedTargetCannotClickTheElementUnderItsReleaseOrRetainCaptureAfterReadding()
	{
		var screen = new UiScreen( "capture" );
		var probe = AddProbe( screen );
		var context = OriginalUiTests.FakeContext( 2048, 1536 );
		var clicked = 0;
		screen.Add( new UiButton { Bounds = new UiRect( 100, 0, 100, 100 ), Anchor = UiAnchor.TopLeft, Clicked = () => clicked++ } );
		screen.Update( context, Down() );
		screen.Remove( probe );
		screen.Add( probe );
		screen.Update( context, Up() );
		Assert.AreEqual( 0, clicked );
		CollectionAssert.DoesNotContain( probe.Calls, "click" );
		screen.Update( context, Down() );
		screen.Update( context, Up() );
		CollectionAssert.Contains( probe.Calls, "click", "a fresh press may capture the readded target" );
	}

	[DataTestMethod]
	[DataRow( "disabled" )] [DataRow( "hidden" )] [DataRow( "notClick" )] [DataRow( "missingPointer" )]
	public void UnsafeOrUnqualifiedReleasesDoNotRunTheCapturedClick( string gate )
	{
		var screen = new UiScreen( "capture" );
		var probe = AddProbe( screen );
		var context = OriginalUiTests.FakeContext( 2048, 1536 );
		screen.Update( context, Down() );
		if ( gate == "disabled" ) probe.Enabled = false;
		if ( gate == "hidden" ) probe.Visible = false;
		var release = Up() with { NotClick = gate == "notClick" };
		if ( gate == "missingPointer" ) release = release with { Mouse = new NVector2( -1, -1 ) };
		screen.Update( context, release );
		CollectionAssert.DoesNotContain( probe.Calls, "click" );
		CollectionAssert.DoesNotContain( probe.Calls, "activate" );
		Assert.AreEqual( 1, probe.Calls.Count( call => call == "cancel" ) );
		probe.Enabled = probe.Visible = true;
		screen.Update( context, Up() );
		CollectionAssert.DoesNotContain( probe.Calls, "click", "reenabling cannot revive the cancelled press" );
		Assert.AreEqual( 1, probe.Calls.Count( call => call == "cancel" ) );
	}

	[TestMethod]
	public void LostButtonStateCancelsExactlyOnceAndCannotBeRevivedByALaterRelease()
	{
		var screen = new UiScreen( "capture" );
		var probe = AddProbe( screen );
		var context = OriginalUiTests.FakeContext( 2048, 1536 );
		screen.Update( context, Down() );
		screen.Update( context, UiInput.Idle( new NVector2( 99, 50 ) ) );
		screen.Update( context, UiInput.Idle( new NVector2( 99, 50 ) ) );
		screen.Update( context, Up() );
		Assert.AreEqual( 1, probe.Calls.Count( call => call == "cancel" ) );
		CollectionAssert.DoesNotContain( probe.Calls, "click" );
		CollectionAssert.DoesNotContain( probe.Calls, "end", "lost capture cannot later release the stale target" );
	}

	[TestMethod]
	public void PoppingAndReusingAScreenDoesNotRestoreItsOldPressRecipient()
	{
		var stack = new UiScreenStack();
		var screen = new UiScreen( "capture" );
		var probe = AddProbe( screen );
		var context = OriginalUiTests.FakeContext( 2048, 1536 );
		stack.Push( screen );
		stack.Update( context, Down() );
		stack.Pop();
		stack.Push( screen );
		stack.Update( context, Up() );
		CollectionAssert.DoesNotContain( probe.Calls, "click" );
	}

	[TestMethod]
	public void RemovingADefaultPressedButtonDoesNotActivateAnotherButtonOnRelease()
	{
		var screen = new UiScreen( "ordinary" );
		var clicked = 0;
		var first = screen.Add( new UiButton { Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft } );
		screen.Add( new UiButton { Bounds = new UiRect( 100, 0, 100, 100 ), Anchor = UiAnchor.TopLeft, Clicked = () => clicked++ } );
		var context = OriginalUiTests.FakeContext( 2048, 1536 );
		screen.Update( context, Down() );
		screen.Remove( first );
		screen.Update( context, Up() );
		Assert.AreEqual( 0, clicked, "removing a normal widget retains its baseline release comparison" );
	}

	[TestMethod]
	public void DefaultButtonsStillRequireReleaseOverThePressedElement()
	{
		var screen = new UiScreen( "ordinary" );
		var clicked = 0;
		screen.Add( new UiButton { Bounds = new UiRect( 0, 0, 100, 100 ), Anchor = UiAnchor.TopLeft, Clicked = () => clicked++ } );
		var context = OriginalUiTests.FakeContext( 2048, 1536 );
		screen.Update( context, Down() );
		screen.Update( context, Up() );
		Assert.AreEqual( 0, clicked );
		screen.Update( context, Down() );
		screen.Update( context, Up( 99 ) );
		Assert.AreEqual( 1, clicked );
	}
}
