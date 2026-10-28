using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The park fence over track records against tp.exe's own routines (docs/reverse/ORACLE.md X37, PATH-007): the cases of <see cref="ParkFenceTrackGolden"/> were produced by running the executable's fence set-up
/// 0x5323F0, the Buy Land validator 0x531A50 and the purchase 0x5327E0 in the x86 oracle on synthetic maps whose second layer also holds track tiles (origins, stations and extensions). The fence routines read and write the
/// same 0x28-byte record a track tile lives in, so <see cref="FenceEngine"/> must leave the cell flags, the fence's own records and every field of a tile's record (type, link, flags, word, counter, the fence links)
/// as the executable does, answer the validator the same and refuse the purchases on which the executable hangs or crashes.
/// </summary>
[TestClass]
public class ParkFenceTrackGoldenTests
{
	private const int Side = 128;

	private static ParkCellMap MapOf( ParkFenceTrackGolden.Case golden )
	{
		var cells = new ParkCellMap( Side, Side );
		foreach ( var (type, x0, y0, width, height) in golden.Terrain )
			for ( var y = y0; y < Math.Min( Side, y0 + height ); y++ )
				for ( var x = x0; x < Math.Min( Side, x0 + width ); x++ )
					cells.SetType( x, y, (byte)type );
		foreach ( var (x, y) in golden.NoModify )
		{
			cells.SetType( x, y, ParkCellType.Path );
			cells.SetFlags( x, y, ParkCellFlags.NoModify );
		}
		return cells;
	}

	/// <summary>The track tool's records on the layer: an extension names its origin (the word is the origin's cell id), an origin carries its ride word.</summary>
	private static void LayTracks( ParkCellMap cells, ParkFenceTrackGolden.Case golden )
	{
		foreach ( var laid in golden.Tracks )
		{
			var extension = laid.Type == TrackCellTypes.Extension;
			var origin = extension ? golden.Tracks.First( other => other.Y * Side + other.X + 1 == laid.Word ) : laid;
			cells.Tracks.Set( laid.X, laid.Y, new TrackCell
			{
				Type = (byte)laid.Type, Mask = (byte)laid.Mask, Link = (byte)laid.Link, Flags = (byte)laid.Flags, Overlap = (short)laid.Counter,
				Parent = extension ? laid.Word - 1 : -1, Owner = origin.Word
			} );
		}
	}

	/// <summary>The fence layer's hash of <see cref="ParkFenceGoldenTests.LayerHash"/> without the cells that hold a track record.</summary>
	private static string LayerHash( FenceLayer layer, HashSet<int> skip )
	{
		var bytes = new byte[Side * Side * 5];
		for ( var index = 0; index < Side * Side; index++ )
		{
			if ( skip.Contains( index ) )
				continue;
			bytes[index * 5] = layer.Type[index];
			bytes[index * 5 + 1] = layer.Links[index];
			bytes[index * 5 + 2] = layer.Direction[index];
			bytes[index * 5 + 3] = (byte)layer.Owner[index];
			bytes[index * 5 + 4] = (byte)(layer.Owner[index] >> 8);
		}
		return Convert.ToHexString( SHA256.HashData( bytes ) ).ToLowerInvariant();
	}

	/// <summary>A laid record as the oracle reads it back: the tile's own record while the track layer holds it, the fence layer's once the fence retyped it (the mask is the track's alone).</summary>
	private static ParkFenceTrackGolden.After RecordOf( ParkCellMap cells, FenceLayer layer, ParkFenceTrackGolden.Laid laid, ParkFenceTrackGolden.After expected )
	{
		var track = cells.Tracks.Get( laid.X, laid.Y );
		var index = laid.Y * Side + laid.X;
		if ( track.Type != 0 )
			return new( track.Type, track.Mask, track.Link, track.Flags, track.IsExtension ? track.Parent + 1 : track.Owner, layer.Links[index], track.Overlap );
		return new( layer.Type[index], expected.Mask, layer.Direction[index], layer.Flags[index], layer.Owner[index], layer.Links[index], layer.Counter[index] );
	}

	[TestMethod]
	public void TheTrackRecordsAndTheFenceEndAsTheExecutableLeavesThem()
	{
		Assert.IsTrue( ParkFenceTrackGolden.Cases.Length >= 10 );
		foreach ( var golden in ParkFenceTrackGolden.Cases )
		{
			var cells = MapOf( golden );
			var land = ParkLand.NewPark( cells, new ParkLandSetup( golden.PostA, golden.PostB, golden.Clicks ) );
			Assert.IsNotNull( land, golden.Name );
			cells.SetLand( land );
			LayTracks( cells, golden );
			foreach ( var buy in golden.Buys )
			{
				var before = ParkFenceGoldenTests.FlagRuns( cells );
				var check = land!.CheckPurchase( cells, buy.First, buy.Second, ( x, y ) => cells.RawTypeAt( x, y ) is 7 or 0x1E, 100, null );
				var where = $"{golden.Name}: buy {buy.First}-{buy.Second}";
				if ( !buy.Accepted )
				{
					Assert.IsFalse( check.Allowed, where );
					Assert.AreNotEqual( LandPurchaseRefusal.Fence, check.Refusal, where );
					continue;
				}
				Assert.AreEqual( buy.Cells, check.Cells, where );
				if ( buy.Faults )
				{
					Assert.AreEqual( LandPurchaseRefusal.Fence, check.Refusal, where );
					Assert.IsFalse( land.Purchase( cells, buy.First, buy.Second ), where );
					CollectionAssert.AreEqual( before, ParkFenceGoldenTests.FlagRuns( cells ), $"{where}: a refused purchase writes nothing" );
					continue;
				}
				Assert.IsTrue( check.Allowed, where );
				Assert.IsTrue( land.Purchase( cells, buy.First, buy.Second ), where );
			}
			var layer = land!.LayerFor( Side, Side );
			var skip = golden.Tracks.Select( laid => laid.Y * Side + laid.X ).ToHashSet();
			CollectionAssert.AreEqual( golden.FlagRuns, ParkFenceGoldenTests.FlagRuns( cells ), $"{golden.Name}: cell flags" );
			Assert.AreEqual( golden.LayerSha256, LayerHash( layer, skip ), $"{golden.Name}: fence records" );
			Assert.AreEqual( golden.OwnedCells, Enumerable.Range( 0, Side * Side ).Count( index => !cells.FlagsAt( index % Side, index / Side ).HasFlag( ParkCellFlags.Unowned ) ), golden.Name );
			Assert.AreEqual( golden.Posts, Enumerable.Range( 0, Side * Side ).Count( index => !skip.Contains( index ) && layer.Type[index] == FenceLayer.PostType ), golden.Name );
			for ( var i = 0; i < golden.Tracks.Length; i++ )
			{
				var laid = golden.Tracks[i];
				Assert.AreEqual( golden.Records[i], RecordOf( cells, layer, laid, golden.Records[i] ), $"{golden.Name}: record ({laid.X}, {laid.Y}) laid as type {laid.Type}" );
			}
		}
	}

	/// <summary>The cases are not vacuous: across them the fence changes a station's link, the bridge bits, the fence links, a word, a counter and a record's type on tiles.</summary>
	[TestMethod]
	public void TheCasesChangeEveryFieldTheFenceTouches()
	{
		var changed = new HashSet<string>();
		foreach ( var golden in ParkFenceTrackGolden.Cases )
		{
			for ( var i = 0; i < golden.Tracks.Length; i++ )
			{
				var (laid, after) = (golden.Tracks[i], golden.Records[i]);
				if ( after.Type != laid.Type ) changed.Add( "type" );
				if ( after.Link != laid.Link ) changed.Add( "link" );
				if ( after.Flags != laid.Flags ) changed.Add( "flags" );
				if ( after.Word != laid.Word ) changed.Add( "word" );
				if ( after.Counter != laid.Counter ) changed.Add( "counter" );
				if ( after.FenceLinks != 0 ) changed.Add( "links" );
			}
		}
		CollectionAssert.AreEquivalent( new[] { "type", "link", "flags", "word", "counter", "links" }, changed.ToArray() );
	}
}
