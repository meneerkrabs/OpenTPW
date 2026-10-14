using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The kart and water tracks on the original PC data (inconclusive without OPENTPW_GAME_PATH): the piece meshes and height profiles of every theme,
/// the water ride's script against the controller, and guests riding a headlessly built Splish Splash on the Jungle level.
/// </summary>
[TestClass]
public class TrackAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	[TestInitialize]
	public void Init()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original ride archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	private static IEnumerable<ObjectCatalogEntry> TrackRides( int whichTrackType ) =>
		ObjectCatalog.Themes.SelectMany( theme => ObjectCatalog.Load( theme ).Entries )
			.Where( entry => !entry.IsBonus && entry.Settings.GetInt( "Bumper.WhichTrackType" ) == whichTrackType && entry.Category == ObjectCategory.Ride );

	[TestMethod]
	public void TheFourWaterRidesAndFourKartRidesHaveCompletePieceKits()
	{
		var water = TrackRides( 2 ).ToArray();
		var karts = TrackRides( 1 ).ToArray();
		Assert.AreEqual( 4, water.Length, "Splish Splash, Ooze Crooz, The Blobulator and Taptastic Rapids" );
		Assert.AreEqual( 4, karts.Length, "DinoKarts, Crypt Karts, Space Racers and Bumble Buggies" );
		foreach ( var entry in water )
		{
			var template = BumperTemplates.ForRide( 2, entry.Settings.GetInt( "Bumper.BumperType" ) );
			Assert.IsNotNull( template, $"{entry.DisplayName}'s Bumper.BumperType {entry.Settings.GetInt( "Bumper.BumperType" )} is a water template" );
			Assert.IsTrue( template.IsWater );
			var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, TrackFamily.Water );
			Assert.AreEqual( 25, kit.Entries.Count );
			Assert.AreEqual( 0, kit.Missing().Count, $"{entry.DisplayName}: {string.Join( ", ", kit.Missing() )}" );
			foreach ( var piece in kit.Entries.Where( item => item.Index != 0 ) )
			{
				var model = kit.GetModel( piece.Index );
				Assert.IsNotNull( model, $"{entry.DisplayName} piece {piece.Index} {kit.StemOf( piece.Index )}" );
				Assert.IsTrue( model.Meshes.Count > 0 );
				var profile = kit.GetHeightProfile( piece.Index );
				Assert.IsNotNull( profile, $"{entry.DisplayName} height profile of piece {piece.Index}" );
				Assert.AreEqual( (2, 2, 5, 10, 10), (profile.Width, profile.Height, profile.SamplesPerCell, profile.ProfileWidth, profile.ProfileHeight), "every piece profile is 2 × 2 cells of 5 samples" );
				Assert.AreEqual( 100, profile.Profile.Length );
				Assert.AreEqual( 4, profile.CellMaxima.Length );
				Assert.IsTrue( profile.CellFlags.Span.ToArray().All( flag => flag == 0 ), "the per-cell flags are zero in every shipped file" );
			}
			// the station's own 4 × 4 profile
			var station = new HmpFile( entry.FileSystem.OpenRead( $"{entry.ArchivePath}/{Path.GetFileNameWithoutExtension( entry.ModelPath )}.hmp" ) );
			Assert.AreEqual( (4, 4, 5), (station.Width, station.Height, station.SamplesPerCell) );
		}
		foreach ( var entry in karts )
		{
			var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, TrackFamily.Kart );
			Assert.AreEqual( 20, kit.Entries.Count );
			Assert.AreEqual( 0, kit.Missing().Count, $"{entry.DisplayName}: {string.Join( ", ", kit.Missing() )}" );
			Assert.IsNotNull( kit.GetHeightProfile( TrackPieceSelector.PieceCorner2 ) );
		}
		// the bumper types of the data match the traced template table (jungle water −5; the other themes' water rides use −8, −12 and −13)
		CollectionAssert.AreEquivalent( new[] { -5, -8, -12, -13 }, water.Select( entry => entry.Settings.GetInt( "Bumper.BumperType" ) ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { -4, -7, -9, -10 }, karts.Select( entry => entry.Settings.GetInt( "Bumper.BumperType" ) ).ToArray() );
		// the supplemental boat mesh is in the archive
		var jungle = water.First( entry => entry.InfoId == 1160 );
		Assert.AreEqual( "wr_ring.md2", jungle.Settings["SupplementalMeshes[0].FileName"]!.Trim( '"', ' ' ) );
		Assert.IsTrue( jungle.FileSystem.GetFiles( jungle.ArchivePath ).Any( file => file.EndsWith( "wr_ring.md2", StringComparison.OrdinalIgnoreCase ) ) );
	}

	/// <summary>The surface a boat floats on is the piece's water mesh: the straight's channel is a plane about 3.25 units above the tile, and its banks have none.</summary>
	[TestMethod]
	public void ThePieceWaterMeshesGiveTheBoatSurface()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Get( 1160 );
		var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, TrackFamily.Water );
		var straight = TrackSurface.FromModel( kit.GetModel( TrackPieceSelector.PieceStraight )! );
		Assert.IsTrue( straight.TriangleCount > 0 );
		Assert.AreEqual( 3.25f, straight.HeightAt( 10, 10 )!.Value, 0.2f, "the channel's water plane" );
		Assert.AreEqual( 3.25f, straight.HeightAt( 8, 2 )!.Value, 0.2f );
		Assert.IsNull( straight.HeightAt( 1, 10 ), "the bank beside the channel has no water face" );
		var bend = TrackSurface.FromModel( kit.GetModel( TrackPieceSelector.PieceCorner )! );
		Assert.IsTrue( bend.HeightAt( 14, 14 ).HasValue, "a bend's channel runs through its diagonal" );
		Assert.IsNull( bend.HeightAt( 1, 1 ) );
		// a boat across the straight sits level at the water plane
		var pose = TrackSurface.Pose( 10, 10, 0, 3.33f, ( x, y ) => straight.HeightAt( x, y ) ?? 0 );
		Assert.AreEqual( 3.25f, pose.Height, 0.2f );
		Assert.AreEqual( 0f, pose.Pitch, 0.05f );
	}

	[TestMethod]
	public void EveryShippedHeightProfileIsAWellFormedHmp()
	{
		var count = 0;
		foreach ( var entry in TrackRides( 2 ).Concat( TrackRides( 1 ) ) )
		{
			foreach ( var file in entry.FileSystem.GetFiles( entry.ArchivePath ).Where( file => file.EndsWith( ".hmp", StringComparison.OrdinalIgnoreCase ) ) )
			{
				using var stream = entry.FileSystem.OpenRead( file );
				var profile = new HmpFile( stream );
				Assert.AreEqual( 5, profile.SamplesPerCell, file );
				Assert.AreEqual( 100, profile.Scale, file );
				Assert.IsTrue( profile.BoundingBox.Count == 6 && profile.BoundingBox[5] <= 40.01f, file );
				Assert.AreEqual( profile.Width * profile.Height, profile.CellMaxima.Length );
				count++;
			}
		}
		Assert.IsTrue( count >= 8 * 10, $"{count} profiles" );
	}

	/// <summary>The jungle water ride's script against its controller: it waits for a valid circuit, opens, takes a guest, launches a boat on its timer and hands the guest back.</summary>
	[TestMethod]
	public void SplishSplashScriptDrivesItsControllerThroughALap()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Get( 1160 );
		var world = new RideScriptWorld( 3 );
		var runtime = new OriginalObjectRuntime( entry, world, seed: 5, open: true );
		var bump = runtime.Bump;
		Assert.IsNotNull( bump, "Splish Splash has a controller" );
		Assert.AreEqual( -5, bump.Type );
		Assert.IsFalse( bump.IsTrackValid );
		Assert.IsNotNull( runtime.Visitors.ExcitementBase, "the controller feeds the ride's excitement" );
		// no track yet: the script waits at BUMP 5 and nothing opens
		for ( var tick = 0; tick < 5 * 60; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( 0, bump.Phase );
		Assert.IsFalse( runtime.Visitors.IsOpen, "an invalid circuit takes no guests" );
		// lay a closed circuit with a station at (10, 10) and let the script see it
		var rig = BuildCircuit( bump );
		for ( var tick = 0; tick < 3 * 60; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.PhaseRunning, bump.Phase, "BUMP 7 opened the ride" );
		Assert.IsTrue( runtime.Visitors.IsOpen );
		// a guest asks to board: the host writes its id to VAR_LETMEON, the script moves it into the next boat (BUMP 1)
		runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), 42 );
		runtime.Simulate( Tick );
		runtime.Simulate( Tick );
		Assert.AreEqual( 1, runtime.GetVariable( RideVariables.VAR_ONRIDE ) );
		Assert.AreEqual( 0, runtime.GetVariableByName( "VAR_LETMEON" ), "the script consumed the request" );
		var launched = false;
		var left = 0;
		for ( var tick = 0; tick < 60 * 60 && left == 0; tick++ )
		{
			runtime.Simulate( Tick );
			launched |= bump.VehicleCount > 0;
			left = runtime.GetVariableByName( "VAR_LETMEOFF" );
		}
		Assert.IsTrue( launched, "the boat launched when the 10-second wait ran out" );
		Assert.AreEqual( 42, left, "BUMP 2 handed the guest back through VAR_LETMEOFF after the boat unloaded" );
		Assert.IsTrue( bump.VehicleCount >= 1, "a water ride launches a boat every 10 seconds, with or without guests" );
		Assert.AreEqual( 1, bump.CompletedRuns );
		Assert.AreEqual( 0, runtime.Script!.UnimplementedEffects.GetValueOrDefault( Opcode.BUMP ), "every BUMP command reached the controller: " + string.Join( ", ", runtime.Script.UnimplementedEffects.Select( pair => $"{pair.Key}x{pair.Value}" ) ) );
		// a broken-down ride: the script reports VAR_BREAKSTAT to BUMP 8 and BUMP 17 stops the water
		runtime.SetVariable( "VAR_BREAKSTAT", 1 );
		for ( var tick = 0; tick < 120 * 60 && bump.WaterRunning; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.StateBroken, bump.State );
		Assert.IsFalse( bump.WaterRunning, "BUMP 17 with a nonzero parameter stops the water" );
		runtime.SetVariable( "VAR_BREAKSTAT", 0 );
		for ( var tick = 0; tick < 120 * 60 && (bump.State != BumpController.StateFine || !bump.WaterRunning); tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.StateFine, bump.State );
		Assert.IsTrue( bump.WaterRunning, "after the repair BUMP 17 with 0 starts the water again" );
		runtime.Stop();
		Assert.AreEqual( 0, world.Bumpers.Controllers.Count, "a removed water ride leaves the world" );
		GC.KeepAlive( rig );
	}

	private static object BuildCircuit( BumpController bump )
	{
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, TrackFamily.Water, () => null, new FlatGrid() );
		var station = TrackStation.FromPlacement( 10, 10, 0 );
		builder.PlaceStation( station );
		var writer = new TrackLineWriter( builder );
		Assert.IsTrue( writer.Commit( (14, 10), (14, 12) ).Completed );
		Assert.IsTrue( writer.Commit( (14, 12), (8, 12) ).Completed );
		Assert.IsTrue( writer.Commit( (8, 12), (8, 10) ).Completed );
		var circuit = TrackCircuitWalker.WalkAndRefresh( builder, station );
		Assert.IsTrue( circuit.IsValid, circuit.Failure );
		var centre = TrackStation.Centre( station.First );
		bump.SetStation( centre.X, centre.Y, station.Heading );
		bump.Rebuild( circuit );
		return builder;
	}

	private sealed class FlatGrid : IParkGrid
	{
		public int Width => 40;
		public int Height => 40;
		public System.Numerics.Vector2 Origin => default;
		public OriginalPlacementResult CheckTerrain( int x, int y ) => OriginalPlacementResult.Allowed;
		public float GetCellHeight( int x, int y ) => 0;
	}
}
