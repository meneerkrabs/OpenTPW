using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Guests at the shipped Jungle Burger Shop (Info.Id 1207: shape "**/*2", no exit of its own, RideHandlesSprite 1, a script that takes a guest, waits 1000 ms and lets it off), run with its original
/// script in every rotation (inconclusive without OPENTPW_GAME_PATH). Every customer is let off again about a second after the shop takes it, stays in view at the counter meanwhile, and none is lost.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ShopVisitAssetTests
{
	private const int BurgerShop = 1207;
	private const float Tick = 1f / FixedStepClock.TicksPerSecond;
	private BaseFileSystem? previousFiles;

	[TestInitialize]
	public void Init()
	{
		Log = new();
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original Jungle Burger Shop." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "jungle", "shops", "burger.wad" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/jungle/shops/burger.wad." );
		previousFiles = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( previousFiles != null )
			FileSystem = previousFiles;
	}

	/// <summary>Records each guest's spans in state 16 and whether the shop kept it in view through them.</summary>
	private sealed class UseTracker : IGuestObserver
	{
		private readonly Dictionary<int, (double Since, int Attraction, bool InView)> using_ = new();
		public readonly List<(int Guest, int Attraction, double Seconds, bool InView)> Uses = new();
		public double Now;
		public IEnumerable<(int Guest, int Attraction, double Seconds)> Open => using_.Select( pair => (pair.Key, pair.Value.Attraction, Now - pair.Value.Since) );
		public void Admitted( Guest guest, int fee ) { }
		public void Spent( Guest guest, int amount, int attractionId ) { }
		public void QueueRejected( Guest guest, IRideVisitorBridge attraction, QueueEndReason reason ) { }
		public void QueueJoined( Guest guest, IRideVisitorBridge attraction ) { }
		public void QueueBoarded( Guest guest, IRideVisitorBridge attraction, long waitTurns ) { }
		public void QueueEnded( Guest guest, int attractionId, QueueEndReason reason, long waitTurns ) { }
		public void Decided( Guest guest, bool chose ) { }
		public void Leaving( Guest guest, DepartureCause cause ) { }
		public void Departed( Guest guest ) { }
		public void Recalled( Guest guest ) { }
		public void Restored( GuestSimulation simulation ) { }
		public void Ticked( GuestSimulation simulation, float deltaSeconds )
		{
			Now += deltaSeconds;
			foreach ( var guest in simulation.Guests )
			{
				if ( guest.State == GuestState.Using )
				{
					var inView = simulation.ShowsKeptBody( guest );
					using_[guest.Id] = using_.TryGetValue( guest.Id, out var open ) ? open with { InView = open.InView && inView } : (Now, guest.AttractionId, inView);
				}
				else if ( using_.Remove( guest.Id, out var ended ) )
					Uses.Add( (guest.Id, ended.Attraction, Now - ended.Since, ended.InView) );
			}
		}
	}

	/// <summary>
	/// The gate's park as the game runs it (the build flow's rules, the economy, the thing sweep), with the Burger Shop as its shop and a guest group every 15 timer units: the shop is used hundreds of
	/// times and every customer leaves it after the script's one second. The gate's spine offers no site for rotation 180; <see cref="TheBurgerShopReleasesEveryCustomerOnLaidAccessCells"/> covers it.
	/// </summary>
	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 90 )]
	[DataRow( 270 )]
	public void TheBurgerShopLetsEveryCustomerOffInAWholeParkRun( int rotation )
	{
		var tracker = new UseTracker();
		var shopId = 0;
		var still = new List<(int Guest, int Attraction, double Seconds)>();
		var run = M3GateRun.Execute( new M3GateOptions
		{
			Minutes = 20, ArrivalTimeBetween = 15, DeterminismProbe = false, SweptTurns = true, GuestObserver = tracker,
			ShopFilter = entry => entry.InfoId == BurgerShop, ShopRotation = rotation,
			BeforeTeardown = simulation =>
			{
				shopId = simulation.Attractions.Single( attraction => attraction.Name == "Burger Shop" ).AttractionId;
				still.AddRange( tracker.Open );
			}
		}, M3Gate.TicksForMinutes( 20 ) );
		var shop = run.Rows.Single( row => row.Id == "build.shop" );
		Assert.AreEqual( M3GateVerdict.Pass, shop.Verdict, shop.Evidence.ToJsonString() );
		Assert.AreEqual( rotation, (int)shop.Evidence["rotation"]! );
		var uses = tracker.Uses.Where( use => use.Attraction == shopId ).ToList();
		Assert.IsTrue( uses.Count >= 100, $"the shop was used {uses.Count} times" );
		// the script writes VAR_LETMEOFF after its WAIT 1000 and the ride update of the next park turn (248 ms) lets the guest off (tp.exe 0x004E1410)
		Assert.IsTrue( uses.All( use => use.Seconds is > 1.0 and < 1.3 ), $"each use lasts the script's WAIT 1000 and up to one park turn: {uses.Min( use => use.Seconds ):F2}..{uses.Max( use => use.Seconds ):F2} s" );
		Assert.IsTrue( uses.All( use => use.InView ), "every customer stands in view at the counter while it is served" );
		Assert.IsTrue( still.Where( use => use.Attraction == shopId ).All( use => use.Seconds < 1.3 ), "nobody is left in the shop: " + string.Join( ", ", still ) );
	}

	/// <summary>
	/// The Burger Shop placed the way the level places a build (its footprint typed, the cells outside its entrance laid, then registered with the guests) next to an Easymode path in each of the four
	/// rotations: ten hungry guests sent to it are all served and let off.
	/// </summary>
	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 90 )]
	[DataRow( 180 )]
	[DataRow( 270 )]
	public void TheBurgerShopReleasesEveryCustomerOnLaidAccessCells( int rotation )
	{
		var park = OriginalPark.Load( "jungle" );
		if ( park.Save == null )
			Assert.Inconclusive( "The Jungle Easymode.TPWI save is missing." );
		var entry = ObjectCatalog.Load( "jungle" ).Find( BurgerShop )!;
		Assert.IsNotNull( entry );
		Assert.IsFalse( entry.HasQueue );
		var tracker = new UseTracker();
		var simulation = new GuestSimulation( GuestPathGrid.FromOriginal( park.Map, park.Save ), GuestSettings.Load( "jungle" ), 7 ) { ArrivalsEnabled = false, Observer = tracker };
		var grid = simulation.Grid;
		var cells = grid.Cells;
		var site = Enumerable.Range( 0, grid.CountX * grid.CountY ).Select( index => (X: index % grid.CountX, Y: index / grid.CountX) ).First( anchor =>
			ObjectFootprint.GetCells( entry.Shape, anchor.X, anchor.Y, rotation ).All( cell => cells.InBounds( cell.X, cell.Y ) && cells.RawTypeAt( cell.X, cell.Y ) == 0 && !grid.IsWalkable( cell.X, cell.Y ) )
			&& ObjectFootprint.GetAccessPoints( entry.Shape, anchor.X, anchor.Y, rotation ).All( point => grid.IsWalkable( point.OutsideX, point.OutsideY ) ) );
		var owner = ParkCellMap.CellWord( site.X, site.Y );
		var points = ObjectFootprint.GetAccessPoints( entry.Shape, site.X, site.Y, rotation ).ToArray();
		// Level's placement handlers in their order: the footprint, the laid access cells, then the registration
		cells.WriteFootprint( ObjectFootprint.GetCells( entry.Shape, site.X, site.Y, rotation ), owner, rotation );
		ObjectAccessCells.Lay( cells, points, owner, entry.HasQueue );
		var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: null, open: true );
		Assert.IsTrue( Level.RegisterWithGuests( runtime, points, simulation, rotation, owner, ownExitCell: true ) );
		var shop = runtime.Visitors;
		Assert.IsTrue( shop.RideHandlesSprite, "Burger.sam: UsageInfo.RideHandlesSprite 1" );
		Assert.AreEqual( ParkCellTypes.Entrance, cells.RawTypeAt( GuestSimulation.StandCellOf( shop ).X, GuestSimulation.StandCellOf( shop ).Y ), "the stand point lies in the shop's own entrance cell" );
		var guests = Enumerable.Range( 0, 10 ).Select( _ => simulation.SpawnInPark( shop.EntranceCell.X, shop.EntranceCell.Y ) ).ToList();
		foreach ( var guest in guests )
		{
			guest.AttractionId = shop.AttractionId;
			guest.State = GuestState.GoingToRide;
			guest.Money = 1000;
		}
		for ( var tick = 0; tick < 120 * 60; tick++ )
		{
			simulation.Tick( Tick );
			runtime.Simulate( Tick );
		}
		Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, runtime.Script.FaultMessage );
		Assert.AreEqual( 10, shop.BoardedTotal, $"rotation {rotation}: every guest was served" );
		Assert.AreEqual( 10, shop.ReleasedTotal, $"rotation {rotation}: every guest was let off" );
		Assert.AreEqual( 10, tracker.Uses.Count( use => use.Attraction == shop.AttractionId && use.Seconds is > 1.0 and < 1.3 && use.InView ) );
		Assert.IsFalse( simulation.Guests.Any( guest => guest.State == GuestState.Using ), "nobody is left in the shop" );
	}
}
