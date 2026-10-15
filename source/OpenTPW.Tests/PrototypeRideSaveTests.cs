using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The prototype part of the park-resume envelope, without game data: a park whose only attraction is the developer
/// Totem saves and reloads its part through the codec, a save from before the prototype slice (no member) still
/// validates, the guests' queue snapshot resolves the Totem through the part — and dropping the part from a park
/// whose guests still reference the Totem is refused — while doctored parts (wrong entry, wrong attraction id,
/// out-of-allocator id, a placed object's id, a non-finite position, root-script claims that do not resolve,
/// guest cells that are half-present, out of the cell map, dropped while the guests still hold the record, or
/// carry an economy instance without them, and economy associations that do not join) are each refused with
/// their reason.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PrototypeRideSaveTests
{
	private const string TotemArchive = "/levels/jungle/rides/totem";

	/// <summary>The synthetic scriptless prototype part: the Totem's identity with a hand-built runtime state and
	/// guest cells (null when the scenario tests the unregistered Totem).</summary>
	private static PrototypeRideSaveData Part( int attractionId = 1, int infoId = PrototypeRide.InfoId, string archive = TotemArchive,
		int? economyInstance = null, bool economyBought = false, (int X, int Y)? entrance = null ) => new()
	{
		AttractionId = attractionId,
		PositionX = 390f,
		PositionY = 210f,
		PositionZ = 0f,
		Open = true,
		ScriptSeed = 0,
		RootScriptId = 0,
		Runtime = ParkResumeSaveFileTests.RuntimeSnapshot( attractionId, infoId, archive ),
		EntranceCell = entrance,
		ExitCell = entrance,
		EconomyInstanceId = economyInstance,
		EconomyBought = economyBought
	};

	/// <summary>
	/// A park whose only attraction is the Totem: the miniature rig's guest snapshot (its bridge is attraction 1)
	/// over an empty layout — the prototype part is what places attraction 1.
	/// </summary>
	private static ParkResumeSnapshot TotemOnlyPark()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		return snapshot with
		{
			Layout = snapshot.Layout with { Objects = new List<ParkLayoutSnapshot.PlacedObjectData>(), NextAttractionId = 2 },
			Scripts = snapshot.Scripts with { NextAttractionId = 2 },
			Runtimes = new List<ParkResumeRuntime>(),
			EconomyBindings = new List<ParkResumeEconomyBinding>(),
			Prototype = Part( entrance: (5, 5) )
		};
	}

	[TestMethod]
	public void APrototypeOnlyParkValidatesRoundTripsAndSavesBeforeTheSliceStillLoad()
	{
		var snapshot = TotemOnlyPark();
		Assert.AreEqual( PrototypeRide.InfoId, 1110, "the part describes the Inca Totem" );
		Assert.IsTrue( string.Equals( PrototypeRide.ArchivePath, TotemArchive, StringComparison.OrdinalIgnoreCase ), "the archive path is the Totem's" );
		ParkResumeSaveFile.ValidateShape( snapshot );

		var restored = ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( snapshot ) );
		ParkResumeSaveFile.ValidateShape( restored );
		Assert.IsNotNull( restored.Prototype, "the prototype part survives the codec" );
		Assert.AreEqual( 1110, restored.Prototype!.Runtime.EntryInfoId );
		Assert.AreEqual( 390f, restored.Prototype.PositionX );
		Assert.IsNull( restored.Prototype.EconomyInstanceId );

		// A save from before the prototype slice carries no member at all and stays valid.
		var before = ParkResumeSaveFileTests.Envelope();
		Assert.IsNull( ParkResumeSaveFile.Deserialize( ParkResumeSaveFile.Serialize( before ) ).Prototype );
		ParkResumeSaveFile.ValidateShape( before );
	}

	[TestMethod]
	public void DroppingThePartLosesTheGuestsAttractionAndIsRefused()
	{
		var snapshot = TotemOnlyPark();
		var corrupted = snapshot with { Prototype = null };
		var error = Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( corrupted ) );
		StringAssert.Contains( error.Message, "attraction 1 is registered with the guests, but the park does not place it" );
	}

	[TestMethod]
	public void DoctoredPrototypePartsAreRefusedWithTheirReason()
	{
		var snapshot = TotemOnlyPark();
		void Rejects( PrototypeRideSaveData part, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateShape( snapshot with { Prototype = part } ) ).Message, message );

		Rejects( Part() with { AttractionId = 2 }, "its runtime is attraction 1" );
		Rejects( Part( infoId: 1100 ), $"not the Inca Totem ({PrototypeRide.InfoId}" );
		Rejects( Part( archive: "/levels/jungle/rides/bouncy.wad" ), "not the Inca Totem" );
		Rejects( Part( attractionId: 2 ) with { Runtime = ParkResumeSaveFileTests.RuntimeSnapshot( 2, PrototypeRide.InfoId, TotemArchive ) },
			"outside the saved allocator (1..2)" );
		Rejects( Part() with { PositionX = float.NaN }, "not finite or outside the map-scale bound" );
		Rejects( Part() with { PositionY = PrototypeRideSave.MaximumCoordinate * 2 }, "not finite or outside the map-scale bound" );
		Rejects( Part() with { RootScriptId = 7 }, "names root script 7 without a live script" );
		// A script-holding runtime whose root does not resolve in the saved world.
		Rejects( Part( attractionId: 1 ) with
		{
			Runtime = ParkResumeSaveFileTests.RuntimeSnapshot( 1, PrototypeRide.InfoId, TotemArchive ) with { HasScript = true, ScriptIdentityHash = 12345 },
			RootScriptId = 9
		}, "root script 9, which the world does not hold as a root" );
		// The guest cells: half-present, outside the cell map, dropped while the guests still hold the record, or an
		// economy instance without the registration that derived it.
		Rejects( Part( entrance: (5, 5) ) with { ExitCell = null }, "one guest cell but not the other" );
		Rejects( Part( entrance: (20, 5) ), "entrance cell (20, 5) is outside the cell map" );
		Rejects( Part(), "carries no guest cells" );
		var bound = ParkResumeSaveFileTests.Envelope();
		Rejects( Part( economyInstance: bound.EconomyBindings[0].InstanceId ), "without the guest cells" );
		// The economy joins: an instance the economy does not hold, and one that is not the Totem's.
		Rejects( Part( entrance: (5, 5), economyInstance: 99 ), "economy object 99, which the economy does not hold" );
		var drinks = ParkResumeSaveFileTests.Envelope();
		Rejects( Part( entrance: (5, 5), economyInstance: drinks.EconomyBindings[0].InstanceId ), $"not the Inca Totem's {PrototypeRide.InfoId}" );
	}

	[TestMethod]
	public void APrototypeSharingAPlacedObjectsIdOrEconomyInstanceIsRefused()
	{
		// The standard envelope places attraction 1 (bouncy, bound to its economy instance) under an allocator at 2.
		var snapshot = ParkResumeSaveFileTests.Envelope() with
		{
			Layout = ParkResumeSaveFileTests.Envelope().Layout with { NextAttractionId = 3 },
			Scripts = ParkResumeSaveFileTests.Envelope().Scripts with { NextAttractionId = 3 }
		};
		var bound = snapshot.EconomyBindings[0].InstanceId;
		void Rejects( PrototypeRideSaveData part, string message ) =>
			StringAssert.Contains( Assert.ThrowsException<InvalidDataException>(
				() => ParkResumeSaveFile.ValidateShape( snapshot with { Prototype = part } ) ).Message, message );
		Rejects( Part( attractionId: 1 ) with { Runtime = ParkResumeSaveFileTests.RuntimeSnapshot( 1, PrototypeRide.InfoId, TotemArchive ) },
			"is also a placed object's" );
		Rejects( Part( attractionId: 2, entrance: (5, 5) ) with
		{
			Runtime = ParkResumeSaveFileTests.RuntimeSnapshot( 2, PrototypeRide.InfoId, TotemArchive ),
			EconomyInstanceId = bound
		}, "is bound to a placed attraction and the prototype" );
		// The same part without an economy association is fine: the allocator covers attraction 2, and the guests hold
		// no record for it (its cells are absent, as for a Totem the live park never registered).
		ParkResumeSaveFile.ValidateShape( snapshot with
		{
			Prototype = Part( attractionId: 2, entrance: null ) with { Runtime = ParkResumeSaveFileTests.RuntimeSnapshot( 2, PrototypeRide.InfoId, TotemArchive ) }
		} );
	}
}
