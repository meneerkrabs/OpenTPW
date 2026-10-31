using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The four original tour rides on the PC data (inconclusive without OPENTPW_GAME_PATH): each theme's real tour script drives its flying-car controller, with
/// guests queueing, boarding, flying and getting off (docs/TOURS.md).
/// </summary>
[TestClass]
public class TourAssetTests
{
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private static readonly (string Theme, int InfoId, string Script)[] Tours =
	{
		("jungle", 1170, "TourRide.RSE"), ("hallow", 2170, "TourRide.RSE"), ("space", 3170, "scitour.RSE"), ("fantasy", 4170, "twetours.RSE")
	};

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

	/// <summary>Every TOUR instruction of a script in program order as <c>command:parameter</c> (a variable parameter by its name).</summary>
	private static string TourSequence( ObjectCatalogEntry entry )
	{
		using var stream = entry.FileSystem.OpenRead( entry.ScriptPath! );
		var file = new RideScriptFile( stream );
		return string.Join( " ", file.Instructions.Where( instruction => instruction.Opcode == (ushort)Opcode.TOUR )
			.Select( instruction => $"{instruction.Operands[0].Value}:{(instruction.Operands[1].Kind == RideScriptOperandKind.Variable ? file.VariableNames[instruction.Operands[1].Value] : instruction.Operands[1].Value.ToString())}" ) );
	}

	/// <summary>The four tour scripts issue exactly the traced TOUR commands, none of them the refused ones (6, 7, 13).</summary>
	[TestMethod]
	public void TheFourTourScriptsIssueTheTracedTourSequences()
	{
		var expected = new Dictionary<string, string>
		{
			["jungle"] = "1:0 12:1 9:1 14:0 14:1 5:VAR_TEMP 16:VAR_RUNNING 14:0 14:1 9:3 15:0 9:4 9:2 9:1 10:0 4:VAR_LETMEOFF 3:VAR_LETMEON 11:0 8:VAR_DURATION",
			["hallow"] = "1:0 12:1 9:1 14:1 17:2048 5:VAR_TEMP 16:VAR_RUNNING 14:0 14:1 9:3 15:0 9:4 9:2 9:1 10:0 4:VAR_LETMEOFF 3:VAR_LETMEON 11:0 8:VAR_DURATION",
			["space"] = "1:0 5:3 12:1 9:1 14:0 14:1 5:VAR_TEMP 16:VAR_RUNNING 14:0 14:1 9:3 15:0 9:4 9:2 9:1 10:0 4:VAR_TEMP 3:VAR_PEEPID 11:0 8:VAR_DURATION",
			["fantasy"] = "1:0 12:1 9:1 14:1 5:VAR_TEMP 16:VAR_RUNNING 14:0 14:1 9:3 15:0 9:4 9:2 9:1 10:0 4:VAR_TEMP 3:VAR_PEEPID 11:0 8:VAR_DURATION"
		};
		foreach ( var (theme, infoId, _) in Tours )
		{
			var entry = ObjectCatalog.Load( theme ).Get( infoId );
			var sequence = TourSequence( entry );
			Assert.AreEqual( expected[theme], sequence, $"{theme}: {entry.DisplayName}" );
			Assert.IsFalse( sequence.Split( ' ' ).Any( word => word.StartsWith( "6:" ) || word.StartsWith( "7:" ) || word.StartsWith( "13:" ) ), "the refused commands are never issued" );
			Assert.AreEqual( 0, entry.Settings.GetInt( "Bumper.WhichTrackType" ), "a tour ride has no track type: nothing to lay" );
			Assert.AreEqual( 1, entry.Settings.GetInt( "Info.RunsContinuously" ), "guests may board while a car flies" );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			Assert.IsTrue( model.DummyAttributes.Any( attribute => (attribute.Type & 0x800) != 0 && attribute.Value == 99 ), $"{theme}: the launch pad node (class 0x800, number 99)" );
		}
	}

	/// <summary>
	/// TOUR-003: a tour script runs TOUR 1 (the launch pad read: node lookup 0x0044B220 then position 0x004386D0) before it triggers any clip, so the live node instance
	/// it reads is the model's rest pose, which the port reads.
	/// </summary>
	[TestMethod]
	public void TheLaunchPadIsReadBeforeAnyClipPlays()
	{
		var triggers = new[] { Opcode.TRIGANIM, Opcode.LOOPANIM, Opcode.TRIGWAITANIM, Opcode.TRIGANIMSPEED, Opcode.TRIGANIM_CH, Opcode.LOOPANIM_CH, Opcode.TRIGWAITANIM_CH }.Select( x => (ushort)x ).ToHashSet();
		foreach ( var (theme, infoId, _) in Tours )
		{
			var entry = ObjectCatalog.Load( theme ).Get( infoId );
			using var stream = entry.FileSystem.OpenRead( entry.ScriptPath! );
			var file = new RideScriptFile( stream );
			var before = file.Instructions.TakeWhile( instruction => !(instruction.Opcode == (ushort)Opcode.TOUR && instruction.Operands[0].Value == 1) ).ToList();
			Assert.IsTrue( before.Count < file.Instructions.Count, $"{theme}: the script issues TOUR 1" );
			Assert.IsFalse( before.Any( instruction => triggers.Contains( instruction.Opcode ) ), $"{theme}: no clip is triggered before TOUR 1" );
		}
	}

	/// <summary>
	/// TOUR-007: the Star and Tweety Tours walk their riders off between two slot nodes of the ride model (WALKON's third and fourth node operands, class 0x800), which lie 10 model
	/// units apart, so the original's <c>ftol(distance) x 100</c> ms (0x005571A0) is the one second the port always used; no clip moves those nodes.
	/// </summary>
	[TestMethod]
	public void TheTourWalkOffsSpanTheModelsSlotNodes()
	{
		foreach ( var (theme, infoId, _) in Tours.Where( tour => tour.Theme is "space" or "fantasy" ) )
		{
			var entry = ObjectCatalog.Load( theme ).Get( infoId );
			using var stream = entry.FileSystem.OpenRead( entry.ScriptPath! );
			var walkOn = new RideScriptFile( stream ).Instructions.Single( instruction => instruction.Opcode == (ushort)Opcode.WALKON );
			var (from, to, action) = ((int)walkOn.Operands[3].Value, (int)walkOn.Operands[4].Value, (int)walkOn.Operands[5].Value);
			var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), 1 );
			var distance = runtime.WalkNodeDistance( action, from, to );
			Assert.IsNotNull( distance, $"{theme}: the slot nodes {from} and {to} exist" );
			Assert.AreEqual( 1000, RideVisitorBridge.WalkOffDuration( distance!.Value ), $"{theme}: distance {distance}" );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			// the nodes a clip moves (its tracks; a clip's node list only toggles a mesh) and their descendants
			var moved = new HashSet<int>( entry.Animations.SelectMany( file => ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip?.Tracks.Select( track => (int)track.NodeIndex ) ?? Enumerable.Empty<int>() ) );
			foreach ( var number in new[] { from, to } )
			{
				var index = model.DummyAttributes.ToList().FindIndex( attribute => (attribute.Type & 0x800) != 0 && attribute.Value == number );
				for ( var node = model.Counts.Unknown46 + index; node >= 0; node = model.Nodes[node].ParentIndex )
					Assert.IsFalse( moved.Contains( node ), $"{theme}: slot node {number} (or an ancestor) is never moved by a clip" );
			}
			runtime.Stop();
		}
	}

	/// <summary>The ride's flying-car mesh is the supplemental mesh 0 and carries the rider seats (head dummies numbered from 1, one more than the script's per-car load).</summary>
	[TestMethod]
	public void TheCarMeshesCarryTheRiderSeats()
	{
		var seats = new Dictionary<string, int> { ["jungle"] = 9, ["hallow"] = 9, ["space"] = 8, ["fantasy"] = 9 };
		foreach ( var (theme, infoId, _) in Tours )
		{
			var entry = ObjectCatalog.Load( theme ).Get( infoId );
			var name = entry.Settings["SupplementalMeshes[0].FileName"]!.Trim( '"', ' ' );
			var path = entry.FileSystem.GetFiles( entry.ArchivePath ).First( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) );
			var model = ObjectAssets.LoadModel( entry.FileSystem, path );
			Assert.AreEqual( ModelFileKind.Geometry, model.Kind, name );
			var count = 0;
			while ( RideHeadSpace.NodeForHead( model, count + 1 ) != RideHeadSpace.NoRecord )
				count++;
			Assert.IsTrue( count >= seats[theme], $"{theme}: {name} has {count} head nodes, a car carries {seats[theme]}" );
		}
	}

	/// <summary>
	/// The substep scheduler gives the tour cars their step on the even substeps only, after the script pass (tp.exe 0x54F5D7), once per 62 ms: about 16 steps a second, never in a tick that
	/// starts no even substep, and the renderers' interpolation time stays inside one step.
	/// </summary>
	[TestMethod]
	public void TheTourCarsStepOnEvenSubstepsOnly()
	{
		var entry = ObjectCatalog.Load( "jungle" ).Get( 1170 );
		var world = new RideScriptWorld( 3 );
		var runtime = new OriginalObjectRuntime( entry, world, seed: 5, open: true );
		long previous = 0;
		var steps = 0L;
		const int ticks = 20 * 60;
		for ( long tick = 0; tick < ticks; tick++ )
		{
			var substep = SimulationSubstep.ForTick( tick, running: true );
			if ( substep.Due )
				world.RunPass( SimulationSubstep.Milliseconds );
			runtime.Simulate( Tick, substep );
			runtime.Visitors.WalkUpdate(); // the frame's walk update (0x00557D80)
			if ( runtime.Tour is not { } tour )
				continue;
			// the controller is created by the script's TOUR 1 during a pass, and the step of that same substep may already follow
			if ( tour.TickCount != previous )
			{
				Assert.IsTrue( substep.TourDue, $"tick {tick}: a tour step outside an even substep" );
				steps += tour.TickCount - previous;
				previous = tour.TickCount;
			}
			Assert.IsTrue( tour.TickRemainder >= 0 && tour.TickRemainder < TourController.TickSeconds, $"tick {tick}: the interpolation time {tour.TickRemainder}" );
		}
		Assert.IsTrue( steps > 15 * 15, $"{steps} steps in 20 s, about 16 a second once the controller exists" );
		Assert.IsTrue( steps <= ticks * 1000L / (2 * 1860) + 1, "no more than one step per even substep" );
		runtime.Stop();
	}

	/// <summary>The TOUR commands of a running ride reach its controller: creation by TOUR 1 (and its first car), nothing unimplemented.</summary>
	[TestMethod]
	public void TheScriptsCreateTheirControllerAndOpenTheRide()
	{
		foreach ( var (theme, infoId, _) in Tours )
		{
			var entry = ObjectCatalog.Load( theme ).Get( infoId );
			var world = new RideScriptWorld( 3 );
			var runtime = new OriginalObjectRuntime( entry, world, seed: 5, open: true );
			Assert.IsTrue( runtime.UsesTour, theme );
			Assert.IsNull( runtime.Tour, "no controller before the script ran TOUR 1" );
			for ( var tick = 0; tick < 10 * 60; tick++ )
				runtime.Simulate( Tick );
				runtime.Visitors.WalkUpdate(); // the frame's walk update (0x00557D80)
			var tour = runtime.Tour;
			Assert.IsNotNull( tour, $"{theme}: TOUR 1 created the controller" );
			Assert.AreSame( tour, runtime.LiveTour );
			Assert.AreEqual( 1, tour.ApproachMode, "TOUR 12 1" );
			Assert.IsTrue( tour.CarCount >= 1, "the first car is created with the controller" );
			Assert.IsTrue( tour.TickCount > 0 );
			Assert.AreEqual( 0, runtime.Script!.UnimplementedEffects.GetValueOrDefault( Opcode.TOUR ), "every TOUR command reached the controller" );
			runtime.Stop();
			Assert.AreEqual( 0, world.Tours.Controllers.Count, "a removed ride leaves the module" );
			Assert.IsNull( runtime.LiveTour );
		}
	}

	/// <summary>
	/// Guests ride every theme's real tour script for ten minutes on that theme's park: the script waits for a ready car, loads the queue,
	/// departs, and unloads what lands. Boarded equals released, a full cycle ran, and after the ride closes nobody is left aboard or Using.
	/// </summary>
	[DataTestMethod]
	[DataRow( "jungle", 1170 )]
	[DataRow( "hallow", 2170 )]
	[DataRow( "space", 3170 )]
	[DataRow( "fantasy", 4170 )]
	public void SixteenGuestsRideTheRealTourScriptForTenMinutes( string theme, int infoId )
	{
		var park = OriginalPark.Load( theme, readShippedSave: false );
		var terrain = new OriginalParkGrid( park );
		var grid = GuestPathGrid.FromOriginal( park.Map, null );
		var guests = new GuestSimulation( grid, GuestSettings.Load( theme ), 7 ) { ArrivalsEnabled = false };
		var entry = ObjectCatalog.Load( theme ).Get( infoId );
		var scripts = new RideScriptWorld( 11 );
		var field = park.Heightfield;
		scripts.Tours.MapWidth = field.CellCountX;
		scripts.Tours.MapHeight = field.CellCountZ;
		scripts.Tours.CellHeight = ( x, y ) => field.GetCornerHeight( x, y );

		var site = FindSite( entry, terrain, grid );
		var runtime = new OriginalObjectRuntime( entry, scripts, seed: 5, open: true );
		var centre = ((site.X + 2) * 10, (site.Y + 2) * 10);
		runtime.SetTourStation( () => new TourStation( centre.Item1 * 300, (int)field.GetCornerHeight( site.X + 2, site.Y + 2 ) * 300, centre.Item2 * 300, 0 ) );
		Assert.IsTrue( Level.RegisterWithGuests( runtime, ObjectFootprint.GetAccessPoints( entry.Shape, site.X, site.Y, 0 ), guests ), "the ride is a guest attraction" );
		var entrance = runtime.Visitors.EntranceCell;
		var spawned = Enumerable.Range( 0, 16 ).Select( _ =>
		{
			var guest = guests.SpawnInPark( entrance.X, entrance.Y );
			guest.AttractionId = runtime.Visitors.AttractionId;
			guest.State = GuestState.GoingToRide;
			return guest;
		} ).ToArray();

		var peak = 0;
		for ( var tick = 0; tick < 10 * 60 * 60; tick++ )
		{
			guests.Tick( Tick );
			runtime.Simulate( Tick );
			runtime.Visitors.WalkUpdate(); // the frame's walk update (0x00557D80)
			peak = Math.Max( peak, runtime.Tour?.Riders ?? 0 );
		}
		var tour = runtime.Tour!;
		Assert.IsNotNull( tour );
		Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
		Assert.AreEqual( 0, runtime.Script.UnimplementedEffects.GetValueOrDefault( Opcode.TOUR ), "every TOUR command reached the controller" );
		Assert.IsTrue( runtime.Visitors.BoardedTotal >= 9, $"{theme}: boarded {runtime.Visitors.BoardedTotal}; guests {string.Join( ", ", spawned.Select( guest => guest.State ) )}" );
		Assert.IsTrue( tour.Departures >= 1 && tour.Released >= 1, $"{theme}: {tour.Departures} departures, {tour.Released} released" );
		Assert.IsTrue( peak >= 1 );

		// Closing ends the flights: every car lands and the script unloads it. One exception is the original's (TOUR-010): a car whose departure the script had ordered (TOUR 11
		// cleared the ready flag) stays docked with its riders aboard, TOUR 10 reads 0 and the script waits until the ride reopens. Deleting the ride still releases everybody.
		runtime.Close();
		for ( var tick = 0; tick < 6 * 60 * 60; tick++ )
		{
			guests.Tick( Tick );
			runtime.Simulate( Tick );
			runtime.Visitors.WalkUpdate(); // the frame's walk update (0x00557D80)
		}
		if ( tour.Riders == 0 )
		{
			Assert.AreEqual( runtime.Visitors.BoardedTotal, runtime.Visitors.ReleasedTotal, $"{theme}: boarded and released" );
			Assert.AreEqual( tour.Boarded, tour.Released, "the controller handed every boarded rider back" );
			Assert.AreEqual( 0, runtime.GetVariable( RideVariables.VAR_ONRIDE ) );
			Assert.IsFalse( guests.Guests.Any( guest => guest.State == GuestState.Using ), $"{theme}: nobody is stuck in Using" );
		}
		else
		{
			Assert.IsFalse( tour.Ready, "the stranded car is not ready" );
			Assert.IsTrue( tour.Cars.Any( car => car.State == TourCar.StateDocked && car.RiderCount < 0 ), "the riders wait aboard the docked car" );
			Assert.AreEqual( tour.Riders, tour.AllRiders().Count(), "the controller names every rider aboard" );
		}
		runtime.Stop();
		Assert.AreEqual( 0, scripts.Tours.Controllers.Count );
		for ( var tick = 0; tick < 60 * 60; tick++ )
			guests.Tick( Tick );
		Assert.AreEqual( runtime.Visitors.BoardedTotal, runtime.Visitors.ReleasedTotal, $"{theme}: deleting the ride released everybody" );
		Assert.IsFalse( guests.Guests.Any( guest => guest.State == GuestState.Using ), $"{theme}: nobody is stuck in Using after the ride was deleted" );
	}

	/// <summary>Every catalogue object ships its height map beside its mesh, as the ground lookup of the tour cars (TOUR-005) assumes, and every footprint cell lies inside it at every rotation.</summary>
	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "hallow" )]
	[DataRow( "space" )]
	[DataRow( "fantasy" )]
	public void EveryCatalogueObjectsFootprintLiesInsideItsShippedHeightMap( string theme )
	{
		var maps = new ObjectHeightMaps();
		var checkedObjects = 0;
		foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( item => !item.IsFixedItem ) )
		{
			var map = maps.MapOf( entry );
			Assert.IsNotNull( map, $"{theme} {entry.InfoId}: {entry.ModelPath} has a height map" );
			foreach ( var rotation in new[] { 0, 90, 180, 270 } )
			{
				var placement = new ObjectPlacement( 30, 40, rotation, System.Numerics.Vector2.Zero, 0 );
				var (originX, originZ) = ObjectHeightMaps.InstanceOrigin( placement );
				foreach ( var (x, y, _) in ObjectFootprint.GetCells( entry.Shape, placement.X, placement.Y, rotation ) )
					Assert.IsNotNull( ObjectHeightMaps.CellIndex( map!, rotation, originX, originZ, x, y ), $"{theme} {entry.InfoId} rotation {rotation}: footprint cell ({x}, {y}) lies inside the {map!.Width} x {map.Height} map" );
			}
			checkedObjects++;
		}
		Assert.IsTrue( checkedObjects > 20, $"{theme}: {checkedObjects} objects checked" );
	}

	/// <summary>The mesh loader (0x004629D0) rebuilds a map whose size differs from the object's shape; every shipped map matches its shape, so the rebuild never runs for the catalogue (TOUR-005).</summary>
	[DataTestMethod]
	[DataRow( "jungle" )]
	[DataRow( "hallow" )]
	[DataRow( "space" )]
	[DataRow( "fantasy" )]
	public void EveryShippedHeightMapHasTheSizeOfItsObjectsShape( string theme )
	{
		var maps = new ObjectHeightMaps();
		var mismatches = new List<string>();
		foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( item => !item.IsFixedItem ) )
		{
			var map = maps.MapOf( entry )!;
			if ( (map.Width, map.Height) != (entry.Shape.Width, entry.Shape.Height) )
				mismatches.Add( $"{entry.InfoId} map {map.Width}x{map.Height} shape {entry.Shape.Width}x{entry.Shape.Height}" );
		}
		Assert.AreEqual( 0, mismatches.Count, string.Join( "; ", mismatches ) );
	}

	/// <summary>A free 4 × 4 site on the park's land whose access cell is a path guests can walk, nearest the walkways.</summary>
	private static (int X, int Y) FindSite( ObjectCatalogEntry entry, OriginalParkGrid terrain, GuestPathGrid grid )
	{
		var walkway = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) ).Where( cell => grid.IsWalkable( cell.X, cell.Y ) ).ToArray();
		Assert.IsTrue( walkway.Length > 0 );
		bool Free( int x, int y ) => terrain.CheckTerrain( x, y ) == OriginalPlacementResult.Allowed && !grid.IsWalkable( x, y ) && !grid.IsQueue( x, y );
		foreach ( var candidate in Enumerable.Range( 0, terrain.Width * terrain.Height ).Select( index => (X: index % terrain.Width, Y: index / terrain.Width) )
			.OrderBy( cell => walkway.Min( path => Math.Abs( path.X - cell.X ) + Math.Abs( path.Y - cell.Y ) ) ).ThenBy( cell => cell.Y ).ThenBy( cell => cell.X ) )
		{
			var cells = ObjectFootprint.GetCells( entry.Shape, candidate.X, candidate.Y, 0 ).Select( cell => (cell.X, cell.Y) ).ToHashSet();
			if ( cells.Count > 0 && cells.All( cell => Free( cell.X, cell.Y ) ) && Level.ResolveVisitorCells( ObjectFootprint.GetAccessPoints( entry.Shape, candidate.X, candidate.Y, 0 ), grid ) != null )
				return candidate;
		}
		Assert.Fail( "no free site for the ride" );
		return default;
	}
}
