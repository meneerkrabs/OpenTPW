using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

[TestClass]
public class LoadChooserOwnershipReviewTests
{
	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
	private static UiScreen Open( UiScreenStack stack, List<ParkLoadEntry> calls, params ParkLoadEntry[] entries )
	{
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), entries, calls.Add );
		stack.Push( screen );
		return screen;
	}

	[TestMethod]
	public void MouseLoadAndAcceptInOneActualFrameDeliverOnlyOneCallback()
	{
		var stack = new UiScreenStack(); var calls = new List<ParkLoadEntry>();
		var entry = new ParkLoadEntry( "jungle", -1, false, "/owned.json", "Owned" );
		var screen = Open( stack, calls, entry );
		var point = screen.Find( "loadSelected" )!.ScreenRect( Context().Canvas ).Center;
		stack.Update( Context(), UiInput.Click( point ) with { Pressed = UiKeys.Accept } );
		Assert.AreEqual( 1, calls.Count, "A combined release and Enter represent one load dispatch." );
		Assert.AreSame( entry, calls[0] );
	}

	[TestMethod]
	public void BackAndAcceptInOneActualFrameCancelWithoutLoading()
	{
		var stack = new UiScreenStack(); var calls = new List<ParkLoadEntry>();
		var parent = new UiScreen( "pause" ); stack.Push( parent );
		Open( stack, calls, new ParkLoadEntry( "jungle", 0, true ) );
		stack.Update( Context(), UiInput.Key( UiKeys.Back | UiKeys.Accept ) );
		Assert.AreEqual( 0, calls.Count, "Cancellation must win over activation in the same dispatch." );
		Assert.AreSame( parent, stack.Top );
	}

	[TestMethod]
	public void RetiredChooserControlsCannotLoadOrPopAFreshChooser()
	{
		var stack = new UiScreenStack(); var calls = new List<ParkLoadEntry>();
		var parent = new UiScreen( "lobby" ); stack.Push( parent );
		var old = Open( stack, calls, new ParkLoadEntry( "old", -1, false, "/old.json", "Old" ) );
		stack.Update( Context(), UiInput.Key( UiKeys.Back ) );
		var entry = new ParkLoadEntry( "fresh", -1, false, "/fresh.json", "Fresh" );
		var fresh = Open( stack, calls, entry );
		old.Find( "loadSelected" )!.Activate();
		old.Find( "parks" )!.Activate();
		old.Find( "back" )!.Activate();
		old.Back!();
		Assert.AreEqual( 0, calls.Count );
		Assert.AreSame( fresh, stack.Top );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreSame( entry, calls[0] ); Assert.AreEqual( 1, calls.Count );
	}

	[TestMethod]
	public void ARefusedLoaderDoesNotDisableTheNextValidFrame()
	{
		var stack = new UiScreenStack(); var attempts = 0;
		var entry = new ParkLoadEntry( "jungle", 0, false );
		ParkLoadEntry? accepted = null;
		var screen = FrontEndMenu.CreateLoadScreen( stack, OriginalUiTests.FakeStrings(), new[] { entry }, selected =>
		{
			if ( ++attempts == 1 ) throw new InvalidDataException( "Synthetic loader refusal" );
			accepted = selected;
		} );
		stack.Push( screen );
		Assert.ThrowsException<InvalidDataException>( () => stack.Update( Context(), UiInput.Key( UiKeys.Accept ) ) );
		Assert.AreSame( screen, stack.Top );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 2, attempts ); Assert.AreSame( entry, accepted );
	}

	[TestMethod]
	public void RightClickCancellationAlsoWinsOverAccept()
	{
		var stack = new UiScreenStack(); var calls = new List<ParkLoadEntry>();
		var parent = new UiScreen( "lobby" ); stack.Push( parent );
		Open( stack, calls, new ParkLoadEntry( "jungle", 0, false ) );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) with { RightPressed = true } );
		Assert.AreEqual( 0, calls.Count ); Assert.AreSame( parent, stack.Top );
	}

	[TestMethod]
	public void SeparateValidFramesCanActivateNewSelectionAndPreserveMetadata()
	{
		var stack = new UiScreenStack(); var calls = new List<ParkLoadEntry>();
		var first = new ParkLoadEntry( "same", -1, false, "/first.json", "First" );
		var second = new ParkLoadEntry( "same", 3, true, null, null );
		var screen = Open( stack, calls, first, second );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		stack.Update( Context(), UiInput.Key( UiKeys.Right ) );
		stack.Update( Context(), UiInput.Key( UiKeys.Accept ) );
		Assert.AreEqual( 2, calls.Count ); Assert.AreSame( first, calls[0] ); Assert.AreSame( second, calls[1] );
		Assert.AreEqual( "/first.json", calls[0].OwnSavePath ); Assert.AreEqual( "First", calls[0].DisplayName );
		Assert.IsTrue( calls[1].Sandbox ); Assert.AreEqual( 3, calls[1].ThemeNameIndex ); Assert.IsNull( calls[1].OwnSavePath );
	}
}
