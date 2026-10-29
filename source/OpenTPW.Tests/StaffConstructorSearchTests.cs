using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// DET-015: a new employee's role constructor runs the role's first search inside the placing click (tp.exe 0x004D4340 → 0x004D46D0, 0x004D5D40 → 0x004D63D0, 0x004D6B60 → 0x004D7100,
/// 0x004D9EB0 → 0x004DA5B0, 0x00502600 → 0x00502C70), after the hire's base-speed and variant draws and before the turn's sweep, so the first update does not search again. A headless economy has
/// no staff world and keeps the first-update search.
/// </summary>
[TestClass]
[DoNotParallelize]
public class StaffConstructorSearchTests
{
	private AutomaticCheckpointCpuRendererScope? renderer;
	private UpgradeCatalogueDefaultsTests? fixture;

	[TestInitialize]
	public void Initialize()
	{
		renderer = new AutomaticCheckpointCpuRendererScope();
		fixture = new UpgradeCatalogueDefaultsTests();
		fixture.Setup();
	}

	[TestCleanup]
	public void Cleanup()
	{
		fixture?.Cleanup();
		renderer?.Dispose();
	}

	/// <summary>A cell an employee may stand on, on the paths and with the most links, so that a patrol has somewhere to go.</summary>
	private static (int X, int Y) PathCell( Level level ) =>
		Enumerable.Range( 0, level.Guests!.Grid.Cells.Height ).SelectMany( y => Enumerable.Range( 0, level.Guests.Grid.Cells.Width ).Select( x => (x, y) ) )
			.Where( cell => level.StaffHands!.IsValidCell( cell.x, cell.y ) && level.Guests.Grid.IsWalkable( cell.x, cell.y ) )
			.OrderByDescending( cell => System.Numerics.BitOperations.PopCount( (uint)level.Guests.Grid.Cells.LinksAt( cell.x, cell.y ) ) ).First();

	private ParkSettingCommandLevelTests.Park PoolPark()
	{
		var park = ParkSettingCommandLevelTests.NewPark( fixture! );
		// the person things of the staff come from a pool; the fixture's economy has none, so no hire would take its constructor draws
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		park.Economy.Staff.AttachThings( pool );
		// a straight path a patrol can walk along (the fixture's level has two path cells)
		for ( var x = 2; x < 22; x++ )
			park.Level.Guests!.Grid.SetPath( x, 5, true );
		return park;
	}

	private static StaffCandidate Candidate( ParkEconomy economy, StaffType type ) =>
		economy.Staff.Candidates.FirstOrDefault( item => item.Type == type && economy.Staff.CanHire( type ) ) ?? throw new AssertInconclusiveException( $"no {type} candidate" );

	[TestMethod]
	public void EveryRoleSearchesInsideThePlacingClickAndTheFirstUpdateDoesNotSearchAgain()
	{
		var patrols = new List<StaffType>();
		foreach ( var type in new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Entertainer, StaffType.Guard, StaffType.Researcher } )
		{
			var park = PoolPark();
			var level = park.Level;
			var economy = park.Economy;
			var cell = PathCell( level );
			var candidate = Candidate( economy, type );
			var start = economy.World.State;
			// the hire's own draws: the base-speed draw, and the guard's variant draw
			var hireDraws = new WorldRng( economy.World.State );
			hireDraws.NextWord();
			if ( type == StaffType.Guard )
				hireDraws.NextWord();
			Assert.IsTrue( level.StaffPlacement!.BeginHire( candidate.Id ) );
			Assert.AreEqual( StaffClickResult.Placed, level.StaffPlacement.Click( cell.X, cell.Y ), type.ToString() );
			var member = level.StaffPlacement.LastPlaced!;
			Assert.IsNotNull( member.Navigation, $"{type}: the constructor's search ran at the click, the member has its walk" );
			Assert.AreNotEqual( hireDraws.State, economy.World.State, $"{type}: the search drew after the hire's draws" );
			// the twin: the same hire without the click's constructor, then the constructor's search by hand, gives the same park generator and job
			var twin = PoolPark();
			Assert.AreEqual( start, twin.Economy.World.State, $"{type}: the twins start from one generator state" );
			var twinCandidate = Candidate( twin.Economy, type );
			Assert.AreEqual( candidate.Id, twinCandidate.Id );
			twin.Economy.Staff.SetPendingHire( 0, twinCandidate.Id );
			twin.Economy.Staff.EndPlacement( 0, placed: true );
			var hired = twin.Economy.Hire( twinCandidate.Id );
			hired.PatrolArea = (0, 0, ParkLitter.MapSize - 1, ParkLitter.MapSize - 1);
			StaffPlacement.PutDown( hired, cell.X, cell.Y );
			Assert.AreEqual( hireDraws.State, twin.Economy.World.State, $"{type}: nothing but the hire's draws before the constructor's search" );
			Assert.IsNull( hired.Navigation );
			Assert.IsTrue( twin.Level.StaffWorld!.Constructed( twin.Economy, hired ) );
			Assert.AreEqual( economy.World.State, twin.Economy.World.State, $"{type}: the click's search is the constructor's search" );
			Assert.AreEqual( member.Job, hired.Job, type.ToString() );
			Assert.AreEqual( member.IdleStamp, hired.IdleStamp, type.ToString() );
			// the first update goes on from the state the constructor chose: a patrol takes the walking voice's one draw and no search
			if ( member.Job == StaffJob.Patrol )
			{
				patrols.Add( type );
				var before = new WorldRng( economy.World.State );
				before.NextWord();
				level.StaffWorld!.UpdateStaffMember( economy, member, economy.Turn );
				Assert.AreEqual( before.State, economy.World.State, $"{type}: the first update of a patrolling member takes only the voice draw" );
			}
		}
		Assert.IsTrue( patrols.Count >= 3, $"the first update was checked for a patrolling member of {patrols.Count} roles" );
	}

	private static readonly (StaffType Type, uint State, StaffJob Job, (int X, int Y)? Destination)[] Pins =
	{
		(StaffType.Handyman, 169346220u, StaffJob.Patrol, (2, 5)), (StaffType.Mechanic, 169346220u, StaffJob.Patrol, (2, 5)),
		(StaffType.Entertainer, 663873947u, StaffJob.Idle, null), (StaffType.Guard, 1490415135u, StaffJob.Idle, null),
		(StaffType.Researcher, 2622474534u, StaffJob.Patrol, (6, 5))
	};

	/// <summary>The click path's pin: the world generator's state, the job and the route's destination right after the placing click, per role.</summary>
	[TestMethod]
	public void ThePlacingClickLeavesPinnedGeneratorStateJobAndDestinationPerRole()
	{
		foreach ( var pin in Pins )
		{
			var park = PoolPark();
			var level = park.Level;
			var cell = PathCell( level );
			Assert.IsTrue( level.StaffPlacement!.BeginHire( Candidate( park.Economy, pin.Type ).Id ) );
			Assert.AreEqual( StaffClickResult.Placed, level.StaffPlacement.Click( cell.X, cell.Y ) );
			var member = level.StaffPlacement.LastPlaced!;
			var destination = member.Navigation is { } walk && member.Job != StaffJob.Idle ? (walk.TargetX >> 16, walk.TargetY >> 16) : ((int, int)?)null;
			Assert.AreEqual( (pin.State, pin.Job, pin.Destination), (park.Economy.World.State, member.Job, destination), pin.Type.ToString() );
		}
	}

	[TestMethod]
	public void AHeadlessEconomyKeepsTheFirstUpdateSearch()
	{
		var economy = EconomyTestData.Park();
		var pool = new ThingPool();
		pool.AllocateWorldSetup();
		economy.Staff.AttachThings( pool );
		var grid = new GuestPathGrid( 20, 20 );
		for ( var x = 0; x < 16; x++ )
			grid.SetPath( x, 5, true );
		economy.OpenPark();
		economy.Litter.Cells = grid.Cells;
		var world = new StaffWorldSimulation( grid, () => economy.Staff.Members, _ => null, (0, 5) );
		economy.StaffWorld = world;
		var placement = StaffPlacementRig.Create( () => economy, () => grid.Cells, (_, _) => false );
		var candidate = economy.Staff.Candidates.First( item => item.Type == StaffType.Handyman );
		Assert.IsTrue( placement.BeginHire( candidate.Id ) );
		Assert.AreEqual( StaffClickResult.Placed, placement.Click( 5, 5 ) );
		Assert.IsNull( placement.LastPlaced!.Navigation, "no hands world: the click does not search" );
		world.UpdateStaff( economy, 1 );
		Assert.IsNotNull( placement.LastPlaced.Navigation );
	}
}
