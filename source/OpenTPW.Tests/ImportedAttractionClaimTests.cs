using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class ImportedAttractionClaimTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	private sealed class EmptySweep : IThingSweep
	{
		public void Run( ParkEconomy economy, long turn, bool lost ) { }
	}
	private sealed class NeverArrived : IMechanicJobWorld
	{
		public bool HasArrived( StaffMember member ) => false;
	}

	private static (ParkEconomy Park, OriginalEconomyImport Import) Import( ushort claimant = 40, uint stamp = 700, SaveThingModel role = SaveThingModel.Mechanic, bool active = false, int infoId = 1100 )
	{
		var settings = OriginalSaveImportTests.Settings();
		var body = SaveTestPayloads.Attraction( new SaveTestPayloads.AttractionFields
		{
			PositionX = 384, PositionY = 384, InfoId = (ushort)infoId, State = infoId == 1402 ? 0u : 1u, Repair = infoId == 1402 ? 10 : 40, LifeGauge = 90,
			AssignedStaff = claimant, MarkedTurn = stamp
		} );
		var save = OriginalSaveImportTests.Park( settings, bellyBounce: body, placedInfoId: infoId, extraThings: new[]
		{
			(40u, (uint)role, SaveTestPayloads.Staff( role, state: active ? 13u : 0u, position: (384, 384), role: active ? new byte[] { 3, 0, 0, 0, 12, 0 } : null )),
			(41u, (uint)(role == SaveThingModel.Handyman ? role : SaveThingModel.Mechanic), SaveTestPayloads.Staff( role == SaveThingModel.Handyman ? role : SaveThingModel.Mechanic, state: 0, position: (384, 384) ))
		} );
		var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
		return (park, OriginalEconomyImport.Apply( park, save ));
	}
	private static ParkEconomy Roundtrip( ParkEconomy park ) => ParkSaveFile.Restore(
		ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
	private static ParkObjectState Ride( ParkEconomy park ) => park.Objects.Single( item => item.InfoId == 1100 );
	private static StaffMember Member( ParkEconomy park, int thing ) => park.Staff.Members.Single( member => member.ThingId == thing );
	private static ParkObjectState? Find( ParkEconomy park, StaffMember member )
	{
		park.MechanicCursor = 0;
		return park.FindRideForMechanic( member, (1, 1), _ => (1, 1) );
	}

	[TestMethod]
	public void ImportedIdleLeaseSurvivesOwnSaveAndWorldAttachmentUntilTurn101()
	{
		var (original, import) = Import();
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-006]" ) ) );
		foreach ( var park in new[] { original, Roundtrip( original ) } )
		{
			var ride = Ride( park ); var owner = Member( park, 40 ); var other = Member( park, 41 );
			Assert.AreEqual( (owner.Id, 700L), (ride.MechanicId, ride.ClaimTurn) );
			Assert.AreNotEqual( 40, owner.Id, "the saved thing identity maps to the roster identity" );
			Assert.AreEqual( 0, owner.RideId ); Assert.AreEqual( 0, owner.AssignedInstanceId );
			var grid = new GuestPathGrid( 3, 3 ); grid.SetPath( 1, 1, true );
			var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => (1, 1), (1, 1) );
			park.StaffWorld = world;
			world.BeginStaffTurn( park, park.Turn );
			Assert.AreEqual( (owner.Id, 700L), (ride.MechanicId, ride.ClaimTurn), "attachment does not re-age the idle lease" );
			park.ThingSweep = new EmptySweep(); // inspect the calendar boundary without making the idle staff search
			Assert.IsNull( Find( park, other ) ); Assert.AreEqual( ride.Id, Find( park, owner )?.Id );
			EconomyTestData.AdvanceToTurn( park, 800 );
			Assert.IsNull( Find( park, other ), "exactly 100 turns remains reserved" );
			EconomyTestData.AdvanceToTurn( park, 801 );
			Assert.AreEqual( (owner.Id, 700L), (ride.MechanicId, ride.ClaimTurn), "hourly headless bookkeeping must not query a world lease" );
			Assert.AreEqual( ride.Id, Find( park, other )?.Id );
			Assert.AreEqual( (0, 0L), (ride.MechanicId, ride.ClaimTurn) );
		}
	}

	[TestMethod]
	public void HeadlessDispatchExpiresAnImportedIdleLeaseAfterSaveAndHundredTurns()
	{
		var (park, _) = Import();
		Member( park, 40 ).State = StaffState.PickedUp; // only the second mechanic can take the released job
		park = Roundtrip( park );
		var ride = Ride( park ); var owner = Member( park, 40 ); var other = Member( park, 41 );
		EconomyTestData.AdvanceToTurn( park, 800 );
		Assert.AreEqual( owner.Id, ride.MechanicId ); Assert.AreEqual( 0, other.AssignedInstanceId );
		EconomyTestData.AdvanceToTurn( park, 801 );
		Assert.AreEqual( other.Id, ride.MechanicId ); Assert.AreEqual( ride.Id, other.AssignedInstanceId );
		Assert.IsTrue( other.BusyTicks > 0 );
	}

	[TestMethod]
	public void ImportedIdleHandymanLeaseExpiresBeforeHeadlessToiletAssignment()
	{
		var (park, _) = Import( role: SaveThingModel.Handyman, infoId: 1402 );
		Member( park, 40 ).State = StaffState.PickedUp;
		park = Roundtrip( park );
		var toilet = park.Objects.Single( item => item.InfoId == 1402 );
		var owner = Member( park, 40 ); var other = Member( park, 41 );
		Assert.AreEqual( (owner.Id, 700L), (toilet.MechanicId, toilet.ClaimTurn) );
		EconomyTestData.AdvanceToTurn( park, 800 );
		Assert.AreEqual( owner.Id, toilet.MechanicId ); Assert.AreEqual( 0, other.AssignedInstanceId );
		EconomyTestData.AdvanceToTurn( park, 801 );
		Assert.AreEqual( other.Id, toilet.MechanicId ); Assert.AreEqual( toilet.Id, other.AssignedInstanceId );
	}

	[TestMethod]
	public void ActiveHeadlessHandymanKeepsTheToiletBeyondHundredTurns()
	{
		var park = EconomyTestData.Park();
		var toilet = park.RegisterExisting( 1402 ); toilet.Repair = 10;
		var owner = EconomyTestData.HireBest( park, StaffType.Handyman );
		var other = EconomyTestData.HireBest( park, StaffType.Handyman );
		EconomyTestData.AdvanceToTurn( park, 1 );
		Assert.AreEqual( owner.Id, toilet.MechanicId );
		owner.BusyTicks = 300;
		EconomyTestData.AdvanceToTurn( park, 110 );
		Assert.AreEqual( owner.Id, toilet.MechanicId ); Assert.AreEqual( toilet.Id, owner.AssignedInstanceId );
		Assert.AreEqual( 0, other.AssignedInstanceId ); Assert.IsTrue( owner.BusyTicks > 0 );
	}

	[TestMethod]
	public void AClaimByAThingTheImportDoesNotHoldIsKeptAsSavedAndLapsesLazily()
	{
		// [BIN:TPW-EXE:0x004E0220 claim lapse] the claimant thing 99 is no staff member: the claim stays on the attraction as saved, blocks the finder for the other mechanic through turn 800 and lapses at the first query after it
		var (park, import) = Import( 99, 700 );
		var ride = Ride( park ); var other = Member( park, 41 );
		Assert.AreEqual( (-99, 700L), (ride.MechanicId, ride.ClaimTurn) );
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-006]" ) ) );
		Assert.IsTrue( import.Evidence.Any( line => line.Contains( "claim by thing 99 on turn 700 imported as saved" ) && line.Contains( "no imported staff member" ) ) );
		park = Roundtrip( park );
		ride = Ride( park ); other = Member( park, 41 );
		Assert.AreEqual( (-99, 700L), (ride.MechanicId, ride.ClaimTurn), "the own save keeps the claim" );
		park.ThingSweep = new EmptySweep();
		EconomyTestData.AdvanceToTurn( park, 800 );
		Assert.IsNull( Find( park, other ), "exactly 100 turns remain reserved" );
		EconomyTestData.AdvanceToTurn( park, 801 );
		Assert.AreEqual( ride.Id, Find( park, other )?.Id );
		Assert.AreEqual( (0, 0L), (ride.MechanicId, ride.ClaimTurn) );
	}

	[TestMethod]
	public void AClaimByAMemberOfAnotherRoleIsKeptAndLapsesLazily()
	{
		// [BIN:TPW-EXE:0x00506580 claimant test] only a mechanic's ride +0x218 or a handyman's toilet +0x21A can hold a claim past its turn + 100: a guard's claim is imported as saved and lapses on the query
		var (park, import) = Import( 40, 700, SaveThingModel.Guard );
		var ride = Ride( park ); var guard = Member( park, 40 );
		Assert.AreEqual( (guard.Id, 700L), (ride.MechanicId, ride.ClaimTurn) );
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-006]" ) ) );
		park.ThingSweep = new EmptySweep();
		EconomyTestData.AdvanceToTurn( park, 800 );
		Assert.AreEqual( guard.Id, park.Claimant( ride ) );
		EconomyTestData.AdvanceToTurn( park, 801 );
		Assert.AreEqual( 0, park.Claimant( ride ) );
		Assert.AreEqual( (0, 0L), (ride.MechanicId, ride.ClaimTurn) );
	}

	[TestMethod]
	public void AClaimStampAheadOfTheSavesTurnIsTakenAsMadeNow()
	{
		// a stamp after the save's own clock cannot come from the game; the own save's validation refuses one, so it is imported as made on the save's turn and reported
		var (park, import) = Import( 40, 756 );
		var ride = Ride( park );
		Assert.AreEqual( (Member( park, 40 ).Id, park.Turn), (ride.MechanicId, ride.ClaimTurn) );
		Assert.IsTrue( import.Evidence.Any( line => line.Contains( "claim stamp 756 lies after the save's turn" ) ) );
		Assert.IsFalse( import.Evidence.Any( line => line.Contains( "[APPROX:SAVE-006]" ) ) );
		Assert.AreEqual( ride.MechanicId, Roundtrip( park ).Objects.Single( item => item.InfoId == 1100 ).MechanicId );
	}

	[TestMethod]
	public void ActiveImportedJobStillTakesItsClaimOnlyAtWorldAttachment()
	{
		var (original, _) = Import( active: true );
		var park = Roundtrip( original );
		var owner = Member( park, 40 ); var ride = Ride( park );
		Assert.AreEqual( 0, ride.MechanicId );
		Assert.AreEqual( 700L, owner.ResumeClaimTurn );
		Assert.AreEqual( StaffJob.Repairing, owner.Job );
		var grid = new GuestPathGrid( 3, 3 ); grid.SetPath( 1, 1, true );
		var world = new StaffWorldSimulation( grid, () => park.Staff.Members, _ => (1, 1), (1, 1) );
		park.StaffWorld = world;
		world.BeginStaffTurn( park, park.Turn );
		Assert.AreEqual( (owner.Id, 700L), (ride.MechanicId, ride.ClaimTurn) );
		Assert.IsNull( owner.ResumeClaimTurn );
	}

	[TestMethod]
	public void AnAlreadyExpiredLeaseUsesTheExistingLazyRead()
	{
		var (park, _) = Import( stamp: 654 );
		Assert.AreEqual( 654L, Ride( park ).ClaimTurn );
		Assert.AreEqual( 0, park.Claimant( Ride( park ) ) );
		Assert.AreEqual( 0L, Ride( park ).ClaimTurn );
	}

	[DataTestMethod]
	[DataRow( "long" )]
	[DataRow( "awaiting" )]
	[DataRow( "animation" )]
	public void ActiveHeadlessJobKeepsItsOwnerBeyondHundredTurns( string mode )
	{
		var park = EconomyTestData.Park();
		var ride = park.RegisterExisting( 1100 ); ride.IsBrokenDown = true; ride.Repair = 40;
		var owner = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var other = EconomyTestData.HireBest( park, StaffType.Mechanic );
		if ( mode == "awaiting" ) park.MechanicJobWorld = new NeverArrived();
		EconomyTestData.AdvanceToTurn( park, 1 );
		Assert.AreEqual( owner.Id, ride.MechanicId );
		owner.BusyTicks = mode == "animation" ? 1 : 300;
		if ( mode == "animation" ) { ride.ScriptHosted = true; ride.ScriptBroken = true; }
		EconomyTestData.AdvanceToTurn( park, 110 );
		Assert.AreEqual( owner.Id, ride.MechanicId ); Assert.AreEqual( ride.Id, owner.AssignedInstanceId );
		Assert.AreEqual( 0, other.AssignedInstanceId ); Assert.IsTrue( owner.BusyTicks > 0 );
		if ( mode == "animation" ) Assert.AreEqual( 1L, owner.BusyTicks );
		if ( mode == "awaiting" ) { Assert.IsTrue( owner.AwaitingJobArrival ); Assert.AreEqual( 300L, owner.BusyTicks ); }
	}
}
