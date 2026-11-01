using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The tile line preview against tp.exe's own tile walker 0x53AAC0 with the ghost tile routine 0x5370E0 (docs/reverse/ORACLE.md, X50; BLUEPRINT-003): every case of <see cref="GhostTileLineGolden"/> is a line of tiles over a
/// random synthetic map; the faces the executable queued (floor and walls: texture, height step, direction byte and flags) and the chooser's words and cost are replayed on <see cref="TileLineGhost"/>.
/// </summary>
[TestClass]
public class GhostTileLineGoldenTests
{
	private sealed class GoldenTileMap : ITileGhostMap
	{
		private readonly Dictionary<(int, int), (byte Type, ushort Flags, ushort Owner)> cells = [];
		private readonly Dictionary<(int, int), (byte Type, byte Links, int Parent, byte Direction, ushort Flags, ushort Owner)> records = [];
		private readonly Dictionary<(int, int), float> ground = [];
		private readonly Dictionary<int, int> anchors;

		public GoldenTileMap( JsonElement scenario )
		{
			foreach ( var c in scenario.GetProperty( "cells" ).EnumerateArray() )
				cells[(c[0].GetInt32(), c[1].GetInt32())] = ((byte)c[2].GetInt32(), (ushort)c[3].GetInt32(), (ushort)c[4].GetInt32());
			foreach ( var r in scenario.GetProperty( "records" ).EnumerateArray() )
				records[(r[0].GetInt32(), r[1].GetInt32())] = ((byte)r[2].GetInt32(), (byte)r[3].GetInt32(), r[4].GetInt32(), (byte)r[5].GetInt32(), (ushort)r[6].GetInt32(), (ushort)r[7].GetInt32());
			foreach ( var h in scenario.GetProperty( "heights" ).EnumerateArray() )
				ground[(h[0].GetInt32(), h[1].GetInt32())] = h[2].GetSingle();
			anchors = scenario.GetProperty( "anchors" ).EnumerateObject().ToDictionary( a => int.Parse( a.Name ), a => a.Value.GetInt32() );
		}

		public bool InBounds( int x, int y ) => x >= 0 && y >= 0 && x < 128 && y < 128;

		public TileCellFacts CellAt( int x, int y )
		{
			var c = cells.GetValueOrDefault( (x, y) );
			return new TileCellFacts( c.Type, c.Flags, c.Owner, c.Type is 4 or 9 or 10 && anchors.TryGetValue( c.Owner, out var priority ) ? priority : null );
		}

		public TileRecordFacts RecordAt( int x, int y )
		{
			var r = records.GetValueOrDefault( (x, y) );
			return new TileRecordFacts( r.Type, r.Links, r.Direction, r.Flags, r.Parent != 0 ? (ushort)r.Parent : r.Owner );
		}

		public TileNeighbourFacts NeighbourAt( int x, int y )
		{
			var raw = RecordAt( x, y );
			if ( raw.Type == 0 )
				return new TileNeighbourFacts( false, 0, 0 );
			if ( raw.Type is 0xC or 0x11 )
			{
				var parent = RecordAt( (raw.Owner - 1) % 128, (raw.Owner - 1) / 128 );
				return new TileNeighbourFacts( true, parent.Type, parent.Owner );
			}
			return new TileNeighbourFacts( true, raw.Type, raw.Owner );
		}

		public float Ground( int x, int y ) => ground.GetValueOrDefault( (x, y) );
	}

	[TestMethod]
	public void TheTileLinePreviewQueuesTheFacesTheExecutableDoes()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( GhostTileLineGolden.Packed ) ), CompressionMode.Decompress ) );
		var rows = unpacked.ReadToEnd().Split( '\n' );
		Assert.AreEqual( GhostTileLineGolden.CaseCount, rows.Length );
		var sawEnd = false;
		var sawWall = false;
		for ( var index = 0; index < rows.Length; index++ )
		{
			var row = JsonDocument.Parse( rows[index] ).RootElement;
			var map = new GoldenTileMap( row );
			var mode = row.GetProperty( "mode" ).GetInt32();
			var start = (X: row.GetProperty( "start" )[0].GetInt32(), Y: row.GetProperty( "start" )[1].GetInt32());
			var end = (X: row.GetProperty( "end" )[0].GetInt32(), Y: row.GetProperty( "end" )[1].GetInt32());
			var price = row.GetProperty( "price" );
			var gates = row.GetProperty( "gates" );
			var bank = row.GetProperty( "bank" );
			var tool = new LineGhostTileTool( 2996, 3, price[mode == 0xB ? 0 : 1].GetInt32(), row.GetProperty( "hoverIsStart" ).GetBoolean(), (gates[0].GetInt32(), gates[1].GetInt32()), (gates[2].GetInt32(), gates[3].GetInt32()) );
			var latches = new TileLatches { Refuse = row.GetProperty( "presetRefuse" ).GetInt32() != 0, Bank = bank.ValueKind == JsonValueKind.Null ? null : bank.GetInt32() };
			var result = TileLineGhost.Run( map, mode, start, end, tool, latches );
			var expected = row.GetProperty( "faces" ).EnumerateArray().Select( f => new TileFace( f[0].GetInt32(), f[1].GetInt32(), f[2].GetInt32(), f[3].GetInt32(), f[4].GetInt64(), f[5].GetInt32(), f[6].GetInt32(), f[7].GetInt32() ) ).ToList();
			CollectionAssert.AreEqual( expected, result.Faces, $"case {index} faces" );
			var expectedLatches = row.GetProperty( "latches" ).EnumerateArray().Select( l => l.GetInt32() ).ToArray();   // l50, l58, l54, l5c, l4c, l60, l48
			var l = result.Latches;
			CollectionAssert.AreEqual( expectedLatches, new[] { l.Refuse, l.Pending, l.L54, l.Cash, l.MeetsOwn, false, l.Refused }.Select( b => b ? 1 : 0 ).ToArray(), $"case {index} words" );
			Assert.AreEqual( row.GetProperty( "cost" ).GetInt32(), l.Cost, $"case {index} cost" );
			sawEnd |= result.Tiles.Any( t => t.Answer == 0xB );
			sawWall |= result.Faces.Any( f => f.Code != 0 );
		}
		Assert.IsTrue( sawEnd && sawWall, "the sweep reaches m_end and the walls" );
	}
}
