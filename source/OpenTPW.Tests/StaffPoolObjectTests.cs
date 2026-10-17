using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A staff member standing where a pools-extension object (the changing room, content/pools/objects/jungle/cabin) is built over his path cell: his cell becomes an object cell
/// without links, and his patrol must take the original's off-path destination search (tp.exe 0x004F9490) to walk out instead of idling inside the footprint for good.
/// </summary>
[TestClass]
public class StaffPoolObjectTests
{
	/// <summary>The shape of Cabin.sam: a 2 x 2 cabin whose entrance is the lower left cell.</summary>
	private static readonly string[] CabinShape = ["**", "2*"];

	private sealed class Fixture
	{
		public readonly GuestPathGrid Grid = new( 20, 20 );
		public readonly ParkEconomy Park = EconomyTestData.Park();
		public readonly GuestSimulation Guests;
		public readonly StaffWorldSimulation World;
		public readonly StaffMember Entertainer;

		public Fixture()
		{
			// a crossing of paths with the cabin's anchor cell in its middle: (5, 5) is the anchor, (5, 6) the cell the entertainer stands on
			for ( var i = 3; i <= 9; i++ )
			{
				Grid.SetPath( i, 3, true );
				Grid.SetPath( i, 9, true );
				Grid.SetPath( 3, i, true );
				Grid.SetPath( 9, i, true );
				Grid.SetPath( i, 6, true );
				Grid.SetPath( 6, i, true );
			}
			Park.OpenPark();
			var settings = GuestTests.CreateSettings();
			settings.ExitLevelSeconds = 10_000;
			Guests = new GuestSimulation( Grid, settings, 5 ) { ArrivalsEnabled = false };
			World = new StaffWorldSimulation( Grid, () => Park.Staff.Members, _ => null, (3, 3) ) { Guests = Guests };
			Park.StaffWorld = World;
			Entertainer = EconomyTestData.HireBest( Park, StaffType.Entertainer );
			World.Synchronize();
			Turns( 3 );
		}

		public void Turns( int turns, System.Action? each = null )
		{
			for ( var turn = 0; turn < turns; turn++ )
			{
				var next = Park.Turn + 1;
				while ( ParkCalendar.Turn( Guests.TickCount ) < next )
					Guests.Tick( GuestSnapshotTests.Tick );
				EconomyTestData.AdvanceToTurn( Park, next );
				each?.Invoke();
			}
		}

		public (int X, int Y) Cell => Entertainer.Navigation!.Cell;

		public bool InObject => Grid.Cells.RawTypeAt( Cell.X, Cell.Y ) is ParkCellTypes.Object or ParkCellTypes.Entrance or ParkCellTypes.Exit;

		/// <summary>Stands the entertainer idle on a cell, as a member waiting for his next search.</summary>
		public void Stand( int x, int y )
		{
			var walk = Entertainer.Navigation!;
			(walk.X, walk.Y) = GuestNavigation.BytePoint( (x, y), 128, 128 );
			walk.SetDestination( walk.X, walk.Y, [] );
			(Entertainer.CellX, Entertainer.CellY) = (x + 0.5f, y + 0.5f);
			Entertainer.Job = StaffJob.Idle;
			Entertainer.State = StaffState.Idle;
			Entertainer.IdleStamp = 0;
		}

		/// <summary>Builds the cabin as the level's placement does: the footprint, then the path cell outside the entrance.</summary>
		public void BuildCabin( int rotation )
		{
			var shape = new ObjectShape( CabinShape );
			var owner = ParkCellMap.CellWord( 5, 5 );
			Grid.Cells.WriteFootprint( ObjectFootprint.GetCells( shape, 5, 5, rotation ), owner, rotation );
			ObjectAccessCells.Lay( Grid.Cells, ObjectFootprint.GetAccessPoints( shape, 5, 5, rotation ), owner, hasQueue: false );
		}
	}

	[TestMethod]
	public void AnEntertainerStandingUnderABuiltCabinWalksOutAndStaysOut()
	{
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var f = new Fixture();
			f.Stand( 5, 6 );
			f.BuildCabin( rotation );
			var footprint = ObjectFootprint.GetCells( new ObjectShape( CabinShape ), 5, 5, rotation ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
			// the rotations whose footprint covers (5, 6) put him inside it; the others leave him on his path (nothing to prove, he must simply never enter)
			var startedInside = footprint.Contains( (5, 6) );
			Assert.AreEqual( startedInside, f.InObject, $"rotation {rotation}: the start is inside exactly when the cabin covers his cell" );
			f.Turns( 300 );
			Assert.IsFalse( f.InObject, $"rotation {rotation}: after 300 turns he walks outside the cabin" );
			Assert.IsTrue( f.Grid.IsWalkable( f.Cell.X, f.Cell.Y ), $"rotation {rotation}: on a path cell" );
			f.Turns( 3000, () => Assert.IsFalse( f.InObject, $"rotation {rotation}: he never goes back into the cabin" ) );
		}
	}
}
