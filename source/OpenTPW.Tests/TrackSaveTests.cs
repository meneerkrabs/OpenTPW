using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The track part of the park-resume envelope (docs/PARK-SAVES.md, docs/TRACKS.md), asset-free: a structurally valid part passes the shape check and
/// the codec, corrupt parts are refused with a reason, a park without a track keeps its earlier format and hash, and the cells and a controller with a
/// boat on the water survive a save and restore exactly.
/// </summary>
[TestClass]
public class TrackSaveTests
{
	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}

	/// <summary>The envelope fixture's 20 × 10 map with a station tile at (4, 2) and a controller entry for its placed attraction 1.</summary>
	private static ParkResumeSnapshot WithTracks( Func<TrackWorldSaveData, TrackWorldSaveData>? change = null )
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		var width = snapshot.Layout.Cells.Width;
		var cells = new List<TrackCellSaveData>
		{
			new( 4, 2, TrackCellTypes.WaterStart, 0x44, 0, 0, -1, 0, 0, 0, 0, 0, 1 ),
			new( 5, 2, TrackCellTypes.Extension, 0, 0, 0, 2 * width + 4, 0, 0, 0, 0, 0, 0 ),
			new( 4, 3, TrackCellTypes.Extension, 0, 0, 0, 2 * width + 4, 0, 0, 0, 0, 0, 0 ),
			new( 5, 3, TrackCellTypes.Extension, 0, 0, 0, 2 * width + 4, 0, 0, 0, 0, 0, 0 )
		};
		snapshot.Layout.Cells.Cells.AddRange( cells.Select( cell => new ParkLayoutSnapshot.CellData { X = cell.X, Y = cell.Y, Type = cell.Type, Flags = 0, Placements = 0, Links = 0, QueueLink = 0 } ) );
		var controller = new BumpControllerSaveData( 1, -5, -1, 50, 2, 0, 1200, 0.01, true, true, new List<int> { 4 }, new List<int>(),
			new List<TrackSectionSaveData> { new( 9, true, 12288, 6144, 1 ) },
			new List<BumpVehicleSaveData> { Vehicle( 3, 40000 ) } );
		var part = new TrackWorldSaveData { Cells = cells, Controllers = new List<BumpControllerSaveData> { controller }, CosmeticState = 99 };
		return snapshot with { Tracks = change?.Invoke( part ) ?? part };
	}

	private static BumpVehicleSaveData Vehicle( int slot, int x ) => new( slot, 0x4004009, 1, x, 33000, 80, -3, 80, -3, 80, 128, 40, 2, 1024, 0, x + 100, 33000, 5, -7, 2, -1, 0, 120, 1, 0, 0, 0, new List<int> { 5, 6 } );

	private static void Rejects( ParkResumeSnapshot snapshot, string message ) =>
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( snapshot ) ).Message, message );

	[TestMethod]
	public void PreservedBaseTypesRequireAStationOrItsExtension()
	{
		var valid = WithTracks( part => part with { Cells = part.Cells.Select( cell => cell with { BaseType = 4 } ).ToList() } );
		ParkResumeSaveFile.ValidateShape( valid );
		ParkResumeSaveFile.ValidateShape( ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( valid ) ) );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.Type == TrackCellTypes.Extension ? cell with { BaseType = 4, Parent = 0 } : cell ).ToList() } ), "without a station origin" );
		var ordinary = WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.Type == TrackCellTypes.WaterStart ? cell with { Type = TrackCellTypes.WaterOrigin, BaseType = 4 } : cell ).ToList() } );
		var index = ordinary.Layout.Cells.Cells.FindIndex( cell => cell.X == 4 && cell.Y == 2 );
		ordinary.Layout.Cells.Cells[index] = ordinary.Layout.Cells.Cells[index] with { Type = TrackCellTypes.WaterOrigin };
		Rejects( ordinary, "without a station origin" );
	}

	[TestMethod]
	public void AWellFormedTrackPartPassesTheShapeCheckAndTheCodec()
	{
		var snapshot = WithTracks();
		ParkResumeSaveFile.ValidateShape( snapshot );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateShape( restored );
		var part = restored.Tracks!;
		Assert.AreEqual( 4, part.Cells.Count );
		Assert.AreEqual( 99u, part.CosmeticState );
		var controller = part.Controllers.Single();
		Assert.AreEqual( (1, -5, 2, 1200), (controller.AttractionId, controller.Type, controller.Phase, controller.TickCount) );
		CollectionAssert.AreEqual( new[] { 4 }, controller.Pending );
		Assert.AreEqual( 0.01, controller.TickRemainder, "the tick clock survives the codec" );
		CollectionAssert.AreEqual( new[] { 5, 6 }, controller.Vehicles.Single().Riders );
		Assert.AreEqual( (9, true, 12288, 6144, 1), (controller.Sections[0].Kind, controller.Sections[0].NoBuoy, controller.Sections[0].X, controller.Sections[0].Y, controller.Sections[0].Segment) );
	}

	[TestMethod]
	public void ATeleportsPassingFlagIsWrittenOnlyWhenSetAndSurvivesTheCodec()
	{
		var plain = System.Text.Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( WithTracks() ) );
		Assert.IsFalse( plain.Contains( "AddOnPassing" ), "a section without the flag is written as before, so earlier saves and their hashes stay as they were" );
		var passing = WithTracks( part => part with
		{
			Controllers = part.Controllers.Select( controller => controller with { Sections = controller.Sections.Select( section => section with { AddOnPassing = true } ).ToList() } ).ToList()
		} );
		var json = System.Text.Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( passing ) );
		StringAssert.Contains( json, "\"AddOnPassing\":true" );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( passing ) );
		Assert.IsTrue( restored.Tracks!.Controllers.Single().Sections.Single().AddOnPassing );
		Assert.IsFalse( ParkResumeSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( plain ) ).Tracks!.Controllers.Single().Sections.Single().AddOnPassing, "an earlier save reads without it" );
	}

	[TestMethod]
	public void ASaveWithoutTracksStaysWithoutTracks()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Assert.IsNull( snapshot.Tracks );
		var json = System.Text.Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( snapshot ) );
		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		Assert.IsNull( restored.Tracks );
		ParkResumeSaveFile.ValidateShape( restored );
		StringAssert.Contains( json, "\"Tracks\":null", "the optional part is absent as null: old readers that allow it are unaffected, new readers load old saves" );
	}

	[TestMethod]
	public void CorruptedTrackDataIsRefusedWithAReason()
	{
		Rejects( WithTracks( part => part with { Cells = part.Cells.Append( part.Cells[0] ).ToList() } ), "saved twice" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { StationLayout = 7 } ).ToList() } ), "unknown station layout" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 3 ? cell with { X = 99 } : cell ).ToList() } ), "outside the cell map" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 2 ? cell with { Type = 77 } : cell ).ToList() } ), "no track type" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 2 ? cell with { Mask = 0x02 } : cell ).ToList() } ), "outside 0x55" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 5 && cell.Y == 2 ? cell with { Parent = 100000 } : cell ).ToList() } ), "parent" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 2 ? cell with { Type = TrackCellTypes.WaterOrigin } : cell ).ToList() } ), "saved cell map" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { AttractionId = 9 } ).ToList() } ), "does not place" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Concat( part.Controllers ).ToList() } ), "does not follow" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Phase = 9 } ).ToList() } ), "out of range" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Performance = 101 } ).ToList() } ), "outside 0..100" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { TickRemainder = double.NaN } ).ToList() } ), "tick clock" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Pending = new List<int> { 0 } } ).ToList() } ), "guest list" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 1 ), Vehicle( 3, 2 ) } } ).ToList() } ), "repeated slot" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Vehicles = new List<BumpVehicleSaveData> { Vehicle( 300, 1 ) } } ).ToList() } ), "bad or repeated slot" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 1 ) with { Heading = 600 } } } ).ToList() } ), "heading" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 1 ) with { TargetBuoy = -5 } } } ).ToList() } ), "negative" );
	}

	/// <summary>The fixture with a second placed ride (attraction 2) beside the track ride.</summary>
	private static ParkResumeSnapshot WithSecondRide( Func<TrackWorldSaveData, TrackWorldSaveData> change )
	{
		var snapshot = WithTracks( change );
		var second = snapshot.Layout.Objects[0] with { AttractionId = 2, X = 12, Y = 6, CreationSeed = snapshot.Layout.Objects[0].CreationSeed ^ 3 };
		return snapshot with
		{
			Layout = snapshot.Layout with { Objects = snapshot.Layout.Objects.Append( second ).ToList(), NextSeed = 3, NextAttractionId = 3 },
			Scripts = snapshot.Scripts with { NextAttractionId = 3 },
			Runtimes = snapshot.Runtimes.Append( new ParkResumeRuntime( 0, ParkResumeSaveFileTests.RuntimeSnapshot( 2, second.InfoId, second.Archive ) ) ).ToList()
		};
	}

	[TestMethod]
	public void TheSharedModulePartIsRefusedWhenOutOfRange()
	{
		Rejects( WithTracks( part => part with { FrameCount = -1 } ), "frame counter or clock" );
		Rejects( WithTracks( part => part with { FrameCount = 5, TickRemainder = double.NaN } ), "frame counter or clock" );
		Rejects( WithTracks( part => part with { FrameCount = 5, TickRemainder = BumpController.MaximumSavedRemainder } ), "frame counter or clock" );
		Rejects( WithTracks( part => part with { FrameCount = 5, TickRemainder = -0.001 } ), "frame counter or clock" );
		ParkResumeSnapshot Slots( params int[] slots ) => WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { ObjectSlots = slots.ToList() } ).ToList() } );
		ParkResumeSaveFile.ValidateShape( Slots( 0, 2047 ) );
		Rejects( Slots( -1 ), "outside the module's pool" );
		Rejects( Slots( BumpWorld.ObjectPool ), "outside the module's pool" );
		ParkResumeSnapshot Foreign( int attraction ) => WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with
		{
			Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 40000 ) with { ObjectAttraction = attraction, ObjectIndex = 0 } }
		} ).ToList() } );
		Rejects( Foreign( 1 ), "not another placed ride" );
		Rejects( Foreign( 7 ), "not another placed ride" );
		// a placed neighbour is accepted (the park restore checks its object exists)
		ParkResumeSaveFile.ValidateShape( WithSecondRide( part => part with { Controllers = part.Controllers.Select( controller => controller with
		{
			Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 40000 ) with { ObjectAttraction = 2, ObjectIndex = 0 } }
		} ).ToList() } ) );
	}

	[TestMethod]
	public void ASharedModuleSaveNamesEverySlotOnceAcrossTheRides()
	{
		// the second ride's controller holds no section (a fresh water ride's entry suffices for the shape check)
		BumpControllerSaveData Second( params BumpVehicleSaveData[] vehicles ) => new( 2, -5, -1, 50, 0, 0, 0, 0, true, false, new List<int>(), new List<int>(),
			new List<TrackSectionSaveData>(), vehicles.ToList(), new List<int> { 5 } );
		ParkResumeSnapshot Two( long? frames, int vehicleSlot, int objectSlot, List<int>? firstSlots = null ) => WithSecondRide( part => part with
		{
			FrameCount = frames,
			Controllers = new List<BumpControllerSaveData>
			{
				part.Controllers[0] with { ObjectSlots = firstSlots ?? new List<int> { 0 }, BuoyCount = 1 },
				Second( Vehicle( vehicleSlot, 41000 ) with { Riders = new List<int>() } ) with { ObjectSlots = new List<int> { objectSlot }, BuoyCount = 1 }
			}
		} );
		ParkResumeSaveFile.ValidateShape( Two( 9, 4, 1 ) );
		Rejects( Two( 9, 3, 1 ), "another ride's vehicle holds" );
		Rejects( Two( 9, 4, 0 ), "a slot another object of the module holds" );
		Rejects( WithTracks( part => part with { FrameCount = 9 } ), "names no collision-object slots" );
		Rejects( WithTracks( part => part with { FrameCount = 9, Controllers = part.Controllers.Select( controller => controller with { ObjectSlots = new List<int>() } ).ToList() } ), "buoy count" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { BuoyCount = BumpWorld.BuoyPool + 1 } ).ToList() } ), "buoy count" );
		// a save from before the shared module may repeat a slot across rides: the restore moves the later vehicle to the first free one
		ParkResumeSaveFile.ValidateShape( Two( null, 3, 0 ) );
	}

	[TestMethod]
	public void ABumperCarOfAnArenaThePoolsRefusedSitsInNoObject()
	{
		ParkResumeSnapshot Bumper( List<int>? slots, int objectIndex ) => WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with
		{
			Type = -1, Phase = 2, Sections = new List<TrackSectionSaveData>(), ObjectSlots = slots,
			Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 40000 ) with { Flags = 0x4404009, ObjectIndex = objectIndex, Riders = new List<int> { 5 }, Engine = true, WallHits = -1 } }
		} ).ToList() } );
		ParkResumeSaveFile.ValidateShape( Bumper( new List<int>(), -1 ) );
		ParkResumeSaveFile.ValidateShape( Bumper( new List<int> { 0 }, 0 ) );
		Rejects( Bumper( new List<int> { 0 }, -1 ), "not in the arena object" );
		Rejects( Bumper( null, -1 ), "not in the arena object" );
	}

	[TestMethod]
	public void AParkWhoseLastBumpRideWasRemovedKeepsItsModuleThroughTheEnvelope()
	{
		var rng = 5u;
		var world = new BumpWorld();
		var ride = new BumpController( BumperTemplates.Get( -6 ), () => rng = unchecked( rng * 1664525u + 1013904223u ), world );
		ride.SetStation( 5 * 0xC00, 5 * 0xC00, TrackHeading.PosY );
		for ( var tick = 0; tick < 7; tick++ )
			ride.Tick();
		ride.Advance( BumpController.TickSeconds / 3 );
		world.CosmeticState = 1234;
		world.Remove( ride );
		var part = TrackSave.Capture( new ParkCellMap( 20, 10 ), world, Array.Empty<(int, BumpController)>() );
		Assert.IsNotNull( part, "the module outlives its last ride" );
		var snapshot = ParkResumeSaveFileTests.Envelope() with { Tracks = part };
		ParkResumeSaveFile.ValidateShape( snapshot );
		var back = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateShape( back );
		Assert.AreEqual( 0, back.Tracks!.Controllers.Count );
		var fresh = new BumpWorld();
		TrackSave.RestoreModule( fresh, back.Tracks, Array.Empty<(BumpController, IReadOnlyList<int>?)>() );
		Assert.AreEqual( (7L, world.TickRemainder, 1234u), (fresh.FrameCount, fresh.TickRemainder, fresh.CosmeticState), "clock, remainder and generator come back" );
		var again = TrackSave.Capture( new ParkCellMap( 20, 10 ), fresh, Array.Empty<(int, BumpController)>() )!;
		Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( part ), System.Text.Json.JsonSerializer.Serialize( again ), "and save the same part again" );
		// a ride built after the resume ticks on from the saved counter
		var next = new BumpController( BumperTemplates.Get( -6 ), () => 0u, fresh );
		next.SetStation( 5 * 0xC00, 5 * 0xC00, TrackHeading.PosY );
		next.Tick();
		Assert.AreEqual( 8L, fresh.FrameCount );
	}

	[TestMethod]
	public void CellsAndAControllerWithABoatSurviveASaveAndRestore()
	{
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, TrackFamily.Water, () => null, new FlatGrid() );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
		builder.PlaceStation( station );
		var writer = new TrackLineWriter( builder );
		writer.Commit( (14, 10), (14, 12) );
		writer.Commit( (14, 12), (8, 12) );
		writer.Commit( (8, 12), (8, 10) );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		var world = new BumpWorld();
		var rng = 7u;
		var controller = new BumpController( BumperTemplates.Get( -5 ), () => { rng = unchecked( rng * 1103515245u + 12345u ); return rng >> 4; }, world );
		world.Add( controller );
		var centre = station.Reference;
		controller.SetStation( centre.X, centre.Y, station.Heading );
		controller.Rebuild( circuit );
		controller.Open();
		controller.AddGuest( 21 );
		controller.Launch();
		controller.AddGuest( 22 );
		for ( var tick = 0; tick < 200; tick++ )
			controller.Tick();
		world.NextCosmetic();
		var saved = TrackSave.Capture( cells, world, new[] { (1, controller) } )!;
		Assert.AreEqual( 8 * 4, saved.Cells.Count, "eight tiles of four cells" );
		var back = ParkResumeJson.Deserialize<TrackWorldSaveData>( ParkResumeJson.Serialize( saved ) );

		var cells2 = new ParkCellMap( 40, 40 );
		var builder2 = new TrackBuilder( cells2, TrackFamily.Water, () => null, new FlatGrid() );
		foreach ( var item in cells.Tracks.Cells() )
			cells2.SetType( item.X, item.Y, item.Cell.Type );
		TrackSave.RestoreCells( cells2, back );
		var circuit2 = TrackCircuitWalker.WalkAndRefresh( builder2, station );
		Assert.IsTrue( circuit2.IsValid );
		var world2 = new BumpWorld { CosmeticState = back.CosmeticState };
		var controller2 = new BumpController( BumperTemplates.Get( -5 ), () => 0, world2 );
		world2.Add( controller2 );
		controller2.SetStation( centre.X, centre.Y, station.Heading );
		controller2.Rebuild( circuit2 );
		controller2.Restore( back.Controllers.Single() );

		var (hashA, hashB) = (new StateHasher(), new StateHasher());
		cells.AddCanonicalState( hashA );
		cells2.AddCanonicalState( hashB );
		world.AddCanonicalState( hashA );
		world2.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value, "cells, links, segments, controller, boat and generator are equal" );
		Assert.AreEqual( circuit.Steps.Select( step => step.Segment ).Sum(), cells2.Tracks.Cells().Where( item => item.Cell.IsOrigin ).Sum( item => item.Cell.Segment ), "the walk reproduced the segment numbers" );
	}

	[TestMethod]
	public void ALoadRunsTheBridgePassSoOldWaterBridgesTurnPlain()
	{
		// a save from before TRACK-010 dropped terrain water kept bridge flags and pieces on straights over water; the restore re-runs the bridge pass
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, TrackFamily.Water, () => null, new FlatGrid() );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
		builder.PlaceStation( station );
		new TrackLineWriter( builder ).Commit( (14, 10), (20, 10) );
		var saved = TrackSave.Capture( cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
		var straights = saved.Cells.Where( item => TrackCellTypes.IsOrigin( item.Type ) && TrackBuilder.IsStraight( item.Mask ) && !TrackCellTypes.IsStation( item.Type ) ).ToList();
		Assert.IsTrue( straights.Count >= 3, "the line lays plain straights" );
		var old = saved with
		{
			Cells = saved.Cells.Select( item => straights.Contains( item )
				? item with { Flags = (byte)(item.Flags | TrackCell.FlagBridge), Piece = TrackPieceSelector.Select( item.Type, item.Mask, item.Link, (byte)(item.Flags | TrackCell.FlagBridge) ).Piece }
				: item ).ToList()
		};
		Assert.AreEqual( straights.Count, old.Cells.Count( item => (item.Flags & TrackCell.FlagBridge) != 0 ), "the old save holds bridge flags" );
		Assert.IsTrue( old.Cells.Except( saved.Cells ).All( item => straights.All( plain => plain.Piece != item.Piece ) ), "and bridge pieces" );

		var restored = new ParkCellMap( 40, 40 );
		foreach ( var item in cells.Tracks.Cells() )
			restored.SetType( item.X, item.Y, item.Cell.Type );
		TrackSave.RestoreCells( restored, old );
		var (hashA, hashB) = (new StateHasher(), new StateHasher());
		cells.Tracks.AddCanonicalState( hashA );
		restored.Tracks.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value, "the loaded tiles carry the plain pieces again, without an edit next to them" );
	}

	[TestMethod]
	public void TheTrackLayerJoinsTheCellDigestOnlyWhileATrackExists()
	{
		var empty = new StateHasher();
		new ParkCellMap( 20, 20 ).AddCanonicalState( empty );
		var untouched = new ParkCellMap( 20, 20 );
		var builder = new TrackBuilder( untouched, TrackFamily.Water, () => null, new FlatGrid() );
		// laying and removing a tile leaves the map as it was: the digest is the trackless one again
		Assert.AreEqual( CellBuildResult.Ok, builder.PlaceTile( 6, 6 ) );
		var withTile = new StateHasher();
		untouched.AddCanonicalState( withTile );
		Assert.AreNotEqual( empty.Value, withTile.Value );
		Assert.IsTrue( builder.RemoveTile( 6, 6 ) );
		var removed = new StateHasher();
		untouched.AddCanonicalState( removed );
		// the tile's type writes stamped their block (the walk's changed-ground reroute, WALK-007): compare with a map stamped alike
		var stamped = new ParkCellMap( 20, 20 );
		stamped.RestoreBlockStamps( untouched.StampCounter, untouched.CaptureBlockStamps() );
		var reference = new StateHasher();
		stamped.AddCanonicalState( reference );
		Assert.AreEqual( reference.Value, removed.Value );
		Assert.AreEqual( 0, untouched.Tracks.Count );
		// the world hash reads the controllers only when one exists
		var scripts = new RideScriptWorld( 5 );
		var before = WorldStateHash.Compute( new WorldStateSources { Scripts = scripts } );
		var controller = new BumpController( BumperTemplates.Get( -5 ), () => 0, scripts.Bumpers );
		scripts.Bumpers.Add( controller );
		Assert.AreNotEqual( before, WorldStateHash.Compute( new WorldStateSources { Scripts = scripts } ) );
		scripts.Bumpers.Remove( controller );
		Assert.AreEqual( before, WorldStateHash.Compute( new WorldStateSources { Scripts = scripts } ), "a world without controllers hashes as before" );
	}

	[TestMethod]
	public void BumperCarsMustSitInTheArenaAndOneCarOwnsTheEngine()
	{
		ParkResumeSnapshot Bumper( int phase, params BumpVehicleSaveData[] cars ) => WithTracks( part => part with { Controllers = part.Controllers.Select( controller =>
			controller with { Type = -1, Phase = phase, Sections = new List<TrackSectionSaveData>(), Vehicles = cars.ToList() } ).ToList() } );
		var good = Vehicle( 3, 40000 ) with { Flags = 0x4404009, ObjectIndex = 0, Riders = new List<int> { 5 }, Engine = true, WallHits = -1 };
		ParkResumeSaveFile.ValidateShape( Bumper( 2, good ) );
		Rejects( Bumper( 2, good with { ObjectIndex = -1 } ), "not in the arena object" );
		Rejects( Bumper( 1, good ), "engine voice" );
		Rejects( Bumper( 2, good with { Flags = 0x4004009 } ), "engine voice" );
		Rejects( Bumper( 2, good, Vehicle( 4, 41000 ) with { Flags = 0x4404009, ObjectIndex = 0, Riders = new List<int> { 6 }, Engine = true, WallHits = -1 } ), "engine voice" );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Type = -9, Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 40000 ) with { Engine = true } } } ).ToList() } ), "engine voice" );
		// a boat holds its engine voice for its whole life, but never a smoke emitter (water boats have no sparks node)
		ParkResumeSaveFile.ValidateShape( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 40000 ) with { Engine = true } } } ).ToList() } ) );
		Rejects( WithTracks( part => part with { Controllers = part.Controllers.Select( controller => controller with { Vehicles = new List<BumpVehicleSaveData> { Vehicle( 3, 40000 ) with { Smoke = true } } } ).ToList() } ), "smoke emitter" );
	}

	[TestMethod]
	public void BoatsGuestsAndScalarsOutsideTheParkAreRefused()
	{
		BumpControllerSaveData Boats( BumpControllerSaveData controller, params BumpVehicleSaveData[] vehicles ) => controller with { Vehicles = vehicles.ToList() };
		ParkResumeSnapshot With( Func<BumpControllerSaveData, BumpControllerSaveData> change ) => WithTracks( part => part with { Controllers = part.Controllers.Select( change ).ToList() } );
		Rejects( With( controller => controller with { Pending = new List<int> { 99999 } } ), "guest list" );
		Rejects( With( controller => controller with { Pending = new List<int> { 4, 4 } } ), "listed twice" );
		Rejects( With( controller => controller with { Pending = new List<int> { 5 } } ), "listed twice" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { Riders = new List<int> { 5, 5 } } ) ), "listed twice" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { Riders = new List<int> { 99999 } } ) ), "rider the park does not hold" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { X = int.MinValue } ) ), "outside the park" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { TargetY = int.MaxValue } ) ), "outside the park" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { VX = int.MinValue } ) ), "velocity" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { VY0 = int.MaxValue } ) ), "velocity" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { Spin = int.MinValue } ) ), "scalar" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { Radius = -1 } ) ), "scalar" );
		Rejects( With( controller => Boats( controller, Vehicle( 3, 1 ) with { Flags = 0 } ) ), "not live" );
		Rejects( With( controller => controller with { TickRemainder = 1.0 } ), "tick clock" );
		Rejects( With( controller => controller with { TickRemainder = -0.5 } ), "tick clock" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 2 ? cell with { Rotation = 45 } : cell ).ToList() } ), "ranges" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 2 ? cell with { Piece = 4000 } : cell ).ToList() } ), "ranges" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 2 ? cell with { Owner = -1 } : cell ).ToList() } ), "ranges" );
		Rejects( WithTracks( part => part with { Cells = part.Cells.Select( cell => cell.X == 4 && cell.Y == 2 ? cell with { Overlap = -3 } : cell ).ToList() } ), "ranges" );
		ParkResumeSaveFile.ValidateShape( With( controller => Boats( controller, Vehicle( 3, 1 ) with { Riders = new List<int> { 5, 6 } } ) ) );
	}

	[TestMethod]
	public void TheOwnerOfATileSurvivesTheCodecAndTheDigest()
	{
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, TrackFamily.Water, () => null, new FlatGrid() );
		var station = TrackStation.FromPlacement( 10, 9, 0 );
		builder.PlaceStation( station );
		var owner = builder.OwnerOf( station );
		new TrackLineWriter( builder, owner: owner ).Commit( (14, 10), (14, 12) );
		var saved = TrackSave.Capture( cells, new BumpWorld(), Array.Empty<(int, BumpController)>() )!;
		Assert.IsTrue( saved.Cells.All( cell => cell.Owner == owner ), "tiles and extension cells carry the ride" );
		var back = ParkResumeJson.Deserialize<TrackWorldSaveData>( ParkResumeJson.Serialize( saved ) );
		var restored = new ParkCellMap( 40, 40 );
		foreach ( var item in cells.Tracks.Cells() )
			restored.SetType( item.X, item.Y, item.Cell.Type );
		TrackSave.RestoreCells( restored, back );
		Assert.IsTrue( restored.Tracks.Cells().All( item => item.Cell.Owner == owner ) );
		var (hashA, hashB, hashC) = (new StateHasher(), new StateHasher(), new StateHasher());
		cells.AddCanonicalState( hashA );
		restored.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value );
		var cell = restored.Tracks.Get( 14, 10 );
		cell.Owner = owner + 1;
		restored.Tracks.Set( 14, 10, cell );
		restored.AddCanonicalState( hashC );
		Assert.AreNotEqual( hashA.Value, hashC.Value, "the owner is part of the digest" );
	}

	[TestMethod]
	public void ASaveFromBeforeTheKartControllersStillMatchesItsKartRide()
	{
		var live = new Dictionary<int, BumperTemplate> { [1] = BumperTemplates.Get( -5 ), [2] = BumperTemplates.Get( -4 ) };
		Assert.IsTrue( ParkResumeSaveFile.TrackControllersMatch( live, new[] { 1, 2 } ), "a new save holds both controllers" );
		Assert.IsTrue( ParkResumeSaveFile.TrackControllersMatch( live, new[] { 1 } ), "an old-shape save has no kart controller: it is migrated on load (TRACK-027)" );
		Assert.IsFalse( ParkResumeSaveFile.TrackControllersMatch( live, new[] { 2 } ), "a water ride still needs its controller" );
		Assert.IsFalse( ParkResumeSaveFile.TrackControllersMatch( live, new[] { 1, 9 } ), "a controller of a ride the park does not place is refused" );
		Assert.IsTrue( ParkResumeSaveFile.TrackControllersMatch( new Dictionary<int, BumperTemplate> { [2] = BumperTemplates.Get( -4 ) }, Array.Empty<int>() ), "a park with only a kart ride and no track part" );
	}
}
