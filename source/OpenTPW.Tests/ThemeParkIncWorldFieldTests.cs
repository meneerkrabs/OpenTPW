using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The meaning of the Theme Park Inc World's control records, arrivals and special rides (SAVE-941 to SAVE-943). The synthetic tests build the records the way the game's constructors
/// and serializers write them (0x5cff00, 0x5bc670, 0x623dc0, 0x6244b0); the prebuilt tests need OPENTPW_TPI_PATH.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThemeParkIncWorldFieldTests
{
	private static byte[]? prebuilt;

	private static byte[] Prebuilt()
	{
		if ( prebuilt != null )
			return (byte[])prebuilt.Clone();
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH for the Theme Park Inc prebuilt-save fixture." );
		var dataPath = Directory.Exists( Path.Combine( path!, "Data" ) ) ? Path.Combine( path!, "Data" ) : path!;
		using var reader = new SaveReader( Path.Combine( dataPath, "PreBuilt", "prebuilt.TPWS" ) );
		prebuilt = reader.ReadFile();
		return (byte[])prebuilt.Clone();
	}

	private static ThemeParkIncSaveWorld World( byte[] payload ) =>
		ThemeParkIncSaveWorld.Parse( payload, SavePayloadLayout.ParseThemeParkInc( payload ), 128, 128 );

	/// <summary>A control table the constructor 0x5cff00 would leave: ids first, then the words of each record, a count and the search key.</summary>
	private static byte[] ControlTable( params (ushort Id, ushort Padding, int Price, uint Word8, uint WordC, uint Researched, uint Level, int Built, int Tick, int Placed)[] records )
	{
		var table = new byte[ThemeParkIncObjectControls.Bytes];
		var at = 0;
		foreach ( var record in records )
		{
			BitConverter.TryWriteBytes( table.AsSpan( at ), record.Id );
			BitConverter.TryWriteBytes( table.AsSpan( at + 2 ), record.Padding );
			BitConverter.TryWriteBytes( table.AsSpan( at + 4 ), record.Price );
			BitConverter.TryWriteBytes( table.AsSpan( at + 8 ), record.Word8 );
			BitConverter.TryWriteBytes( table.AsSpan( at + 12 ), record.WordC );
			BitConverter.TryWriteBytes( table.AsSpan( at + 16 ), record.Researched );
			BitConverter.TryWriteBytes( table.AsSpan( at + 20 ), record.Level );
			BitConverter.TryWriteBytes( table.AsSpan( at + 24 ), record.Built );
			BitConverter.TryWriteBytes( table.AsSpan( at + 28 ), record.Tick );
			BitConverter.TryWriteBytes( table.AsSpan( at + 32 ), record.Placed );
			at += ThemeParkIncObjectControl.Bytes;
		}
		BitConverter.TryWriteBytes( table.AsSpan( ThemeParkIncObjectControls.Slots * ThemeParkIncObjectControl.Bytes ), (uint)records.Length );
		return table;
	}

	[TestMethod]
	public void ControlRecordWordsAreTheOnesTheConstructorAndTheGameWrite()
	{
		// a fresh record: researched when the info has no research cost, nothing built or placed, the first-build tick -1
		var fresh = ControlTable( (1101, 0, 1400, 5, 0, 1, 0, 0, -1, 0) );
		var control = ThemeParkIncObjectControls.Read( fresh, 0 ).Controls.Single();
		Assert.AreEqual( 1101, control.Id );
		Assert.AreEqual( 1400, control.Price );
		Assert.IsTrue( control.Researched );
		Assert.AreEqual( 0, control.BuiltCount );
		Assert.AreEqual( -1, control.FirstBuiltTick );
		Assert.AreEqual( 0, control.PlacementCount );

		// after a placement and a build (0x667cb0 then 0x5dba8a) and a second one: built twice, first tick kept, two placements
		var built = ControlTable( (1413, 0, 100, 0, 0, 1, 2, 2, 47, 2) );
		control = ThemeParkIncObjectControls.Read( built, 0 ).Controls.Single();
		Assert.AreEqual( 2, control.BuiltCount );
		Assert.AreEqual( 47, control.FirstBuiltTick );
		Assert.AreEqual( 2, control.PlacementCount );
		Assert.AreEqual( 2u, control.ResearchLevel );

		// a record whose objects were destroyed again: the count falls back to 0 while the tick and the placement count stay
		var destroyed = ControlTable( (1413, 0, 100, 0, 0, 1, 2, 0, 47, 2) );
		control = ThemeParkIncObjectControls.Read( destroyed, 0 ).Controls.Single();
		Assert.AreEqual( 0, control.BuiltCount );
		Assert.AreEqual( 47, control.FirstBuiltTick );
	}

	[TestMethod]
	public void ControlIdPaddingIsRawMemoryAndNotValidated()
	{
		var table = ControlTable( (1101, 0xCDCD, 1400, 5, 0, 1, 0, 0, -1, 0) );
		var control = ThemeParkIncObjectControls.Read( table, 0 ).Controls.Single();
		Assert.AreEqual( 1101, control.Id );
		Assert.AreEqual( 0xCDCD, control.IdPadding );

		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncObjectControls.Read( ControlTable( (0, 0, 0, 0, 0, 1, 0, 0, -1, 0) ), 0 ) );
		Assert.ThrowsException<InvalidDataException>( () => ThemeParkIncObjectControls.Read( ControlTable( (5, 0, 0, 0, 0, 2, 0, 0, -1, 0) ), 0 ) );
	}

	[TestMethod]
	public void ArrivalsAreTheSixFieldsOfTheSerializer()
	{
		var bytes = new byte[ThemeParkIncArrivals.Bytes];
		BitConverter.TryWriteBytes( bytes.AsSpan( 0 ), 12u );
		BitConverter.TryWriteBytes( bytes.AsSpan( 4 ), 0x01020304u );
		BitConverter.TryWriteBytes( bytes.AsSpan( 8 ), 5u );
		BitConverter.TryWriteBytes( bytes.AsSpan( 12 ), 3u );
		bytes[16] = 1;
		bytes[17] = 0;
		var arrivals = ThemeParkIncArrivals.Read( bytes, 0 );
		Assert.AreEqual( 12u, arrivals.ArrivalRate );
		Assert.AreEqual( 0x01020304u, arrivals.TimeSig );
		Assert.AreEqual( 5u, arrivals.TargetVehicleCapacity );
		Assert.AreEqual( 3u, arrivals.PeopleOnBus );
		Assert.IsTrue( arrivals.Offloading );
		Assert.IsFalse( arrivals.GatesOpen );
		Assert.AreEqual( 18, ThemeParkIncSaveWorld.ArrivalBytes );
	}

	[TestMethod]
	public void SpecialRideRecordsHaveTwentySlotsOfThingIdAbilityAndType()
	{
		var slots = new byte[200];
		for ( var slot = 0; slot < 20; slot++ )
		{
			BitConverter.TryWriteBytes( slots.AsSpan( slot * 10 + 2 ), 0u );
			BitConverter.TryWriteBytes( slots.AsSpan( slot * 10 + 6 ), ThemeParkIncSpecialRideSlot.UnusedType );
		}
		BitConverter.TryWriteBytes( slots.AsSpan( 0 ), (ushort)9 );
		BitConverter.TryWriteBytes( slots.AsSpan( 2 ), 14u );
		BitConverter.TryWriteBytes( slots.AsSpan( 6 ), 5u );
		var record = new ThemeParkIncSpecialRide( 0, 17, slots );
		Assert.AreEqual( 20, record.Entries.Count );
		Assert.AreEqual( new ThemeParkIncSpecialRideSlot( 9, 14, 5 ), record.Entries[0] );
		Assert.IsTrue( record.Entries[0].IsUsed );
		Assert.AreEqual( 19, record.Entries.Count( slot => !slot.IsUsed ) );
		Assert.AreEqual( 202, ThemeParkIncSpecialRide.Bytes );
		// the game's table holds four records before its own count word
		Assert.AreEqual( 4, ThemeParkIncSaveWorld.MaximumSpecialRides );
		Assert.AreEqual( 0xaa071c - 0xaa0330, 4 * 0xf4 + 0x1c );
	}

	[TestMethod]
	public void PrebuiltControlsCarryBuiltCountsTicksAndPlacements()
	{
		var world = World( Prebuilt() );
		var controls = world.ObjectControls.Controls;
		// prebuilt objects were made at tick 0 and never placed by the player; the two the player placed carry their placement count and a later first tick
		var byId = controls.ToDictionary( control => control.Id );
		Assert.AreEqual( 1, byId[1101].BuiltCount );
		Assert.AreEqual( 33, byId[1101].FirstBuiltTick );
		Assert.AreEqual( 1, byId[1101].PlacementCount );
		Assert.AreEqual( 2, byId[1413].BuiltCount );
		Assert.AreEqual( 47, byId[1413].FirstBuiltTick );
		Assert.AreEqual( 2, byId[1413].PlacementCount );
		Assert.AreEqual( 1, byId[1180].BuiltCount );
		Assert.AreEqual( 0, byId[1180].FirstBuiltTick );
		Assert.AreEqual( 0, byId[1180].PlacementCount );
		// an object never built keeps the constructor's -1 and every record's price is a plain non-negative cost
		Assert.IsTrue( controls.Where( control => control.BuiltCount == 0 ).All( control => control.FirstBuiltTick == -1 ) );
		Assert.IsTrue( controls.Where( control => control.BuiltCount > 0 ).All( control => control.FirstBuiltTick >= 0 ) );
		Assert.IsTrue( controls.All( control => control.Price >= 0 && control.IdPadding == 0 ) );
		Assert.IsTrue( controls.All( control => control.PlacementCount <= control.BuiltCount ) );
	}

	[TestMethod]
	public void PrebuiltCalendarFollowsTheSerializerOrder()
	{
		var payload = Prebuilt();
		var world = World( payload );
		var calendar = world.Calendar;
		// mFunnyTimeStart, mSessionStart, mYearAtLastUpdate, mMonthAtLastUpdate, mDayAtLastUpdate, mFunnySecsPerRealSec in file order
		var at = calendar.Offset;
		Assert.AreEqual( calendar.FunnyTimeStart, BitConverter.ToInt64( payload, at ) );
		Assert.AreEqual( calendar.SessionStart, BitConverter.ToInt64( payload, at + 8 ) );
		Assert.AreEqual( 2000, BitConverter.ToInt32( payload, at + 16 ) );
		Assert.AreEqual( 0, BitConverter.ToInt32( payload, at + 20 ) );
		Assert.AreEqual( 4, BitConverter.ToInt32( payload, at + 24 ) );
		Assert.AreEqual( 15000, BitConverter.ToInt32( payload, at + 28 ) );
		// the session starts the game after the funny clock's start
		Assert.IsTrue( calendar.SessionStart > calendar.FunnyTimeStart );
	}

	[TestMethod]
	public void PrebuiltArrivalsAndSpecialRidesAreTyped()
	{
		var world = World( Prebuilt() );
		var arrivals = world.ArrivalState;
		Assert.AreEqual( world.ArrivalOffset, arrivals.Offset );
		Assert.AreEqual( 0u, arrivals.ArrivalRate );
		Assert.AreEqual( 0u, arrivals.TimeSig );
		Assert.AreEqual( 5u, arrivals.TargetVehicleCapacity );
		Assert.AreEqual( 0u, arrivals.PeopleOnBus );
		Assert.IsFalse( arrivals.Offloading );
		Assert.IsTrue( arrivals.GatesOpen );

		CollectionAssert.AreEqual( new ushort[] { 17, 18, 19, 20 }, world.SpecialRides.Select( ride => ride.Id ).ToArray() );
		var first = world.SpecialRides[0];
		Assert.AreEqual( new ThemeParkIncSpecialRideSlot( 0, 5, 1 ), first.Entries[0] );
		Assert.AreEqual( new ThemeParkIncSpecialRideSlot( 0, 14, 5 ), first.Entries[1] );
		Assert.IsTrue( first.Entries.Skip( 2 ).All( slot => !slot.IsUsed && slot.Ability == 0 && slot.ThingId == 0 ) );
		foreach ( var ride in world.SpecialRides )
			Assert.IsTrue( ride.Entries.Any( slot => slot.IsUsed ), ride.Id.ToString() );
	}

	[TestMethod]
	public void MoreThanFourSpecialRidesAreRefused()
	{
		var payload = Prebuilt();
		var world = World( payload );
		var copy = (byte[])payload.Clone();
		copy[world.SpecialRidesOffset] = 5;
		Assert.ThrowsException<InvalidDataException>( () => World( copy ) );
	}
}
