using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The cells a demolished object leaves (ECON-050) against tp.exe's own demolition 0x527EE0 with the cell remover 0x5367A0 (docs/reverse/ORACLE.md, F31): the vectors were produced by running the
/// executable's routines in the x86 oracle on synthetic cells (the queue part and the presentation stubbed), so they hold no original data. Each cell is (type, links, queue link, flags, owner, counter).
/// </summary>
[TestClass]
public class ParkDemolitionTests
{
	private sealed record Case( string Name, (int X, int Y) Anchor, int[][] Mask, (int X, int Y, (int Type, int Links, int Queue, int Flags, int Owner, int Counter) Before, bool InitialPath, (int Type, int Links, int Queue, int Flags, int Owner, int Counter) After)[] Cells );

	private static readonly Case[] Cases =
	[
		// ride, separate stub paths, NoModify stubs
		new( "ride, separate stub paths, NoModify stubs", (40, 40), [[1, 1, 1], [1, 1, 1]],
			[
				( 40, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 40, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 40, (9, 4, 64, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 41, (10, 4, 4, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 43, 40, (1, 64, 0, 32, 0, 0), false, (1, 0, 0, 0, 0, 0) ),
				( 43, 41, (1, 64, 0, 32, 0, 0), false, (1, 0, 0, 0, 0, 0) ),
			] ),
		// stub path with MAP attr bit 8 (initial path)
		new( "stub path with MAP attr bit 8 (initial path)", (40, 40), [[1, 1, 1], [1, 1, 1]],
			[
				( 40, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 40, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 40, (9, 4, 64, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 41, (10, 4, 4, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 43, 40, (1, 64, 0, 32, 0, 0), true, (1, 0, 0, 32, 0, 0) ),
				( 43, 41, (1, 64, 0, 32, 0, 0), true, (1, 0, 0, 32, 0, 0) ),
			] ),
		// stub paths without NoModify
		new( "stub paths without NoModify", (40, 40), [[1, 1, 1], [1, 1, 1]],
			[
				( 40, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 40, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 40, (9, 4, 64, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 41, (10, 4, 4, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 43, 40, (1, 64, 0, 0, 0, 0), false, (1, 0, 0, 0, 0, 0) ),
				( 43, 41, (1, 64, 0, 0, 0, 0), false, (1, 0, 0, 0, 0, 0) ),
			] ),
		// stub path shared by entrance and exit (count 2, flag stays)
		new( "stub path shared by entrance and exit (count 2, flag stays)", (40, 40), [[1, 1, 1, 0], [1, 1, 1, 1]],
			[
				( 40, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 40, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 40, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 40, (9, 4, 64, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 41, (4, 0, 0, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 43, 40, (1, 80, 0, 32, 0, 0), false, (1, 0, 0, 32, 0, 0) ),
				( 43, 41, (10, 1, 1, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
			] ),
		// mask cells of other types: (1 path NoModify), 7 and 2; object cells flags 0x480
		new( "mask cells of other types: (1 path NoModify), 7 and 2; object cells flags 0x480", (40, 40), [[1, 1, 1], [1, 1, 1]],
			[
				( 40, 40, (4, 0, 0, 1152, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 40, 41, (7, 0, 0, 64, 0, 0), false, (7, 0, 0, 64, 0, 0) ),
				( 41, 40, (1, 68, 0, 32, 0, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 41, 41, (2, 0, 0, 64, 0, 0), false, (2, 0, 0, 64, 0, 0) ),
				( 42, 40, (9, 4, 64, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 42, 41, (10, 4, 4, 0, 5161, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 43, 40, (1, 64, 0, 32, 0, 0), false, (1, 0, 0, 0, 0, 0) ),
				( 43, 41, (1, 64, 0, 32, 0, 0), false, (1, 0, 0, 0, 0, 0) ),
			] ),
		// mask runs off the map edge (x=128 ignored)
		new( "mask runs off the map edge (x=128 ignored)", (126, 10), [[1, 1, 1]],
			[
				( 126, 10, (4, 0, 0, 0, 1407, 0), false, (0, 0, 0, 0, 0, 0) ),
				( 127, 10, (4, 0, 0, 0, 1407, 0), false, (0, 0, 0, 0, 0, 0) ),
			] ),
	];

	private static (int, int, int, int, int, int) Read( ParkCellMap map, int x, int y ) =>
		(map.RawTypeAt( x, y ), map.LinksAt( x, y ), map.QueueLinkAt( x, y ), (int)map.FlagsAt( x, y ), map.OwnerAt( x, y ), map.PlacementCountAt( x, y ));

	/// <summary>The shape symbol of a mask cell: the cell's own entrance, exit or object symbol (the type of a mask cell only chooses what the placement wrote).</summary>
	private static char SymbolOf( int type ) => type switch { 9 => '2', 10 => 'S', _ => '*' };

	[TestMethod]
	public void ADemolishedObjectLeavesTheCellsTheOriginalLeaves()
	{
		foreach ( var golden in Cases )
		{
			var map = new ParkCellMap( 128, 128 );
			var initial = new HashSet<(int, int)>();
			foreach ( var (x, y, before, isInitial, _) in golden.Cells )
			{
				map.SetType( x, y, (byte)before.Type );
				map.SetLinks( x, y, (byte)before.Links );
				map.SetQueueLink( x, y, (byte)before.Queue );
				map.SetFlags( x, y, (ParkCellFlags)before.Flags );
				map.SetOwner( x, y, (ushort)before.Owner );
				map.SetPlacementCount( x, y, (short)before.Counter );
				if ( isInitial )
					initial.Add( (x, y) );
			}
			map.InitialPathSource = ( x, y ) => initial.Contains( (x, y) );
			var footprint = new List<(int X, int Y, ObjectShapeCell Cell)>();
			for ( var row = 0; row < golden.Mask.Length; row++ )
			{
				for ( var column = 0; column < golden.Mask[row].Length; column++ )
				{
					if ( golden.Mask[row][column] == 0 )
						continue;
					var (x, y) = (golden.Anchor.X + column, golden.Anchor.Y + row);
					var type = golden.Cells.FirstOrDefault( cell => cell.X == x && cell.Y == y ).Before.Type;
					footprint.Add( (x, y, new ObjectShapeCell( column, row, ObjectCellKind.Occupied, SymbolOf( type ) )) );
				}
			}
			map.DemolishFootprint( footprint );
			foreach ( var (x, y, _, _, after) in golden.Cells )
				Assert.AreEqual( after, Read( map, x, y ), $"{golden.Name}: cell ({x}, {y})" );
		}
	}

	/// <summary>A type-3 cell of the mask credits the queue cell's refund while its owner cell is not empty (raster order: the owner cell, the first one, is emptied before the later cells are looked at).</summary>
	[TestMethod]
	public void AQueueCellOfTheMaskRefundsWhileItsOwnerCellHoldsSomething()
	{
		var map = new ParkCellMap( 128, 128 );
		var owner = ParkCellMap.CellWord( 40, 40 );
		map.SetType( 40, 40, ParkCellTypes.Object );
		map.SetOwner( 40, 40, owner );
		map.SetType( 41, 40, ParkCellType.Queue );
		map.SetOwner( 41, 40, owner );
		map.SetType( 40, 41, ParkCellType.Queue );
		map.SetOwner( 40, 41, owner );
		var footprint = new[] { (40, 40, 'Q'), (41, 40, 'Q'), (40, 41, 'Q') }.Select( cell => (cell.Item1, cell.Item2, new ObjectShapeCell( 0, 0, ObjectCellKind.Occupied, cell.Item3 )) ).ToList();
		// the owner cell comes second here: it still holds the object when the first queue cell goes, and is gone for the next
		footprint = new[] { footprint[1], footprint[0], footprint[2] }.ToList();
		var refunds = new List<ushort>();
		map.DemolishFootprint( footprint, refunds.Add );
		CollectionAssert.AreEqual( new[] { owner }, refunds, "only the cell removed while the owner cell was still typed refunds" );
		Assert.AreEqual( 0, map.RawTypeAt( 41, 40 ) + map.RawTypeAt( 40, 41 ) + map.RawTypeAt( 40, 40 ) );
	}

	/// <summary>The queue the demolition clears is left empty whole: links, queue link, owner, counter and the flag word (the NoModify flag included).</summary>
	[TestMethod]
	public void ATornDownQueueCellKeepsNoFlagWord()
	{
		var map = new ParkCellMap( 128, 128 );
		var owner = ParkCellMap.CellWord( 20, 20 );
		map.SetType( 21, 20, ParkCellType.Queue );
		map.SetLinks( 21, 20, 0x40 );
		map.SetQueueLink( 21, 20, 0x40 );
		map.SetOwner( 21, 20, owner );
		map.SetFlags( 21, 20, ParkCellFlags.NoModify | ParkCellFlags.EntranceArea );
		map.SetPlacementCount( 21, 20, 2 );
		ObjectAccessCells.ClearQueue( map, owner );
		Assert.AreEqual( (0, 0, 0, 0, 0, 0), Read( map, 21, 20 ) );
	}
}
