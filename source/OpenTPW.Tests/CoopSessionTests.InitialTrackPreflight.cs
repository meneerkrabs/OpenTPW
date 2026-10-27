using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

public partial class CoopSessionTests
{
	private static ObjectCatalogEntry InitialTrackEntry( Park park, string settings, params string[] shape ) =>
		new( "jungle", ObjectCategory.Ride, "/initial-track-preflight", new ObjectSettings( new[] { new ObjectSettingsFile( settings, "station.sam" ) } ),
			new ObjectShape( shape ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(),
			park.One.Entry.FileSystem, null );

	private (ObjectCatalogEntry Entry, TrackStation Station, (int X, int Y) Anchor, OriginalObject Other) InitialTrackPlacement( Park park, TrackFamily family, int rotation )
	{
		park.Enter();
		TypeFootprints( park );
		var other = PlaceKartRide( park ); // real registration/removal hooks and both builders, away from the proposed station
		// Use a purchasable id from EconomyTestData, so the positive control goes through the command and purchase, not just TryPlace.
		var entry = InitialTrackEntry( park, "Info.Id 1100\nInfo.Name InitialTrack\nInfo.WhichUIType 0\nInfo.IsChoosable 1\nInfo.OverwritePriority 5\n"
			+ (family == TrackFamily.Kart ? "Bumper.WhichTrackType 1\nBumper.BumperType -4\n" : "Bumper.WhichTrackType 2\nBumper.BumperType -5\n"),
			"****", "****", "****", "****" );
		var level = park.Level;
		var catalog = (ObjectCatalog)typeof( ObjectCatalog ).GetConstructor( NonPublic, null, new[] { typeof( string ), typeof( IReadOnlyList<ObjectCatalogEntry> ) }, null )!
			.Invoke( new object[] { "jungle", level.Objects.Catalog.Entries.Append( entry ).ToArray() } );
		typeof( ParkObjects ).GetField( "<Catalog>k__BackingField", NonPublic )!.SetValue( level.Objects, catalog );
		level.SetBuildGrant( 1, true );
		level.Commands.Rights = new CoopBuildRights( level );
		var anchor = Level.GetCentredAnchor( entry, 16, 18, rotation );
		return (entry, TrackStation.FromPlacement( anchor.X, anchor.Y, rotation ), anchor, other);
	}

	private static void AssertInitialTrackRefusalIsAtomic( Park park, ObjectCatalogEntry entry, int rotation, params int[] players )
	{
		var level = park.Level;
		var cells = level.Guests!.Grid.Cells;
		var balance = park.Economy.Balance;
		var objectVersion = level.Objects.Version;
		var cellVersion = cells.Version;
		var trackVersion = cells.Tracks.Version;
		var tracks = cells.Tracks.Cells().ToArray();
		var claims = level.RideClaims.ToArray();
		foreach ( var player in players )
		{
			var result = level.Commands.ApplyRemote( new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), 16, 18, rotation ) { Player = player } );
			Assert.IsTrue( result.RightsRefused, $"rotation {rotation}, player {player}: {result.Reason}" );
			Assert.AreEqual( balance, park.Economy.Balance, "before purchase, not a refund after partial placement" );
			Assert.AreEqual( objectVersion, level.Objects.Version, "no object was registered or demolished" );
			Assert.AreEqual( cellVersion, cells.Version, "no footprint or queue link was written" );
			Assert.AreEqual( trackVersion, cells.Tracks.Version, "no station, foreign origin or extension was written" );
			CollectionAssert.AreEqual( tracks, cells.Tracks.Cells().ToArray() );
			CollectionAssert.AreEqual( claims, level.RideClaims.ToArray() );
		}
	}

	[TestMethod]
	public void InitialTrackPreflightProtectsEveryAdjacentObjectEvenWhenTheTileWillBeRefused()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var park = NewPark();
			var (entry, station, anchor, _) = InitialTrackPlacement( park, family, rotation );
			var level = park.Level;
			var cells = level.Guests!.Grid.Cells;
			foreach ( var tile in new[] { station.Exit, station.Entry } )
			foreach ( var at in TrackStation.TileCellsOf( tile.X, tile.Y ) )
			foreach ( var fixedItem in new[] { false, true } )
			{
				// TPW fixed items are anchored at (0,0), with world-space shape offsets. The first text row has the greatest V coordinate.
				var shape = fixedItem ? new[] { new string( '.', at.X ) + "*" }.Concat( Enumerable.Repeat( ".", at.Y ) ).ToArray() : new[] { "*" };
				var victimEntry = InitialTrackEntry( park, "Info.Id 1100\nInfo.OverwritePriority 9\n"
					+ (fixedItem ? "Info.DontApplyOffset 1\n" : "Bumper.WhichTrackType 3\n"), shape );
				var victim = level.Objects.TryPlace( victimEntry, fixedItem ? 0 : at.X, fixedItem ? 0 : at.Y, 0, out var placed );
				Assert.IsNotNull( victim, placed.ToString() );
				Assert.AreSame( victim, level.Objects.FindAt( at.X, at.Y ), "the protected object must actually occupy this adjacent cell" );
				if ( fixedItem )
					Assert.AreEqual( 0, level.RideClaims.Count, "fixed objects need protection even with no active claims" );
				else
				{
					Assert.IsNotNull( victim!.Runtime.Coaster );
					Assert.IsTrue( level.ExecuteClaimRide( 2, victim.Runtime.AttractionId ).Accepted );
				}
				var blocked = TrackStation.TileCellsOf( tile.X, tile.Y ).First( cell => cell != at );
				level.Guests.Grid.SetPath( blocked.X, blocked.Y, true );
				cells.SetFlags( blocked.X, blocked.Y, ParkCellFlags.NoModify );
				Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( entry, anchor.X, anchor.Y, rotation, out var plan ) );
				Assert.IsFalse( plan.Overwritten.Contains( victim! ), "the ordinary footprint does not cover the victim" );
				AssertInitialTrackRefusalIsAtomic( park, entry, rotation, fixedItem ? new[] { 1 } : new[] { 0, 1 } );
				Assert.IsFalse( victim!.IsDeleted );
				Assert.IsTrue( ((CoopBuildRights)level.Commands.Rights).PlacementLeavesClaimsAlone( fixedItem ? 0 : 2, entry, 16, 18, rotation ), "host for a fixed object, holder for a claimed ride" );
				if ( !fixedItem )
					level.ExecuteReleaseRide( 0, victim.Runtime.AttractionId );
				level.Objects.Remove( victim );
				cells.SetFlags( blocked.X, blocked.Y, ParkCellFlags.None );
				cells.SetType( blocked.X, blocked.Y, ParkCellType.Empty );
			}
		}
	}

	[TestMethod]
	public void InitialTrackPreflightProtectsQueueJoinsOnEveryExitAndEntryCell()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var park = NewPark();
			var (entry, station, anchor, _) = InitialTrackPlacement( park, family, rotation );
			var level = park.Level;
			var grid = level.Guests!.Grid;
			ConfigureQueue( grid, park.One, 10, 5 );
			MarkStation( park, park.One, seat: 2 );
			var outside = new[] { station.Exit, station.Entry }.SelectMany( tile => TrackStation.TileCellsOf( tile.X, tile.Y ) ).ToArray();
			var occupied = ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).Select( cell => (cell.X, cell.Y) ).Concat( outside ).ToHashSet();
			foreach ( var at in outside )
			{
				var direction = Enumerable.Range( 0, 4 ).First( d => !occupied.Contains( (at.X + GuestPathGrid.Directions[d].DX, at.Y + GuestPathGrid.Directions[d].DY) ) );
				var (dx, dy) = GuestPathGrid.Directions[direction];
				var queue = (X: at.X + dx, Y: at.Y + dy);
				grid.SetQueue( queue.X, queue.Y, -1 );
				grid.Cells.SetOwner( queue.X, queue.Y, park.One.Visitors.OwnerWord );
				grid.Cells.SetLinks( queue.X, queue.Y, GuestPathGrid.LinkValue( (direction + 2) % 4 ) );
				grid.SetPath( at.X, at.Y, true, (byte)(1 << direction) );
				Assert.AreSame( park.One, level.QueueOwnerAt( queue.X, queue.Y ) );
				Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( entry, anchor.X, anchor.Y, rotation, out var plan ) );
				Assert.IsFalse( plan.ClearedPaths.Contains( at ) );
				AssertInitialTrackRefusalIsAtomic( park, entry, rotation, 0, 1 );
				Assert.IsTrue( ((CoopBuildRights)level.Commands.Rights).PlacementLeavesClaimsAlone( 2, entry, 16, 18, rotation ) );
				grid.ClearQueue( queue.X, queue.Y );
				grid.SetPath( at.X, at.Y, false );
			}
			MarkStation( park, park.One, station: false );
		}
	}

	[TestMethod]
	public void InitialTrackPreflightProtectsOffsetForeignOriginsAndExtensionsByTrackOwner()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var park = NewPark();
			var (entry, station, anchor, other) = InitialTrackPlacement( park, family, rotation );
			var level = park.Level;
			var builder = level.BuilderOf( other )!;
			Assert.IsTrue( level.ExecuteClaimRide( 2, other.Runtime.AttractionId ).Accepted );
			var owner = builder.OwnerOf( level.TrackStations[other] );
			var footprint = ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
			foreach ( var tile in new[] { station.Exit, station.Entry } )
			foreach ( var offset in new[] { (X: -1, Y: 0), (X: 1, Y: 0), (X: 0, Y: -1), (X: 0, Y: 1) } )
			{
				var foreign = (X: tile.X + offset.X, Y: tile.Y + offset.Y);
				if ( TrackStation.TileCellsOf( foreign.X, foreign.Y ).Any( footprint.Contains ) )
					continue;
				Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( foreign.X, foreign.Y, owner ) );
				var affected = builder.InitialStationGiveWayCells( station ).Where( cell => builder.Tracks.Get( cell.X, cell.Y ).Owner == owner ).ToArray();
				Assert.IsTrue( affected.Length > 0 );
				Assert.IsTrue( affected.All( cell => level.Objects.FindAt( cell.X, cell.Y ) == null ), "track records have no object footprint owner" );
				Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( entry, anchor.X, anchor.Y, rotation ) );
				AssertInitialTrackRefusalIsAtomic( park, entry, rotation, 0, 1 );
				Assert.IsTrue( ((CoopBuildRights)level.Commands.Rights).PlacementLeavesClaimsAlone( 2, entry, 16, 18, rotation ), "the holder's own track" );
				builder.EraseTile( foreign.X, foreign.Y );
			}
		}
	}

	[TestMethod]
	public void InitialTrackPreflightKeepsStationSkipGatesAndAllowsOrdinaryScenery()
	{
		foreach ( var family in new[] { TrackFamily.Kart, TrackFamily.Water } )
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var park = NewPark();
			var (entry, station, anchor, _) = InitialTrackPlacement( park, family, rotation );
			var level = park.Level;
			var builder = family == TrackFamily.Kart ? level.KartTracks! : level.WaterTracks!;
			// Another real station's first tile lies under this exit. Its missing entry is a valid refused/cleared initial tile, not an obstacle inside our footprint.
			var other = level.Objects.TryPlace( entry, anchor.X + 4 * TrackHeading.Dx( station.Heading ), anchor.Y + 4 * TrackHeading.Dy( station.Heading ), rotation, out var placed );
			Assert.IsNotNull( other, placed.ToString() );
			var otherStation = level.TrackStations[other!];
			Assert.AreEqual( station.Exit, otherStation.First );
			builder.EraseTile( otherStation.Entry.X, otherStation.Entry.Y );
			Assert.IsTrue( level.ExecuteClaimRide( 2, other!.Runtime.AttractionId ).Accepted );
			var foreign = TrackStation.TileCellsOf( station.Exit.X, station.Exit.Y ).Select( at => builder.Tracks.Get( at.X, at.Y ) ).ToArray();
			Assert.AreSame( other, level.Objects.FindAt( station.Exit.X, station.Exit.Y ) );
			Assert.IsFalse( builder.InitialStationGiveWayCells( station ).Contains( station.Exit ) );
			Assert.IsTrue( ((CoopBuildRights)level.Commands.Rights).PlacementLeavesClaimsAlone( 1, entry, 16, 18, rotation ) );
			var scenery = InitialTrackEntry( park, "Info.Id 1100\nInfo.OverwritePriority 0\n", "*" );
			var victims = TrackStation.TileCellsOf( station.Entry.X, station.Entry.Y ).Select( at => level.Objects.TryPlace( scenery, at.X, at.Y, 0, out _ )! ).ToArray();
			Assert.IsTrue( victims.All( item => item != null ) );
			var result = level.Commands.ApplyRemote( new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), 16, 18, rotation ) { Player = 1 } );
			Assert.IsTrue( result.Accepted, result.Reason );
			Assert.IsFalse( other.IsDeleted );
			CollectionAssert.AreEqual( foreign, TrackStation.TileCellsOf( station.Exit.X, station.Exit.Y ).Select( at => builder.Tracks.Get( at.X, at.Y ) ).ToArray() );
			Assert.IsTrue( victims.All( item => item.IsDeleted ), "authorized demolition still runs, not a silently skipped partial edit" );
			Assert.IsTrue( builder.Tracks.Get( station.Entry.X, station.Entry.Y ).IsOrigin );
		}
	}
}
