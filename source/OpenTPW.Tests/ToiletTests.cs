using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Toilet wear and the handyman's toilet job (tp.exe 0x004E2440, 0x004D7880, 0x004DFD80; QUEUE-008).</summary>
[TestClass]
public class ToiletTests
{
	private const int Toilet = 1402;

	[TestMethod]
	public void EachUseWearsTheToiletByTheGuestsToiletMeter()
	{
		var park = EconomyTestData.Park();
		var toilet = park.RegisterExisting( Toilet );
		var dirty = new List<ParkEvent>();
		park.EventRaised += e => { if ( e.Kind == ParkEventKind.ToiletDirty ) dirty.Add( e ); };
		Assert.IsFalse( park.UseRelief( toilet.Id, 80 ) );
		// (int64)80 × −0.05f + 100 in double precision, stored as a single
		Assert.AreEqual( (float)(80 * (double)-0.05f + 100), (float)toilet.Repair );
		// a byte meter: 300 & 0xFF = 44 → 2.2 less
		var before = (float)toilet.Repair;
		park.UseRelief( toilet.Id, 300 );
		Assert.AreEqual( (float)(44 * (double)-0.05f + before), (float)toilet.Repair );
		var uses = 0;
		while ( !park.UseRelief( toilet.Id, 100 ) )
			uses++;
		Assert.IsTrue( (int)toilet.Repair < ParkEconomy.DirtyToiletGauge );
		Assert.AreEqual( 1, dirty.Count, "the dirty message once, on the crossing" );
		for ( var i = 0; i < 40; i++ )
			park.UseRelief( toilet.Id, 255 );
		Assert.AreEqual( 0.0, toilet.Repair, "clamped at 0" );
		Assert.AreEqual( 1, dirty.Count );
		Assert.IsFalse( park.UseRelief( park.RegisterExisting( 1100 ).Id, 100 ), "a ride is no relief object" );
	}

	[TestMethod]
	public void AnOnlineVisitWearsNoToiletAndCleansADirtyOneAtOnce()
	{
		// [BIN:TPW-EXE:0x004E2478] the game-type-1 branch of the wear step
		var park = EconomyTestData.Park();
		park.OnlineVisit = true;
		var toilet = park.RegisterExisting( Toilet );
		park.PlaceObjectEffects( toilet.Id, (10, 10) );
		Assert.IsFalse( park.UseRelief( toilet.Id, 200 ) );
		Assert.AreEqual( 100.0, toilet.Repair, "a use wears nothing" );
		toilet.Repair = 10;
		toilet.MaintenanceRequested = true;
		Assert.IsFalse( park.UseRelief( toilet.Id, 200 ), "no dirty message either" );
		Assert.AreEqual( 100.0, toilet.Repair, "a dirty toilet is cleaned by the next use" );
		Assert.IsFalse( toilet.MaintenanceRequested );
		Assert.AreEqual( 0, toilet.MechanicId );
	}

	[TestMethod]
	public void AHandymanCleansADirtyToiletAfterItsWorkDuration()
	{
		var park = EconomyTestData.Park();
		var toilet = park.RegisterExisting( Toilet );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		var cleaned = 0;
		park.EventRaised += e => { if ( e.Kind == ParkEventKind.ToiletCleaned ) cleaned++; };
		while ( !park.UseRelief( toilet.Id, 255 ) ) { }
		var start = park.Turn;
		EconomyTestData.AdvanceToTurn( park, start + 1 );
		Assert.AreEqual( handyman.Id, toilet.MechanicId, "a free handyman takes the dirty toilet" );
		Assert.AreEqual( StaffState.Working, handyman.State );
		var workDuration = park.Settings[StaffType.Handyman].WorkDuration[handyman.Grade];
		// state 11 ends on the update after WorkDuration turns: the job takes WorkDuration + 1 staff updates
		EconomyTestData.AdvanceToTurn( park, start + 1 + workDuration );
		Assert.AreEqual( 0, cleaned );
		EconomyTestData.AdvanceToTurn( park, start + 2 + workDuration );
		Assert.AreEqual( 1, cleaned );
		Assert.AreEqual( 100.0, toilet.Repair );
		Assert.AreEqual( 0, toilet.MechanicId );
		Assert.AreEqual( StaffState.Patrolling, handyman.State );
	}

	[TestMethod]
	public void ACleanToiletGetsNoHandyman()
	{
		var park = EconomyTestData.Park();
		var toilet = park.RegisterExisting( Toilet );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		park.UseRelief( toilet.Id, 255 );   // 87.25: worn, not dirty
		EconomyTestData.AdvanceToTurn( park, park.Turn + 3 );
		Assert.AreEqual( 0, toilet.MechanicId );
		Assert.AreEqual( 0L, handyman.BusyTicks );
	}

	[TestMethod]
	public void SellingAToiletMidJobFreesTheHandyman()
	{
		var park = EconomyTestData.Park();
		var toilet = park.RegisterExisting( Toilet );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		while ( !park.UseRelief( toilet.Id, 255 ) ) { }
		EconomyTestData.AdvanceToTurn( park, park.Turn + 1 );
		Assert.AreEqual( toilet.Id, handyman.AssignedInstanceId );
		park.Sell( toilet.Id );
		Assert.AreEqual( 0L, handyman.BusyTicks );
		Assert.AreEqual( 0, handyman.AssignedInstanceId );
		EconomyTestData.AdvanceToTurn( park, park.Turn + 3 );
	}

	[TestMethod]
	public void AToiletJobSurvivesASaveAndLoad()
	{
		var park = EconomyTestData.Park();
		var toilet = park.RegisterExisting( Toilet );
		var handyman = EconomyTestData.HireBest( park, StaffType.Handyman );
		while ( !park.UseRelief( toilet.Id, 255 ) ) { }
		EconomyTestData.AdvanceToTurn( park, park.Turn + 2 );
		var remaining = handyman.BusyTicks;
		Assert.IsTrue( remaining > 1 );
		var json = ParkSaveFile.Serialize( park );
		var loaded = ParkSaveFile.Restore( ParkSaveFile.Deserialize( System.Text.Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.IsTrue( loaded.TryGetObject( toilet.Id, out var loadedToilet ) );
		var loadedHandyman = loaded.Staff.Members.Single( member => member.Id == handyman.Id );
		Assert.AreEqual( toilet.Id, loadedHandyman.AssignedInstanceId );
		Assert.AreEqual( handyman.Id, loadedToilet!.MechanicId );
		EconomyTestData.AdvanceToTurn( loaded, loaded.Turn + remaining );
		Assert.AreEqual( 100.0, loadedToilet.Repair, "the loaded job still cleans the toilet" );
		Assert.AreEqual( 0, loadedToilet.MechanicId );
	}
}
