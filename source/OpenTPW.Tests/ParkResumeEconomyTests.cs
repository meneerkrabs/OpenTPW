using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class ParkResumeEconomyTests
{
	[TestInitialize]
	public void Initialize() => Log ??= new();

	private static GuestSimulation Guests() => new( new GuestPathGrid( new ParkCellMap( 8, 8 ) ),
		GuestTests.CreateSettings(), 7 );

	[TestMethod]
	public void ResumeAttachesToTheRestoredEconomyWithoutOpeningAClosedPark()
	{
		var original = EconomyTestData.Park();
		original.OpenPark();
		original.ClosePark();
		var saved = ParkSaveFile.Capture( original );
		var restored = ParkSaveFile.Restore( saved, original.Settings, original.Catalog );
		var runtime = ParkEconomyRuntime.ForResume( restored, ParkStartKind.FullSimulation );
		var guests = Guests();
		var opened = 0;
		runtime.EventRaised += item => { if ( item.Kind == ParkEventKind.ParkOpened ) opened++; };
		runtime.AttachGuests( guests, openParkOnAttach: false );
		Assert.AreSame( restored, runtime.Economy );
		Assert.IsFalse( runtime.Economy.IsParkOpen );
		Assert.IsFalse( runtime.Start.ImportShippedSave );
		Assert.AreEqual( 0, opened, "resume must not manufacture a park-open event" );
		Assert.AreSame( runtime.Guests, guests.Payments );
		runtime.Economy.OpenPark();
		Assert.AreEqual( 1, opened, "events are forwarded by the new runtime exactly once" );
		Assert.IsFalse( original.IsParkOpen, "actions in the restored park do not change the old economy" );
	}

	[TestMethod]
	public void AnInPlaceLoadForwardsTheLoadedEconomysEventsOnly()
	{
		// Level subscribes to the runtime, not to an economy: LoadPark replaces the economy, and the door and the repair and upgrade
		// reopening must follow the loaded one.
		var runtime = ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation );
		var before = runtime.Economy;
		var path = Path.Combine( Path.GetTempPath(), $"opentpw-load-{Guid.NewGuid():N}.tpwpark" );
		try
		{
			runtime.Save( path );
			var events = new List<ParkEventKind>();
			runtime.EventRaised += item => events.Add( item.Kind );
			runtime.Load( path );
			Assert.AreNotSame( before, runtime.Economy, "the load replaces the economy" );
			runtime.Economy.OpenPark();
			before.ClosePark();
			CollectionAssert.AreEqual( new[] { ParkEventKind.ParkOpened }, events, "the loaded economy's events arrive, the discarded one's do not" );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public void NormalAttachmentKeepsItsExistingOpenParkBehavior()
	{
		var economy = EconomyTestData.Park();
		var runtime = ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation );
		runtime.AttachGuests( Guests() );
		Assert.IsTrue( economy.IsParkOpen );
	}

	[TestMethod]
	public void ResumeRejectsAStartThatDoesNotMatchTheSavedGameMode()
	{
		Assert.ThrowsException<InvalidDataException>( () =>
			ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.InstantAction ) );
	}
}
