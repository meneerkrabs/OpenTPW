using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The track tile chooser against tp.exe's own routine 0x539760 (docs/reverse/ORACLE.md, X51; BLUEPRINT-003, UI-100): every case of <see cref="GhostTileGolden"/> is a random tile with its cells, records, line state, sticky
/// words, cost and money; the answer, the words, the cost and the owner priority the executable left are replayed on <see cref="TrackTileChooser"/>.
/// </summary>
[TestClass]
public class GhostTileGoldenTests
{
	[TestMethod]
	public void TheTileChooserAnswersAndSetsTheWordsAsTheExecutableDoes()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( GhostTileGolden.Packed ) ), CompressionMode.Decompress ) );
		var rows = unpacked.ReadToEnd().Split( '\n' );
		Assert.AreEqual( GhostTileGolden.CaseCount, rows.Length );
		var answers = new HashSet<int>();
		for ( var index = 0; index < rows.Length; index++ )
		{
			var row = JsonDocument.Parse( rows[index] ).RootElement;
			var origin = (X: row.GetProperty( "origin" )[0].GetInt32(), Y: row.GetProperty( "origin" )[1].GetInt32());
			var anchors = row.GetProperty( "anchors" ).EnumerateObject().ToDictionary( a => int.Parse( a.Name ), a => a.Value.GetInt32() );
			var globals = row.GetProperty( "globals" ).EnumerateObject().ToDictionary( g => int.Parse( g.Name ), g => g.Value.GetInt64() );
			var cells = row.GetProperty( "cells" ).EnumerateArray().Select( c => (X: c[0].GetInt32(), Y: c[1].GetInt32(), Type: (byte)c[2].GetInt32(), Flags: (ushort)c[3].GetInt32(), Owner: (ushort)c[4].GetInt32()) ).ToDictionary( c => (c.X, c.Y) );
			var records = row.GetProperty( "records" ).EnumerateArray().Select( r => (X: r[0].GetInt32(), Y: r[1].GetInt32(), Type: (byte)r[2].GetInt32(), Links: (byte)r[3].GetInt32(), Parent: r[4].GetInt32(), Direction: (byte)r[5].GetInt32(), Flags: (ushort)r[6].GetInt32(), Owner: (ushort)r[7].GetInt32()) ).ToDictionary( r => (r.X, r.Y) );
			TileCellFacts Cell( int dx, int dy )
			{
				var c = cells[(origin.X + dx, origin.Y + dy)];
				return new TileCellFacts( c.Type, c.Flags, c.Owner, c.Type is 4 or 9 or 10 && anchors.TryGetValue( c.Owner, out var priority ) ? priority : null );
			}
			// the record's owner word is its parent word for an extension
			(byte Type, byte Links, byte Direction, ushort Flags, ushort Owner) Record( int x, int y ) => records.TryGetValue( (x, y), out var r ) ? (r.Type, r.Links, r.Direction, r.Flags, r.Parent != 0 ? (ushort)r.Parent : r.Owner) : ((byte)0, (byte)0, (byte)0, (ushort)0, (ushort)0);
			TileNeighbourFacts Neighbour( int dx, int dy )
			{
				var raw = Record( origin.X + dx, origin.Y + dy );
				if ( raw.Type == 0 )
					return new TileNeighbourFacts( false, 0, 0 );
				if ( raw.Type is 0xC or 0x11 )
				{
					var parent = Record( (raw.Owner - 1) % 128, (raw.Owner - 1) / 128 );
					return new TileNeighbourFacts( true, parent.Type, parent.Owner );
				}
				return new TileNeighbourFacts( true, raw.Type, raw.Owner );
			}
			var own = Record( origin.X, origin.Y );
			long G( int address ) => globals.GetValueOrDefault( address );
			var bank = row.GetProperty( "bank" );
			var tool = new TileTool( row.GetProperty( "mode" ).GetInt32(), row.GetProperty( "second" ).GetInt32() != 0, (byte)row.GetProperty( "direction" ).GetInt32(), G( 0x820AC8 ) != 0, G( 0x820AD0 ) != 0, G( 0x818688 ) != 0,
				(ushort)G( 0x818698 ), (int)G( 0x818C10 ), (int)(row.GetProperty( "mode" ).GetInt32() == 0xB ? G( 0x81AEBC ) : row.GetProperty( "mode" ).GetInt32() == 0x10 ? G( 0x81B43C ) : 0), origin,
				((int)G( 0x7639F0 ), (int)G( 0x7639F4 )), ((int)G( 0x7639F8 ), (int)G( 0x7639FC )) ) { PlacementPaid = G( 0x8186D4 ) != 0, CornerGate = G( 0x8186D0 ) != 0 };
			var latches = new TileLatches { Refuse = G( 0x816D50 ) != 0, Pending = G( 0x816D58 ) != 0, L54 = G( 0x816D54 ) != 0, Cost = (int)G( 0x81AF4C ), Bank = bank.ValueKind == JsonValueKind.Null ? null : bank.GetInt32() };
			var answer = TrackTileChooser.Choose( tool, Cell( 0, 0 ), Cell( 1, 0 ), Cell( 1, 1 ), Cell( 0, 1 ), new TileRecordFacts( own.Type, own.Links, own.Direction, own.Flags, own.Owner ), Neighbour( 1, 0 ), Neighbour( 1, 1 ), Neighbour( 0, 1 ), ref latches );
			answers.Add( answer );
			var expected = row.GetProperty( "latches" ).EnumerateArray().Select( l => l.GetInt32() ).ToArray();   // l50, l58, l54, l5c, l4c, l60, l48
			var actual = new[] { latches.Refuse, latches.Pending, latches.L54, latches.Cash, latches.MeetsOwn, false, latches.Refused }.Select( b => b ? 1 : 0 ).ToArray();
			Assert.AreEqual( row.GetProperty( "answer" ).GetInt32(), answer, $"case {index} answer" );
			CollectionAssert.AreEqual( expected, actual, $"case {index} words" );
			Assert.AreEqual( row.GetProperty( "cost" ).GetInt32(), latches.Cost, $"case {index} cost" );
			Assert.AreEqual( (int)row.GetProperty( "priority" ).GetUInt32(), latches.CellPriority, $"case {index} priority" );
		}
		CollectionAssert.IsSubsetOf( new[] { 0, 1, 9, 0xB, 0xD }, answers.ToArray(), "the sweep reaches every answer" );
	}
}
