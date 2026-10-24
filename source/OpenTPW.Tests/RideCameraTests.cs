using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The ride camera of a read-only visit (docs/reverse/ONLINE-VISIT.md): riding follows a coaster's front car
/// along the sampled track and the prototype Totem's carriage height. Inconclusive without OPENTPW_GAME_PATH,
/// like the other original-data tests.
/// </summary>
[TestClass]
public class RideCameraTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;

	/// <summary>A headlessly built coaster and the pieces a shared-park round trip needs to rebuild it elsewhere.</summary>
	internal sealed record BuiltCoaster( OriginalObjectRuntime Station, CoasterBuilder Builder, CoasterRide Ride,
		int AnchorX, int AnchorY, OriginalParkGrid Grid, RideScriptWorld Scripts );

	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original ride archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	/// <summary>A completed coaster on the Jungle level, built headlessly as the coaster asset test builds it.</summary>
	internal static BuiltCoaster BuildCoaster()
	{
		var park = OriginalPark.Load( "jungle", readShippedSave: false );
		var terrain = new OriginalParkGrid( park );
		var grid = GuestPathGrid.FromOriginal( park.Map, null );
		var scripts = new RideScriptWorld( 11 );
		var entry = ObjectCatalog.Load( "jungle" ).Get( 1180 );
		bool Free( int x, int y ) => terrain.CheckTerrain( x, y ) == OriginalPlacementResult.Allowed && !grid.IsWalkable( x, y ) && !grid.IsQueue( x, y );
		var anchor = Enumerable.Range( 0, terrain.Width * terrain.Height ).Select( index => (X: index % terrain.Width, Y: index / terrain.Width) )
			.Where( cell => ObjectFootprint.GetCells( entry.Shape, cell.X, cell.Y, 0 ).All( inside => Free( inside.X, inside.Y ) ) )
			.Select( cell => (cell.X, cell.Y, Ends: CoasterBuilder.StationEnds( entry.Shape, cell.X, cell.Y, 0 )!.Value) )
			.First( site => CoasterTests.Circuit.All( offset => Free( site.Ends.Depart.X + offset.X, site.Ends.Depart.Y + offset.Y ) ) );
		var station = new OriginalObjectRuntime( entry, scripts, seed: 5, open: true );
		var economy = ParkEconomy.CreateForTheme( "jungle", easy: false );
		var builder = new CoasterBuilder( station.Coaster!, CoasterSite.FromGrid( terrain ), () => economy );
		var (depart, back) = CoasterBuilder.StationEnds( entry.Shape, anchor.X, anchor.Y, 0 )!.Value;
		builder.SetStation( depart, back );
		builder.Height = 50;
		foreach ( var (dx, dy) in CoasterTests.Circuit )
			builder.Place( depart.X + dx, depart.Y + dy );
		Assert.AreEqual( CoasterPlaceResult.Ok, CoasterTests.CloseAndComplete( builder ) );
		return new BuiltCoaster( station, builder, station.Coaster!, anchor.X, anchor.Y, terrain, scripts );
	}

	[TestMethod]
	public void TheCameraRidesTheFrontCarAlongTheTrack()
	{
		var built = BuildCoaster();
		var station = built.Station;
		var ride = built.Ride;

		// Only a coaster with a completed track can be ridden; a shop's runtime cannot.
		Assert.IsTrue( RideCameraMode.CanRide( station ) );
		Assert.IsFalse( RideCameraMode.CanRide( new OriginalObjectRuntime( ObjectCatalog.Load( "jungle" ).Find( 1100 )!, new RideScriptWorld( 1 ) ) ), "a shop without a track cannot be ridden" );

		var origin = new System.Numerics.Vector2( 12.5f, -3.25f );
		void CheckFrame()
		{
			var (eye, look) = RideCameraMode.CoasterFrame( ride, origin );
			// The train's position is its middle car's; the camera car stands its offset ahead (0x43bc20) in the train with the smallest position (0x43d7dd).
			var sample = ride.SampleAt( ride.CarCoordinate( ride.Trains[ride.RideViewTrain], ride.Definition.RideCameraCar ) );
			Assert.AreEqual( origin.X + sample.Position.X * ObjectPlacement.ModelScale, eye.X, 1e-3f, "the eye sits on the front car's sample" );
			Assert.AreEqual( origin.Y + sample.Position.Z * ObjectPlacement.ModelScale, eye.Y, 1e-3f );
			Assert.AreEqual( sample.Position.Y * ObjectPlacement.ModelScale + RideCameraMode.CoasterSeatHeight, eye.Z, 1e-3f );
			var direction = new Vector3( sample.Direction.X, sample.Direction.Z, sample.Direction.Y ).Normal;
			Assert.AreEqual( direction.X, (look - eye).Normal.X, 1e-2f, "the view follows the track's direction" );
			Assert.AreEqual( direction.Z, (look - eye).Normal.Z, 1e-2f );
		}
		CheckFrame();

		// The station script initialises the controller, the train dispatches and runs, and the camera follows it.
		var first = RideCameraMode.CoasterFrame( ride, origin ).Eye;
		for ( var tick = 0; tick < 30 * 30 && (ride.Trains[0].Speed == 0 || !ride.Initialised); tick++ )
			station.Simulate( Tick );
		Assert.IsTrue( ride.Initialised, "the station script initialises the coaster controller" );
		Assert.IsTrue( ride.Trains[0].Speed > 0, "the train keeps circulating: it never stops at the station" );
		for ( var tick = 0; tick < 120; tick++ )
			station.Simulate( Tick );
		CheckFrame();
		Assert.AreNotEqual( first.X, RideCameraMode.CoasterFrame( ride, origin ).Eye.X, 1e-2f, "the camera moved with the train" );
	}

	[TestMethod]
	public void TheCameraRidesThePrototypeTotemsCarriage()
	{
		// The prototype frame is pure: the eye rests at the anchor plus the seat height and rises with the carriage.
		var anchor = new Vector3( 4, -2, 1 );
		var (eye, look) = RideCameraMode.PrototypeFrame( anchor, motionHeight: 0, heading: new Vector3( 0, 1, 0 ) );
		Assert.AreEqual( new Vector3( 4, -2, 1 + RideCameraMode.PrototypeSeatHeight ), eye );
		Assert.AreEqual( 0, (look - eye).Normal.X, 1e-3f, "the entry heading is kept" );
		Assert.AreEqual( 1, (look - eye).Normal.Y, 1e-3f );
		var risen = RideCameraMode.PrototypeFrame( anchor, motionHeight: 6.5f, heading: new Vector3( 0, 1, 0 ) ).Eye;
		Assert.AreEqual( 1 + 6.5f + RideCameraMode.PrototypeSeatHeight, risen.Z, 1e-3f, "the eye rises with the carriage" );
		// A degenerate heading falls back to the world's forward axis instead of an empty view.
		var fallback = RideCameraMode.PrototypeFrame( anchor, 0, Vector3.Zero );
		Assert.AreEqual( Vector3.Forward, (fallback.Look - fallback.Eye).Normal );
		Assert.AreEqual( 90f, RideCameraMode.ViewFieldOfView, "the original's one projection angle" );
	}
}
