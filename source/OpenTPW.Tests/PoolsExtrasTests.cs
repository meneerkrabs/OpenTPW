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
				Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= width10 + 0.01f && p.Z >= -0.01f && p.Z <= (kind == PoolExtraKind.DivingBoard ? 24.01f : 16f) && p.Y >= (kind == PoolExtraKind.Ladder ? -4.5f : -0.01f) && p.Y < 14f ), $"{name}: inside its cells and the overhang" );
				Assert.AreEqual( 0, RideHeadSpace.Create( model, seed: 1 )?.Count ?? 0, name );
			}
	}

	[TestMethod]
	public void TheLadderModelsRungsAreAtTheFootPointWhereTheGuestClimbs()
	{
		// PoolExtraSettings.LadderFootDistance is derived from this: 10 model units a cell, the model's Z the depth of the cell (0 the back, 10 the cell's edge, the wall at 12.5), Y up (the deck 0, the water 0.6 below)
		using var scope = Enabled( true, FileSystem );
		for ( var theme = 0; theme < Themes.Length; theme++ )
		{
			var entry = ObjectCatalog.Load( Themes[theme] ).Get( IdOf( theme, PoolExtraKind.Ladder ) );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
			var footZ = PoolExtraSettings.LadderFootDistance / (float)Fixed16.One * 10f + 5f;
			// the part in the water: below the deck, at the wall
			var inWater = composed.Where( p => p.Y < -0.7f && p.Z > 12.55f ).ToArray();
			Assert.IsTrue( inWater.Length > 0, $"{Themes[theme]}: the rails go down into the water" );
			Assert.AreEqual( footZ, (inWater.Min( p => p.Z ) + inWater.Max( p => p.Z )) / 2f, 0.15f, $"{Themes[theme]}: the rails and rungs hang at the foot point ({footZ} model units from the back of the cell)" );
			Assert.IsTrue( inWater.Min( p => p.Z ) > 12.5f, $"{Themes[theme]}: off the wall, not in it" );
			Assert.IsTrue( composed.Min( p => p.Y ) < -4f && composed.Min( p => p.Y ) > -4.5f, $"{Themes[theme]}: the rails reach about 4 model units under the deck, 3.6 under the water" );
			// the rungs: horizontal bars between the rails at the foot, every 1.7 units from above the deck down below the water
			// a rung's ends (X 2.75 and 7.25) lie inside the rails, whose own vertices are at X 2.7 and 3.1 (and mirrored)
			var ys = composed.Where( p => MathF.Abs( MathF.Abs( p.X - 5f ) - 2.25f ) < 0.03f && MathF.Abs( p.Z - footZ ) < 0.2f ).Select( p => p.Y ).OrderBy( y => y ).ToList();
			var rungs = new List<float>();
			for ( var i = 0; i < ys.Count; )
			{
				var j = i;
				while ( j + 1 < ys.Count && ys[j + 1] - ys[j] < 0.5f )
					j++;
				rungs.Add( (ys[i] + ys[j]) / 2f );
				i = j + 1;
			}
			Assert.AreEqual( 5, rungs.Count, $"{Themes[theme]}: five rungs at the foot point ({string.Join( ", ", rungs )})" );
			for ( var i = 1; i < rungs.Count; i++ )
				Assert.AreEqual( 1.7f, rungs[i] - rungs[i - 1], 0.05f, $"{Themes[theme]}: a rung every 1.7 model units (0.34 engine units, a quarter of a kid's height)" );
			Assert.IsTrue( rungs[^1] > 3f && rungs[0] < -3f && rungs.Any( y => y > -0.6f && y < 0.6f ), $"{Themes[theme]}: from above the deck to under the water, one at the water line" );
			Assert.IsTrue( composed.All( p => p.X >= -0.01f && p.X <= 10.01f && p.Z <= 13.1f ), $"{Themes[theme]}: nothing reaches further than the rails" );
			// the guest's feet (water to deck, 0.6 units) are at the rungs: the climbing guest is on the rail line
			Assert.IsTrue( PoolExtraSettings.LadderFootDistance - PoolExtraSettings.RimDistance < Fixed16.One / 3 && footZ > 12.5f, "the foot is within a third of a cell of the rim, off the wall" );
		}
	}

	[TestMethod]
	public void TheLoungersRaisedHeadEndIsAtTheFrontAwayFromItsBack()
	{
		// LoungerActivity.LieDirection lays a guest with its feet towards the lounger's back (PoolExtras.WaterDirection of its rotation, the model's +Z) and its head on the raised end: that end must be at the front
		using var scope = Enabled( true, FileSystem );
		for ( var theme = 0; theme < Themes.Length; theme++ )
		{
			var entry = ObjectCatalog.Load( Themes[theme] ).Get( IdOf( theme, PoolExtraKind.Lounger ) );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			var composed = model.Meshes.SelectMany( mesh => mesh.Vertices.Select( vertex => System.Numerics.Vector3.Transform( vertex.Position.GetSystemVector3(), WorldOf( model, mesh.NodeIndex ) ) ) ).ToArray();
			// the cushion's top is 3.4 model units above the deck; the raised head end rises to 5.8
			var raised = composed.Where( p => p.Y > 4f ).ToArray();
			Assert.IsTrue( raised.Length > 0, $"{Themes[theme]}: the lounger has a raised head end" );
			Assert.IsTrue( raised.All( p => p.Z < 3.5f ), $"{Themes[theme]}: the raised end is at the front of the cell (Z {raised.Min( p => p.Z )} to {raised.Max( p => p.Z )}), the foot end towards the back" );
			Assert.IsTrue( raised.Any( p => p.X < 10f ) && raised.Any( p => p.X > 10f ), $"{Themes[theme]}: one for each seat (the two cells along the model's X)" );
			Assert.IsTrue( composed.Where( p => p.Y > 3.3f && p.Y < 3.5f ).Max( p => p.Z ) > 7f, $"{Themes[theme]}: the cushion runs to the back of the cell" );
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
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5, 6 }, steps, "swim to the foot, climb on the spot, step onto the deck, stand, walk to the rim, jump, splash" );
		Assert.IsTrue( cells.Contains( (4, 5) ), "it stood on the ladder's deck cell" );
		CollectionAssert.AreEqual( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.LadderClimb, PoolSwimSlots.Walk, PoolSwimSlots.Stand, PoolSwimSlots.Dive, PoolSwimSlots.SplashDown }, slots.Distinct().ToArray() );
		Assert.AreEqual( GuestState.Swimming, guest.State, "back in the water" );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
		Assert.IsTrue( guest.Happiness >= happiness + PoolExtraSettings.LadderHappiness - 0.01f || guest.Happiness >= 99.99f, $"happiness {happiness} to {guest.Happiness}" );
		Assert.IsTrue( turns is > 20 and < 140, $"{turns} turns" );
	}

	/// <summary>Runs one activity of a guest turn by turn and notes, for every turn spent in the step, the guest's point, heading and drawn feet (water 2, deck 3.5).</summary>
	private static (List<(int X, int Y, float Feet, float HeadingX)> Climb, List<(int X, int Y)> Step, (int X, int Y) Foot, bool Done) ClimbTrace( Rig rig, Guest guest, string id, int climbStep, int afterStep )
	{
		var heights = new PoolFeetHeights( 3f, 2f, 3.5f );
		var climb = new List<(int X, int Y, float Feet, float HeadingX)>();
		var stepOver = new List<(int X, int Y)>();
		var foot = (0, 0);
		var seen = false;
		rig.Until( () =>
		{
			if ( guest.State == GuestState.PoolActivity && guest.PoolActivityId == id )
			{
				seen = true;
				var step = guest.PoolActivityPhase & 15;
				var feet = rig.Simulation.PoolActivityOf( guest )!.FeetHeight( rig.Simulation, guest, heights ) ?? heights.Default;
				if ( step == climbStep )
					climb.Add( (guest.Navigation.X, guest.Navigation.Y, feet, guest.HeadingX) );
				else if ( step == afterStep )
					stepOver.Add( (guest.Navigation.X, guest.Navigation.Y) );
				else if ( step < climbStep )
					foot = (guest.Navigation.X, guest.Navigation.Y);
				return false;
			}
			return seen;
		}, 4000 );
		return (climb, stepOver, foot, seen && guest.State == GuestState.Swimming);
	}

	[TestMethod]
	public void ALadderIsClimbedOnTheSpotWithTheFeetRisingFromTheWaterToTheDeck()
	{
		const int One = 65536;
		var rig = ExtrasRig( Site( 301, PoolExtraKind.Ladder, 90, (4, 5) ) );
		var guest = rig.Send();
		var (climb, stepOver, _, done) = ClimbTrace( rig, guest, "extra.ladder", 1, 2 );
		var foot = (climb[0].X, climb[0].Y);
		var expectedFoot = (4 * One + One / 2 + PoolExtraSettings.LadderFootDistance, 5 * One + One / 2);
		Assert.AreEqual( expectedFoot, foot, "it swam to the foot of the ladder: just off the rim, in front of the rungs" );
		Assert.IsTrue( PoolExtraSettings.LadderFootDistance - PoolExtraSettings.RimDistance < One / 3, "x" );
		Assert.AreEqual( PoolExtraSettings.ClimbTurns, climb.Count, "the climb takes its turns" );
		Assert.IsTrue( climb.All( point => (point.X, point.Y) == expectedFoot ), "no horizontal travel while climbing" );
		Assert.IsTrue( climb.All( point => point.HeadingX < -0.99f ), "the guest faces the ladder (the land, to the west)" );
		for ( var i = 1; i < climb.Count; i++ )
			Assert.IsTrue( climb[i].Feet > climb[i - 1].Feet, $"the feet rise every turn ({climb[i - 1].Feet} to {climb[i].Feet})" );
		Assert.IsTrue( climb[0].Feet >= 2f && climb[0].Feet < 2.3f && climb[^1].Feet <= 3.5f && climb[^1].Feet > 3.2f, $"from the water ({climb[0].Feet}) up to the deck ({climb[^1].Feet})" );
		Assert.IsTrue( stepOver.Count is >= 2 and <= 8, $"the step onto the deck is short ({stepOver.Count} turns)" );
		Assert.IsTrue( stepOver.Select( point => point.X ).Max() - stepOver.Select( point => point.X ).Min() <= One * 3 / 5, "and about half a cell (0.55)" );
		Assert.IsTrue( done, "the activity completed and returned the guest to the water" );
		Assert.IsTrue( rig.Region.IsWater( guest.Navigation.X, guest.Navigation.Y ) );
	}

	[TestMethod]
	public void ALoungerIsClimbedOntoOnTheSpotToo()
	{
		const int One = 65536;
		var rig = ExtrasRig( Site( 304, PoolExtraKind.Lounger, 90, (4, 5), (4, 6) ) );
		var guest = rig.Send();
		var (climb, stepOver, _, _) = ClimbTrace( rig, guest, "extra.lounger", 1, 2 );
		var foot = (X: climb[0].X, Y: climb[0].Y);
		Assert.AreEqual( 4 * One + One / 2 + PoolExtraSettings.LadderFootDistance, foot.X, "it swam to the rim of the seat" );
		Assert.AreEqual( PoolExtraSettings.ClimbTurns, climb.Count );
		Assert.IsTrue( climb.All( point => point.X == foot.X && point.Y == foot.Y ), "no horizontal travel while climbing out" );
		Assert.IsTrue( climb.Zip( climb.Skip( 1 ), ( a, b ) => b.Feet > a.Feet ).All( rising => rising ) && climb[^1].Feet <= 3.5f, "the feet rise from the water to the deck" );
		Assert.IsTrue( stepOver.Count > 0 && stepOver.Last().X < foot.X - One / 3, "then it steps over the rim onto the seat" );
	}

	[TestMethod]
	public void ADiverClimbsTheBoardWalksOutAndDivesIntoTheWaterTwoCellsOut()
	{
		var rig = ExtrasRig( Site( 302, PoolExtraKind.DivingBoard, 90, (4, 5) ) );
		var guest = rig.Send();
		
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		var (steps, cells, slots, _) = Trace( rig, guest, "extra.board", limit: 3000 );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5 }, steps );
		Assert.IsTrue( cells.Contains( (4, 5) ) && cells.Contains( (6, 5) ), "from the tower over the water to the landing" );
		CollectionAssert.AreEqual( new[] { PoolSwimSlots.Crawl, PoolSwimSlots.LadderClimb, PoolSwimSlots.Walk, PoolSwimSlots.Stand, PoolSwimSlots.Dive, PoolSwimSlots.SplashDown }, slots.Distinct().ToArray() );
		Assert.AreEqual( GuestState.Swimming, guest.State );
		// the drawn height, with the water at 2, the deck at 3.5 and the default 3: from the water up the steps, on the board at the deck plus the lift, falling to the water with the dive
		var feet = new PoolFeetHeights( 3f, 2f, 3.5f );
		var top = 3.5f + PoolExtraSettings.BoardLift;
		const int One = 65536;
		(guest.State, guest.PoolActivityId, guest.PoolActivityPhase) = (GuestState.PoolActivity, "extra.board", 2);
		(guest.PoolActivityX, guest.PoolActivityY) = (4 * One + One / 2, 5 * One + One / 2);
		Assert.AreEqual( top, rig.Simulation.PoolFeetHeight( guest, feet ), 0.001f, "on the board" );
		guest.PoolActivityPhase = 1;
		rig.Simulation.PlacePoolGuest( guest, 4 * One + One / 2 + PoolExtraSettings.EntryDistance, 5 * One + One / 2 );
		Assert.AreEqual( 2f, rig.Simulation.PoolFeetHeight( guest, feet ), 0.05f, "at the foot of the steps it is at the water" );
		rig.Simulation.PlacePoolGuest( guest, 4 * One + One / 2, 5 * One + One / 2 );
		Assert.AreEqual( top, rig.Simulation.PoolFeetHeight( guest, feet ), 0.05f, "at the top of the steps it is at the board" );
		guest.PoolActivityPhase = 4 | (1 << 4);
		rig.Simulation.PlacePoolGuest( guest, 4 * One + One / 2 + PoolExtraSettings.BoardEnd, 5 * One + One / 2 );
		Assert.AreEqual( top, rig.Simulation.PoolFeetHeight( guest, feet ), 0.05f, "the dive starts at the board's height" );
		rig.Simulation.PlacePoolGuest( guest, 4 * One + One / 2 + PoolExtraSettings.BoardLanding, 5 * One + One / 2 );
		Assert.AreEqual( 2f, rig.Simulation.PoolFeetHeight( guest, feet ), 0.05f, "and ends at the water" );
		guest.PoolActivityPhase = 5 | (1 << 4);
		Assert.AreEqual( 3f, rig.Simulation.PoolFeetHeight( guest, feet ), "the splash is at the default (the water)" );
		// the ladder interpolates between the same two heights
		guest.PoolActivityId = "extra.ladder";
		guest.PoolActivityPhase = 1 | (1 << 4);
		guest.PoolActivityTurns = 0;
		Assert.AreEqual( 2f, rig.Simulation.PoolFeetHeight( guest, feet ), 0.001f, "the ladder starts at the water" );
		guest.PoolActivityTurns = PoolExtraSettings.ClimbTurns / 2;
		Assert.AreEqual( 2.75f, rig.Simulation.PoolFeetHeight( guest, feet ), 0.001f, "half way up after half the turns, wherever the guest is" );
		guest.PoolActivityTurns = PoolExtraSettings.ClimbTurns;
		Assert.AreEqual( 3.5f, rig.Simulation.PoolFeetHeight( guest, feet ), 0.001f, "and reaches the deck" );
		guest.PoolActivityPhase = 3 | (1 << 4);
		Assert.AreEqual( 3.5f, rig.Simulation.PoolFeetHeight( guest, feet ), "standing on the deck" );
		guest.PoolActivityPhase = 5 | (1 << 4);
		rig.Simulation.PlacePoolGuest( guest, 4 * One + One / 2 + PoolExtraSettings.EntryDistance, 5 * One + One / 2 );
		Assert.AreEqual( 2f, rig.Simulation.PoolFeetHeight( guest, feet ), 0.05f, "the jump ends in the water" );
		(guest.State, guest.PoolActivityId, guest.PoolActivityPhase, guest.PoolActivityX, guest.PoolActivityY) = (GuestState.Swimming, null, 0, 0, 0);
	}

	[TestMethod]
	public void AGuestShowersFirstThenSwimsAndTheShowerLastsAFewSeconds()
	{
		var rig = ExtrasRig( Site( 303, PoolExtraKind.Shower, 0, (4, 2) ) );
		var guests = Enumerable.Range( 0, 6 ).Select( _ => rig.Send() ).ToList();
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
		Assert.IsTrue( steps.Count >= 1, "at least one of six guests showered (60 percent each)" );
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
		var site = Site( 304, PoolExtraKind.Lounger, 90, (4, 5), (4, 6) );
		var backInWater = 0;
		var ended = 0;
		var bothAtOnce = false;
		var seatCells = new HashSet<(int X, int Y)>();
		// a few batches of swimmers (each run is deterministic): the end of a lie is a 60 / 40 draw, so both outcomes are seen across them
		for ( var batch = 0; batch < 6 && (backInWater == 0 || ended == 0); batch++ )
		{
			var rig = ExtrasRig( site );
			var guests = Enumerable.Range( 0, 3 ).Select( _ => rig.Send() ).ToList();
			var swum = new HashSet<int>();
			var forced = false;
			var waited = 0;
			var onLounger = new Dictionary<int, bool>();
			rig.Until( () =>
			{
				foreach ( var guest in guests.Where( guest => guest.InSwimSession ) )
					swum.Add( guest.Id );
				// two swimmers at once: send the first two to the lounger as soon as both can be offered a seat (a swimmer far from the seats is not offered one)
				// (each batch waits a turn longer than the one before: the end of a lie is a draw of the swimmer's own stream, which a run with the same seeds would repeat)
				if ( !forced && guests.Count( guest => guest.State == GuestState.Swimming ) >= 2 && waited++ >= batch )
				{
					foreach ( var guest in guests.Where( guest => guest.State == GuestState.Swimming ) )
						if ( guests.Count( other => other.PoolActivityId == "extra.lounger" ) < 2 )
							rig.Simulation.DebugStartActivity( guest, "extra.lounger" );
					// (a seat taken is kept, the other swimmers are tried again on the next turn)
					if ( guests.Count( guest => guest.PoolActivityId == "extra.lounger" ) < 2 )
						goto next;
					forced = true;
					bothAtOnce |= guests.Count( guest => guest.PoolActivityId == "extra.lounger" ) == 2;
					Assert.AreEqual( 2, guests.Select( guest => (guest.PoolActivityX, guest.PoolActivityY) ).Distinct().Count( seat => guests.Any( g => g.PoolActivityId == "extra.lounger" && (g.PoolActivityX, g.PoolActivityY) == seat ) ), "on two different seats" );
				}
			next:
				foreach ( var guest in guests )
				{
					var doing = guest.PoolActivityId == "extra.lounger";
					if ( doing && rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) == PoolSwimSlots.Lying )
						seatCells.Add( guest.Navigation.Cell );
					if ( onLounger.TryGetValue( guest.Id, out var was ) && was && !doing )
					{
						// the lounger let go of it: back in the water, or out of the session (climbing out)
						if ( guest.State == GuestState.Swimming )
							backInWater++;
						else if ( guest.State == GuestState.ClimbingOut )
							ended++;
					}
					onLounger[guest.Id] = doing;
				}
				Assert.IsTrue( guests.Count( guest => guest.PoolActivityId == "extra.lounger" ) <= 2, "two seats" );
				return swum.Count == guests.Count && guests.All( guest => !guest.InSwimSession );
			}, 6000 );
		}
		Assert.IsTrue( bothAtOnce, "two guests lay on the lounger at once" );
		Assert.IsTrue( seatCells.IsSubsetOf( new[] { (4, 5), (4, 6) } ) && seatCells.Count == 2, $"on both seats: {string.Join( ", ", seatCells )}" );
		Assert.IsTrue( backInWater > 0 && ended > 0, $"both outcomes: {backInWater} back in the water, {ended} ended their session" );
	}

	/// <summary>
	/// A lounger whose seat (4, 5) has the pool to its east, turned to each rotation: its back (the foot end) faces the water only at 90; at 0 and 180 the guest climbs out at the seat's side and at 270 at
	/// its raised head end. The two seats lie side by side across the lying direction (the shape "**" turned), the second away from the pool.
	/// </summary>
	private static PoolExtraSite LoungerAt( int rotation )
	{
		var across = ObjectFootprint.RotateDirection( rotation, 1, 0 );
		var other = (4 + across.X, 5 + across.Y).Item1 >= 5 ? (4 - across.X, 5 - across.Y) : (4 + across.X, 5 + across.Y);
		return Site( 304, PoolExtraKind.Lounger, rotation, (4, 5), other );
	}

	[TestMethod]
	public void AGuestLiesAlongTheLoungerWithItsFeetToTheFootEndWhicheverSideItClimbedOutOn()
	{
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var site = LoungerAt( rotation );
			var lie = PoolExtras.WaterDirection( rotation );
			Assert.AreEqual( 0, lie.X * (site.Cells[1].X - site.Cells[0].X) + lie.Y * (site.Cells[1].Y - site.Cells[0].Y), $"rotation {rotation}: the seats lie side by side across the lying direction" );
			var rig = ExtrasRig( site );
			var guest = rig.Send();
			rig.Until( () => guest.State == GuestState.Swimming, 400 );
			Assert.IsTrue( rig.Simulation.DebugStartActivity( guest, LoungerActivity.ActivityId ), $"rotation {rotation}: the seat beside the water is offered" );
			var side = PoolExtraMotion.WaterSide( guest );
			Assert.AreEqual( (1, 0), side, $"rotation {rotation}: it climbs out on the pool's side of the seat" );
			var lying = new List<(int X, int Y, float HeadingX, float HeadingY, float Feet)>();
			var heights = new PoolFeetHeights( 3f, 2f, 3.5f );
			rig.Until( () =>
			{
				if ( guest.PoolActivityId == LoungerActivity.ActivityId && rig.Simulation.PoolActivityOf( guest )!.SpriteSlot( guest ) == PoolSwimSlots.Lying )
					lying.Add( (guest.Navigation.X, guest.Navigation.Y, guest.HeadingX, guest.HeadingY, rig.Simulation.PoolFeetHeight( guest, heights )) );
				return lying.Count > 0 && guest.PoolActivityId != LoungerActivity.ActivityId;
			}, 4000 );
			Assert.IsTrue( lying.Count >= PoolExtraSettings.LoungerTurns - 1, $"rotation {rotation}: it lay for the lie's turns ({lying.Count})" );
			var seat = PoolSwimRegion.Centre( (4, 5) );
			Assert.IsTrue( lying.All( turn => (turn.X, turn.Y) == seat ), $"rotation {rotation}: it lies still in the middle of its seat" );
			Assert.IsTrue( lying.All( turn => turn.HeadingX == lie.X && turn.HeadingY == lie.Y ),
				$"rotation {rotation}: every lying turn, the first too, faces the foot end {lie} (not the side it climbed out on, {side}): {string.Join( " ", lying.Select( turn => (turn.HeadingX, turn.HeadingY) ).Distinct() )}" );
			Assert.IsTrue( lying.All( turn => turn.Feet == heights.Deck + PoolExtraSettings.LoungerBedLift ), $"rotation {rotation}: on the cushion" );
		}
	}

	[TestMethod]
	public void ALyingGuestHoldsItsPoseAndIsDrawnInFrontOfTheLounger()
	{
		// the two lying frames are two resting poses: a pose is held for seconds, not flipped six times a second as the strokes' cycle
		for ( var guestId = 0; guestId < 5; guestId++ )
		{
			var changes = 0;
			for ( var tick = 1; tick <= 60 * 30; tick++ )
				if ( PoolSwimSlots.ActivityStep( PoolSwimSlots.Lying, tick / 60.0, guestId ) != PoolSwimSlots.ActivityStep( PoolSwimSlots.Lying, (tick - 1) / 60.0, guestId ) )
					changes++;
			Assert.IsTrue( changes is >= 3 and <= 5, $"guest {guestId}: {changes} pose changes in 30 seconds" );
		}
		Assert.AreEqual( 6, PoolSwimSlots.ActivityStep( PoolSwimSlots.Walk, 1.01, 0 ), "the other activity slots still run at six frames a second" );
		Assert.AreEqual( 9, PoolSwimSlots.ActivityStep( PoolSwimSlots.Dive, 1.01, 3 ), "offset by the guest's id" );

		var rig = ExtrasRig( LoungerAt( 0 ) );
		var guest = rig.Send();
		rig.Until( () => guest.State == GuestState.Swimming, 400 );
		Assert.IsTrue( rig.Simulation.DebugStartActivity( guest, LoungerActivity.ActivityId ) );
		var pulls = new Dictionary<int, HashSet<float>>();
		rig.Until( () =>
		{
			if ( guest.PoolActivityId != LoungerActivity.ActivityId )
				return pulls.Count > 0;
			var step = guest.PoolActivityPhase & 15;
			if ( !pulls.TryGetValue( step, out var set ) )
				pulls[step] = set = new HashSet<float>();
			set.Add( LoungerActivity.DepthPull( rig.Simulation, guest ) );
			return false;
		}, 4000 );
		CollectionAssert.AreEqual( new[] { PoolExtraSettings.LoungerDepthPull }, pulls[3].ToArray(), "lying, it is pulled towards the camera past the lounger's head end and seat" );
		Assert.IsTrue( pulls.Where( pair => pair.Key != 3 ).All( pair => pair.Value.SetEquals( new[] { 0f } ) ), "and only then" );
		Assert.AreEqual( 0f, LoungerActivity.DepthPull( rig.Simulation, guest ), "not after the lounger" );
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
			("extra.ladder", Site( 301, PoolExtraKind.Ladder, 90, (4, 5) ), 3),
			("extra.board", Site( 302, PoolExtraKind.DivingBoard, 90, (4, 5) ), 3),
			("extra.lounger", Site( 304, PoolExtraKind.Lounger, 90, (4, 5), (4, 6) ), 3)
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
