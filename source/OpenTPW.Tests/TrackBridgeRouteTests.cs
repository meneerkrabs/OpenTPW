using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class TrackBridgeRouteTests
{
	private sealed class Grid : IParkGrid
	{
		public int Width => 64;
		public int Height => 64;
		public System.Numerics.Vector2 Origin => default;
		public float GetCellHeight( int x, int y ) => 0;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
	}

	private sealed class Rig
	{
		public readonly ParkCellMap Cells = new( 64, 64 );
		public readonly TrackBuilder Builder;
		public TrackStation Station;
		public readonly List<(int X, int Y)> Route = new();
		public const int Target = 10;
		public Rig( TrackFamily family = TrackFamily.Water, int rotation = 0, int neighbors = 1, int link = 1 )
		{
			Builder = new TrackBuilder( Cells, family, () => null, new Grid() );
			Station = TrackStation.FromPlacement( 30, 30, rotation );
			var dx = TrackHeading.Dx( Station.Heading );
			var dy = TrackHeading.Dy( Station.Heading );
			var local = new[] { (0,0), (1,0), (2,0), (3,0), (4,0), (4,1), (4,2), (4,3), (3,3), (2,3), (1,3), (0,3), (-1,3), (-1,2), (-1,1), (-1,0) };
			foreach ( var (a,b) in local )
				Route.Add( (Station.First.X + 2 * (a * dx - b * dy), Station.First.Y + 2 * (a * dy + b * dx)) );
			for ( var i = 0; i < Route.Count; i++ )
			{
				var current = Route[i];
				var entry = Heading( Route[(i + Route.Count - 1) % Route.Count], current );
				var exit = Heading( current, Route[(i + 1) % Route.Count] );
				var flags = i == Target || i == Target + 1 && (neighbors & 1) != 0 || i == Target - 1 && (neighbors & 2) != 0 ? TrackBuilder.FlagOverPath : (byte)0;
				var tileLink = i == Target ? link == 0 ? (byte)0 : link == 1 ? entry : TrackHeading.Opposite( entry ) : exit;
				var tile = new TrackCell { Type = i < 2 ? Builder.StartType : Builder.OriginType, Mask = (byte)(TrackHeading.Opposite( entry ) | exit), Link = tileLink, Flags = flags, Owner = Builder.OwnerOf( Station ), Parent = -1, Overlap = 2, BaseType = 7 };
				Cells.SetType( current.X, current.Y, tile.Type );
				Builder.Tracks.Set( current.X, current.Y, tile );
			}
		}
		public TrackCell At( int index ) => Builder.Tracks.Get( Route[index].X, Route[index].Y );
		public void Put( int index, TrackCell cell ) => Builder.Tracks.Set( Route[index].X, Route[index].Y, cell );
		public TrackCell[] Snapshot() => Route.Select( (p, i) => At( i ) ).ToArray();
		private static byte Heading( (int X, int Y) a, (int X, int Y) b ) => b.X > a.X ? TrackHeading.PosX : b.X < a.X ? TrackHeading.NegX : b.Y > a.Y ? TrackHeading.PosY : TrackHeading.NegY;
	}

	[TestMethod]
	public void PublicWalkUsesRouteNotStraightLinkAcrossFamiliesRotationsAndNeighborStates()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		foreach ( var link in new[] { 0, 1, 2 } )
		foreach ( var neighbors in new[] { 0, 1, 2, 3 } )
		{
			var rig = new Rig( family, rotation, neighbors, link );
			var before = rig.At( Rig.Target );
			Assert.IsTrue( TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station ).IsValid );
			var actual = rig.At( Rig.Target );
			var expected = new byte[] { 1, 2, 8, 4 }[neighbors];
			Assert.AreEqual( (byte)(TrackBuilder.FlagOverPath | expected), actual.Flags, $"{family}/{rotation}/{link}/{neighbors}" );
			Assert.AreEqual( before.Link, actual.Link );
			Assert.AreEqual( before.Owner, actual.Owner );
			Assert.AreEqual( before.Overlap, actual.Overlap );
			Assert.AreEqual( before.BaseType, actual.BaseType );
			Assert.AreEqual( before.Mask, actual.Mask );
			var selected = TrackPieceSelector.Select( actual.Type, actual.Mask, before.Link, actual.Flags );
			Assert.AreEqual( (selected.Family, selected.Piece, selected.Rotation), (actual.Family, actual.Piece, actual.Rotation) );
			var once = rig.Snapshot();
			TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station );
			CollectionAssert.AreEqual( once, rig.Snapshot(), "Repeated live refresh is idempotent." );
		}
	}

	[TestMethod]
	public void ConsecutiveRunIsUpDeckDownAndFinalPassIsIndependentOfWriteOrder()
	{
		var rig = new Rig( neighbors: 3, link: 2 );
		var circuit = TrackCircuitWalker.WalkAndRefresh( rig.Builder, rig.Station );
		Assert.AreEqual( (byte)2, (byte)(rig.At( 9 ).Flags & 15) );
		Assert.AreEqual( (byte)4, (byte)(rig.At( 10 ).Flags & 15) );
		Assert.AreEqual( (byte)8, (byte)(rig.At( 11 ).Flags & 15) );
		var expected = rig.Snapshot();
		foreach ( var step in circuit.Steps.Reverse() ) rig.Builder.Refresh( step.X, step.Y );
		rig.Builder.RefreshClosedCircuitBridges( rig.Station, circuit );
		CollectionAssert.AreEqual( expected, rig.Snapshot() );
	}

	[TestMethod]
	public void FinalPassPreservesEveryNonClassificationFieldAndIgnoresCornerPathMemory()
	{
		var rig = new Rig( neighbors: 1, link: 2 );
		var corner = rig.At( 7 );
		corner.Flags = TrackBuilder.FlagOverPath;
		rig.Put( 7, corner );
		var circuit = TrackCircuitWalker.Walk( rig.Builder, rig.Station );
		foreach ( var step in circuit.Steps ) rig.Builder.Refresh( step.X, step.Y );
		Assert.AreEqual( (byte)0, (byte)(rig.At( 7 ).Flags & TrackCell.BridgeMask) );
		var before = rig.Snapshot();
		var primary = rig.Route.Select( p => rig.Cells.RawTypeAt( p.X, p.Y ) ).ToArray();
		rig.Builder.RefreshClosedCircuitBridges( rig.Station, circuit );
		var after = rig.Snapshot();
		for ( var i = 0; i < after.Length; i++ )
		{
			if ( before[i].Type != rig.Builder.OriginType || before[i].Mask is not (0x11 or 0x44) || (before[i].Flags & 15) == 0 )
				Assert.AreEqual( before[i], after[i], $"untouched tile {i}" );
			var restored = after[i];
			restored.Flags = before[i].Flags;
			restored.Family = before[i].Family;
			restored.Piece = before[i].Piece;
			restored.Rotation = before[i].Rotation;
			Assert.AreEqual( before[i], restored, $"only classification fields may change at {i}" );
			Assert.AreEqual( before[i].Flags & ~15, after[i].Flags & ~15 );
		}
		CollectionAssert.AreEqual( primary, rig.Route.Select( p => rig.Cells.RawTypeAt( p.X, p.Y ) ).ToArray() );
	}

	[TestMethod]
	public void UnsupportedRoutesKeepExactlyTheOldWalkAndRefreshResult()
	{
		foreach ( var fault in new[] { "open", "armless", "single", "tee", "cross", "nonreciprocal", "zero-owner", "wrong-owner", "alternate", "mixed", "extra-station", "legacy", "addon", "unknown", "diagonal", "heading", "second" } )
		{
			var rig = new Rig( link: 2 );
			var tile = rig.At( 3 );
			switch ( fault )
			{
				case "open": tile.Type = 0; break;
				case "armless": tile.Mask = 0; break;
				case "single": tile.Mask = 4; break;
				case "tee": tile.Mask = 0x45; break;
				case "cross": tile.Mask = 0x55; break;
				case "nonreciprocal": tile.Mask = 0x11; break;
				case "zero-owner": tile.Owner = 0; break;
				case "wrong-owner": tile.Owner++; break;
				case "alternate": tile.Type = 25; break;
				case "mixed": tile.Type = TrackCellTypes.KartOrigin; break;
				case "extra-station": tile.Type = rig.Builder.StartType; break;
				case "legacy": rig.Station = rig.Station with { Legacy = true }; break;
				case "addon": tile.Flags |= TrackCell.FlagAddOn; break;
				case "unknown": tile.Flags |= 0x80; break;
				case "diagonal": tile.Mask |= 2; break;
				case "heading": rig.Station = rig.Station with { Rotation = 90 }; break;
				case "second": rig.Station = rig.Station with { Second = (0, 0) }; break;
			}
			rig.Put( 3, tile );
			var circuit = TrackCircuitWalker.Walk( rig.Builder, rig.Station );
			foreach ( var step in circuit.Steps ) rig.Builder.Refresh( step.X, step.Y );
			var oldResult = rig.Snapshot();
			rig.Builder.RefreshClosedCircuitBridges( rig.Station, circuit );
			CollectionAssert.AreEqual( oldResult, rig.Snapshot(), fault );
		}
	}
}
