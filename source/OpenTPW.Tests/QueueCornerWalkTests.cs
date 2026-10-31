using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The queue tool's corner walk (QUEUE-015, ECON-050) against tp.exe's own routine 0x530120 run in the x86 oracle (docs/reverse/ORACLE.md, X44; the cases are <see cref="QueueCornerWalkGolden"/>,
/// written by tools/ppc-analysis/oracle_x86/queue_golden.py from synthetic cells): the stack of points, the line start, the returned byte and every cell's type, links, queue link, flags, owner and
/// placement counter after the walk.
/// </summary>
[TestClass]
public class QueueCornerWalkTests
{
	private sealed record Golden( string Name, (int X, int Y) Entrance, int Rotation, List<(int X, int Y, int[] Before, int[] After)> Cells, int Count, int Value, (int X, int Y) Start, List<(int X, int Y)> Stack );

	private static int[] Numbers( string text ) => text.Split( ' ' ).Select( int.Parse ).ToArray();

	private static List<Golden> Cases()
	{
		var cases = new List<Golden>();
		Golden? current = null;
		foreach ( var line in QueueCornerWalkGolden.Lines )
		{
			var parts = line.Split( '|' );
			if ( parts[0].StartsWith( "case " ) )
			{
				var entrance = Numbers( parts[1] );
				current = new Golden( parts[0][5..], (entrance[0], entrance[1]), int.Parse( parts[2] ), new(), 0, 0, default, new() );
				cases.Add( current );
			}
			else if ( parts[0].StartsWith( "cell " ) )
			{
				var at = Numbers( parts[0][5..] );
				current!.Cells.Add( (at[0], at[1], Numbers( parts[1] ), Numbers( parts[2] )) );
			}
			else if ( parts[0].StartsWith( "stack " ) )
			{
				var start = Numbers( parts[2] );
				var stack = parts[3].Length == 0 ? new List<(int, int)>() : parts[3].Split( ';' ).Select( point => point.Split( ',' ) ).Select( p => (int.Parse( p[0] ), int.Parse( p[1] )) ).ToList();
				cases[^1] = current! with { Count = int.Parse( parts[0][6..] ), Value = int.Parse( parts[1] ), Start = (start[0], start[1]), Stack = stack };
				current = cases[^1];
			}
		}
		return cases;
	}

	private static void Lay( ParkCellMap map, int x, int y, int[] fields )
	{
		// type, links, queue link, flags, owner, counter
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
	public void TheWalkLeavesTheStackTheCountersAndTheLinksTheExecutableLeaves()
	{
		var cases = Cases();
		Assert.AreEqual( 82, cases.Count );
		foreach ( var golden in cases )
		{
			var map = new ParkCellMap( 128, 128 );
			foreach ( var (x, y, before, _) in golden.Cells )
				Lay( map, x, y, before );
			var result = QueueCornerWalk.Run( map, golden.Entrance, golden.Rotation, write: true );
			CollectionAssert.AreEqual( golden.Stack, result.Stack.ToList(), $"{golden.Name}: the stack" );
			Assert.AreEqual( golden.Count, result.Stack.Count, golden.Name );
			Assert.AreEqual( golden.Start, result.Start, $"{golden.Name}: the line start" );
			Assert.AreEqual( golden.Value, result.Value, $"{golden.Name}: the returned byte" );
			foreach ( var (x, y, _, after) in golden.Cells )
				CollectionAssert.AreEqual( after, Read( map, x, y ), $"{golden.Name}: cell ({x}, {y}) after" );
		}
	}

	[TestMethod]
	public void AWalkThatDoesNotWriteLeavesTheCellsAlone()
	{
		foreach ( var golden in Cases().Where( golden => golden.Cells.Any( cell => !cell.Before.SequenceEqual( cell.After ) ) ).Take( 20 ) )
		{
			var map = new ParkCellMap( 128, 128 );
			foreach ( var (x, y, before, _) in golden.Cells )
				Lay( map, x, y, before );
			var result = QueueCornerWalk.Run( map, golden.Entrance, golden.Rotation, write: false );
			CollectionAssert.AreEqual( golden.Stack, result.Stack.ToList(), golden.Name );
			foreach ( var (x, y, before, _) in golden.Cells )
				CollectionAssert.AreEqual( before, Read( map, x, y ), $"{golden.Name}: cell ({x}, {y}) is as laid" );
		}
	}

	[TestMethod]
	public void TheStackStopsAtTheOriginalsCapacity()
	{
		var serpentine = Cases().Single( golden => golden.Name == "serpentine" );
		Assert.AreEqual( QueueCornerWalk.StackCapacity, serpentine.Stack.Count );
	}
}
