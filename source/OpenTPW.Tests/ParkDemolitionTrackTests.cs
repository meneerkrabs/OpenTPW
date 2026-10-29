using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The second layer's record clear of the object demolition (ECON-050) against tp.exe's own 0x527EE0 and 0x53B280 run in the x86 oracle (docs/reverse/ORACLE.md, X37; the cases are
/// <see cref="ParkDemolitionTrackGolden"/>, written by tools/ppc-analysis/oracle_x86/demolition_golden.py from synthetic cells): an add-on object on a tile takes the tile with it (the clear
/// (0, 0, 1) of every mask cell's record, which only the exit and entry tiles of the track tool survive), the add-on a cleared tile takes demolishes the records' flag alone, and a ride's station
/// tiles go with its footprint.
/// </summary>
[TestClass]
public class ParkDemolitionTrackTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 128;
		public int Height => 128;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	private sealed record Golden( string Name, (int X, int Y) Anchor, bool Moved, (int X, int Y) Exit, (int X, int Y) Entry, int[][] Mask,
		List<(int X, int Y, int[] Before, int[] After)> Cells, List<(int X, int Y, int[] Before, int[] After)> Records, List<int> Credits );

	private static int[] Numbers( string text ) => text.Split( ' ' ).Select( int.Parse ).ToArray();

	private static List<Golden> Cases()
	{
		var cases = new List<Golden>();
		Golden? current = null;
		foreach ( var line in ParkDemolitionTrackGolden.Lines )
		{
			var parts = line.Split( '|' );
			var head = parts[0];
			if ( head.StartsWith( "case " ) )
			{
				var anchor = Numbers( parts[1] );
				var exit = Numbers( parts[3] );
				var entry = Numbers( parts[4] );
				current = new Golden( head[5..], (anchor[0], anchor[1]), parts[2] == "1", (exit[0], exit[1]), (entry[0], entry[1]),
					parts[5].Split( ',' ).Select( row => row.Select( c => c - '0' ).ToArray() ).ToArray(), new(), new(), new() );
				cases.Add( current );
			}
			else if ( head.StartsWith( "cell " ) || head.StartsWith( "rec " ) )
			{
				var at = Numbers( head[(head.IndexOf( ' ' ) + 1)..] );
				var item = (at[0], at[1], Numbers( parts[1] ), Numbers( parts[2] ));
				( head.StartsWith( "cell " ) ? current!.Cells : current!.Records ).Add( item );
			}
			else if ( head.StartsWith( "credit " ) && int.Parse( head[7..] ) != 0 )
				current!.Credits.Add( int.Parse( head[7..] ) );
		}
		return cases;
	}

	private static bool IsOrigin( int type ) => type is 0xB or 0xD or 0x10 or 0x12;

	[TestMethod]
	public void TheDemolitionClearsTheRecordsUnderItsMaskAsTheExecutableDoes()
	{
		var cases = Cases();
		Assert.AreEqual( 17, cases.Count );
		foreach ( var golden in cases )
		{
			var map = new ParkCellMap( 128, 128 );
			var family = golden.Records.Any( record => record.Before[0] is 0x10 or 0x11 or 0x12 ) ? TrackFamily.Water : TrackFamily.Kart;
			var builder = new TrackBuilder( map, family, () => null, new FlatGrid() );
			// the station whose exit and entry tiles the track tool spares: the golden's tiles, or a far one (the executable's tool globals pointed away from every tile)
			var heading = TrackHeading.PosX;
			var station = golden.Exit.X < 100
				? new TrackStation( 0, heading, (golden.Exit.X - 4, golden.Exit.Y), (golden.Exit.X - 2, golden.Exit.Y) )
				: golden.Entry.X < 100 ? new TrackStation( 0, heading, (golden.Entry.X + 2, golden.Entry.Y), (golden.Entry.X + 4, golden.Entry.Y) )
				: new TrackStation( 0, heading, (100, 100), (102, 100) );
			var owner = builder.OwnerOf( station );
			foreach ( var (x, y, before, _) in golden.Records )
			{
				var origin = IsOrigin( before[0] );
				var cell = new TrackCell
				{
					Type = origin ? (byte)before[0] : TrackCellTypes.Extension, Mask = (byte)before[1], Link = (byte)before[2], Flags = (byte)before[3], Overlap = (short)before[5], Owner = owner,
					Parent = origin ? -1 : map.Tracks.IndexOf( 0, 0 ) // the origin's index is set below
				};
				map.Tracks.Set( x, y, cell );
			}
			// the extension cells name their origin (the cell id of the record's parent word)
			foreach ( var (x, y, before, _) in golden.Records.Where( record => !IsOrigin( record.Before[0] ) ) )
			{
				var word = before[4] - 1;
				map.Tracks.Set( x, y, map.Tracks.Get( x, y ) with { Parent = word % 128 + word / 128 * 128 } );
			}
			var stationTile = golden.Records.Any( record => record.Before[0] is 0xD or 0x12 );
			foreach ( var (x, y, before, _) in golden.Cells )
			{
				var track = map.Tracks.Get( x, y );
				if ( !track.IsEmpty )
				{
					// the type byte shows the tile; a station's cell keeps the object type it overwrote (the base type)
					map.SetType( x, y, track.Type );
					if ( stationTile && golden.Records.Any( record => record.X == x && record.Y == y && record.Before[0] is 0xD or 0x12 or 0xC && stationTile ) )
						map.Tracks.Set( x, y, track with { BaseType = (byte)before[0] } );
				}
				else
					map.SetType( x, y, (byte)before[0] );
				map.SetFlags( x, y, (ParkCellFlags)before[3] );
				map.SetOwner( x, y, (ushort)before[4] );
				map.SetPlacementCount( x, y, (short)before[5] );
			}
			var credits = new List<int>();
			var hooks = new TrackEditHooks { TileCleared = _ => credits.Add( family == TrackFamily.Water ? 30 : 20 ) };
			var editor = new TrackEditor( builder, station, hooks );
			var footprint = new List<(int X, int Y, ObjectShapeCell Cell)>();
			var addOn = golden.Cells.Any( cell => cell.Before[3] == 0x10 );
			for ( var row = 0; row < golden.Mask.Length; row++ )
			{
				for ( var column = 0; column < golden.Mask[row].Length; column++ )
				{
					if ( golden.Mask[row][column] != 0 )
						footprint.Add( (golden.Anchor.X + column, golden.Anchor.Y + row, new ObjectShapeCell( column, row, addOn ? ObjectCellKind.Overlay : ObjectCellKind.Occupied, addOn ? '+' : '*' )) );
				}
			}
			map.DemolishFootprint( footprint, null, ( x, y ) => editor.ClearUnderDemolition( (x, y) ), golden.Moved );
			foreach ( var (x, y, _, after) in golden.Cells )
			{
				Assert.AreEqual( after[0], map.PrimaryTypeAt( x, y ), $"{golden.Name}: cell ({x}, {y}) type" );
				Assert.AreEqual( after[3], (int)map.FlagsAt( x, y ), $"{golden.Name}: cell ({x}, {y}) flags" );
				Assert.AreEqual( after[4], (int)map.OwnerAt( x, y ), $"{golden.Name}: cell ({x}, {y}) owner" );
			}
			foreach ( var (x, y, _, after) in golden.Records )
			{
				var record = map.Tracks.Get( x, y );
				Assert.AreEqual( after[0] == 0, record.IsEmpty, $"{golden.Name}: record ({x}, {y}) cleared" );
				if ( after[0] == 0 )
					continue;
				Assert.AreEqual( after[1], (int)record.Mask, $"{golden.Name}: record ({x}, {y}) arms" );
				Assert.AreEqual( after[2], (int)record.Link, $"{golden.Name}: record ({x}, {y}) flow link" );
				Assert.AreEqual( after[3], (int)record.Flags, $"{golden.Name}: record ({x}, {y}) flags" );
				Assert.AreEqual( after[5], (int)record.Overlap, $"{golden.Name}: record ({x}, {y}) overlap word" );
			}
			CollectionAssert.AreEqual( golden.Credits, credits, $"{golden.Name}: the refunds" );
		}
	}
}
