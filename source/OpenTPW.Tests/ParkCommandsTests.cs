using System;
using System.Collections.Generic;
using System.IO;
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
				// a command that writes, then throws (a rule its own checks missed)
				if ( command is RemoveAtCommand { X: 66 } )
					throw new InvalidDataException( "a missed rule" );
				// a command whose run issues another (a tool callback inside an edit) runs that one inside itself
				if ( command is RemoveAtCommand { X: 99 } )
					Commands.Apply( new RemoveObjectAtCommand( 1, 1 ) );
				if ( command is RemoveAtCommand { X: 88 } )
					Commands.Issue( new RemoveObjectAtCommand( 3, 3 ) );
				// a HUD that follows its own player's edit (the tool a placed ride opens) acts after the command
				if ( command is RemoveAtCommand { X: 77 } && Commands.IsIssuing )
					Commands.AfterIssue( () =>
					{
						Assert.IsFalse( Commands.IsExecuting, "the follow-up runs after the command" );
						Commands.Issue( new RemoveObjectAtCommand( 2, 2 ) );
					} );
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

	/// <summary>
	/// [EXT:online-coop-commands] What the issuer's own HUD does after its command (<see cref="ParkCommands.AfterIssue"/>) runs once the command has returned and issues commands that are logged on their
	/// own; a scheduled command (a replay, a peer's) is not this machine's issue, so nothing follows it.
	/// </summary>
	[TestMethod]
	public void AFollowUpOfTheIssuersCommandRunsAfterItAndIsLoggedOnItsOwnButNotAfterAScheduledOne()
	{
		var park = new Park();
		Assert.IsFalse( park.Commands.IsIssuing );
		Assert.IsTrue( park.Commands.Issue( new RemoveAtCommand( 77, 0 ) ).Accepted );
		CollectionAssert.AreEqual( new ParkCommand[] { new RemoveAtCommand( 77, 0 ), new RemoveObjectAtCommand( 2, 2 ) }, park.Ran );
		CollectionAssert.AreEqual( new[] { ParkCommandKind.RemoveAt, ParkCommandKind.RemoveObjectAt }, park.Commands.Log.Select( entry => entry.Command.Kind ).ToArray() );
		Assert.IsFalse( park.Commands.IsIssuing );
		park.Commands.Schedule( new RemoveAtCommand( 77, 0 ), 0 );
		park.Commands.ApplyDue( 0 );
		Assert.AreEqual( 3, park.Ran.Count, "a scheduled command moves no tool of this machine" );
		var ran = false;
		park.Commands.AfterIssue( () => ran = true );
		Assert.IsTrue( ran, "outside a command a follow-up runs at once" );
	}

	/// <summary>
	/// [EXT:online-coop-commands] A command issued inside another takes the outer command's player and is not asked of the rights again, so a guest replaying the host's command runs it the same way
	/// although its own player has fewer rights.
	/// </summary>
	[TestMethod]
	public void ACommandIssuedInsideAnotherIsTheOuterCommandsPlayers()
	{
		var park = new Park();
		var rights = new OnlyPaths();
		park.Commands.Rights = rights;
		park.Commands.Player = 2;
		park.Commands.Schedule( new RemoveAtCommand( 88, 0 ), 0 );
		park.Commands.ApplyDue( 0 );
		CollectionAssert.AreEqual( new ParkCommand[] { new RemoveAtCommand( 88, 0 ), new RemoveObjectAtCommand( 3, 3 ) }, park.Ran, "the host's command and the one inside it, both player 0's" );
		CollectionAssert.AreEqual( new[] { (0, ParkCommandKind.RemoveAt) }, rights.Asked, "the rights were asked of the outer command only" );
	}

	/// <summary>
	/// [EXT:online-coop-commands] A command that throws is a rejection with what it wrote before the throw, logged like any other (a replay or a peer throws the same way at the same place);
	/// the funnel goes on with the next command.
	/// </summary>
	[TestMethod]
	public void ACommandThatThrowsIsALoggedRejectionAndTheFunnelGoesOn()
	{
		var park = new Park();
		var applied = new List<ParkCommandResult>();
		park.Commands.Applied += ( _, result ) => applied.Add( result );
		var failed = park.Commands.Issue( new RemoveAtCommand( 66, 0 ) );
		Assert.IsFalse( failed.Accepted );
		Assert.IsInstanceOfType( failed.Error, typeof( InvalidDataException ) );
		StringAssert.Contains( failed.Reason, "a missed rule" );
		Assert.AreEqual( 1, park.Commands.Log.Count, "the failed command is logged" );
		Assert.AreSame( failed, applied.Single() );
		Assert.IsFalse( park.Commands.IsExecuting );
		Assert.IsTrue( park.Commands.Issue( new RemoveAtCommand( 99, 0 ) ).Accepted, "the next command runs, with the one inside it" );
		Assert.AreEqual( 3, park.Ran.Count, "the failed command, the next one and the one inside it" );
	}

	/// <summary>
	/// [EXT:online-coop-commands] An edit issued while a simulation tick runs throws where that is checked (the tests, a strict run): a peer would apply it a tick later. The held commands still apply
	/// inside the tick, and off the check a game of one runs such a command at once.
	/// </summary>
	[TestMethod]
	public void AnEditIssuedDuringATickThrowsWhereThatIsCheckedAndHeldCommandsStillApply()
	{
		var strict = ParkCommands.ThrowOnIssueInTick;
		try
		{
			var park = new Park();
			ParkCommands.ThrowOnIssueInTick = true;
			using ( park.Commands.EnterTick() )
			{
				Assert.IsTrue( park.Commands.IsInTick );
				var error = Assert.ThrowsException<InvalidOperationException>( () => park.Commands.Issue( new RemoveAtCommand( 1, 1 ) ) );
				StringAssert.Contains( error.Message, "simulation tick" );
				Assert.IsTrue( park.Commands.Apply( new RemoveAtCommand( 2, 2 ) ).Accepted, "a held command's apply is not an issue" );
			}
			Assert.IsFalse( park.Commands.IsInTick );
			Assert.IsTrue( park.Commands.Issue( new RemoveAtCommand( 1, 1 ) ).Accepted );
			ParkCommands.ThrowOnIssueInTick = false;
			using ( park.Commands.EnterTick() )
				Assert.IsTrue( park.Commands.Issue( new RemoveAtCommand( 3, 3 ) ).Accepted, "a game of one runs it at once" );
			CollectionAssert.AreEqual( new ParkCommand[] { new RemoveAtCommand( 2, 2 ), new RemoveAtCommand( 1, 1 ), new RemoveAtCommand( 3, 3 ) }, park.Ran );
			Assert.AreEqual( 3, park.Commands.Log.Count );
		}
		finally
		{
			ParkCommands.ThrowOnIssueInTick = strict;
		}
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
		Assert.AreEqual( 5 % 100, ((BacktrackCommand)park.Commands.Log[0].Command).X, "the oldest kept entry is the sixth issued" );
		CollectionAssert.AreEqual( Enumerable.Range( 5, ParkCommands.MaximumLog ).Select( index => index % 100 ).ToArray(),
			park.Commands.Log.Select( entry => ((BacktrackCommand)entry.Command).X ).ToArray(), "oldest first, as issued" );
	}

	/// <summary>The log's ring keeps exactly what a list that drops its first entry when full keeps, in the same order, through many wraps.</summary>
	[TestMethod]
	public void TheLogRingKeepsTheEntriesAndOrderOfAListThatDropsItsOldest()
	{
		foreach ( var capacity in new[] { 1, 2, 3, 7 } )
		{
			var ring = new ParkCommandLogRing( capacity );
			var reference = new List<ParkCommandLogEntry>();
			for ( var index = 0; index < capacity * 5 + 3; index++ )
			{
				var entry = new ParkCommandLogEntry( index, new BacktrackCommand( index, 0 ) );
				reference.Add( entry );
				var dropped = reference.Count > capacity;
				if ( dropped )
					reference.RemoveAt( 0 );
				Assert.AreEqual( dropped, ring.Add( entry ), $"capacity {capacity}, entry {index}" );
				Assert.AreEqual( reference.Count, ring.Count );
				CollectionAssert.AreEqual( reference, ring.ToArray(), $"capacity {capacity}, entry {index}" );
				for ( var at = 0; at < reference.Count; at++ )
					Assert.AreEqual( reference[at], ring[at] );
			}
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => ring[-1] );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => ring[ring.Count] );
		}
		var changing = new ParkCommandLogRing( 2 );
		changing.Add( new ParkCommandLogEntry( 0, new BacktrackCommand( 0, 0 ) ) );
		Assert.ThrowsException<InvalidOperationException>( () =>
		{
			foreach ( var _ in changing )
				changing.Add( new ParkCommandLogEntry( 1, new BacktrackCommand( 1, 0 ) ) );
		}, "an add during an enumeration fails, as a list's does" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new ParkCommandLogRing( 0 ) );
	}
}
