using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Phase 6b of the smarter-guests plan (docs/SMARTER-GUESTS.md): a guest whose litter meter is full looks eight cells around it for a litter bin instead of the original's three. The host keeps the bin search,
/// the route, the walk and the drop (with its draw); the policy only answers how far to look. These tests pin the constants, the policy's switches, the host's use of the radius (the boundary, the refusal of
/// a bad answer), that a park without a bin in reach runs as version 1.5.0 did, and that the new reach is deterministic and resumable. The measured effect is in the page's tables.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SmarterGuestsBinsTests
{
	private const float Tick = GuestSnapshotTests.Tick;

	private static GuestView Guest() =>
		new( 8, 7, 0, 60, 10, 10, 10, 0, 500, 3, 3, Array.AsReadOnly( new int[4] ), Array.AsReadOnly( new int[4] ), -1, -1, -1 );

	private static GuestSimulation Row( IGuestDecisions? decisions, ParkLitter litter, LitterBin[] bins )
	{
		var grid = new GuestPathGrid( 16, 16 );
		for ( var x = 0; x < 14; x++ )
			grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		settings.ExitLevelSeconds = 10_000;
		settings.SmallHappinessChange = 0;
		litter.Cells = grid.Cells;
		return new GuestSimulation( grid, settings, 3, decisions ) { ArrivalsEnabled = false, Litter = litter, Bins = () => bins };
	}

	/// <summary>A guest at (2, 5) with a full meter and a bin whose access cell is (<paramref name="column"/>, 5); the bins the guest threw its litter into and the litter left on the path.</summary>
	private static (List<int> Binned, int Litter, bool Emptied) Run( IGuestDecisions? decisions, int column ) => Run( decisions, new LitterBin( 9, (column, 6), (column, 5) ) );

	private static (List<int> Binned, int Litter, bool Emptied) Run( IGuestDecisions? decisions, LitterBin bin )
	{
		var litter = new ParkLitter();
		var simulation = Row( decisions, litter, [bin] );
		var binned = new List<int>();
		simulation.LitterBinned += ( _, bin ) => binned.Add( bin );
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		var emptied = false;
		for ( var tick = 0; tick < 120 * 60 && !emptied; tick++ )
		{
			simulation.Tick( Tick );
			emptied = guest.Litter == 0;
		}
		return (binned, litter.Count, emptied);
	}

	// ---- The constants and the policy's switches ---------------------------------------------------

	[TestMethod]
	public void TheRadiusIsEightAgainstTheOriginalsThree()
	{
		Assert.AreEqual( 3, GuestSimulation.BinRadius );
		Assert.AreEqual( 8, SmarterBinCarry.SearchRadius );
		Assert.IsTrue( SmarterBinCarry.SearchRadius > GuestSimulation.BinRadius && SmarterBinCarry.SearchRadius <= GuestSimulation.MaxBinRadius );
		Assert.AreEqual( 8, SmarterBinCarry.Radius( Guest() ) );
	}

	[TestMethod]
	public void OnlyThePolicyWithTheBinRuleAsksForAnotherRadius()
	{
		IGuestDecisions original = new OriginalGuestDecisions();
		Assert.IsFalse( original.UsesBinCarry );
		Assert.AreEqual( GuestSimulation.BinRadius, original.BinSearchRadius( new DecisionContext( GuestTests.CreateSettings(), 0, false ), Guest() ), "the default answer is the original's radius" );
		Assert.IsTrue( new SmarterGuestDecisions( SmarterRuleSet.All ).UsesBinCarry );
		Assert.IsTrue( new SmarterGuestDecisions( SmarterRuleSet.Bins ).UsesBinCarry );
		Assert.IsFalse( new SmarterGuestDecisions( SmarterRuleSet.Phase6 ).UsesBinCarry, "version 1.5.0's rules do not ask" );
		Assert.IsFalse( new SmarterGuestDecisions( SmarterRuleSet.None ).UsesBinCarry );
		Assert.AreEqual( SmarterRuleSet.Phase6 | SmarterRuleSet.Bins, SmarterRuleSet.All, "the shipped rules: the five of 1.5.0 and the bins" );
		Assert.AreEqual( new ModVersion( 1, 6, 0 ), OfficialExtensions.SmarterGuests.Version );
		Assert.IsTrue( OfficialExtensions.SmarterGuests.PlayerSelectable, "released as opt-in: the CPU gate was confirmed on a hosted runner (docs/SMARTER-GUESTS.md, Cost and release)" );
	}

	// ---- The host ----------------------------------------------------------------------------------

	[TestMethod]
	public void TheOriginalDropsTheLitterWhereAGuestThatLooksFurtherCarriesItToTheBin()
	{
		// a bin 6 cells along the street: out of the original's reach, within the extension's
		var original = Run( null, 8 );
		Assert.IsTrue( original.Emptied );
		Assert.AreEqual( 0, original.Binned.Count );
		Assert.AreEqual( 1, original.Litter, "the original drops it on the cell" );
		var phase6 = Run( new SmarterGuestDecisions( SmarterRuleSet.Phase6 ), 8 );
		Assert.AreEqual( (0, 1), (phase6.Binned.Count, phase6.Litter), "version 1.5.0's rules drop it as well" );
		var smarter = Run( new SmarterGuestDecisions( SmarterRuleSet.All ), 8 );
		Assert.IsTrue( smarter.Emptied );
		CollectionAssert.AreEqual( new[] { 9 }, smarter.Binned );
		Assert.AreEqual( 0, smarter.Litter, "nothing dropped" );
	}

	[TestMethod]
	public void TheBoundaryOfTheRadiusIsExclusiveLikeTheOriginals()
	{
		var policy = new SmarterGuestDecisions( SmarterRuleSet.All );
		// the bin's cell is one row off the street: distance squared = (column - 2)^2 + 1 against 8 * 8 = 64
		Assert.AreEqual( 1, Run( policy, 9 ).Binned.Count, "(9 - 2)^2 + 1 = 50 is below 64" );
		Assert.AreEqual( 0, Run( policy, 10 ).Binned.Count, "(10 - 2)^2 + 1 = 65 is not" );
		Assert.AreEqual( 1, Run( policy, 10 ).Litter, "no bin in reach: the original's drop" );
	}

	[TestMethod]
	public void ASquaredDistanceOfExactlySixtyFourIsNotInReach()
	{
		var policy = new SmarterGuestDecisions( SmarterRuleSet.All );
		// the bin's own cell is on the street, eight cells from the guest at (2, 5): 8 * 8 + 0 = 64, not below it
		Assert.AreEqual( 0, Run( policy, new LitterBin( 9, (10, 5), (10, 5) ) ).Binned.Count );
		Assert.AreEqual( 1, Run( policy, new LitterBin( 9, (9, 5), (9, 5) ) ).Binned.Count, "49 is below 64" );
	}

	[TestMethod]
	public void ABinBehindAWallIsNotWalkedToWhenTheRouteIsLongButTheStraightLineIsShort()
	{
		// two streets joined only at their far end: the bin at (6, 8) is 5 cells away in a straight line (25) and 20 cells away on foot
		ParkLitter litter = new();
		GuestSimulation Park( IGuestDecisions? policy )
		{
			var grid = new GuestPathGrid( 16, 16 );
			for ( var x = 0; x < 14; x++ )
			{
				grid.SetPath( x, 5, true );
				grid.SetPath( x, 7, true );
			}
			grid.SetPath( 13, 6, true );
			var settings = GuestTests.CreateSettings();
			settings.ExitLevelSeconds = 10_000;
			settings.SmallHappinessChange = 0;
			litter.Cells = grid.Cells;
			LitterBin[] bins = [new LitterBin( 9, (6, 8), (6, 7) )];
			return new GuestSimulation( grid, settings, 3, policy ) { ArrivalsEnabled = false, Litter = litter, Bins = () => bins };
		}
		var simulation = Park( new SmarterGuestDecisions( SmarterRuleSet.All ) );
		var binned = 0;
		simulation.LitterBinned += ( _, _ ) => binned++;
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		for ( var tick = 0; tick < 120 * 60 && guest.Litter != 0; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( 0, binned, "the walk is longer than twice the radius" );
		Assert.AreEqual( 1, litter.Count, "the litter falls where the guest stands" );
		Assert.AreEqual( 0, guest.BinId );
		Assert.AreEqual( 2 * SmarterBinCarry.SearchRadius, SmarterBinCarry.MaxRouteCells );
		// the policy without the bound (a spy that only widens the radius) does walk it
		var unbounded = Park( new CountingPolicy { Radius = 8 } );
		var walked = 0;
		unbounded.LitterBinned += ( _, _ ) => walked++;
		var other = unbounded.SpawnInPark( 2, 5 );
		other.Litter = 95;
		for ( var tick = 0; tick < 120 * 60 && other.Litter != 0; tick++ )
			unbounded.Tick( Tick );
		Assert.AreEqual( 1, walked, "no bound: the long walk is taken" );
	}

	[TestMethod]
	public void AParkWithoutABinInReachRunsLikeVersion150()
	{
		ulong Hash( IGuestDecisions policy )
		{
			var litter = new ParkLitter();
			var simulation = Row( policy, litter, [new LitterBin( 9, (13, 6), (13, 5) )] );   // 11 cells away: out of every reach
			var guest = simulation.SpawnInPark( 2, 5 );
			guest.Litter = 95;
			for ( var tick = 0; tick < 3000; tick++ )
				simulation.Tick( Tick );
			return simulation.ComputeStateHash();
		}
		Assert.AreEqual( Hash( new SmarterGuestDecisions( SmarterRuleSet.Phase6 ) ), Hash( new SmarterGuestDecisions( SmarterRuleSet.All ) ) );
	}

	[TestMethod]
	public void AParkWithoutAnyBinNeverAsksTheRadius()
	{
		var policy = new CountingPolicy();
		var litter = new ParkLitter();
		var simulation = Row( policy, litter, []);
		simulation.Bins = null;
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		for ( var tick = 0; tick < 600; tick++ )
			simulation.Tick( Tick );
		Assert.AreEqual( 0, policy.Asked, "no bin list: nothing to look for" );
		Assert.AreEqual( 1, litter.Count );
	}

	[TestMethod]
	public void ABadRadiusIsRefusedNamingThePolicy()
	{
		foreach ( var radius in new[] { -1, GuestSimulation.MaxBinRadius + 1, int.MaxValue } )
		{
			var simulation = Row( new CountingPolicy { Radius = radius }, new ParkLitter(), [new LitterBin( 9, (4, 6), (4, 5) )] );
			var guest = simulation.SpawnInPark( 2, 5 );
			guest.Litter = 95;
			var error = Assert.ThrowsException<InvalidOperationException>( () =>
			{
				for ( var tick = 0; tick < 600; tick++ )
					simulation.Tick( Tick );
			} );
			StringAssert.Contains( error.Message, nameof( CountingPolicy ) );
			StringAssert.Contains( error.Message, radius.ToString() );
		}
		// the largest allowed radius and the original's are fine
		foreach ( var radius in new[] { 0, GuestSimulation.BinRadius, GuestSimulation.MaxBinRadius } )
		{
			var simulation = Row( new CountingPolicy { Radius = radius }, new ParkLitter(), [new LitterBin( 9, (4, 6), (4, 5) )] );
			simulation.SpawnInPark( 2, 5 ).Litter = 95;
			for ( var tick = 0; tick < 600; tick++ )
				simulation.Tick( Tick );
		}
	}

	[TestMethod]
	public void TheLongerWalkTakesTheOriginalsOwnRoute()
	{
		// the walk to a far bin is the original's bin walk: the same state, the same catchable states, the litter meter stays full until the throw
		var litter = new ParkLitter();
		var simulation = Row( new SmarterGuestDecisions( SmarterRuleSet.All ), litter, [new LitterBin( 9, (9, 6), (9, 5) )] );
		var guest = simulation.SpawnInPark( 2, 5 );
		guest.Litter = 95;
		var walked = 0;
		for ( var tick = 0; tick < 120 * 60 && guest.Litter != 0; tick++ )
		{
			simulation.Tick( Tick );
			if ( guest.State == GuestState.ThrowingAwayLitter )
			{
				walked++;
				Assert.AreEqual( 9, guest.BinId );
				Assert.AreEqual( 95, guest.Litter, "the litter is carried, not dropped" );
			}
		}
		Assert.IsTrue( walked > 20, "seven cells take a while" );
		Assert.AreEqual( 0, litter.Count );
	}

	[TestMethod]
	public void TheRuleWorksInTheScenarioAndReplaysTheSame()
	{
		(ulong Hash, long Binned, long Dropped) Run( SmarterRuleSet rules )
		{
			var park = new ParkScenario( new WorldSeed( 3 ), new ParkScenarioOptions { FoodShop = true, Litter = true, Bins = true, LitterEffect = 25, HandymanTurns = 400, ArrivalTimeBetween = 10 }, new SmarterGuestDecisions( rules ) );
			var metrics = new GuestMetrics();
			metrics.Attach( park.Guests );
			metrics.BeginWindow();
			park.Run( GuestReport.TicksForDays( 60 ) );
			var paths = metrics.EndWindow().Paths!;
			return (park.Hash, paths.LitterBinned, paths.LitterDropped);
		}
		var first = Run( SmarterRuleSet.All );
		Assert.AreEqual( first, Run( SmarterRuleSet.All ), "replays the same" );
		Assert.IsTrue( first.Binned > 0, "guests carried litter to the bins" );
		Assert.IsTrue( first.Dropped > 0, "and some litter still fell (vomit, and guests without a bin in reach)" );
		var before = Run( SmarterRuleSet.Phase6 );
		Assert.AreEqual( 0, before.Binned, "the bins stand beyond the original's reach of where the guests litter" );
		Assert.AreNotEqual( before.Hash, first.Hash );
	}

	[TestMethod]
	public void AResumedParkContinuesLikeTheUninterruptedOneWhileAGuestCarriesLitterToABin()
	{
		var bins = new[] { new LitterBin( 9, (9, 6), (9, 5) ) };
		var park = Row( new SmarterGuestDecisions( SmarterRuleSet.All ), new ParkLitter(), bins );
		park.SpawnInPark( 2, 5 ).Litter = 95;
		var guest = park.Guests.Single();
		var tick = 0;
		for ( ; tick < 120 * 60 && guest.State != GuestState.ThrowingAwayLitter; tick++ )
			park.Tick( Tick );
		for ( var more = 0; more < 30; more++, tick++ )
			park.Tick( Tick );
		Assert.AreEqual( GuestState.ThrowingAwayLitter, guest.State, "on its way to the bin" );
		var snapshot = park.CaptureSnapshot();
		Assert.AreEqual( 0, park.Litter!.Count, "nothing has fallen yet: the litter is in the guest's hands" );
		var resumed = Row( new SmarterGuestDecisions( SmarterRuleSet.All ), new ParkLitter(), bins );
		resumed.RestoreSnapshot( snapshot );
		Assert.AreEqual( park.ComputeStateHash(), resumed.ComputeStateHash() );
		var binned = (Uninterrupted: 0, Resumed: 0);
		park.LitterBinned += ( _, _ ) => binned.Uninterrupted++;
		resumed.LitterBinned += ( _, _ ) => binned.Resumed++;
		for ( var step = 0; step < 600; step++ )
		{
			park.Tick( Tick );
			resumed.Tick( Tick );
			Assert.AreEqual( park.ComputeStateHash(), resumed.ComputeStateHash(), $"the parks diverged {step} ticks after the save" );
		}
		Assert.AreEqual( (1, 1), binned, "both threw the litter into the bin after the save point" );
	}

	/// <summary>A policy of the original's behaviour that asks for another bin radius and counts being asked.</summary>
	private sealed class CountingPolicy : IGuestDecisions
	{
		private readonly OriginalGuestDecisions original = new();
		public int Radius { get; init; } = GuestSimulation.BinRadius;
		public int Asked;
		public bool UsesBinCarry => true;
		public int BinMaxRouteCells( in DecisionContext context, in GuestView guest ) => int.MaxValue;
		public int BinSearchRadius( in DecisionContext context, in GuestView guest ) { Asked++; return Radius; }
		public BoothDecision DecideBooth( in DecisionContext context, in GuestView guest, in BoothView booth ) => original.DecideBooth( context, guest, booth );
		public uint ScoreAttraction( in DecisionContext context, in GuestView guest, in AttractionView attraction ) => original.ScoreAttraction( context, guest, attraction );
		public bool BeatsInMajorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMajorDecision( context, guest, score, best );
		public bool BeatsInMinorDecision( in DecisionContext context, in GuestView guest, uint score, uint best ) => original.BeatsInMinorDecision( context, guest, score, best );
		public QueueAdmission DecideAdmission( in DecisionContext context, in GuestView guest, in AdmissionView admission ) => original.DecideAdmission( context, guest, admission );
		public QueuePatienceDecision DecideQueuePatience( in DecisionContext context, in GuestView guest, in QueuePatienceView queue ) => original.DecideQueuePatience( context, guest, queue );
		public PostUseDecision RememberUse( in DecisionContext context, in GuestView guest, in AttractionView ride ) => original.RememberUse( context, guest, ride );
		public GuestWander.Destination ChooseWanderGoal( in DecisionContext context, in GuestView guest, in GuestWander.Destination proposal ) => original.ChooseWanderGoal( context, guest, proposal );
	}
}
