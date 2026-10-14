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
		Assert.AreSame( bump, runtime.LiveBump, "a running ride's controller is live" );
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
		Assert.IsNull( runtime.LiveBump, "a stopped ride has no live controller, so the park-resume capture and restore both skip it" );
		Assert.IsNotNull( runtime.Bump );
		GC.KeepAlive( rig );
	}

	// ---- Go-karts ------------------------------------------------------------------------------------

	private static ObjectCatalogEntry KartRide( string theme ) =>
		ObjectCatalog.Load( theme ).Entries.First( entry => !entry.IsBonus && entry.Category == ObjectCategory.Ride && entry.Settings.GetInt( "Bumper.WhichTrackType" ) == 1 );

	/// <summary>Every BUMP instruction of a script in program order as <c>command:parameter</c> (a variable parameter by its name).</summary>
	private static string BumpSequence( ObjectCatalogEntry entry )
	{
		var member = entry.Scripts.First( script => !Path.GetFileName( script ).StartsWith( "EventMap", StringComparison.OrdinalIgnoreCase ) );
		using var stream = entry.FileSystem.OpenRead( member );
		var file = new RideScriptFile( stream );
		return string.Join( " ", file.Instructions.Where( instruction => instruction.Opcode == (ushort)Opcode.BUMP )
			.Select( instruction => $"{instruction.Operands[0].Value}:{(instruction.Operands[1].Kind == RideScriptOperandKind.Variable ? file.VariableNames[instruction.Operands[1].Value] : instruction.Operands[1].Value.ToString())}" ) );
	}

	/// <summary>The four go-kart scripts issue exactly the traced BUMP commands (the corpus pins the controller's contract: BUMP 9 is never issued, BUMP 17 only in space).</summary>
	[TestMethod]
	public void TheKartScriptsIssueTheTracedBumpSequences()
	{
		const string common = "5:0 7:0 5:0 10:0 10:0 7:0";
		var expected = new Dictionary<string, string>
		{
			["jungle"] = $"{common} 1:VAR_LETMEON 4:0 14:VAR_DURATION 3:0 5:0 10:0 6:0 2:VAR_LETMEOFF 7:0 8:VAR_BREAKSTAT 8:0",
			["hallow"] = $"{common} 1:VAR_LETMEON 4:0 4:0 14:VAR_DURATION 3:0 5:0 10:0 6:0 2:VAR_LETMEOFF 7:0 8:VAR_BREAKSTAT 8:0",
			["space"] = $"{common} 1:VAR_LETMEON 4:0 14:VAR_DURATION 3:0 5:0 10:0 6:0 2:VAR_TEMP 7:0 8:1 8:0 17:1 17:0",
			["fantasy"] = $"{common} 1:VAR_PEEPID 4:0 14:VAR_DURATION 3:0 5:0 10:0 6:0 2:VAR_PEEPID 7:0 8:1 8:0"
		};
		foreach ( var (theme, sequence) in expected )
		{
			var entry = KartRide( theme );
			Assert.AreEqual( sequence, BumpSequence( entry ), $"{theme}: {entry.DisplayName}'s GoKarts.RSE" );
			Assert.IsFalse( BumpSequence( entry ).Split( ' ' ).Any( word => word.StartsWith( "9:" ) ), "no kart script issues BUMP 9 (worn)" );
		}
		// the lap count of the script's BUMP 14 is VAR_DURATION, which the ride's InitDuration (3) sets
		foreach ( var theme in expected.Keys )
			Assert.AreEqual( 3, KartRide( theme ).Upgrades.First( level => level.Level == 0 ).GetInt( "InitDuration" ), theme );
	}

	/// <summary>The four kart rides: template and model range, the supplemental meshes, the head and sparks dummies and the EventMap ids.</summary>
	[TestMethod]
	public void TheKartRidesCarryTheirTemplatesMeshesDummiesAndEventMaps()
	{
		var expected = new[]
		{
			(Theme: "jungle", Type: -4, Base: 0, Count: 4, Heads: 1, Events: new[] { 194, 195, 199 }),
			(Theme: "hallow", Type: -7, Base: 0, Count: 4, Heads: 2, Events: new[] { 165, 166, 170 }),
			(Theme: "space", Type: -9, Base: 1, Count: 4, Heads: 1, Events: new[] { 167, 168, 172 }),
			(Theme: "fantasy", Type: -10, Base: 1, Count: 1, Heads: 1, Events: new[] { 134, 135, 139 })
		};
		foreach ( var item in expected )
		{
			var entry = KartRide( item.Theme );
			var template = BumperTemplates.ForRide( 1, entry.Settings.GetInt( "Bumper.BumperType" ) );
			Assert.IsNotNull( template, item.Theme );
			Assert.AreEqual( (item.Type, item.Base, item.Count), (template.Type, template.ModelBase, template.ModelCount), item.Theme );
			Assert.IsTrue( template.IsKart && !template.IsWater );
			var kit = TrackPieceKit.Load( entry.FileSystem, entry.ArchivePath, TrackFamily.Kart );
			Assert.AreEqual( 0, kit.Missing().Count, item.Theme );
			// every mesh the model range names exists in the archive, and the controller's cycling stays inside the supplemental list
			for ( var variant = item.Base; variant < item.Base + item.Count; variant++ )
			{
				var name = entry.Settings[$"SupplementalMeshes[{variant}].FileName"]!.Trim( '"', ' ' );
				var path = entry.FileSystem.GetFiles( entry.ArchivePath ).First( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) );
				var model = ObjectAssets.LoadModel( entry.FileSystem, path );
				Assert.AreEqual( ModelFileKind.Geometry, model.Kind, name );
				for ( var head = 1; head <= item.Heads; head++ )
					Assert.IsTrue( RideHeadSpace.NodeForHead( model, head ) >= 0, $"{name}: a rider seat dummy {head}" );
				Assert.AreEqual( RideHeadSpace.NoRecord, RideHeadSpace.NodeForHead( model, item.Heads + 1 ), $"{name}: no more seats than the ride's riders per car" );
				Assert.IsTrue( model.DummyAttributes.Any( attribute => attribute.Type == 0x131 && attribute.Value == 1 ), $"{name}: the sparks node" );
			}
			if ( template.Type is -9 or -10 )
				Assert.AreEqual( "gk_shadow.md2", entry.Settings["SupplementalMeshes[0].FileName"]!.Trim( '"', ' ' ), "the hover karts' shadow is mesh 0" );
			// the sound child's event ids (engine, stop, horn)
			var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld( 3 ), seed: 1, open: false );
			for ( var tick = 0; tick < 10; tick++ )
				runtime.Simulate( Tick );
			Assert.AreEqual( item.Events[0], runtime.EventMapEvent( BumpEffects.SlotEngine ), $"{item.Theme} engine" );
			Assert.AreEqual( item.Events[1], runtime.EventMapEvent( BumpEffects.SlotStop ), $"{item.Theme} stop" );
			Assert.AreEqual( item.Events[2], runtime.EventMapEvent( BumpEffects.SlotToot ), $"{item.Theme} horn" );
			Assert.IsNotNull( runtime.Bump, "a kart ride has a controller" );
			Assert.AreEqual( item.Type, runtime.Bump.Type );
			runtime.Stop();
		}
	}

	/// <summary>The jungle Dino Karts script against its controller: it opens, boards guests, parks the karts on the grid, starts the race, ends it and hands the guests back.</summary>
	[TestMethod]
	public void DinoKartsScriptDrivesItsControllerThroughARace()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Get( 1150 );
		var world = new RideScriptWorld( 3 );
		var runtime = new OriginalObjectRuntime( entry, world, seed: 9, open: true );
		var bump = runtime.Bump!;
		Assert.AreEqual( -4, bump.Type );
		var effects = new List<BumpEffect>();
		bump.Effect += effects.Add;
		for ( var tick = 0; tick < 3 * 60; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.PhaseClosed, bump.Phase, "no circuit: the script waits at BUMP 5" );
		var rig = BuildCircuit( bump, TrackFamily.Kart );
		for ( var tick = 0; tick < 2 * 60; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.PhaseLoading, bump.Phase, "BUMP 7 opened the ride for loading" );
		// three guests board, one after the other; each gets its own kart (BUMP 1 then BUMP 4)
		foreach ( var guest in new[] { 41, 42, 43 } )
		{
			runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), guest );
			for ( var tick = 0; tick < 6; tick++ )
				runtime.Simulate( Tick );
			Assert.AreEqual( 0, runtime.GetVariableByName( "VAR_LETMEON" ), "the script took the guest" );
		}
		Assert.AreEqual( 3, bump.VehicleCount, "three karts" );
		Assert.AreEqual( 3, runtime.GetVariable( RideVariables.VAR_ONRIDE ) );
		Assert.AreEqual( new[] { 0, 1, 2 }.Sum(), bump.Vehicles.Sum( vehicle => vehicle.TargetBuoy ), "slot n aims at the n-th grid buoy" );
		// the loading ends 10 s after the last boarding; the script waits 2 s, sets the laps (BUMP 14) and starts (BUMP 3)
		var started = false;
		var phases = new List<int> { bump.Phase };
		var handed = new List<int>();
		for ( var tick = 0; tick < 240 * 60 && handed.Count < 3; tick++ )
		{
			runtime.Simulate( Tick );
			started |= bump.Phase == BumpController.PhaseRunning;
			if ( phases[^1] != bump.Phase )
				phases.Add( bump.Phase );
			if ( runtime.GetVariableByName( "VAR_LETMEOFF" ) is var left and not 0 )
			{
				handed.Add( left );
				runtime.SetVariable( "VAR_LETMEOFF", 0 );
			}
		}
		Assert.IsTrue( started, "the script started the race" );
		CollectionAssert.AreEqual( new[] { BumpController.PhaseLoading, BumpController.PhaseRunning, BumpController.PhaseOver, BumpController.PhaseLoading }, phases, "loading, running, race over, loading again" );
		CollectionAssert.AreEquivalent( new[] { 41, 42, 43 }, handed, "BUMP 2 handed every guest back through VAR_LETMEOFF" );
		Assert.AreEqual( 0, bump.VehicleCount );
		Assert.AreEqual( 3, bump.CompletedRuns );
		Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_RUNNING ), "the script's cycle ended" );
		Assert.AreEqual( 0, runtime.Script!.UnimplementedEffects.GetValueOrDefault( Opcode.BUMP ), "every BUMP command reached the controller: " + string.Join( ", ", runtime.Script.UnimplementedEffects.Select( pair => $"{pair.Key}x{pair.Value}" ) ) );
		Assert.IsTrue( effects.Any( effect => effect.Kind == BumpEffectKind.EngineParameter ), "the engines ran" );
		Assert.AreEqual( 3, effects.Count( effect => effect.Kind == BumpEffectKind.EngineStart ) );
		Assert.AreEqual( 3, effects.Count( effect => effect.Kind == BumpEffectKind.EngineStop ) );
		Assert.IsTrue( effects.Count( effect => effect.Kind == BumpEffectKind.Particle && effect.Id == BumpEffects.ParticleBeamUpCar ) >= 6, "particle 15 at every spawn and unload" );
		// a second cycle with a breakdown during the race: the run loop's subroutine reports VAR_BREAKSTAT to BUMP 8, the karts coast, then the repair
		runtime.SetVariable( nameof( RideVariables.VAR_LETMEON ), 44 );
		for ( var tick = 0; tick < 30 * 60 && bump.Phase != BumpController.PhaseRunning; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.PhaseRunning, bump.Phase, "the second race started" );
		for ( var tick = 0; tick < 3 * 60; tick++ )
			runtime.Simulate( Tick );
		runtime.SetVariable( "VAR_BREAKSTAT", 1 );
		for ( var tick = 0; tick < 5 * 60 && bump.State != BumpController.StateBroken; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.StateBroken, bump.State );
		runtime.SetVariable( "VAR_BREAKSTAT", 0 );
		for ( var tick = 0; tick < 5 * 60 && bump.State != BumpController.StateFine; tick++ )
			runtime.Simulate( Tick );
		Assert.AreEqual( BumpController.StateFine, bump.State );
		runtime.Stop();
		GC.KeepAlive( rig );
	}

	private static object BuildCircuit( BumpController bump, TrackFamily family = TrackFamily.Water )
	{
		var cells = new ParkCellMap( 40, 40 );
		var builder = new TrackBuilder( cells, family, () => null, new FlatGrid() );
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
