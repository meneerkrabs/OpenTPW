using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Water slides against a real <see cref="Level"/> (docs/POOLS.md, "Water slides"): a tower placed beside a pool registers its slide, the track keeps its cells for the pylons, demolishing the tower takes the
/// slide and its riders with it, the splash cell cannot be erased under riders, and the slide goes through the park-resume capture. The level is the one the pools level tests assemble without a GPU.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PoolSlideLevelTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;
	private Level level = null!;
	private ParkEconomy economy = null!;
	private OriginalObject one = null!;
	private Dictionary<string, string> assets = null!;

	private static object? Call( object target, string name, params object[] args ) => target.GetType().GetMethod( name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public )!.Invoke( target, args );

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
		var type = typeof( UpgradeCatalogueDefaultsTests );
		var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
		assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
		typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
		(level, economy, one) = (world.Level, world.Economy, world.One);
		Call( level, "SetupPaths" );
		level.ConfigureObjectHooks();
		Assert.IsNotNull( level.PoolBuilder, "the level builds its pool tool" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	private ParkCellMap Cells => level.Guests!.Grid.Cells;

	private static T With<T>( bool pools, Func<T> action )
	{
		using var scope = OpenTpwExtensions.Use( PoolsTests.PoolsSet( pools ) );
		return action();
	}

	private ParkResumeSnapshot Capture() => level.CaptureResumeSnapshot( null, "slides", assets, new DateTimeOffset( 2026, 10, 17, 12, 0, 0, TimeSpan.Zero ) );

	/// <summary>A synthetic slide tower: 2 x 2, no entrance, <c>Pools.SlideTower 1</c>; its front (stairs) faces north at rotation 0.</summary>
	private ObjectCatalogEntry Tower( int id = 65100, string extra = "" )
	{
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( $"Info.Id {id}\nInfo.Name SyntheticTower{id}\nInfo.OverwritePriority 5\nPools.SlideTower 1\n{extra}", $"tower{id}.sam" ) } );
		return new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic", settings, new ObjectShape( new[] { "**", "**" } ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), one.Entry.FileSystem, null );
	}

	/// <summary>The pool of 7 x 7 cells at x 12 to 18, y 12 to 18; the tower at (13, 19) has its front on the pool's bottom edge.</summary>
	private OriginalObject PoolAndTower()
	{
		Assert.IsTrue( level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (12, 12), (18, 18) ) ).Completed );
		// a real park gives every object a thing id (the guests on its slide hold it as their target)
		level.Objects.Things ??= new ThingPool();
		var placed = level.Objects.TryPlace( Tower(), 13, 19, 0, out _ );
		Assert.IsNotNull( placed, "a tower beside the pool" );
		Assert.AreNotEqual( 0, placed!.ThingId );
		Cells.WriteFootprint( placed!.Cells );
		return placed;
	}

	private static readonly (int X, int Y)[] Track = { (15, 19), (16, 19), (17, 19), (17, 18) };

	private int Build( OriginalObject tower )
	{
		foreach ( var (x, y) in Track )
			Assert.AreEqual( PoolSlideResult.Ok, level.PoolSlides!.AddCell( tower.Placement.X, tower.Placement.Y, x, y ), $"({x}, {y})" );
		return PoolSlide.KeyOf( tower.Placement.X, tower.Placement.Y );
	}

	[TestMethod]
	public void ATowerBesideAPoolRegistersItsSlideAndTheLevelHandsItToTheGuests()
	{
		With( true, () =>
		{
			Assert.IsNotNull( level.PoolSlides );
			Assert.AreSame( level.PoolSlides, level.Guests!.PoolSlides );
			Assert.AreEqual( OriginalPlacementResult.NeedsPool, level.Objects.Check( Tower(), 13, 19, 0 ), "no pool: no tower" );
			var tower = PoolAndTower();
			Assert.AreEqual( PoolSlideResult.BadStart, level.PoolSlides!.AddCell( 13, 19, 13, 18 ), "not on the stairs' side" );
			var id = Build( tower );
			var slide = level.PoolSlides.Find( id )!;
			Assert.IsTrue( slide.IsComplete && level.PoolSlides.IsUsable( slide ), $"{slide.SlopePermille}" );
			Assert.AreEqual( (17, 18), slide.LandingCell );
			Assert.IsTrue( level.PoolSlides.Count == 1 );
			return 0;
		} );
	}

	[TestMethod]
	public void TheTracksLandCellsAreKeptForThePylonsButItsWaterCellsAreNotAndTheHashSeesTheSlide()
	{
		With( true, () =>
		{
			var tower = PoolAndTower();
			var before = level.ComputeStateHash();
			Build( tower );
			Assert.AreNotEqual( before, level.ComputeStateHash(), "a slide is part of the world hash" );
			Assert.IsTrue( (bool)Call( level, "IsPathBlocked", 16, 19 )!, "no path under the track" );
			Assert.IsFalse( (bool)Call( level, "IsPathBlocked", 17, 18 )!, "the splash cell is water, no pylon stands there" );
			Assert.AreEqual( PoolBuildResult.Occupied, level.PoolBuilder!.Check( 16, 19 ), "no pool water painted under the track" );
			var shop = new ObjectCatalogEntry( "jungle", ObjectCategory.Shop, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1201\nInfo.Name Synthetic\nInfo.OverwritePriority 5\n", "s.sam" ) } ),
				new ObjectShape( new[] { "**", "**" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), one.Entry.FileSystem, null );
			Assert.AreEqual( OriginalPlacementResult.Occupied, level.Objects.Check( shop, 16, 19, 0 ), "no object under the track" );
			Assert.AreEqual( OriginalPlacementResult.Allowed, level.Objects.Check( shop, 22, 22, 0 ), "control: elsewhere" );
			return 0;
		} );
	}

	[TestMethod]
	public void DemolishingTheTowerTakesTheSlideWithItRefundsItAndEndsTheRidesOnIt()
	{
		With( true, () =>
		{
			var tower = PoolAndTower();
			var balance = economy.Balance;
			var id = Build( tower );
			Assert.AreEqual( balance - 4 * ParkEconomy.SlidePieceCost, economy.Balance );
			level.Guests!.Grid.SetPath( 20, 20, true );
			var guest = level.Guests.SpawnInPark( 20, 20 );
			guest.State = GuestState.PoolActivity;
			guest.PoolActivityId = PoolSlideActivity.ActivityId;
			guest.PoolActivityTarget = tower.ThingId;
			guest.PoolActivityPhase = PoolSlidePhases.Ride;
			level.Objects.Remove( tower );
			Assert.IsNull( level.PoolSlides!.Find( id ), "the slide went with its tower" );
			Assert.AreEqual( balance, economy.Balance, "the pieces came back" );
			Assert.AreNotEqual( GuestState.PoolActivity, guest.State, "the rider's activity was ended at once" );
			return 0;
		} );
	}

	[TestMethod]
	public void TheSplashCellCannotBeErasedWhileRidersAreOnTheSlideNorTheFootOfTheStairsWhileGuestsClimbIt()
	{
		With( true, () =>
		{
			var tower = PoolAndTower();
			var id = Build( tower );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder!.CheckErase( 17, 18 ), "control: nobody on the slide" );
			level.Guests!.Grid.SetPath( 21, 21, true );
			var rider = level.Guests.SpawnInPark( 21, 21 );
			(rider.State, rider.PoolActivityId, rider.PoolActivityTarget, rider.PoolActivityPhase) = (GuestState.PoolActivity, PoolSlideActivity.ActivityId, tower.ThingId, PoolSlidePhases.Ride);
			Assert.AreEqual( PoolBuildResult.SlideRiders, level.PoolBuilder.CheckErase( 17, 18 ), "a rider is on the track" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.CheckErase( 16, 12 ), "another cell of the pool can go" );
			rider.PoolActivityPhase = PoolSlidePhases.Splash;
			Assert.AreEqual( PoolBuildResult.SlideRiders, level.PoolBuilder.CheckErase( 17, 18 ), "and while it splashes" );
			var foot = level.PoolSlides!.TowerOf( level.PoolSlides.Find( id )! )!.PoolSideCell( Cells )!.Value;
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.CheckErase( foot.X, foot.Y ), "nobody climbs yet" );
			rider.PoolActivityPhase = PoolSlidePhases.Climb;
			Assert.AreEqual( PoolBuildResult.SlideRiders, level.PoolBuilder.CheckErase( foot.X, foot.Y ), "a climber needs the foot of the stairs" );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.CheckErase( 17, 18 ), "the climber is not on the track" );
			rider.State = GuestState.DoingNothing;
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.CheckErase( foot.X, foot.Y ) );
			Assert.AreEqual( PoolBuildResult.Ok, level.PoolBuilder.Erase( 17, 18 ), "no riders: the cell goes" );
			Assert.IsFalse( level.PoolSlides.IsUsable( level.PoolSlides.Find( id )! ), "and the slide is not usable without its landing" );
			return 0;
		} );
	}

	[TestMethod]
	public void ASlideGoesThroughTheResumeCaptureAndAParkWithoutSlidesSavesNoPart()
	{
		With( true, () =>
		{
			// the capture validates the layout against the catalog, which does not list the synthetic tower: the slide is built on a tower the level knows without an object
			Assert.IsTrue( level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (12, 12), (18, 18) ) ).Completed );
			var towers = (Dictionary<int, PoolSlideTower>)typeof( Level ).GetProperty( "slideTowers", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( level )!;
			var shape = new ObjectShape( new[] { "**", "**" } );
			towers[PoolSlide.KeyOf( 13, 19 )] = new PoolSlideTower( 13, 19, PoolSlideTowers.Footprint( shape, 13, 19, 0 ), PoolSlideTowers.PoolSideCandidates( shape, 13, 19, 0 ), PoolSlideProfile.Tube, 3 );
			Assert.IsNull( Capture().PoolSlides, "no slide: no part" );
			foreach ( var (x, y) in Track )
				Assert.AreEqual( PoolSlideResult.Ok, level.PoolSlides!.AddCell( 13, 19, x, y ) );
			var snapshot = Capture();
			Assert.IsNotNull( snapshot.PoolSlides );
			Assert.AreEqual( 1, snapshot.PoolSlides!.Slides.Count );
			Assert.AreEqual( 1, snapshot.PoolSlides.Slides[0].Profile, "the profile is the tower's" );
			CollectionAssert.AreEqual( new[] { "opentpw.pools" }, snapshot.Mods!.Select( mod => mod.Id ).ToArray() );
			var bytes = ParkResumeSaveFile.Serialize( snapshot );
			StringAssert.Contains( Encoding.UTF8.GetString( bytes ), "PoolSlides" );
			var loaded = ParkResumeSaveFile.Deserialize( bytes );
			CollectionAssert.AreEqual( bytes, ParkResumeSaveFile.Serialize( loaded ), "the file round-trips" );
			Assert.IsNull( PoolSlideSaves.ShapeProblem( loaded.PoolSlides, loaded.Mods ) );
			Assert.IsNotNull( PoolSlideSaves.ShapeProblem( loaded.PoolSlides, null ), "a slide without the extension recorded" );
			Assert.IsNotNull( PoolSlideSaves.ShapeProblem( new PoolSlidesSaveData( Array.Empty<PoolSlideSaveData>() ), loaded.Mods ) );
			// the world restores from the saved part over the same tower
			level.PoolSlides!.RestoreSnapshot( loaded.PoolSlides );
			Assert.AreEqual( 1, level.PoolSlides.Count );
			Assert.IsTrue( level.PoolSlides.IsUsable( level.PoolSlides.Slides.Single() ) );
			return 0;
		} );
	}

	[TestMethod]
	public void WithTheExtensionOffATowerHasNoRuleAndNoSlideCanBeBuilt()
	{
		var tower = Tower();
		With( false, () =>
		{
			Assert.IsNull( PoolSlideTowers.PlacementRule( tower, Cells, 13, 19, 0 ) );
			Assert.AreEqual( PoolSlideResult.ExtensionOff, level.PoolSlides!.AddCell( 13, 19, 15, 19 ) );
			Assert.IsFalse( level.Guests!.PoolSlides!.ReservesLand( 16, 19, Cells ) );
			return 0;
		} );
	}
}
