using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// TRACK-010: <see cref="TrackBridges"/> against tp.exe's own bridge walks 0x52C6F0 and 0x52CCB0 run in the x86 oracle (tools/ppc-analysis/oracle_x86/bridge_walk_golden.py, docs/reverse/ORACLE.md X47)
/// on synthetic second-layer records: every record's flag word after the op 0x86 pair must be the executable's.
/// </summary>
[TestClass]
public class TrackBridgeWalkGoldenTests
{
	private sealed record Case( string Name, int AnchorX, int AnchorY, int Rotation, int TrackType, List<(int X, int Y, int Kind, int Arms, int Link, int Flags, int Word, int After)> Records, int Forward, int Reverse );

	private static IEnumerable<Case> Cases()
	{
		Case? current = null;
		List<(int, int, int, int, int, int, int, int)>? records = null;
		foreach ( var line in TrackBridgeWalkGolden.Lines )
		{
			var parts = line.Split( '|' );
			if ( line.StartsWith( "case ", StringComparison.Ordinal ) )
			{
				var anchor = parts[1].Split( ' ' ).Select( int.Parse ).ToArray();
				records = new();
				current = new Case( parts[0], anchor[0], anchor[1], int.Parse( parts[2], CultureInfo.InvariantCulture ), int.Parse( parts[3], CultureInfo.InvariantCulture ), new(), 0, 0 );
			}
			else if ( line.StartsWith( "rec ", StringComparison.Ordinal ) )
			{
				var at = parts[0][4..].Split( ' ' ).Select( int.Parse ).ToArray();
				var before = parts[1].Split( ' ' ).Select( int.Parse ).ToArray();
				current!.Records.Add( (at[0], at[1], before[0], before[1], before[2], before[3], before[4], int.Parse( parts[2], CultureInfo.InvariantCulture )) );
			}
			else if ( line.StartsWith( "walk ", StringComparison.Ordinal ) )
			{
				var walk = parts[0][5..];
				yield return current! with { Forward = int.Parse( walk, CultureInfo.InvariantCulture ), Reverse = int.Parse( parts[1], CultureInfo.InvariantCulture ) };
			}
		}
	}

	[TestMethod]
	public void WalksSetTheExecutablesFlagWords()
	{
		var count = 0;
		var failures = new List<string>();
		foreach ( var item in Cases() )
		{
			count++;
			var cells = new ParkCellMap( 128, 128 );
			var tracks = cells.Tracks;
			var station = TrackStation.FromPlacement( item.AnchorX, item.AnchorY, item.Rotation );
			var owner = tracks.IndexOf( station.First.X, station.First.Y ) + 1;
			var rideWord = ParkCellMap.CellWord( item.AnchorX, item.AnchorY );
			int OwnerOf( int word ) => word == rideWord ? owner : word == 0 ? 0 : word + 100_000;
			foreach ( var r in item.Records )
			{
				var type = (byte)r.Kind;
				var cell = new TrackCell { Type = type, Mask = (byte)r.Arms, Link = (byte)r.Link, Flags = (byte)r.Flags };
				if ( type == TrackCellTypes.Extension )
				{
					cell.Parent = r.Word - 1;
					cell.Mask = 0;
					cell.Flags = 0;
				}
				else
				{
					cell.Parent = -1;
					cell.Owner = OwnerOf( r.Word );
					if ( (r.X, r.Y) == station.First )
						cell.Link = station.Heading;
				}
				tracks.Set( r.X, r.Y, cell );
			}
			TrackBridges.Walk( cells, owner );
			foreach ( var r in item.Records.Where( r => r.Kind != TrackCellTypes.Extension ) )
			{
				var actual = tracks.Get( r.X, r.Y ).Flags;
				if ( actual != r.After )
					failures.Add( $"{item.Name} at {r.X},{r.Y} kind {r.Kind} arms {r.Arms:X2} flags {r.Flags}: expected {r.After}, got {actual}" );
			}
		}
		Assert.IsTrue( count >= 500, $"the golden holds {count} cases" );
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures.Take( 12 ) ) );
	}

	private sealed class Grid : IParkGrid
	{
		public int Width => 128;
		public int Height => 128;
		public System.Numerics.Vector2 Origin => default;
		public float GetCellHeight( int x, int y ) => 0;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
	}

	/// <summary>The oracle's track tool lines (the click handler's op list through 0x53AAC0) over paths: the cell types and the records after, replayed on <see cref="TrackLineWriter"/>.</summary>
	[TestMethod]
	public void LinesClearThePathsUnderTheirLastTileAndBridgeTheRestAsTheExecutableDoes()
	{
		var lines = TrackBridgeWalkGolden.Lines;
		var count = 0;
		var failures = new List<string>();
		for ( var at = 0; at < lines.Length; at++ )
		{
			if ( !lines[at].StartsWith( "line ", StringComparison.Ordinal ) )
				continue;
			count++;
			var parts = lines[at].Split( '|' );
			var anchor = parts[1].Split( ' ' ).Select( int.Parse ).ToArray();
			var start = parts[2].Split( ' ' ).Select( int.Parse ).ToArray();
			var end = parts[3].Split( ' ' ).Select( int.Parse ).ToArray();
			var cells = new List<(int X, int Y, int Before, int After)>();
			var tiles = new List<(int X, int Y, int[] Record)>();
			for ( var next = at + 1; next < lines.Length && !lines[next].StartsWith( "line ", StringComparison.Ordinal ); next++ )
			{
				var item = lines[next].Split( '|' );
				var where = item[0].Split( ' ' ).Skip( 1 ).Select( int.Parse ).ToArray();
				if ( lines[next].StartsWith( "cell ", StringComparison.Ordinal ) )
					cells.Add( (where[0], where[1], int.Parse( item[1] ), int.Parse( item[2] )) );
				else if ( lines[next].StartsWith( "tile ", StringComparison.Ordinal ) )
					tiles.Add( (where[0], where[1], item[1].Split( ' ' ).Select( int.Parse ).ToArray()) );
			}
			var map = new ParkCellMap( 128, 128 );
			var builder = new TrackBuilder( map, TrackFamily.Kart, () => null, new Grid() );
			var station = TrackStation.FromPlacement( anchor[0], anchor[1], 0 );
			builder.PlaceStation( station );
			builder.PlaceInitialStation( station );
			var owner = builder.OwnerOf( station );
			foreach ( var (x, y, before, _) in cells.Where( c => c.Before != 0 ) )
				map.SetType( x, y, (byte)before );
			var result = new TrackLineWriter( builder, null, owner ).Commit( (start[0], start[1]), (end[0], end[1]) );
			if ( !result.Completed )
				failures.Add( $"{parts[0]}: the line stopped ({result.StoppedBy})" );
			foreach ( var (x, y, _, after) in cells )
				if ( map.PrimaryTypeAt( x, y ) != after )
					failures.Add( $"{parts[0]}: cell {x},{y} is {map.PrimaryTypeAt( x, y )}, expected {after}" );
			foreach ( var (x, y, record) in tiles )
			{
				var tile = map.Tracks.Get( x, y );
				if ( tile.Type != record[0] || tile.Flags != record[3] )
					failures.Add( $"{parts[0]}: tile {x},{y} is kind {tile.Type} flags {tile.Flags}, expected {record[0]} and {record[3]}" );
			}
		}
		Assert.IsTrue( count >= 100, $"the golden holds {count} lines" );
		Assert.AreEqual( 0, failures.Count, string.Join( Environment.NewLine, failures.Take( 12 ) ) );
	}
}
