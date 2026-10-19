using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>[EXT:online-coop-commands] The park's edit funnel on its own (docs/COOP.md): ticks, the log, the rights hook, the visit gate and held commands.</summary>
[TestClass]
public class ParkCommandsTests
{
	private sealed class Park
	{
		public long Tick;
		public bool Visit;
		public readonly List<ParkCommand> Ran = new();
		public ParkCommands Commands = null!;

		public Park()
		{
			Commands = new ParkCommands( () => Tick, command =>
			{
				Ran.Add( command );
				// a command whose run issues another (a tool callback inside an edit) runs that one inside itself
				if ( command is RemoveAtCommand { X: 99 } )
					Commands.Apply( new RemoveObjectAtCommand( 1, 1 ) );
				return command is RemoveAttractionCommand { Attraction: 0 } ? ParkCommandResult.Rejected( "no attraction 0" ) : ParkCommandResult.Ok( command.Kind );
			}, () => Visit );
		}
	}

	private sealed class OnlyPaths : IParkCommandRights
	{
		public readonly List<(int Player, ParkCommandKind Kind)> Asked = new();
		public bool Allows( int player, ParkCommandKind kind )
		{
			Asked.Add( (player, kind) );
			return player == ParkCommands.LocalPlayer || kind == ParkCommandKind.LayLine;
		}
	}

	[TestMethod]
	public void ACommandRunsAtOnceBeforeTheNextTickAndIsLoggedWithThatTickAcceptedOrNot()
	{
		var park = new Park { Tick = 41 };
		var applied = new List<(ParkCommandLogEntry Entry, ParkCommandResult Result)>();
		park.Commands.Applied += ( entry, result ) => applied.Add( (entry, result) );
		Assert.IsTrue( park.Commands.Issue( new RemoveAtCommand( 1, 2 ) ).Accepted );
		Assert.AreEqual( ParkCommandKind.RemoveAt, park.Commands.Issue( new RemoveAtCommand( 1, 2 ) ).As<ParkCommandKind>() );
		park.Tick = 50;
		var refused = park.Commands.Issue( new RemoveAttractionCommand( 0 ) );
		Assert.IsFalse( refused.Accepted );
		Assert.AreEqual( "no attraction 0", refused.Reason );
		CollectionAssert.AreEqual( new long[] { 41, 41, 50 }, park.Commands.Log.Select( entry => entry.Tick ).ToArray() );
		Assert.AreEqual( 3, applied.Count );
		Assert.IsFalse( applied[2].Result.Accepted, "a refusal is logged too: a replay refuses it the same way" );
	}

	[TestMethod]
	public void ACommandIssuedInsideAnotherRunsAsPartOfItAndIsNotLoggedOnItsOwn()
	{
		var park = new Park();
		park.Commands.Issue( new RemoveAtCommand( 99, 0 ) );
		Assert.AreEqual( 2, park.Ran.Count );
		Assert.AreEqual( 1, park.Commands.Log.Count );
		Assert.IsFalse( park.Commands.IsExecuting );
	}

	[TestMethod]
	public void TheRightsHookRefusesAPlayersCommandBeforeItRunsAndTheLocalPlayerIsTheHost()
	{
		var park = new Park();
		var rights = new OnlyPaths();
		park.Commands.Rights = rights;
		Assert.IsTrue( park.Commands.Apply( new RemoveAtCommand( 1, 1 ) ).Accepted, "player 0, the host" );
		var refused = park.Commands.Apply( new RemoveAtCommand( 1, 1 ) { Player = 2 } );
		Assert.IsFalse( refused.Accepted );
		StringAssert.Contains( refused.Reason, "Player 2" );
		Assert.IsTrue( park.Commands.Apply( new LayLineCommand( CellToolMode.Path, -1, 0, 0, 3, 0 ) { Player = 2 } ).Accepted );
		Assert.AreEqual( 2, park.Ran.Count, "the refused command never ran" );
		CollectionAssert.AreEqual( new[] { (0, ParkCommandKind.RemoveAt), (2, ParkCommandKind.RemoveAt), (2, ParkCommandKind.LayLine) }, rights.Asked );
		park.Commands.Player = 3;
		Assert.AreEqual( 3, park.Commands.Log.Count );
		park.Commands.Issue( new RemoveAtCommand( 1, 1 ) );
		Assert.AreEqual( 3, park.Commands.Log[^1].Command.Player, "Issue stamps the local player's slot" );
	}

	[TestMethod]
	public void AVisitTakesNoEditOfAnyKindAndAMalformedCommandNeverRuns()
	{
		var park = new Park { Visit = true };
		foreach ( var (command, _) in ParkCommandCodecTests.Schema )
			Assert.IsFalse( park.Commands.Apply( command ).Accepted, command.Kind.ToString() );
		park.Visit = false;
		Assert.IsFalse( park.Commands.Apply( new PlaceObjectCommand( "x", 1, 1, 45 ) ).Accepted );
		Assert.IsFalse( park.Commands.Apply( new RemoveAtCommand( 1, 1 ) { Player = -1 } ).Accepted );
		Assert.AreEqual( 0, park.Ran.Count );
	}

	[TestMethod]
	public void HeldCommandsApplyAtTheStartOfTheirTickInTheOrderTheyWereHeld()
	{
		var park = new Park { Tick = 10 };
		park.Commands.Schedule( new RemoveAtCommand( 2, 0 ), 12 );
		park.Commands.Schedule( new RemoveAtCommand( 1, 0 ), 11 );
		park.Commands.Schedule( new RemoveAtCommand( 3, 0 ), 12 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => park.Commands.Schedule( new RemoveAtCommand( 0, 0 ), 9 ) );
		Assert.AreEqual( 3, park.Commands.ScheduledCount );
		park.Commands.ApplyDue( 10 );
		Assert.AreEqual( 0, park.Ran.Count );
		park.Tick = 12;
		park.Commands.ApplyDue( 12 );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, park.Ran.Cast<RemoveAtCommand>().Select( command => command.X ).ToArray() );
		CollectionAssert.AreEqual( new long[] { 12, 12, 12 }, park.Commands.Log.Select( entry => entry.Tick ).ToArray(), "logged at the tick they ran before" );
		Assert.AreEqual( 0, park.Commands.ScheduledCount );
	}

	[TestMethod]
	public void TheInMemoryLogKeepsItsNewestEntriesAndCountsTheDropped()
	{
		var park = new Park();
		for ( var index = 0; index < ParkCommands.MaximumLog + 5; index++ )
			park.Commands.Apply( new BacktrackCommand( index % 100, 0 ) );
		Assert.AreEqual( ParkCommands.MaximumLog, park.Commands.Log.Count );
		Assert.AreEqual( 5, park.Commands.DroppedLog );
		Assert.AreEqual( (ParkCommands.MaximumLog + 4) % 100, ((BacktrackCommand)park.Commands.Log[^1].Command).X );
	}
}
