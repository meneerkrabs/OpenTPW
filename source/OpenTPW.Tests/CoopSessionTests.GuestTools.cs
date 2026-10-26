using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] A guest's tools in a shared park (docs/COOP.md, "Building by every player"; C3a-2): the line, placement and remove tools send their commands to the host and
/// wait, taking no other click; the guest's HUD completes them when their requests come back, outside any command, and says each outcome to its issuer only. A grant taken back before
/// the host takes a request refuses it at its record, a request the host could not take leaves the click as if never made, a tool that changed lets the outcome be only a message, and the
/// host hears and says nothing of the guest's build.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>A park's HUD over an empty build menu, updated as the game updates it after the park's frame.</summary>
	private sealed class View
	{
		public ParkHud Hud { get; init; } = null!;
		public UiStringTable Strings { get; init; } = null!;
		public Action Update { get; init; } = null!;
		/// <summary>The HUD's frame with Escape pressed.</summary>
		public Action Escape { get; init; } = null!;
		/// <summary>The HUD's frame with the left button pressed (and held), the pointer off the park.</summary>
		public Action Press { get; init; } = null!;
		/// <summary>How many of the HUD's lines (the last three) say <paramref name="text"/>.</summary>
		public int Says( OpenTpwText text ) => Hud.Messages.Count( message => message == Strings.Extra( text ) );
	}

	private View ViewOf( Park park, IBuildCatalog? catalog = null )
	{
		park.Enter();
		var strings = OriginalUiTests.FakeStrings();
		var hud = new ParkHud( park.Level, strings, new EconomyParkStatus( () => park.Economy ), catalog ?? new NoItems(), new HudHost() );
		var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
		var context = new UiContext( strings, new UiFonts( _ => font ), new UiModels( name => throw new System.IO.FileNotFoundException( name ) ) )
		{ Canvas = new UiCanvas( 2048, 1536 ), ResolveTexture = _ => null };
		return new View
		{
			Hud = hud,
			Strings = strings,
			Update = () =>
			{
				park.Enter();
				hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ), false, false, false );
			},
			Escape = () =>
			{
				park.Enter();
				hud.Update( context, UiInput.Key( UiKeys.Back ), false, false, false );
			},
			Press = () =>
			{
				park.Enter();
				hud.Update( context, UiInput.Idle( new N.Vector2( -1 ) ) with { LeftDown = true, LeftPressed = true }, false, false, false );
			}
		};
	}

	/// <summary>Frames of the host and the guest as the game runs them (the guest's session step and its HUD after its park), until <paramref name="done"/> holds.</summary>
	private void RoundsUntil( Func<bool> done, Park host, Park guest, View view, string what, int limit = 600 )
	{
		for ( var round = 0; round < limit && !done(); round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
			guest.Frame( 1 / 60.0 );
			guest.Session!.Step();
			view.Update();
		}
		Assert.IsTrue( done(), what );
	}

	/// <summary>The host lets the guest build, or takes it back, and the grant reaches the guest's park.</summary>
	private void Grant( Park host, Park guest, View view, bool granted = true )
	{
		var seat = guest.Level.Commands.Player;
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( seat, granted ) ).Accepted );
		RoundsUntil( () => guest.Level.HasBuildGrant( seat ) == granted, host, guest, view, "the grant reached the guest" );
	}

	/// <summary>The guest's path tool, started at (5, 20) as its first click does.</summary>
	private static CellBuildTool StartPath( Park guest )
	{
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterPathTool( 5, 20 ) );
		Assert.AreEqual( (5, 20), guest.Level.CellTool.Start );
		return guest.Level.CellTool;
	}

	private static bool Walkable( Park park, int x, int y ) => park.Level.Guests!.Grid.IsWalkable( x, y );

	[TestMethod]
	public void AGrantedGuestsLineWaitsForTheHostAndGoesOnFromWhereItsRecordEnded()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var tool = StartPath( guest );
		var sent = guest.Level.ClickCellTool( (12, 20) );
		Assert.IsNotNull( sent?.Waiting, "the line went to the host" );
		Assert.IsNotNull( tool.Waiting );
		Assert.IsFalse( Walkable( guest, 8, 20 ), "nothing is laid before its record" );
		Assert.AreEqual( (0, 1), (tool.SegmentCount, tool.Vertices.Count), "nor stacked" );
		var ghost = tool.Ghost;
		Assert.IsNotNull( ghost );
		Assert.AreEqual( (12, 20), ghost!.Line[^1], "the ghost is the clicked line" );
		Assert.AreSame( ghost, tool.Hover( (5, 30) ), "and stays it while it waits" );
		Assert.IsNull( guest.Level.ClickCellTool( (12, 25) ), "a click while it waits is not taken" );
		Assert.IsFalse( tool.Undo(), "nor an undo" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopWaitingForHost ) );
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the line came back" );
		Assert.IsTrue( Walkable( host, 8, 20 ) && Walkable( guest, 8, 20 ), "laid on both parks at its record" );
		Assert.AreEqual( (12, 20), tool.Start, "the tool goes on from the line's end" );
		CollectionAssert.AreEqual( new[] { (5, 20), (12, 20) }, tool.Vertices.ToArray() );
		Assert.AreEqual( 1, tool.SegmentCount );
		Assert.IsNull( tool.Ghost );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuildHere ) );
		// the guest's segments are the host's to take back
		Assert.IsFalse( tool.Undo() );
		Assert.IsTrue( Walkable( guest, 8, 20 ) );
		// a grant taken back after the host took the request leaves its line
		Grant( host, guest, view, granted: false );
		Assert.IsTrue( Walkable( host, 8, 20 ) && Walkable( guest, 8, 20 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGrantTakenBackBeforeTheHostTakesALineRefusesItAtItsRecordEachTime()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		for ( var attempt = 0; attempt < 2; attempt++ )
		{
			Grant( host, guest, view );
			var tool = StartPath( guest );
			var end = (5, 24);
			Assert.IsNotNull( guest.Level.ClickCellTool( end )?.Waiting, "the line went to the host" );
			// the host takes the grant back before its stream takes the request: the request stands after the revoke in the stream
			Assert.IsTrue( host.Issue( new SetBuildGrantCommand( guest.Level.Commands.Player, false ) ).Accepted );
			RoundsUntil( () => tool.Waiting == null, host, guest, view, "the line came back" );
			Assert.IsFalse( Walkable( host, 5, 22 ) || Walkable( guest, 5, 22 ), "refused on every peer" );
			Assert.IsTrue( tool.LastCommit is { CommandRefused: true }, "it laid nothing" );
			Assert.IsFalse( tool.IsActive, "and ended the tool, as a refused command does" );
			Assert.AreEqual( attempt + 1, view.Says( OpenTpwText.CoopNotGranted ), "each refusal is said, as the rights' own" );
			Assert.AreEqual( 0, view.Says( OpenTpwText.CannotBuildHere ) );
		}
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ARequestTheHostCouldNotTakeLeavesTheLineAsIfNeverClicked()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var tool = StartPath( guest );
		// forty requests between two of the host's frames spend its burst (one-cell lines, accepted without a word): the line after them is too fast, and the host keeps no
		// record of it
		var requests = guest.Session!.GuestCommands!;
		var burst = Enumerable.Range( 0, 40 ).Select( _ => requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 30, 30, 30, 30 ) { Player = guest.Level.Commands.Player } ) ).ToArray();
		var line = guest.Level.ClickCellTool( (12, 20) )!.Waiting!.Request;
		RoundsUntil( () => tool.Waiting == null && burst.All( request => !request.IsOpen ), host, guest, view, "the line came back" );
		Assert.IsTrue( burst.All( request => request.State == CoopGuestRequestState.Applied ), "the burst's requests are records" );
		Assert.AreEqual( (CoopGuestRequestState.Refused, (CoopGuestRefusal?)CoopGuestRefusal.TooFast), (line.State, line.Refusal) );
		Assert.IsFalse( Walkable( host, 8, 20 ) || Walkable( guest, 8, 20 ) );
		Assert.AreEqual( (5, 20), tool.Start, "the tool goes on from where it was" );
		Assert.AreEqual( (0, 1), (tool.SegmentCount, tool.Vertices.Count) );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopHostCouldNotTake ) );
		// clicked again, the line is laid
		Rounds( 120, host, guest );
		Assert.IsNotNull( guest.Level.ClickCellTool( (12, 20) )?.Waiting );
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the line came back again" );
		Assert.IsTrue( Walkable( host, 8, 20 ) && Walkable( guest, 8, 20 ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ALineWhoseToolEndedWhileItWaitedIsOnlyAMessage()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		var tool = StartPath( guest );
		var request = guest.Level.ClickCellTool( (12, 20) )!.Waiting!.Request;
		tool.Cancel();
		Assert.IsFalse( tool.IsActive );
		Assert.IsNull( tool.Waiting, "the cancel drops the wait" );
		// a refusal of the orphan is said, and moves no tool
		Assert.IsTrue( host.Issue( new SetBuildGrantCommand( guest.Level.Commands.Player, false ) ).Accepted );
		RoundsUntil( () => !request.IsOpen, host, guest, view, "the request resolved" );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ) );
		Assert.IsFalse( tool.IsActive, "the ended tool stays ended" );
		Assert.IsNull( tool.LastCommit );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsPlacementFinishesWhenItComesBackAndItsRideOpensItsQueueTool()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var entry = guest.One.Entry;
		// the HUD's armed item, as the buy menu arms it
		var item = new BuildItem( "ride", entry.InfoId, BuildCategory.Rides, -1, 0, null, Array.Empty<string>(), Entry: entry );
		typeof( ParkHud ).GetField( "pendingItem", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( view.Hud, item );
		guest.Level.BuildEntry = entry;
		guest.Level.SetBuildRotation( 90 );
		var sounds = new List<uint>();
		GameAudio.UiSoundObserver = sounds.Add;
		try
		{
			// exactly Ctrl at the click: kept with the request, though the click resets the level's flag
			guest.Level.ClickBuildTool( 20, 12, modifierMask: 0xc );
			Assert.IsNotNull( guest.Level.WaitingPlacement );
			Assert.IsTrue( guest.Level.WaitingPlacement!.KeepTool );
			Assert.IsFalse( guest.Level.KeepBuildToolOnPlace );
			Assert.IsNull( guest.Level.Objects.FindAt( 20, 12 ), "nothing is placed before its record" );
			guest.Level.ClickBuildTool( 24, 12 );
			view.Update();
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopWaitingForHost ), "a second click waits" );
			Assert.AreEqual( 0, sounds.Count, "no cue before the placement" );
			// the host applies the guest's placement and plays no cue of it; the guest's park plays the guest's own when the record applies there
			var (hostSounds, guestSounds) = (0, 0);
			for ( var round = 0; round < 600 && guest.Level.WaitingPlacement != null; round++ )
			{
				clock += 1 / 60.0;
				sounds.Clear();
				host.Frame( 1 / 60.0 );
				hostSounds += sounds.Count;
				sounds.Clear();
				guest.Frame( 1 / 60.0 );
				guest.Session!.Step();
				view.Update();
				guestSounds += sounds.Count;
			}
			Assert.IsNull( guest.Level.WaitingPlacement, "the placement came back" );
			Assert.AreEqual( 0, hostSounds, "the host hears nothing of the guest's placement" );
			Assert.AreEqual( 1, guestSounds, "the guest hears its own, once, when its record applies in its park" );
		}
		finally
		{
			GameAudio.UiSoundObserver = null;
		}
		var placed = guest.Level.Objects.FindAt( 20, 12 );
		Assert.IsNotNull( placed, "placed on the guest's park at its record" );
		Assert.IsNotNull( host.Level.Objects.FindAt( 20, 12 ), "and on the host's" );
		var built = view.Strings.Extra( OpenTpwText.Built ).Replace( "{0}", "" );
		Assert.AreEqual( 1, view.Hud.Messages.Count( message => message.EndsWith( built, StringComparison.Ordinal ) ), "the Built message" );
		// the asset-free ride has no entrance cells, so no queue tool follows it: exactly Ctrl at its click kept the build tool, and a click without it leaves the tool
		Assert.IsFalse( placed!.Visitors.HasCells );
		Assert.AreSame( entry, guest.Level.BuildEntry, "kept for exactly Ctrl at the click" );
		guest.Level.ClickBuildTool( 26, 12 );
		Assert.IsFalse( guest.Level.WaitingPlacement!.KeepTool );
		RoundsUntil( () => guest.Level.WaitingPlacement == null, host, guest, view, "the second placement came back" );
		Assert.IsNotNull( host.Level.Objects.FindAt( 26, 12 ) );
		Assert.IsNull( guest.Level.BuildEntry, "the tool ends after a placement without Ctrl" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsRemovalWaitsAndTheHostSaysNothingOfIt()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		var hostView = ViewOf( host );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Grant( host, guest, view );
		Assert.IsNotNull( guest.Level.Objects.FindAt( 20, 12 ) );
		guest.Enter();
		guest.Level.IsRemovingObjects = true;
		guest.Level.ClickRemoveTool( 20, 12 );
		Assert.IsNotNull( guest.Level.WaitingRemoval );
		Assert.IsNotNull( guest.Level.Objects.FindAt( 20, 12 ), "nothing is removed before its record" );
		guest.Level.ClickRemoveTool( 21, 12 );
		Assert.AreEqual( 1, guest.Level.WaitingClicks, "a second click waits" );
		RoundsUntil( () => guest.Level.WaitingRemoval == null, host, guest, view, "the removal came back" );
		Assert.IsNull( guest.Level.Objects.FindAt( 20, 12 ) );
		Assert.IsNull( host.Level.Objects.FindAt( 20, 12 ) );
		Assert.IsTrue( guest.Level.IsRemovingObjects, "the tool stays on" );
		// a guest's refused placement on the host writes its reason there, which the host's HUD does not say
		guest.Level.IsRemovingObjects = false;
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		RoundsUntil( () => guest.Level.Objects.FindAt( 20, 12 ) != null, host, guest, view, "the host's placement reached the guest" );
		var refused = guest.Session!.GuestCommands!.Submit( guest.Level.BuildCommand( guest.One.Entry, 20, 12, 90 ) with { Player = guest.Level.Commands.Player } );
		RoundsUntil( () => !refused.IsOpen, host, guest, view, "the placement resolved" );
		hostView.Update();
		Assert.IsFalse( refused.Result!.Accepted );
		StringAssert.StartsWith( host.Level.LastActionMessage, "Cannot build" );
		Assert.AreEqual( 0, hostView.Says( OpenTpwText.CannotBuildHere ), "another player's refusal is not the host's to say" );
		Assert.AreEqual( 1, view.Says( OpenTpwText.CannotBuildHere ), "the guest says its own" );
		// the host's own refusal right after, in the same words, is said
		var own = host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) );
		Assert.IsFalse( own.Accepted );
		Assert.AreEqual( refused.Result.Reason, own.Reason, "the same words" );
		hostView.Update();
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CannotBuildHere ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void ATurnedOffRemoveToolDropsItsWaitAndAGuestsBuildingPreCheckSendsNothing()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Grant( host, guest, view );
		guest.Enter();
		guest.Level.IsRemovingObjects = true;
		guest.Level.ClickRemoveTool( 20, 12 );
		var request = guest.Level.WaitingRemoval!;
		guest.Level.IsRemovingObjects = false;
		Assert.IsNull( guest.Level.WaitingRemoval, "leaving the tool drops the wait" );
		guest.Level.IsRemovingObjects = true;
		Assert.IsNull( guest.Level.WaitingRemoval );
		RoundsUntil( () => !request.IsOpen, host, guest, view, "the removal resolved" );
		Assert.IsNull( guest.Level.Objects.FindAt( 20, 12 ), "its outcome still applies" );
		// without a grant the guest's own park refuses the line before it goes out: no request, the notice says why
		Grant( host, guest, view, granted: false );
		var open = guest.Session!.GuestCommands!.Open.Count;
		var tool = StartPath( guest );
		var line = guest.Level.ClickCellTool( (12, 20) );
		Assert.IsNull( line?.Waiting );
		Assert.IsTrue( line is { CommandRefused: true } );
		Assert.AreEqual( open, guest.Session.GuestCommands.Open.Count, "nothing went to the host" );
		Assert.IsNull( tool.Waiting );
		view.Update();
		Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotGranted ) );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>The guest's HUD arms the build tool with the fixture's ride, as the buy menu arms it.</summary>
	private static ObjectCatalogEntry ArmRide( Park guest, View view )
	{
		guest.Enter();
		var entry = guest.One.Entry;
		var item = new BuildItem( "ride", entry.InfoId, BuildCategory.Rides, -1, 0, null, Array.Empty<string>(), Entry: entry );
		typeof( ParkHud ).GetField( "pendingItem", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( view.Hud, item );
		guest.Level.BuildEntry = entry;
		guest.Level.SetBuildRotation( 90 );
		return entry;
	}

	[TestMethod]
	public void AGuestsQueueToolLaysSuccessiveNativeSegmentsThroughTheHost()
	{
		var (host, guest, _, _) = Shared();
		// the same synthetic queue on both parks before their first frame, as the queue removal tests build it
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			ConfigureQueue( park.Level.Guests!.Grid, park.One, 10, 5 );
		}
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		var ride = guest.One.Visitors;
		Assert.IsTrue( guest.Level.EnterQueueTool( ride ) );
		var tool = guest.Level.CellTool;
		Assert.AreEqual( (10, 2), tool.Start, "the tool starts at the queue's back cell" );
		Assert.IsNotNull( guest.Level.ClickCellTool( (8, 2) )?.Waiting );
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the first segment came back" );
		Assert.AreEqual( (8, 2), tool.Start );
		Assert.IsNotNull( tool.LastCommit?.QueueReceipt, "a native segment: the queue writer keeps its receipt for the next one" );
		Assert.IsNotNull( guest.Level.ClickCellTool( (8, 4) )?.Waiting );
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the second segment came back" );
		Assert.AreEqual( (8, 4), tool.Start );
		var seat = guest.Level.Commands.Player;
		var commands = host.Level.Commands.Log.Where( entry => entry.Command.Player == seat ).Select( entry => entry.Command ).ToArray();
		CollectionAssert.AreEqual( new[] { ParkCommandKind.QueueSegment, ParkCommandKind.QueueSegment }, commands.Select( command => command.Kind ).ToArray(),
			"both segments native, the second from the first one's state" );
		Assert.AreEqual( QueueState( host.One.Visitors ), QueueState( ride ) );
		Assert.IsTrue( ride.QueueCells.Contains( (8, 4) ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AGuestsPlacementRemovedBeforeItsOutcomeCameBackStillFinishesItsTool()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		ArmRide( guest, view );
		guest.Level.ClickBuildTool( 20, 12 );
		var request = guest.Level.WaitingPlacement!.Request;
		// the host takes the placement and removes it again before the guest's HUD takes the outcome
		for ( var round = 0; round < 600 && host.Level.Objects.FindAt( 20, 12 ) == null; round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
		}
		Assert.IsTrue( host.Issue( new RemoveAtCommand( 20, 12 ) ).Accepted );
		for ( var round = 0; round < 600 && (request.IsOpen || guest.Level.Objects.FindAt( 20, 12 ) != null || guest.Level.TickCount < host.Level.TickCount); round++ )
		{
			clock += 1 / 60.0;
			host.Frame( 1 / 60.0 );
			guest.Frame( 1 / 60.0 );
		}
		Assert.IsTrue( request.Accepted, "the placement applied at its record" );
		Assert.IsNull( guest.Level.Objects.FindAt( 20, 12 ), "and the host's removal after it" );
		Assert.IsNotNull( guest.Level.WaitingPlacement, "the HUD has not taken the outcome yet" );
		view.Update();
		Assert.IsNull( guest.Level.WaitingPlacement );
		Assert.IsNull( guest.Level.BuildEntry, "a click without Ctrl ends the build tool" );
		Assert.AreEqual( CellToolMode.None, guest.Level.CellTool.Mode, "and no tool follows the removed ride" );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void TheSessionsEndEndsTheWaits()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Assert.IsTrue( host.Issue( host.Level.BuildCommand( host.One.Entry, 20, 12, 90 ) ).Accepted );
		Grant( host, guest, view );
		// a placement, a removal and a line, each waiting for the host
		ArmRide( guest, view );
		guest.Level.ClickBuildTool( 24, 12 );
		guest.Level.IsRemovingObjects = true;
		guest.Level.ClickRemoveTool( 20, 12 );
		var tool = StartPath( guest );
		Assert.IsNotNull( guest.Level.ClickCellTool( (12, 20) )?.Waiting );
		Assert.IsNotNull( guest.Level.WaitingPlacement );
		Assert.IsNotNull( guest.Level.WaitingRemoval );
		guest.Enter();
		guest.Session!.End( "The guest left." );
		Assert.IsNull( tool.Waiting );
		Assert.IsFalse( tool.IsActive, "what became of the line is not known here: its tool ends" );
		Assert.IsNull( guest.Level.WaitingPlacement, "the build tool takes clicks again" );
		Assert.IsNull( guest.Level.WaitingRemoval, "and the remove tool" );
		Assert.IsNull( guest.Level.Commands.Submit, "the park sends no requests now" );
		view.Update();
		Assert.IsFalse( tool.IsActive );
	}

	[TestMethod]
	public void ALineTheHostsParkCutShortSinceTheClickEndsItsTool()
	{
		var (host, guest, _, _) = Shared();
		var view = ViewOf( guest );
		Rounds( 20, host, guest );
		Grant( host, guest, view );
		guest.Enter();
		Assert.IsTrue( guest.Level.EnterPathTool( 8, 4 ) );
		var tool = guest.Level.CellTool;
		Assert.IsNotNull( guest.Level.ClickCellTool( (12, 4) )?.Waiting, "the line's ghost had no refused cell at the click" );
		// the park changes under the line before the host takes it: a ride's queue across it, on both parks alike as a host's edit reaches both (the queue tests' synthetic
		// geometry, from (10, 6) up to (10, 2)); a path stops at a queue cell that is not the queue's end
		foreach ( var park in new[] { host, guest } )
		{
			park.Enter();
			ConfigureQueue( park.Level.Guests!.Grid, park.One, 10, 5 );
		}
		RoundsUntil( () => tool.Waiting == null, host, guest, view, "the line came back" );
		Assert.IsTrue( tool.LastCommit is { Completed: false, CommandRefused: false, StoppedAt: (10, 4) }, $"its record laid part of it ({tool.LastCommit})" );
		Assert.IsTrue( Walkable( host, 9, 4 ) && Walkable( guest, 9, 4 ), "the cells before the queue" );
		Assert.IsFalse( Walkable( guest, 12, 4 ), "none past it" );
		Assert.IsFalse( tool.IsActive, "the tool ends, as the click would have ended it on the ghost the park then showed" );
		CatchUpAndCompare( host, guest );
	}
}
