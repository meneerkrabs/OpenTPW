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
}
