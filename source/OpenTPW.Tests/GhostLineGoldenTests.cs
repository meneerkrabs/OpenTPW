using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The path and queue tools' preview line against tp.exe's own line walker 0x536100 with the ghost SetCell 0x532FC0 (docs/reverse/ORACLE.md, X50; BLUEPRINT-001, BLUEPRINT-003): every case of <see cref="GhostLineGolden"/> is a
/// line over a random synthetic map; the faces the executable queued (floor and walls, texture and height step), the validator's sticky words and the cost after it are replayed on <see cref="BlueprintLineGhost"/>.
/// </summary>
[TestClass]
public class GhostLineGoldenTests
{
	private sealed class GoldenMap : ILineGhostMap
	{
		private readonly Dictionary<(int, int), GhostCell> cells = [];
		private readonly Dictionary<(int, int), (byte Record, byte Map)> raw = [];
		private readonly Dictionary<(int, int), float> ground = [];

		public GoldenMap( JsonElement scenario )
		{
			var records = scenario.GetProperty( "records" ).EnumerateArray().Select( r => (X: r[0].GetInt32(), Y: r[1].GetInt32(), Kind: (byte)r[2].GetInt32(), Links: (byte)r[3].GetInt32(), Parent: r[4].GetInt32()) ).ToDictionary( r => (r.X, r.Y) );
			var anchors = scenario.GetProperty( "anchors" ).EnumerateObject().ToDictionary( a => int.Parse( a.Name ), a => a.Value.GetInt32() );
			var mode = scenario.GetProperty( "mode" ).GetInt32();
			var raws = scenario.GetProperty( "cells" ).EnumerateArray().Select( c => (X: c[0].GetInt32(), Y: c[1].GetInt32(), Type: (byte)c[2].GetInt32(), Flags: c[3].GetInt32(), Links: (byte)c[4].GetInt32(), Owner: c[5].GetInt32()) ).ToDictionary( c => (c.X, c.Y) );
			foreach ( var h in scenario.GetProperty( "heights" ).EnumerateArray() )
				ground[(h[0].GetInt32(), h[1].GetInt32())] = h[2].GetSingle();
			foreach ( var (key, c) in raws )
			{
				byte structureType = 0, structureLinks = 0, recordType = 0;
				if ( records.TryGetValue( key, out var record ) )
				{
					recordType = record.Kind;
					structureType = record.Kind;
					structureLinks = record.Links;
					if ( record.Kind == 0xC && record.Parent > 0 )
					{
						// the validator follows an extension to its parent's record
						var parent = ((record.Parent - 1) % 128, (record.Parent - 1) / 128);
						if ( records.TryGetValue( parent, out var origin ) )
						{
							structureType = origin.Kind;
							structureLinks = origin.Links;
						}
						else
						{
							structureType = 0;
							structureLinks = 0;
						}
					}
				}
				int? priority = c.Type is 4 or 9 or 10 && anchors.TryGetValue( c.Owner, out var found ) ? found : null;
				byte neighbour = 0;
				if ( mode == 1 && c.Type == 3 && System.Numerics.BitOperations.PopCount( c.Links ) == 1 )
				{
					var (dx, dy) = PathTiles.Offsets[System.Numerics.BitOperations.TrailingZeroCount( c.Links )];
					neighbour = raws.TryGetValue( (c.X + dx, c.Y + dy), out var other ) ? other.Type : (byte)0;
				}
				cells[key] = new GhostCell( c.Type, (ushort)c.Flags, c.Links, (ushort)c.Owner, structureType, structureLinks, priority, neighbour );
				raw[key] = (recordType, c.Type);
			}
		}

		public bool InBounds( int x, int y ) => x >= 0 && y >= 0 && x < 128 && y < 128;
		public GhostCell CellAt( int x, int y ) => cells.GetValueOrDefault( (x, y) );
		public float? RaiseAt( int x, int y ) => InBounds( x, y ) ? BlueprintHeights.RaiseOf( raw.GetValueOrDefault( (x, y) ).Record, raw.GetValueOrDefault( (x, y) ).Map, () => ground.GetValueOrDefault( (x, y) ) ) : null;
	}

	[TestMethod]
	public void TheLineGhostMarksEveryCellAndWallAsTheExecutableDoes()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( GhostLineGolden.Packed ) ), CompressionMode.Decompress ) );
		var rows = unpacked.ReadToEnd().Split( '\n' );
		Assert.AreEqual( GhostLineGolden.CaseCount, rows.Length );
		var sawLink = false;
		var sawEnd = false;
		var sawWalls = false;
		for ( var index = 0; index < rows.Length; index++ )
		{
			var scenario = JsonDocument.Parse( rows[index] ).RootElement;
			var map = new GoldenMap( scenario );
			var line = scenario.GetProperty( "line" ).EnumerateArray().Select( p => (X: p[0].GetInt32(), Y: p[1].GetInt32()) ).ToList();
			var price = scenario.GetProperty( "price" );
			var mode = scenario.GetProperty( "mode" ).GetInt32();
			var bank = scenario.GetProperty( "bank" );
			var tool = new LineGhostTool( mode, 2996, scenario.GetProperty( "hoverIsStart" ).GetBoolean(), price[mode == 1 ? 0 : 1].GetInt32(), bank.ValueKind == JsonValueKind.Null ? null : bank.GetInt32() );
			var result = BlueprintLineGhost.Run( map, line, tool );
			var faces = new List<(int, int, int, int, long)>();
			foreach ( var (x, y, marker) in result.Cells )
			{
				faces.Add( (x, y, 0, BlueprintMarkers.SlotOf( marker ), 0) );
				if ( map.RaiseAt( x, y ) is not { } current )
					continue;
				foreach ( var (side, step, _, _, _, _) in BlueprintMarkers.WallSteps( x, y, current, map.RaiseAt ) )
				{
					faces.Add( (x, y, side, marker == BlueprintMarker.Refused ? 1 : 0, step) );
					sawWalls = true;
				}
			}
			var expected = scenario.GetProperty( "faces" ).EnumerateArray().Select( f => (f[0].GetInt32(), f[1].GetInt32(), f[2].GetInt32(), f[3].GetInt32(), f[4].GetInt64()) ).ToList();
			// the floor face's height step is 0 for every cell (the raise is in the face's y: the oracle's step is the face height less 1.5), so compare the floors by cell and texture
			var floors = faces.Where( f => f.Item3 == 0 ).Select( f => (f.Item1, f.Item2, f.Item4) ).ToList();
			var expectedFloors = expected.Where( f => f.Item3 == 0 ).Select( f => (f.Item1, f.Item2, f.Item4) ).ToList();
			CollectionAssert.AreEqual( expectedFloors, floors, $"case {index} floors" );
			var walls = faces.Where( f => f.Item3 != 0 ).ToList();
			var expectedWalls = expected.Where( f => f.Item3 != 0 ).Select( f => (f.Item1, f.Item2, f.Item3, f.Item4, f.Item5) ).ToList();
			CollectionAssert.AreEqual( expectedWalls, walls, $"case {index} walls" );
			sawLink |= result.Cells.Any( c => c.Marker == BlueprintMarker.Link );
			sawEnd |= result.Cells.Any( c => c.Marker == BlueprintMarker.End );
			var latches = result.Latches;
			var actual = new[] { latches.Refuse, latches.Pending, latches.EndsOnPath, latches.MeetsOwn, latches.Cash, result.Refused }.Select( b => b ? 1 : 0 ).ToArray();
			var expectedLatches = scenario.GetProperty( "latches" ).EnumerateArray().Select( l => l.GetInt32() ).ToArray();
			CollectionAssert.AreEqual( expectedLatches, actual, $"case {index} latches" );
			Assert.AreEqual( scenario.GetProperty( "cost" ).GetInt32(), latches.Cost, $"case {index} cost" );
		}
		Assert.IsTrue( sawLink && sawEnd && sawWalls, "the sweep reaches m_link, m_end and the walls" );
	}
}
