using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The thing pool in a park-resume save and in a level's start (tp.exe 0x005156A0, 0x00518D43; WALK-010): a save keeps the pool and every thing names its slot; an older save builds one.</summary>
[TestClass]
public class ParkThingPoolResumeTests
{
	private static ParkResumeSnapshot WithPool( out ThingPool pool )
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		pool = new ThingPool();
		// the fixture's guests keep ids 1.. (their own pool); the park's pool holds them, the world's singletons and the one object
		pool.Restore( snapshot.Guests.Guests.Reverse().Select( guest => new ThingSlot( guest.Id, SaveThingModel.Guest ) ) );
		pool.AllocateWorldSetup();
		var objectId = pool.Allocate( SaveThingModel.Attraction );
		var layout = snapshot.Layout with { Objects = snapshot.Layout.Objects.Select( record => record with { ThingId = objectId } ).ToList() };
		return snapshot with { Things = pool.CaptureSnapshot(), Layout = layout, Guests = snapshot.Guests with { Things = null } };
	}

	[TestMethod]
	public void ASaveWithAPoolValidatesWhenEveryThingNamesItsOwnSlot()
	{
		var snapshot = WithPool( out _ );
		ParkResumeSaveFile.ValidateShape( snapshot );
		void Rejects( ParkResumeSnapshot corrupted, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( corrupted ) ).Message, message );
		Rejects( snapshot with { Layout = snapshot.Layout with { Objects = snapshot.Layout.Objects.Select( record => record with { ThingId = 0 } ).ToList() } }, "names thing id 0" );
		Rejects( snapshot with { Layout = snapshot.Layout with { Objects = snapshot.Layout.Objects.Select( record => record with { ThingId = 1 } ).ToList() } }, "not a Attraction slot" );
		Rejects( snapshot with { Things = snapshot.Things! with { Used = snapshot.Things.Used.Skip( 1 ).ToList() } }, "thing id" );
		var guest = snapshot.Guests.Guests[0];
		Rejects( snapshot with { Guests = snapshot.Guests with { Guests = snapshot.Guests.Guests.Skip( 1 ).ToArray() } }, "no thing names" );
		Rejects( snapshot with { Things = new ThingPoolSnapshot { Used = new[] { new ThingSlotData( 0, 1 ) } } }, "thing pool" );
		Assert.IsTrue( guest.Id >= 1 );
	}

	[TestMethod]
	public void ASaveFromBeforeThePoolNamesNoSlot()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		Assert.IsNull( snapshot.Things );
		ParkResumeSaveFile.ValidateShape( snapshot );
		var named = snapshot with { Layout = snapshot.Layout with { Objects = snapshot.Layout.Objects.Select( record => record with { ThingId = 3 } ).ToList() } };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( named ) ).Message, "holds no thing pool" );
	}

	private static Level PreparedLevel( ParkResumeSnapshot? resume )
	{
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		typeof( Level ).GetField( "<Objects>k__BackingField", Private )!.SetValue( level, new ParkObjects( null!, new SandboxParkGrid() ) );
		typeof( Level ).GetField( "resumeState", Private )!.SetValue( level, resume );
		typeof( Level ).GetMethod( "PrepareThings", Private )!.Invoke( level, null );
		return level;
	}

	[TestMethod]
	public void ANewParkBuildsTheWorldsTenSingletonsAndGivesTheObjectsThePool()
	{
		var level = PreparedLevel( null );
		Assert.AreEqual( 10, level.Things.Count );
		Assert.AreEqual( 11, level.Things.NextId );
		Assert.AreSame( level.Things, level.Objects.Things );
	}

	[TestMethod]
	public void AResumedSaveGetsItsOwnPoolBack()
	{
		var snapshot = WithPool( out var pool );
		var level = PreparedLevel( snapshot );
		CollectionAssert.AreEqual( pool.Capture(), level.Things.Capture() );
		Assert.AreEqual( pool.NextId, level.Things.NextId );
	}

	[TestMethod]
	public void ASaveFromBeforeThePoolKeepsItsGuestsSlotsAndBuildsTheSingletonsAroundThem()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		var level = PreparedLevel( snapshot );
		var guestIds = snapshot.Guests.Guests.Select( guest => guest.Id ).ToArray();
		Assert.IsTrue( guestIds.All( id => level.Things.IsUsed( id ) && level.Things.ModelOf( id ) == SaveThingModel.Guest ) );
		var singletons = level.Things.Capture().Where( slot => slot.Model != SaveThingModel.Guest ).Select( slot => slot.Id ).ToArray();
		Assert.AreEqual( 10, singletons.Length );
		Assert.IsFalse( singletons.Intersect( guestIds ).Any(), "no singleton takes a guest's slot" );
		Assert.AreEqual( guestIds.Length + 10 + 1, level.Things.NextId, "the guests' ids 1.. are kept and the singletons take the next ones" );
	}
}
