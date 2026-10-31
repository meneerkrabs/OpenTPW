using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A ride's demolition with a queue (QUEUE-015, ECON-050) against tp.exe's own 0x527EE0 run in the x86 oracle (docs/reverse/ORACLE.md, X44; the cases are <see cref="QueueDemolitionGolden"/>, written by
/// tools/ppc-analysis/oracle_x86/queue_demolition_golden.py from synthetic cells): the corner walk, the Back loop of the cached size with the forced-clear flag, the cell remover's credits, the debit,
/// and the footprint passes. Cells the walk does not reach stay.
/// </summary>
[TestClass]
public class QueueDemolitionTests
{
	private sealed record Golden( string Name, (int X, int Y) Anchor, (int X, int Y) Entrance, int Rotation, int Size, List<(int X, int Y, int[] Before, int[] After)> Cells, int Credits, int Debits );

	private static int[] Numbers( string text ) => text.Length == 0 ? Array.Empty<int>() : text.Split( ' ' ).Select( int.Parse ).ToArray();

	private static List<Golden> Cases()
	{
		var cases = new List<Golden>();
		Golden? current = null;
		foreach ( var line in QueueDemolitionGolden.Lines )
		{
			var parts = line.Split( '|' );
			if ( parts[0].StartsWith( "case " ) )
			{
				var anchor = Numbers( parts[1] );
				var entrance = Numbers( parts[2] );
				current = new Golden( parts[0][5..], (anchor[0], anchor[1]), (entrance[0], entrance[1]), int.Parse( parts[3] ), int.Parse( parts[4] ), new(), 0, 0 );
				cases.Add( current );
			}
			else if ( parts[0].StartsWith( "cell " ) )
			{
				var at = Numbers( parts[0][5..] );
				current!.Cells.Add( (at[0], at[1], Numbers( parts[1] ), Numbers( parts[2] )) );
			}
			else if ( parts[0].StartsWith( "credits" ) )
			{
				cases[^1] = current! with { Credits = Numbers( parts[0][7..].Trim() ).Length, Debits = Numbers( parts[1][6..].Trim() ).Length };
				current = cases[^1];
			}
		}
		return cases;
	}

	private static void Lay( ParkCellMap map, int x, int y, int[] fields )
	{
		map.SetType( x, y, (byte)fields[0] );
		map.SetLinks( x, y, (byte)fields[1] );
		map.SetQueueLink( x, y, (byte)fields[2] );
		map.SetFlags( x, y, (ParkCellFlags)fields[3] );
		map.SetOwner( x, y, (ushort)fields[4] );
		map.SetPlacementCount( x, y, (short)fields[5] );
	}

	private static int[] Read( ParkCellMap map, int x, int y ) =>
		new[] { map.RawTypeAt( x, y ), map.LinksAt( x, y ), map.QueueLinkAt( x, y ), (int)map.FlagsAt( x, y ), map.OwnerAt( x, y ), map.PlacementCountAt( x, y ) };

	[TestMethod]
	public void ARidesQueueGoesAsTheExecutablesDemolitionTakesIt()
	{
		var cases = Cases();
		Assert.AreEqual( 42, cases.Count );
		var failures = new List<string>();
		foreach ( var golden in cases )
		{
			var map = new ParkCellMap( 128, 128 );
			foreach ( var (x, y, before, _) in golden.Cells )
				Lay( map, x, y, before );
			var grid = new GuestPathGrid( map );
			var owner = ParkCellMap.CellWord( golden.Anchor.X, golden.Anchor.Y );
			var ride = new RideVisitorBridge( 1, "Synthetic queue", RideVisitorKind.Ride, 5, 55, 30 )
			{
				EntranceCell = golden.Entrance, ExitCell = golden.Entrance, AccessCell = golden.Entrance, Rotation = golden.Rotation, OwnerWord = owner, HasCells = true,
				Parameters = new QueueParameters( true, false, false, 130, 60, 60, 5, 30 ), QueueEntryResolver = () => golden.Entrance
			};
			var credits = 0;
			// the cache the original's +0x40 holds
			ride.ImportSavedBackCache( ParkCellMap.CellWord( golden.Entrance.X, golden.Entrance.Y ), golden.Size );
			var demolition = QueuePaths.DemolishQueue( grid, ride, golden.Size );
			credits += demolition.Refunds;
			// the footprint passes of 0x527EE0: the object cell and the entrance cell
			var footprint = new List<(int X, int Y, ObjectShapeCell Cell)>();
			if ( golden.Anchor != golden.Entrance )
				footprint.Add( (golden.Anchor.X, golden.Anchor.Y, new ObjectShapeCell( 0, 0, ObjectCellKind.Occupied, '*' )) );
			footprint.Add( (golden.Entrance.X, golden.Entrance.Y, new ObjectShapeCell( 1, 0, ObjectCellKind.Entrance, '2' )) );
			map.DemolishFootprint( footprint, removedOwner => credits++ );
			foreach ( var (x, y, _, after) in golden.Cells )
			{
				if ( !after.SequenceEqual( Read( map, x, y ) ) )
					failures.Add( $"{golden.Name}: cell ({x}, {y}) is {string.Join( " ", Read( map, x, y ) )}, the executable leaves {string.Join( " ", after )}" );
			}
			if ( golden.Credits != credits )
				failures.Add( $"{golden.Name}: {credits} credits, the executable gives {golden.Credits}" );
			if ( golden.Debits != (demolition.Debited ? 1 : 0) )
				failures.Add( $"{golden.Name}: the debit" );
		}
		Assert.AreEqual( 0, failures.Count, string.Join( "\n", failures ) );
	}
}
