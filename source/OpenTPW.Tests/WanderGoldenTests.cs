using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The wander destination against tp.exe's own routine 0x004F9490 (docs/reverse/ORACLE.md, X19; GUEST-010): every row of <see cref="WanderGolden"/> is a synthetic grid of path, queue, entrance and exit cells
/// with link bytes and queue links, a start cell that has links (any type) and scripted draws, with the answer the real routine gave in the x86 oracle.
/// </summary>
[TestClass]
public class WanderGoldenTests
{
	private static string[] Rows()
	{
		using var unpacked = new StreamReader( new ZLibStream( new MemoryStream( Convert.FromBase64String( WanderGolden.Packed ) ), CompressionMode.Decompress ) );
		return unpacked.ReadToEnd().Split( '\n', StringSplitOptions.RemoveEmptyEntries );
	}

	[TestMethod]
	public void EveryRowOfTheOraclesWanderAnswersAsTheRoutineDid()
	{
		var rows = Rows();
		Assert.AreEqual( WanderGolden.RowCount, rows.Length );
		var stranded = 0;
		var queueStarts = 0;
		var accessStarts = 0;
		foreach ( var row in rows )
		{
			var parts = row.Split( " | " );
			var start = parts[0].Split( ',' ).Select( int.Parse ).ToArray();
			var draws = new Queue<uint>( parts[1].Split( ',' ).Select( uint.Parse ) );
			var tail = parts[2].Split( " > " );
			var grid = new GuestPathGrid( 128, 128 );
			foreach ( var cell in tail[0].Split( ';' ).Select( item => item.Split( ',' ).Select( int.Parse ).ToArray() ) )
			{
				grid.Cells.SetType( cell[0], cell[1], (byte)cell[2] );
				grid.Cells.SetLinks( cell[0], cell[1], (byte)cell[3] );
				if ( cell[4] != 0 )
					grid.Cells.SetQueueLink( cell[0], cell[1], (byte)cell[4] );
			}
			var used = 0;
			uint Draw()
			{
				used++;
				return draws.Dequeue();
			}
			var answer = tail[1].Split( ' ' );
			var picked = GuestWander.Pick( grid, (start[0], start[1]), Draw, null, out var wasStranded );
			if ( picked == null && (grid.LinksAt( start[0], start[1] ) & 0x55) == 0 )
			{
				// a start without links takes the off-path search, whose first pattern is the start cell itself; the oracle's route request always finds a route
				(int X, int Y)? found = null;
				Assert.IsTrue( GuestWander.OffPath( grid, (start[0], start[1]), Draw, cell => { found = cell; return true; } ), row );
				picked = new GuestWander.Destination( found!.Value, GuestWander.CentreByte, GuestWander.CentreByte );
			}
			if ( answer[0] == "none" )
			{
				Assert.IsNull( picked, row );
				Assert.AreEqual( int.Parse( answer[1], CultureInfo.InvariantCulture ), used, row );
				Assert.AreEqual( answer[2] == "1", wasStranded, row );
				stranded += wasStranded ? 1 : 0;
				continue;
			}
			Assert.IsNotNull( picked, row );
			Assert.AreEqual( (int.Parse( answer[0] ), int.Parse( answer[1] ), int.Parse( answer[2] ), int.Parse( answer[3] ), int.Parse( answer[4] )),
				(picked!.Value.Cell.X, picked.Value.Cell.Y, picked.Value.ByteX, picked.Value.ByteY, used), row );
			var type = grid.Cells.RawTypeAt( start[0], start[1] );
			queueStarts += type == 3 ? 1 : 0;
			accessStarts += type is 9 or 10 ? 1 : 0;
		}
		Assert.IsTrue( stranded > 0, "some rows strand the guest" );
		Assert.IsTrue( queueStarts > 5 && accessStarts > 5, $"the rows start on queue ({queueStarts}) and entrance or exit cells ({accessStarts})" );
	}
}
