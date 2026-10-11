using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online;
using OpenTPW.Online.Packages;

namespace OpenTPW.Tests;

/// <summary>Version 2 park packages: the complete built layout and the visit that rebuilds it (docs/ONLINE.md).</summary>
[TestClass]
public class SharedParkTests
{
	private static IReadOnlyList<ParkCellRecord> Cells() => new[]
	{
		new ParkCellRecord( 10, 12, 1, 0x0F, 0, 0x20, 0 ),   // path, all links, NoModify
		new ParkCellRecord( 11, 12, 1, 0x15, 0, 0, 0 ),
		new ParkCellRecord( 10, 13, 3, 0, 4, 0, 0 ),         // queue, link back toward (10, 12)
		new ParkCellRecord( 48, 17, 1, 0x0F, 0, 0, 0 ),
	};

	private static IReadOnlyList<ParkObjectRecord> Objects() => new[]
	{
		new ParkObjectRecord( 1100, 50, 22, 3, 3, 90, false, Open: true, Price: 300, Repair: 87.5 ),
		new ParkObjectRecord( 1101, 60, 30, 2, 2, 0, false, Open: false, Price: 0, Repair: 100 ),
		new ParkObjectRecord( 1, 0, 0, 0, 0, 0, true ),
	};

	private static IReadOnlyList<CoasterTrackRecord> Coasters()
	{
		CoasterNodeRecord[] nodes = { new( 70, 22, 15f, false ), new( 74, 25, 22.5f, true ), new( 70, 29, 15f, false ) };
		return new[] { new CoasterTrackRecord( 0, nodes, Closed: true, Complete: true ) };
	}

	private static ParkStatsRecord Stats() => new( 620, 250_000, 80_000, 143, 25, true, "Year 2, March" );

	internal static MinimalParkPayload LayoutPayload() => new( "jungle", MinimalParkPayload.SourceOriginalLevel,
		new[] { new CellRef( 10, 12 ), new CellRef( 11, 12 ), new CellRef( 48, 17 ) }, Objects(), null, null,
		Cells(), Coasters(), Stats(), null );

	/// <summary>The payload as a writer finishes it: with the digest of its own layout.</summary>
	internal static MinimalParkPayload Sealed()
	{
		var payload = LayoutPayload();
		return payload with { LayoutDigest = payload.ComputeLayoutDigest() };
	}

	internal static ParkPackage LayoutPackage() => ParkPackage.Create( Sealed().ToSnapshot(
			new[] { new RequiredContent( ContentKinds.Level, "jungle", null ) } ),
		new ParkInfo( "Built Park", "Owner's description", "Alice", "jungle" ), new GameInfo( "Theme Park World", "English" ),
		null, OnlineFormatTests.FixedId, OnlineFormatTests.FixedTime );

	[TestMethod]
	public void V2PackageRoundTripsWithLayoutAndDigest()
	{
		var package = LayoutPackage();
		CollectionAssert.AreEqual( package.ToBytes(), LayoutPackage().ToBytes() );
		var read = ParkPackage.Read( new MemoryStream( package.ToBytes() ) );
		Assert.AreEqual( 2, read.Manifest.FormatVersion );
		Assert.AreEqual( 2, read.Manifest.Payload.Version );
		var payload = MinimalParkPayload.FromSnapshot( read.ToSnapshot() );
		Assert.IsTrue( payload.HasBuiltLayout );
		CollectionAssert.AreEqual( Cells().ToList(), payload.Cells!.ToList() );
		CollectionAssert.AreEqual( Objects().ToList(), payload.Objects.ToList() );
		CollectionAssert.AreEqual( Coasters().ToList(), payload.Coasters!.ToList() );
		Assert.AreEqual( Stats(), payload.Stats );
		Assert.AreEqual( payload.ComputeLayoutDigest(), payload.LayoutDigest );
		// The v1 view stays meaningful: exactly the path-type cells.
		CollectionAssert.AreEqual( new[] { new CellRef( 10, 12 ), new CellRef( 11, 12 ), new CellRef( 48, 17 ) }, payload.PathCells.ToList() );
	}

	/// <summary>A version 1 package as OpenTPW 0.7 wrote it: manifest formatVersion 1 and a payload without any v2 member.</summary>
	internal static ParkPackage V1Package()
	{
		var v1Payload = new MinimalParkPayload( "jungle", MinimalParkPayload.SourceOriginalLevel,
			new[] { new CellRef( 47, 17 ) }, new[] { new ParkObjectRecord( 1100, 50, 22, 3, 3, 90, false ) },
			new PrototypeRideState( 12.5f, -40f, true ), null );
		var bytes = StrictJson.Serialize( v1Payload );
		var manifest = LayoutPackage().Manifest with { FormatVersion = 1,
			Payload = LayoutPackage().Manifest.Payload with { Version = 1, Length = bytes.Length, Sha256 = ParkPackage.Hash( bytes ) } };
		return ParkPackage.Read( OnlineFormatTests.Zip(
			(ParkPackage.ManifestEntry, StrictJson.Serialize( manifest )),
			(ParkPackage.PayloadEntry, bytes) ) );
	}

	[TestMethod]
	public void V1PackagesStillReadAndImport()
	{
		var payloadJson = Encoding.UTF8.GetString( StrictJson.Serialize( new MinimalParkPayload( "jungle", MinimalParkPayload.SourceOriginalLevel,
			new[] { new CellRef( 47, 17 ) }, new[] { new ParkObjectRecord( 1100, 50, 22, 3, 3, 90, false ) },
			new PrototypeRideState( 12.5f, -40f, true ), null ) ) );
		Assert.IsFalse( payloadJson.Contains( "cells", StringComparison.Ordinal ), payloadJson );
		Assert.IsFalse( payloadJson.Contains( "coasters", StringComparison.Ordinal ), payloadJson );
		Assert.IsFalse( payloadJson.Contains( "layoutDigest", StringComparison.Ordinal ), payloadJson );
		var v1 = V1Package();
		Assert.AreEqual( 1, v1.Manifest.FormatVersion );
		var payload = MinimalParkPayload.FromSnapshot( v1.ToSnapshot() );
		Assert.IsFalse( payload.HasBuiltLayout );
		Assert.IsNull( payload.Cells );
		Assert.AreEqual( 1, payload.PathCells.Count );
	}

	[TestMethod]
	public void V2MalformedLayoutsAreRejected()
	{
		void Rejected( MinimalParkPayload payload, int version = 2 )
		{
			var bytes = StrictJson.Serialize( payload with { LayoutDigest = null } );
			Assert.ThrowsException<InvalidDataException>( () => MinimalParkPayload.FromBytes( bytes, version ), payload.ToString() );
		}
		Rejected( Sealed() with { Cells = Enumerable.Range( 0, MinimalParkPayload.MaximumCells + 1 ).Select( i => new ParkCellRecord( i % 1000, i / 1000, 1, 0, 0, 0, 0 ) ).ToList() } );
		Rejected( Sealed() with { Cells = new[] { new ParkCellRecord( -1, 0, 1, 0, 0, 0, 0 ) } } );
		Rejected( Sealed() with { Cells = new[] { new ParkCellRecord( 5, 5, 1, 0, 0, 0, 0 ), new ParkCellRecord( 5, 5, 3, 0, 0, 0, 0 ) } } );
		Rejected( Sealed() with { Cells = new[] { new ParkCellRecord( 5, 5, 3, 0, 7, 0, 0 ) } } );
		Rejected( Sealed() with { Objects = new[] { new ParkObjectRecord( 1, 1, 1, 1, 1, 0, false, Price: -1 ) } } );
		Rejected( Sealed() with { Objects = new[] { new ParkObjectRecord( 1, 1, 1, 1, 1, 0, false, Repair: 101 ) } } );
		Rejected( Sealed() with { Coasters = new[] { new CoasterTrackRecord( 99, Array.Empty<CoasterNodeRecord>(), false, false ) } } );
		Rejected( Sealed() with { Coasters = new[] { new CoasterTrackRecord( 0, Array.Empty<CoasterNodeRecord>(), Closed: false, Complete: true ) } } );
		Rejected( Sealed() with { Coasters = new[] { new CoasterTrackRecord( 0, new[] { new CoasterNodeRecord( 2, 2, 200_000f, false ) }, false, false ) } } );
		Rejected( Sealed() with { Stats = new ParkStatsRecord( -1, 0, 0, 0, 0, true, "Year 1, January" ) } );
		Rejected( Sealed() with { Stats = new ParkStatsRecord( 1, 0, 0, 0, 0, true, null! ) } );
		// v2 members in a version 1 document, and a digest that does not match the layout it covers.
		Rejected( Sealed(), version: 1 );
		Assert.ThrowsException<InvalidDataException>( () => (Sealed() with { Cells = new[] { new ParkCellRecord( 1, 1, 1, 0, 0, 0, 0 ) } }).ToBytes() );
	}

	[TestMethod]
	public void VisitRebuildsTheExactCellLayout()
	{
		var map = new ParkCellMap( 64, 64 );
		map.SetType( 10, 12, ParkCellType.Path );
		map.SetLinks( 10, 12, 0x0F );
		map.SetFlags( 10, 12, ParkCellFlags.NoModify );
		map.SetType( 11, 12, ParkCellType.Path );
		map.SetLinks( 11, 12, 0x15 );
		map.SetType( 10, 13, ParkCellType.Queue );
		map.SetQueueLink( 10, 13, 4 );
		var captured = SharedParkLayout.CaptureCells( map );
		Assert.AreEqual( 3, captured.Count );

		// Rebuild into a same-sized map and capture again: the layout digest is identical (the visit check).
		var rebuilt = SharedParkLayout.BuildCellMap( 64, 64, captured );
		Assert.AreEqual( ParkLayoutDigest.Compute( captured, Array.Empty<ParkObjectRecord>(), Array.Empty<CoasterTrackRecord>() ),
			ParkLayoutDigest.Compute( SharedParkLayout.CaptureCells( rebuilt ), Array.Empty<ParkObjectRecord>(), Array.Empty<CoasterTrackRecord>() ) );
		Assert.AreEqual( ParkCellType.Path, rebuilt.TypeAt( 10, 12 ) );
		Assert.AreEqual( ParkCellFlags.NoModify, rebuilt.FlagsAt( 10, 12 ) );
		Assert.AreEqual( ParkCellType.Queue, rebuilt.TypeAt( 10, 13 ) );
		Assert.AreEqual( 4, rebuilt.QueueLinkAt( 10, 13 ) );

		// The digest does not depend on the order the cells are listed in.
		var shuffled = captured.Reverse().ToList();
		Assert.AreEqual( ParkLayoutDigest.Compute( captured, Array.Empty<ParkObjectRecord>(), Array.Empty<CoasterTrackRecord>() ),
			ParkLayoutDigest.Compute( shuffled, Array.Empty<ParkObjectRecord>(), Array.Empty<CoasterTrackRecord>() ) );

		// A changed layout gives a different digest.
		var changed = captured.ToList();
		changed.Add( new ParkCellRecord( 40, 40, 1, 0x0F, 0, 0, 0 ) );
		Assert.AreNotEqual( ParkLayoutDigest.Compute( captured, Array.Empty<ParkObjectRecord>(), Array.Empty<CoasterTrackRecord>() ),
			ParkLayoutDigest.Compute( changed, Array.Empty<ParkObjectRecord>(), Array.Empty<CoasterTrackRecord>() ) );

		Assert.ThrowsException<InvalidDataException>( () => SharedParkLayout.BuildCellMap( 64, 64, new[] { new ParkCellRecord( 64, 64, 1, 0, 0, 0, 0 ) } ) );
	}

	[TestMethod]
	public void OfflineOriginalExportCarriesTheBuiltLayout()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var root = Path.Combine( Path.GetTempPath(), "opentpw-shared-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var previous = OpenTPW.Common.GlobalNamespace.FileSystem;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		OpenTPW.Common.GlobalNamespace.FileSystem = fileSystem;
		try
		{
			var output = Path.Combine( root, "jungle.tpwpark" );
			var folders = new OnlineFolders( Path.Combine( root, "online" ) );
			Assert.IsTrue( OnlineCommands.Run( new[] { "--export-park", output, "--load-original-level", "jungle" }, data, folders, _ => { } ) );
			var imported = OnlineCommands.Import( output, data, folders );
			var payload = imported.Visit.Payload;
			Assert.IsTrue( payload.HasBuiltLayout );
			Assert.AreEqual( 2, imported.Visit.Package.Manifest.FormatVersion );
			Assert.AreEqual( 0, imported.Visit.MissingContent.Count );
			Assert.AreEqual( 0, imported.Visit.Warnings.Count );
			// The visitor rebuilds the payload's cells in a fresh map of the same level and digests the same layout.
			var park = OriginalPark.Load( "jungle" );
			var rebuilt = SharedParkLayout.CaptureCells( SharedParkLayout.BuildCellMap( park.Map.CellCountX, park.Map.CellCountY, payload.Cells! ) );
			Assert.AreEqual( payload.LayoutDigest, ParkLayoutDigest.Compute( rebuilt, payload.Objects, payload.Coasters ?? Array.Empty<CoasterTrackRecord>() ) );
			Assert.AreEqual( park.Save!.PathCells.Count, payload.PathCells.Count );
			// Shipped objects round-trip: every imported object record is placed at its saved anchor…
			Assert.AreEqual( park.Save.PlacedObjects.Count + park.Save.UnresolvedObjects.Count + park.Save.FixedItems.Count, payload.Objects.Count );
			// …except fixed items, which a level places at the origin (their models are authored in park
			// space; Theme Park Inc's would keep their saved anchor, ParkObjects.Add): the payload must
			// describe what a visit can rebuild, or its digest check fails.
			foreach ( var fixedItem in payload.Objects.Where( item => item.Fixed ) )
				Assert.AreEqual( (0, 0, 0), (fixedItem.X, fixedItem.Y, fixedItem.Rotation), $"fixed item {fixedItem.InfoId} is recorded where a level places it" );
			// The placement round trip a visit performs: placing the payload's records through the level's
			// own ParkObjects lands every object where its record says (the fixed-item rule above), so the
			// digest the visit recomputes from the placements equals the published one.
			var objects = new ParkObjects( ObjectCatalog.Load( "jungle", easy: false ), new OriginalParkGrid( park ) );
			var (placed, skipped) = SharedParkLayout.PlaceObjects( objects, payload.Objects );
			Assert.AreEqual( 0, skipped.Count, string.Join( ", ", skipped ) );
			for ( var index = 0; index < payload.Objects.Count; index++ )
			{
				var record = payload.Objects[index];
				Assert.IsNotNull( placed[index], $"object {record.InfoId} placed" );
				Assert.AreEqual( (record.Fixed ? (0, 0, 0) : (record.X, record.Y, record.Rotation)),
					(placed[index]!.Placement.X, placed[index]!.Placement.Y, placed[index]!.Placement.Rotation),
					$"object {record.InfoId} is placed where its record says" );
			}
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previous; Directory.Delete( root, true ); }
	}

	[TestMethod]
	public void VisitPlacementRejectsObjectsOutsideTheLevelMap()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var previous = OpenTPW.Common.GlobalNamespace.FileSystem;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		OpenTPW.Common.GlobalNamespace.FileSystem = fileSystem;
		try
		{
			var grid = new SandboxParkGrid();
			var objects = new ParkObjects( ObjectCatalog.Load( "jungle" ), grid );
			var buildable = objects.Catalog.Buildable.First( entry => entry.Shape.Width > 1 || entry.Shape.Height > 1 );
			// A payload record may name any cell up to 1023; the level's own map is smaller, and a footprint that
			// leaves it is skipped and reported rather than placed (as the original import rejects such records).
			var (placed, skipped) = SharedParkLayout.PlaceObjects( objects, new[]
			{
				new ParkObjectRecord( buildable.InfoId, grid.Width - 1, grid.Height - 1, buildable.Shape.Width, buildable.Shape.Height, 0, false ),
				new ParkObjectRecord( buildable.InfoId, 0, 0, buildable.Shape.Width, buildable.Shape.Height, 0, false ),
			} );
			Assert.IsNull( placed[0] );
			Assert.IsNotNull( placed[1] );
			Assert.AreEqual( 1, skipped.Count );
			StringAssert.Contains( skipped[0], "outside the level map" );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previous; }
	}

	[TestMethod]
	public void V2WithoutDigestIsAcceptedButUnverifiable()
	{
		// Validation allows a version 2 payload without a layout digest; the visit then reports that it cannot
		// verify the layout instead of failing a check against nothing (Level.ApplyVisitedLayout, VisitSmokeTest).
		var payload = Sealed() with { LayoutDigest = null };
		var read = MinimalParkPayload.FromBytes( StrictJson.Serialize( payload ) );
		Assert.IsTrue( read.HasBuiltLayout );
		Assert.IsNull( read.LayoutDigest );
	}

	[TestMethod]
	public void PublishedCoasterTrackRoundTripsIntoAFreshStation()
	{
		var data = OriginalParkImportTests.OriginalDataPath();
		var previous = OpenTPW.Common.GlobalNamespace.FileSystem;
		var fileSystem = new BaseFileSystem( data );
		fileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		OpenTPW.Common.GlobalNamespace.FileSystem = fileSystem;
		try
		{
			// The owner's build: a completed circuit on the Jungle level, captured as a payload record.
			var built = RideCameraTests.BuildCoaster();
			var captured = SharedParkLayout.CaptureCoaster( 0, built.Builder );
			Assert.IsNotNull( captured );
			Assert.IsTrue( captured.Closed && captured.Complete, "the owner's circuit is closed and complete" );

			// The visit's rebuild: a fresh station of the same script world (as PlaceObjects places it), no
			// economy, with the published pylons appended onto its builder (RestoreCoaster's Level path).
			var station = new OriginalObjectRuntime( built.Station.Entry, built.Scripts, seed: 6, open: true );
			var builder = new CoasterBuilder( station.Coaster!, CoasterSite.FromGrid( built.Grid ) );
			var (depart, back) = CoasterBuilder.StationEnds( station.Entry.Shape, built.AnchorX, built.AnchorY, 0 )!.Value;
			builder.SetStation( depart, back );
			Assert.IsTrue( SharedParkLayout.RestoreCoaster( builder, captured.Nodes, captured.Closed, captured.Complete ) );
			Assert.AreEqual( captured, SharedParkLayout.CaptureCoaster( 0, builder ),
				"the rebuilt track is the published one (the layout digest's coaster part)" );

			// And what the visit's Ride action needs of it: the restored track runs, with trains to ride.
			Assert.IsTrue( station.Coaster!.IsOperational, "the restored track is operational" );
			Assert.IsTrue( station.Coaster.Trains.Count > 0, "the restored track has trains" );
		}
		finally { OpenTPW.Common.GlobalNamespace.FileSystem = previous; }
	}
}
