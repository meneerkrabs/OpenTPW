using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Synthetic, asset-free coverage of the existing SAVE-002 rejection boundary, not linked-state restoration.
/// Unsupported guests retain their original thing slots without entering the live guest simulation.
/// </summary>
[TestClass]
public class OriginalGuestPlaceholderImportTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private static SaveGuest GuestRecord( int id, uint state )
	{
		var body = SaveTestPayloads.Guest( new SaveTestPayloads.GuestFields { State = state, PersonType = 0 } );
		var payload = new byte[8 + body.Length];
		body.CopyTo( payload, 8 );
		return SaveGuest.Read( payload, new SaveThing( id, SaveThingModel.Guest, 0, 0, 8, body.Length ) );
	}

	private static GuestSimulation Simulation( ThingPool pool )
	{
		var grid = new GuestPathGrid( 12, 12 );
		grid.SetPath( 5, 5, true );
		var settings = new GuestSettings();
		settings.Types.Add( new GuestType( 60, 600, 40 ) );
		var simulation = new GuestSimulation( grid, settings, 5 ) { ArrivalsEnabled = false };
		simulation.AttachThings( pool );
		return simulation;
	}

	[DataTestMethod]
	[DataRow( 11u )]
	[DataRow( 12u )]
	[DataRow( 13u )]
	[DataRow( 14u )]
	[DataRow( 15u )]
	[DataRow( 16u )]
	[DataRow( 17u )]
	[DataRow( 20u )] // WalkingToVehicle still needs arrival-vehicle identity and script restoration.
	public void EachUnsupportedStateReservesOnlyItsGuestSlot( uint state )
	{
		var slots = new[]
		{
			new ThingSlot( 40, SaveThingModel.Guest ),
			new ThingSlot( 42, SaveThingModel.Guest ),
			new ThingSlot( 41, SaveThingModel.Attraction )
		};
		var pool = new ThingPool();
		pool.Restore( slots );
		var simulation = Simulation( pool );
		var random = simulation.CaptureSnapshot().RandomState;
		// The record for 41 deliberately disagrees with the pool model. Rejection must not mutate that foreign slot.
		var report = simulation.ImportOriginal( new[] { GuestRecord( 42, 6 ), GuestRecord( 41, state ), GuestRecord( 40, state ) }, 755, _ => 0, _ => 0 );
		Assert.AreEqual( 42, simulation.Guests.Single().Id, "the positive-control guest still imports" );
		Assert.AreEqual( GuestState.DoingNothing, simulation.Guests.Single().State );
		Assert.AreEqual( random, simulation.CaptureSnapshot().RandomState, "rejection does not consume the guest RNG" );
		CollectionAssert.AreEqual( new[] { 40 }, pool.HeldIds().ToArray() );
		Assert.IsFalse( pool.IsHeld( 42 ), "a rebuilt guest must not become a placeholder" );
		Assert.IsFalse( pool.IsHeld( 41 ), "a rejected guest record cannot claim another model's slot" );
		Assert.AreEqual( SaveThingModel.Attraction, pool.ModelOf( 41 ) );
		StringAssert.EndsWith( report.Single( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) ),
			$"guest 40 (state {state}), guest 41 (state {state})." );
		AssertPreservedThroughResume( pool, slots, new[] { 40 } );
	}

	[TestMethod]
	public void MixedUnsupportedStatesSurvivePoolResumeWithoutRecyclingTheirIds()
	{
		var unsupported = Enumerable.Range( 11, 7 ).Append( 20 ).ToArray();
		// Deliberately non-numeric used-list order: import sorting must not reorder the saved pool.
		var slots = new[] { 17, 20, 13, 30, 11, 16, 12, 15, 14 }.Select( id => new ThingSlot( id, SaveThingModel.Guest ) ).ToArray();
		var pool = new ThingPool();
		pool.Restore( slots );
		var simulation = Simulation( pool );
		var records = unsupported.Reverse().Select( id => GuestRecord( id, (uint)id ) ).Append( GuestRecord( 30, 6 ) ).ToArray();
		var report = simulation.ImportOriginal( records, 755, _ => 0, _ => 0 );
		Assert.AreEqual( 30, simulation.Guests.Single().Id );
		var dropped = report.Single( line => line.StartsWith( "[APPROX:SAVE-002] Guests not imported:", StringComparison.Ordinal ) );
		foreach ( var state in unsupported )
			StringAssert.Contains( dropped, $"guest {state} (state {state})" );
		CollectionAssert.AreEqual( unsupported, pool.HeldIds().ToArray() );
		Assert.IsFalse( pool.IsHeld( 30 ) );
		AssertPreservedThroughResume( pool, slots, unsupported );
	}

	private static void AssertPreservedThroughResume( ThingPool pool, ThingSlot[] slots, int[] held )
	{
		CollectionAssert.AreEqual( slots, pool.UsedNewestFirst().ToArray(), "slot model and saved used-list order survive import" );
		Assert.AreEqual( slots.Length, pool.Count );
		Assert.AreEqual( 0, pool.QueuedCount, "unsupported does not mean scheduled for destruction" );
		var occupied = slots.Select( slot => slot.Id ).ToHashSet();
		var expectedFree = Enumerable.Range( 1, ThingPool.Capacity ).Where( id => !occupied.Contains( id ) ).ToArray();
		CollectionAssert.AreEqual( expectedFree, pool.FreeInOrder().ToArray(), "independent ascending-free-slot oracle" );
		var snapshot = pool.CaptureSnapshot();
		Assert.AreEqual( 0, ThingPool.Problems( snapshot ).Count );
		CollectionAssert.AreEqual( held, snapshot.Held!.ToArray() );
		var resumed = new ThingPool();
		resumed.RestoreSnapshot( snapshot );
		CollectionAssert.AreEqual( slots, resumed.UsedNewestFirst().ToArray() );
		CollectionAssert.AreEqual( held, resumed.HeldIds().ToArray() );
		CollectionAssert.AreEqual( expectedFree, resumed.FreeInOrder().ToArray() );
		// Cross the reserved IDs, rather than testing only low IDs that precede every placeholder.
		foreach ( var expected in expectedFree.Take( slots.Max( slot => slot.Id ) + 3 ) )
		{
			Assert.AreEqual( expected, pool.Allocate( SaveThingModel.Guest ) );
			Assert.AreEqual( expected, resumed.Allocate( SaveThingModel.Guest ) );
		}
		CollectionAssert.AreEqual( held, pool.HeldIds().ToArray() );
		CollectionAssert.AreEqual( held, resumed.HeldIds().ToArray() );
	}
}
