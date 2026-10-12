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
