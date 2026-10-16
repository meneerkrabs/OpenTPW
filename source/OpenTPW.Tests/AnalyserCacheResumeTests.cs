using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The analyser's caches (the net worth, the 4 × 4 block litter and path counts) are saved with the park, so a resumed park reads the same stale counts
/// as the uninterrupted run (docs/DETERMINISM.md: exact continuation).
/// </summary>
[TestClass]
[DoNotParallelize]
public class AnalyserCacheResumeTests
{
	private const int HalfRun = 3000;

	private sealed class LitteredPark
	{
		private readonly GuestEconomyBridge payments;

		public LitteredPark()
		{
			Economy = EconomyTestData.Park( seed: 11 );
			Economy.Speed = GameSpeed.Fastest;
			Grid = GuestTests.CreateCross();
			Guests = new GuestSimulation( Grid, GuestTests.CreateSettings(), 23 );
			payments = new GuestEconomyBridge( () => Economy, Guests );
			Guests.Payments = payments;
			Guests.ArrivalClock = () => Economy.Turn;
			Attach();
			Economy.OpenPark();
			// litter on and beside the paths, in the blocks the guests walk through
			foreach ( var (x, y) in new[] { (9, 5), (11, 5), (10, 4), (10, 6), (3, 5), (16, 5), (10, 1), (10, 8) } )
				Economy.Litter.Drop( x, y, (LitterType)1 );
		}

		public ParkEconomy Economy { get; private set; }
		public GuestPathGrid Grid { get; }
		public GuestSimulation Guests { get; }
		public List<string> Thoughts { get; } = new();
		private long ticks;

		private void Attach()
		{
			Economy.GuestStatistics = payments;
			Economy.RideOperations = payments;
			Economy.Litter.Cells = Grid.Cells;
		}

		public void Run( int count )
		{
			for ( var tick = 0; tick < count; tick++ )
			{
				Guests.Tick( 1f / FixedStepClock.TicksPerSecond );
				Economy.Advance( (int)Economy.Speed );
				// the loans dialog reads the net worth through its cache (age 20) now and then
				if ( ++ticks % 1700 == 0 )
					Economy.RefreshLoanOffers();
				foreach ( var guest in Guests.Guests.Where( guest => guest.Thought != GuestThought.None ) )
					Thoughts.Add( $"{ticks}:{guest.Id}:{guest.Thought}" );
			}
		}

		public void SaveAndLoad()
		{
			var path = Path.Combine( Path.GetTempPath(), $"opentpw-analyser-{Guid.NewGuid():N}.json" );
			try
			{
				ParkSaveFile.Save( path, Economy );
				var (loaded, _) = ParkSaveFile.LoadState( path, ( theme, easy ) => (Economy.Settings, Economy.Catalog) );
				Economy = loaded;
				Attach();
			}
			finally
			{
				File.Delete( path );
			}
		}

		public string Caches => string.Join( ";", Economy.Litter.LitteredBlockCache ) + "|" + string.Join( ";", Economy.Litter.PathBlockCache ) + "|" + Economy.CachedNetWorth;
	}

	[TestMethod]
	public void AResumedParkReadsTheSameCachedCountsAsTheUninterruptedRun()
	{
		var reference = new LitteredPark();
		reference.Run( 2 * HalfRun );
		var resumed = new LitteredPark();
		resumed.Run( HalfRun );
		var before = resumed.Caches;
		Assert.AreNotEqual( "||", before, "the run filled the caches" );
		resumed.SaveAndLoad();
		Assert.AreEqual( before, resumed.Caches, "the load restores every cache with its stamp" );
		resumed.Run( HalfRun );
		Assert.IsTrue( reference.Thoughts.Count > 0, "guests thought in the run" );
		CollectionAssert.AreEqual( reference.Thoughts, resumed.Thoughts );
		Assert.AreEqual( reference.Caches, resumed.Caches );
		Assert.AreEqual( reference.Economy.NetWorth( 20 ), resumed.Economy.NetWorth( 20 ) );
	}
}
