using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using static OpenTPW.Tests.PoolsSwimSessionTests;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The pool extras of the pools extension (docs/POOLS.md, "Extras"): the shipped objects, their placement rules and the activities swimmers do with them (ladder, diving board, shower, sun lounger)
/// against a real <see cref="GuestSimulation"/>, including a save in the middle of each.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolsExtrasTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "space", "fantasy" };
	private static readonly PoolExtraKind[] Kinds = { PoolExtraKind.Ladder, PoolExtraKind.DivingBoard, PoolExtraKind.Shower, PoolExtraKind.Lounger, PoolExtraKind.Lamp, PoolExtraKind.UnderwaterLight };
	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-extras-" ).FullName;
		previousFileSystem = FileSystem;
		PoolContent.DirectoryOverride = RepositoryFolder();
		var totem = Path.Combine( root, "game", "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( totem );
		File.WriteAllText( Path.Combine( totem, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t100\n" );
		File.WriteAllBytes( Path.Combine( totem, "totem.MD2" ), Array.Empty<byte>() );
		FileSystem = new BaseFileSystem( Path.Combine( root, "game" ) );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		PoolContent.DirectoryOverride = null;
		FileSystem = previousFileSystem!;
		Directory.Delete( root, true );
	}

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
		{
			var candidate = Path.Combine( directory.FullName, "content", "pools" );
			if ( File.Exists( Path.Combine( candidate, "objects.json" ) ) && Directory.Exists( Path.Combine( directory.FullName, "tools", "pools" ) ) )
				return candidate;
		}
		throw new FileNotFoundException( "content/pools not found above the test binaries." );
	}

	private static IDisposable Enabled( bool on, BaseFileSystem fileSystem ) => ModContentRoots.Use( PoolsTests.PoolsSet( on ), fileSystem );

	private static int IdOf( int theme, PoolExtraKind kind ) => 65000 + theme * 10 + (int)kind - 1;

	// ---- The objects -----------------------------------------------------------------------------------------------

	[TestMethod]
	public void EveryThemeListsSixExtrasWithTheirOwnIdsShapesAndNoGuestChoice()
	{
		using var scope = Enabled( true, FileSystem );
		using var extension = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		for ( var theme = 0; theme < Themes.Length; theme++ )
		{
			var catalog = ObjectCatalog.Load( Themes[theme] );
			foreach ( var kind in Kinds )
			{
				var entry = catalog.Get( IdOf( theme, kind ) );
				Assert.AreEqual( kind, PoolExtras.KindOf( entry ), $"{Themes[theme]} {kind}" );
				Assert.AreEqual( "opentpw.pools", entry.ModId );
				Assert.AreEqual( ObjectCategory.Feature, entry.Category );
				Assert.IsTrue( entry.IsBuildable && entry.WhichUIType == 3, "in the build menu's Features tab" );
				Assert.IsFalse( entry.IsChoosable || entry.HasQueue, "guests never choose an extra from the park" );
				Assert.IsFalse( entry.Shape.Entrances.Any() || entry.Shape.Exits.Any(), "nobody walks in: no entrance, no exit" );
				CollectionAssert.AreEqual( kind == PoolExtraKind.Lounger ? new[] { "**" } : new[] { "*" }, entry.Shape.Rows.ToArray() );
				Assert.IsTrue( entry.BuildCost is >= 30 and <= 250, $"{kind} costs {entry.BuildCost}" );
				Assert.IsNotNull( entry.ScriptPath );
			}
		}
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.AreEqual( PoolExtraKind.None, PoolExtras.KindOf( ObjectCatalog.Load( "jungle" ).Get( 65000 ) ), "with the extension off an extra is no extra" );
	}

	[TestMethod]
	public void EachExtrasModelAndTexturesLoadAndStandOnItsCells()
	{
		using var scope = Enabled( true, FileSystem );
		for ( var theme = 0; theme < Themes.Length; theme++ )
			foreach ( var kind in Kinds )
			{
				var name = $"{Themes[theme]} {kind}";
				var entry = ObjectCatalog.Load( Themes[theme] ).Get( IdOf( theme, kind ) );
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				Assert.AreEqual( ModelFileKind.Geometry, model.Kind );
				Assert.IsTrue( model.Meshes.Count >= 1, name );
				var textures = new HashSet<string>();
				foreach ( var mesh in model.Meshes )
				{
					Assert.IsNull( ObjectAssets.GetUnrenderableReason( mesh ), $"{name}: {mesh.Name}" );
					Assert.IsTrue( ObjectAssets.ConvertMesh( mesh ).Length > 0 );
					foreach ( var material in mesh.Materials )
						textures.Add( material.Name );
				}
				Assert.IsTrue( textures.Count >= 2 && textures.All( texture => texture.StartsWith( "ex_", StringComparison.Ordinal ) ), $"{name}: {string.Join( ", ", textures )}" );
				foreach ( var texture in textures )
				{
					var found = ObjectAssets.ResolveTexture( entry, texture );
					Assert.IsNotNull( found, $"{name}: {texture}" );
					var (data, width, height) = Texture.DecodeImage( found!.Value.FileSystem, found.Value.Path );
					Assert.IsTrue( width == 32 && height == 32 && data.Length == width * height * 4, $"{name}: {texture} is {width} x {height}" );
				}
				// the model's X spans the footprint; its Z (the cell's Y) runs from the back of the cell out over the water, up to 2.4 cells for the board; nothing lies behind the cell
				var width10 = entry.Shape.Width * 10f;
				var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
				Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= width10 + 0.01f && p.Z >= -0.01f && p.Z <= (kind == PoolExtraKind.DivingBoard ? 24.01f : 16f) && p.Y >= -0.01f && p.Y < 14f ), $"{name}: inside its cells and the overhang" );
				Assert.AreEqual( 0, RideHeadSpace.Create( model, seed: 1 )?.Count ?? 0, name );
			}
	}

	private static System.Numerics.Matrix4x4 WorldOf( ModelFile model, int node )
	{
		var world = System.Numerics.Matrix4x4.Identity;
		for ( var index = node; index >= 0; index = model.Nodes[index].ParentIndex )
			world *= model.Nodes[index].Transform;
		return world;
	}

	// ---- Placement --------------------------------------------------------------------------------------------------

	[TestMethod]
	public void ExtrasAreOnlyPlacedWhereTheirActivityCanWork()
	{
		var cells = new ParkCellMap( 30, 30 );
		var objects = new ParkObjects( ObjectCatalog.Load( "jungle" ), new PoolsTests.FlatGrid( 30, 30 ) );
		ObjectCatalogEntry Get( PoolExtraKind kind )
		{
			using var scope = Enabled( true, FileSystem );
			return ObjectCatalog.Load( "jungle" ).Get( IdOf( 0, kind ) );
		}
		var (ladder, board, shower, lounger, lamp, light) = (Get( PoolExtraKind.Ladder ), Get( PoolExtraKind.DivingBoard ), Get( PoolExtraKind.Shower ), Get( PoolExtraKind.Lounger ), Get( PoolExtraKind.Lamp ), Get( PoolExtraKind.UnderwaterLight ));
		objects.PlacementRule = ( item, x, y, rotation ) => PoolExtras.PlacementRule( item, cells, objects, x, y, rotation );
		using var extension = OpenTpwExtensions.Use( PoolsTests.PoolsSet( true ) );
		const int Refused = (int)OriginalPlacementResult.NeedsPoolEdge;
		int Check( ObjectCatalogEntry entry, int x, int y, int rotation ) => objects.Check( entry, x, y, rotation ) == OriginalPlacementResult.Allowed ? 0 : (int)objects.Check( entry, x, y, rotation );
		// a pool of 6 x 3 cells at x 10 to 15, y 10 to 12
		for ( var y = 10; y <= 12; y++ )
			for ( var x = 10; x <= 15; x++ )
				cells.SetType( x, y, ParkCellTypes.PoolWater );
		// rotation 90 turns the back (+Y of the model) to +X, 270 to -X, 0 to +Y, 180 to -Y
		Assert.AreEqual( 0, Check( ladder, 9, 11, 90 ), "a land cell on the straight west edge, facing east" );
		Assert.AreEqual( Refused, Check( ladder, 9, 11, 270 ), "facing away from the water" );
		Assert.AreEqual( Refused, Check( ladder, 9, 10, 90 ), "the water cell beyond is a corner of the pool: not a straight edge" );
		Assert.AreEqual( 0, Check( ladder, 12, 9, 0 ), "the north edge" );
		Assert.AreEqual( 0, Check( light, 9, 11, 90 ), "an underwater light needs the same straight edge" );
		Assert.AreEqual( Refused, Check( light, 9, 12, 90 ) );
		Assert.AreEqual( 0, Check( board, 9, 11, 90 ), "a board needs two straight cells of water out" );
		for ( var y = 10; y <= 12; y++ )
			cells.SetType( 11, y, (byte)0 );
		Assert.AreEqual( 0, Check( ladder, 9, 11, 90 ), "the first cell is still straight" );
		Assert.AreEqual( Refused, Check( board, 9, 11, 90 ), "no water two cells out: a short pool has no board" );
		for ( var y = 10; y <= 12; y++ )
			cells.SetType( 11, y, ParkCellTypes.PoolWater );
		Assert.AreEqual( 0, Check( lamp, 9, 11, 0 ), "a lamp needs water beside a cell" );
		Assert.AreEqual( Refused, Check( lamp, 9, 9, 0 ), "a diagonal neighbour does not count" );
		Assert.AreEqual( Refused, Check( lamp, 5, 5, 0 ), "no water beside it" );
		Assert.AreEqual( 0, Check( lounger, 9, 11, 90 ), "a lounger with a seat beside the water" );
		Assert.AreEqual( Refused, Check( lounger, 4, 4, 0 ), "no water" );
		// a shower needs a changing room beside it
		Assert.AreEqual( Refused, Check( shower, 20, 20, 0 ), "no room beside it" );
		// any other object is not asked, and with the extension off the rule has no opinion
		Assert.IsNull( PoolExtras.PlacementRule( ObjectCatalog.Load( "jungle" ).Entries.First( item => PoolExtras.KindOf( item ) == PoolExtraKind.None ), cells, objects, 5, 5, 0 ) );
		using ( OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) ) )
			Assert.IsNull( PoolExtras.PlacementRule( ladder, cells, objects, 5, 5, 0 ), "with the extension off the rule has no opinion" );
	}

	[TestMethod]
	public void TheWaterBesideAnExtraCannotBeErased()
	{
		var sites = new[]
		{
			new PoolExtraSite( 1, PoolExtraKind.Ladder, 90, new[] { (9, 11) } ),
			new PoolExtraSite( 2, PoolExtraKind.DivingBoard, 0, new[] { (20, 9) } ),
			new PoolExtraSite( 3, PoolExtraKind.Shower, 0, new[] { (3, 3) } )
		};
		Assert.IsTrue( PoolExtras.NeedsWater( sites, 10, 11 ), "the water the ladder stands beside stays" );
		Assert.IsFalse( PoolExtras.NeedsWater( sites, 11, 11 ), "the rest of the pool can go" );
		Assert.IsTrue( PoolExtras.NeedsWater( sites, 20, 10 ) && PoolExtras.NeedsWater( sites, 20, 11 ), "a board holds its two cells out" );
		Assert.IsFalse( PoolExtras.NeedsWater( sites, 3, 4 ), "a shower holds no water (its changing room does)" );
	}

	// ---- The activities ---------------------------------------------------------------------------------------------

	/// <summary>The swim rig (pool x 5..10, y 3..8, access (5,3)) with the four activities registered and extras placed on its west side: land cells at x = 4 facing east.</summary>
	private static Rig ExtrasRig( params PoolExtraSite[] sites )
	{
		var rig = new Rig( capacity: 3 );
		foreach ( var activity in PoolActivityRegistry.Create() )
			rig.Simulation.RegisterPoolActivity( activity );
		rig.Simulation.PoolExtraSites = () => sites;
		return rig;
	}

	private static PoolExtraSite Site( int id, PoolExtraKind kind, int rotation, params (int X, int Y)[] cells ) => new( id, kind, rotation, cells );

	/// <summary>Runs turn by turn until the guest has been in an activity with the id, noting the steps it went through, the cells it stood in and its slots.</summary>
	private static (List<int> Steps, List<(int X, int Y)> Cells, List<int> Slots, int Turns) Trace( Rig rig, Guest guest, string id, int limit = 3000 )
	{
		var steps = new List<int>();
		var cells = new List<(int X, int Y)>();
		var slots = new List<int>();
		var turns = 0;
		var seen = false;
		rig.Until( () =>
		{
			if ( guest.State == GuestState.PoolActivity && guest.PoolActivityId == id )
			{
				seen = true;
				turns++;
				var step = guest.PoolActivityPhase & 15;
				if ( steps.Count == 0 || steps[^1] != step )
					steps.Add( step );
				cells.Add( guest.Navigation.Cell );
				slots.Add( rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) );
				return false;
			}
			return seen;
		}, limit );
		return (steps, cells, slots, turns);
	}

	[TestMethod]
	public void ASwimmerClimbsOutByTheLadderStandsAndJumpsBackIn()
	{
		var rig = ExtrasRig( Site( 301, PoolExtraKind.Ladder, 90, (4, 5) ) );
		var guest = rig.Send();
		var happiness = 0f;
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		happiness = guest.Happiness;
		var (steps, cells, slots, turns) = Trace( rig, guest, "extra.ladder" );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5 }, steps, "swim to the edge, climb, stand, walk to the rim, jump, splash" );
		Assert.IsTrue( cells.Contains( (4, 5) ), "it stood on the ladder's deck cell" );
		CollectionAssert.AreEqual( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.LadderClimb, PoolSwimSlots.Stand, PoolSwimSlots.Walk, PoolSwimSlots.Dive, PoolSwimSlots.SplashDown }, slots.Distinct().ToArray() );
		Assert.AreEqual( GuestState.Swimming, guest.State, "back in the water" );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
		Assert.IsTrue( guest.Happiness >= happiness + PoolExtraSettings.LadderHappiness - 0.01f || guest.Happiness >= 99.99f, $"happiness {happiness} to {guest.Happiness}" );
		Assert.IsTrue( turns is > 20 and < 120, $"{turns} turns" );
	}

	[TestMethod]
	public void ADiverClimbsTheBoardWalksOutAndDivesIntoTheWaterTwoCellsOut()
	{
		var rig = ExtrasRig( Site( 302, PoolExtraKind.DivingBoard, 90, (4, 5) ) );
		var guest = rig.Send();
		var heights = new List<float>();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		var board = rig.Simulation.PoolActivities.Single( activity => activity.Id == "extra.board" );
		var (steps, cells, slots, _) = Trace( rig, guest, "extra.board", limit: 3000 );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5 }, steps );
		Assert.IsTrue( cells.Contains( (4, 5) ) && cells.Contains( (6, 5) ), "from the tower over the water to the landing" );
		CollectionAssert.AreEqual( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.LadderClimb, PoolSwimSlots.Walk, PoolSwimSlots.Stand, PoolSwimSlots.Dive, PoolSwimSlots.SplashDown }, slots.Distinct().ToArray() );
		Assert.AreEqual( GuestState.Swimming, guest.State );
		// the drawn height: raised on the steps and the board, falling with the dive, the default elsewhere
		(guest.State, guest.PoolActivityId, guest.PoolActivityPhase, guest.PoolActivityTurns) = (GuestState.PoolActivity, "extra.board", 2, 0);
		Assert.AreEqual( 3f + PoolExtraSettings.BoardLift, rig.Simulation.PoolFeetHeight( guest, 3f ), 0.001f, "on the board" );
		guest.PoolActivityPhase = 4;
		guest.PoolActivityTurns = 3;
		Assert.AreEqual( 3f, rig.Simulation.PoolFeetHeight( guest, 3f ), 0.001f, "at the end of the dive" );
		guest.PoolActivityPhase = 5;
		Assert.AreEqual( 3f, rig.Simulation.PoolFeetHeight( guest, 3f ), "the splash is at the water" );
		Assert.IsNotNull( board );
	}

	[TestMethod]
	public void AGuestShowersFirstThenSwimsAndTheShowerLastsAFewSeconds()
	{
		var rig = ExtrasRig( Site( 303, PoolExtraKind.Shower, 0, (4, 2) ) );
		var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
		var steps = new Dictionary<int, List<int>>();
		var showerTurns = new Dictionary<int, int>();
		var swum = new HashSet<int>();
		rig.Until( () =>
		{
			foreach ( var guest in guests )
			{
				if ( guest.InSwimSession )
					swum.Add( guest.Id );
				if ( guest.State != GuestState.PoolActivity || guest.PoolActivityId != "extra.shower" )
					continue;
				var list = steps.TryGetValue( guest.Id, out var known ) ? known : steps[guest.Id] = new List<int>();
				var step = guest.PoolActivityPhase & 15;
				if ( list.Count == 0 || list[^1] != step )
					list.Add( step );
				if ( rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) == PoolSwimSlots.Shower )
				{
					showerTurns[guest.Id] = showerTurns.GetValueOrDefault( guest.Id ) + 1;
					Assert.AreEqual( (4, 2), guest.Navigation.Cell, "under the shower" );
				}
			}
			return swum.Count == guests.Count && guests.All( guest => !guest.InSwimSession );
		}, 5000 );
		Assert.IsTrue( steps.Count >= 1, "at least one of three guests showered (60 percent each)" );
		foreach ( var (id, list) in steps )
		{
			CollectionAssert.AreEqual( new[] { 0, 1, 2 }, list, "walk there, shower, walk back" );
			Assert.IsTrue( showerTurns[id] is >= PoolExtraSettings.ShowerTurns - 1 and <= PoolExtraSettings.ShowerTurns + PoolExtraSettings.ShowerExtraTurns, $"{showerTurns[id]} turns of showering" );
		}
		Assert.IsTrue( rig.Simulation.Guests.All( guest => guest.PoolActivityId == null ) );
	}

	[TestMethod]
	public void ALoungerHasTwoSeatsAndAGuestLiesOnOneThenGoesBackInOrEndsItsSession()
	{
		var rig = ExtrasRig( Site( 304, PoolExtraKind.Lounger, 90, (4, 5), (4, 6) ) );
		var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
		var seatsAtOnce = 0;
		var lying = new HashSet<int>();
		var seatCells = new HashSet<(int X, int Y)>();
		var swum = new HashSet<int>();
		rig.Until( () =>
		{
			foreach ( var guest in guests.Where( guest => guest.InSwimSession ) )
				swum.Add( guest.Id );
			var onLounger = rig.Simulation.Guests.Where( guest => guest.PoolActivityId == "extra.lounger" ).ToList();
			Assert.IsTrue( onLounger.Count <= 2, "two seats" );
			Assert.AreEqual( onLounger.Count, onLounger.Select( guest => (guest.PoolActivityX, guest.PoolActivityY) ).Distinct().Count(), "no two guests share a seat" );
			seatsAtOnce = Math.Max( seatsAtOnce, onLounger.Count );
			foreach ( var guest in onLounger.Where( guest => rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) == PoolSwimSlots.Lying ) )
			{
				lying.Add( guest.Id );
				seatCells.Add( guest.Navigation.Cell );
			}
			return swum.Count == guests.Count && guests.All( guest => !guest.InSwimSession );
		}, 6000 );
		Assert.IsTrue( lying.Count >= 1, "somebody lay down" );
		Assert.IsTrue( seatCells.All( cell => cell is (4, 5) or (4, 6) ), $"on a seat: {string.Join( ", ", seatCells )}" );
	}

	[TestMethod]
	public void WithoutAnExtraPlacedTheActivitiesDrawNothing()
	{
		var withRegistry = ExtrasRig();
		var without = new Rig( capacity: 3 );
		var a = withRegistry.Send();
		var b = without.Send();
		for ( var turn = 0; turn < 600; turn++ )
		{
			withRegistry.Turns( 1 );
			without.Turns( 1 );
			Assert.AreEqual( b.SwimRandom, a.SwimRandom, $"turn {turn}: no extra, no draw" );
			Assert.AreEqual( (b.State, b.Navigation.X, b.Navigation.Y), (a.State, a.Navigation.X, a.Navigation.Y) );
		}
	}

	[TestMethod]
	public void ASaveInTheMiddleOfEachExtrasActivityRestoresAndContinues()
	{
		foreach ( var (id, site, stage) in new (string, PoolExtraSite, int)[]
		{
			("extra.ladder", Site( 301, PoolExtraKind.Ladder, 90, (4, 5) ), 2),
			("extra.board", Site( 302, PoolExtraKind.DivingBoard, 90, (4, 5) ), 3),
			("extra.lounger", Site( 304, PoolExtraKind.Lounger, 90, (4, 5), (4, 6) ), 2)
		} )
		{
			var rig = ExtrasRig( site );
			var guest = rig.Send();
			rig.Until( () => guest.PoolActivityId == id && (guest.PoolActivityPhase & 15) == stage && guest.PoolActivityTurns >= 2, 4000 );
			var snapshot = ParkResumeJson.Deserialize<GuestSimulationSnapshot>( ParkResumeJson.Serialize( rig.Simulation.CaptureSnapshot() ) );
			Assert.IsTrue( rig.Simulation.ValidateSnapshot( snapshot ).IsValid, string.Join( "; ", rig.Simulation.ValidateSnapshot( snapshot ).Errors ) );
			var copy = new Rig( capacity: 3 );
			foreach ( var activity in PoolActivityRegistry.Create() )
				copy.Simulation.RegisterPoolActivity( activity );
			copy.Simulation.PoolExtraSites = () => new[] { site };
			copy.Simulation.RestoreSnapshot( snapshot );
			var twin = copy.Simulation.Guests.Single();
			Assert.AreEqual( (id, guest.PoolActivityPhase, guest.PoolActivityTurns, guest.PoolActivityX, guest.PoolActivityY), (twin.PoolActivityId, twin.PoolActivityPhase, twin.PoolActivityTurns, twin.PoolActivityX, twin.PoolActivityY) );
			for ( var tick = 0; tick < 90 * 60 && (guest.InPoolActivity || twin.InPoolActivity); tick++ )
			{
				rig.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
				copy.Simulation.Tick( 1f / FixedStepClock.TicksPerSecond );
				Assert.AreEqual( (guest.State, guest.PoolActivityPhase, guest.Navigation.X, guest.Navigation.Y, guest.SwimRandom), (twin.State, twin.PoolActivityPhase, twin.Navigation.X, twin.Navigation.Y, twin.SwimRandom), $"{id} tick {tick}" );
			}
			Assert.IsFalse( twin.InPoolActivity, id );
		}
	}
}
