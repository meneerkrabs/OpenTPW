using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] The command layer's serialization (docs/COOP.md, "Serialization"): every command kind has pinned bytes (the schema), every command round-trips
/// byte-exactly, and malformed or out-of-range bytes are refused. A change of any pinned bytes needs a new <see cref="ParkCommandCodec.Version"/>.
/// </summary>
[TestClass]
public class ParkCommandCodecTests
{
	/// <summary>One sample of every command kind and its bytes under format version 1. Never edit a row: a new field is a new version.</summary>
	internal static readonly (ParkCommand Command, string Hex)[] Schema =
	{
		(new PlaceObjectCommand( "data:/levels/jungle/rides/totem", 12, 7, 90, Moving: true ) { Player = 1 }, "0201023e646174613a2f6c6576656c732f6a756e676c652f72696465732f746f74656d180eb4010101"),
		(new RemoveAtCommand( 3, 4 ), "0202000608"),
		(new RemoveObjectAtCommand( 5, 6 ) { Player = 2 }, "0203040a0c"),
		(new RemoveAttractionCommand( 17 ), "02040022"),
		(new PickUpObjectCommand( 42 ) { Player = 3 }, "02050654"),
		(new LayLineCommand( CellToolMode.Queue, 9, 1, 2, 1, 9 ), "020a00031202040212"),
		(new UndoLineCommand( CellToolMode.Path, -1, 4, 5, new[] { (4, 5), (5, 5) }, new[] { (6, 5) } ) { Player = 1 }, "020b020101080a04080a0a0a020c0a"),
		(new RemovePathCellCommand( 8, 9 ), "020c001012"),
		(new RemoveQueueCellCommand( 10, 11 ), "020d001416"),
		(new BacktrackCommand( 1, 2 ), "020e000204"),
		(new EditQueueCommand( 3, 3 ), "020f000606"),
		(new QueueSegmentCommand( 9, 1, 2, 1, 9 ), "0410001202040212"),
		(new UndoQueueSegmentCommand( 9, 1, 2, 1, 9 ), "0411001202040212"),
		(new PlacePrototypeCommand( 1.5f, -2.25f, 0f ), "0214000000c03f000010c000000000"),
		(new RemovePrototypeCommand(), "021500"),
		(new DismissFinaleCommand(), "021700"),
		(new SetPrototypeRunningCommand( true ) { Player = 1 }, "02160201"),
		// the park's settings (C1 part 2)
		(new SetParkOpenCommand( true ), "021e0001"),
		(new SetEntranceFeeCommand( 35 ) { Player = 1 }, "021f0246"),
		(new SetAttractionOpenCommand( 17, false ), "0220002200"),
		(new SetRideSettingsCommand( 17, -12, 5, 30 ), "02210022170a3c"),
		(new SetPriceCommand( 18, 120 ), "02220024f001"),
		(new SetGamePrizeCommand( 19, 80, 300, 40 ), "02230026a001d80450"),
		(new SetShopSettingsCommand( 18, 100, 70 ), "02240024c8018c01"),
		(new SetTrainingBudgetCommand( StaffType.Mechanic, 250 ), "02250002f403"),
		(new SetResearchEffortCommand( ResearchCategory.Shop, 60 ), "0226000278"),
		(new SetResearchWorkloadCommand( 75 ), "0227009601"),
		(new MoveResearchCommand( ResearchCategory.Ride, -1 ), "0228000001"),
		(new TakeLoanCommand( 2, 18000 ), "02290004a09902"),
		(new RepayLoanCommand( 2, 9000 ), "022a0004d08c01"),
		(new BuyUpgradeCommand( 17 ), "022b0022"),
		(new CancelUpgradeCommand( 17 ), "022c0022"),
		(new RequestMaintenanceCommand( 17, true ), "022d002201"),
		(new AnswerChallengeCommand( 3, 541, false ), "022e0006ba0800"),
		(new RenameStaffCommand( 3, "Zo\u00EB" ) { Player = 2 }, "022f0406085a6fc3ab"),
		(new FireStaffCommand( 3 ), "02300006"),
		(new SetPatrolAreaCommand( 3, 20, 4, 12, 9 ), "023100062808181200"),
		(new RenameParkCommand( "Kevin the", "Cosmic Cow" ), "023200124b6576696e2074686514436f736d696320436f77"),
		(new SetGameSpeedCommand( GameSpeed.Fastest ), "02330008"),
		(new QueueCheatCommand( CheatKind.Ticket, 4 ), "0234000608"),
		(new RefreshLoanOffersCommand(), "023500"),
		(new SetAllObjectsOpenCommand( true ), "02190001"),
	};

	[TestMethod]
	public void EveryCommandKindHasPinnedBytesUnderTheCurrentVersion()
	{
		Assert.AreEqual( 2, ParkCommandCodec.Version, "a new version adds a new schema table beside this one" );
		CollectionAssert.AreEquivalent( Enum.GetValues<ParkCommandKind>(), Schema.Select( row => row.Command.Kind ).ToArray(), "one schema row per kind, no kind without one" );
		var types = typeof( ParkCommand ).Assembly.GetTypes().Where( type => type.IsSubclassOf( typeof( ParkCommand ) ) && !type.IsAbstract ).ToArray();
		CollectionAssert.AreEquivalent( types, Schema.Select( row => row.Command.GetType() ).ToArray(), "every command type has a schema row" );
		foreach ( var (command, hex) in Schema )
			Assert.AreEqual( hex, ParkCommandCodec.ToHex( command ), command.Kind.ToString() );
	}

	[TestMethod]
	public void EveryCommandRoundTripsByteExactly()
	{
		foreach ( var (command, hex) in Schema )
		{
			var bytes = Convert.FromHexString( hex );
			var decoded = ParkCommandCodec.Decode( bytes );
			Assert.AreEqual( command, decoded, command.Kind.ToString() );
			Assert.AreEqual( command.Player, decoded.Player );
			CollectionAssert.AreEqual( bytes, ParkCommandCodec.Encode( decoded ), command.Kind.ToString() );
		}
	}

	[TestMethod]
	public void ValuesAtTheEdgesOfTheirRangeRoundTrip()
	{
		var edges = new ParkCommand[]
		{
			new PlaceObjectCommand( "mod:é/ü", ParkCommandCodec.MaximumCoordinate - 1, 0, 270, false, int.MaxValue ) { Player = int.MaxValue },
			new UndoLineCommand( CellToolMode.Queue, int.MaxValue, 0, 0, Enumerable.Range( 0, ParkCommandCodec.MaximumCells ).Select( i => (i, i) ).ToArray(), Array.Empty<(int X, int Y)>() ),
			new PlacePrototypeCommand( float.MaxValue, -0f, float.Epsilon ),
		};
		foreach ( var command in edges )
		{
			var bytes = ParkCommandCodec.Encode( command );
			Assert.AreEqual( command, ParkCommandCodec.Decode( bytes ) );
			CollectionAssert.AreEqual( bytes, ParkCommandCodec.Encode( ParkCommandCodec.Decode( bytes ) ) );
		}
		Assert.AreEqual( BitConverter.SingleToInt32Bits( -0f ), BitConverter.SingleToInt32Bits( ((PlacePrototypeCommand)ParkCommandCodec.Decode( ParkCommandCodec.Encode( edges[2] ) )).Y ), "floats keep their bit pattern" );
	}

	/// <summary>Bytes that are not exactly one well-formed command: every truncation of every sample, an extra byte, another version, an unknown kind, a padded varint, bad fields.</summary>
	[TestMethod]
	public void MalformedAndOutOfRangeBytesAreRefused()
	{
		foreach ( var (command, hex) in Schema )
		{
			var bytes = Convert.FromHexString( hex );
			for ( var length = 0; length < bytes.Length; length++ )
				Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.AsSpan( 0, length ) ), $"{command.Kind} cut to {length} bytes" );
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.Append( (byte)0 ).ToArray() ), $"{command.Kind} with a trailing byte" );
			var otherVersion = bytes.ToArray();
			otherVersion[0] = 6;
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( otherVersion ), $"{command.Kind} as version 3" );
		}
		var bad = new[]
		{
			"020000", // kind 0
			"02ff00", // kind 255
			"02180000", // a kind no command has
			"02160002", // a prototype run flag that is not a boolean
			"02028000", // a padded player varint
			"020201", // a negative player
			"0202000608" + "", // valid, the control
		};
		foreach ( var hex in bad[..^1] )
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( Convert.FromHexString( hex ) ), hex );
		ParkCommandCodec.Decode( Convert.FromHexString( bad[^1] ) );
		var outOfRange = new ParkCommand[]
		{
			new PlaceObjectCommand( "x", 1, 1, 45 ),
			new PlaceObjectCommand( "", 1, 1, 0 ),
			new PlaceObjectCommand( "x", -1, 1, 0 ),
			new PlaceObjectCommand( "x", 1, ParkCommandCodec.MaximumCoordinate, 0 ),
			new PlaceObjectCommand( "x", 1, 1, 0, AddOnRide: -2 ),
			new RemoveAtCommand( -1, 0 ),
			new RemoveAttractionCommand( -1 ),
			new LayLineCommand( CellToolMode.Path, 3, 0, 0, 1, 0 ),
			new LayLineCommand( CellToolMode.Queue, -1, 0, 0, 1, 0 ),
			new LayLineCommand( CellToolMode.KartTrack, -1, 0, 0, 1, 0 ),
			new UndoLineCommand( CellToolMode.Path, -1, 0, 0, new[] { (-1, 0) }, Array.Empty<(int X, int Y)>() ),
			new PlacePrototypeCommand( float.NaN, 0, 0 ),
			new PlacePrototypeCommand( 0, float.PositiveInfinity, 0 ),
			new SetEntranceFeeCommand( -1 ),
			new SetAttractionOpenCommand( -1, true ),
			new SetRideSettingsCommand( 1, short.MaxValue + 1, 0, 0 ),
			new SetRideSettingsCommand( 1, 0, -1, 0 ),
			new SetRideSettingsCommand( 1, 0, 0, 256 ),
			new SetPriceCommand( 1, -1 ),
			new SetPriceCommand( 1, OpenTPW.Hud.ParkHud.MaximumPrice + 1 ),
			new SetGamePrizeCommand( 1, 0, OpenTPW.Hud.ParkHud.MaximumPrize + 1, 0 ),
			new SetGamePrizeCommand( 1, 0, 0, 101 ),
			new SetShopSettingsCommand( 1, 256, 0 ),
			new SetShopSettingsCommand( 1, 0, 101 ),
			new SetTrainingBudgetCommand( (StaffType)42, 1 ),
			new SetTrainingBudgetCommand( StaffType.Guard, -1 ),
			new SetResearchEffortCommand( ResearchCategory.Ride, 101 ),
			new SetResearchEffortCommand( (ResearchCategory)77, 1 ),
			new SetResearchWorkloadCommand( -1 ),
			new MoveResearchCommand( ResearchCategory.Ride, 2 ),
			new TakeLoanCommand( -1, 0 ),
			new TakeLoanCommand( 1, -1 ),
			new RepayLoanCommand( -1, 0 ),
			new RepayLoanCommand( 1, -1 ),
			new CancelUpgradeCommand( -1 ),
			new AnswerChallengeCommand( -1, 0, true ),
			new SetTrainingBudgetCommand( StaffType.Guard, OpenTPW.Hud.ParkHud.TrainingBudgetMaximum + 1 ),
			new BuyUpgradeCommand( -1 ),
			new RequestMaintenanceCommand( -1, true ),
			new RenameStaffCommand( -1, "Ann" ),
			new FireStaffCommand( -1 ),
			new SetPatrolAreaCommand( 1, -1, 0, 0, 0 ),
			new SetPatrolAreaCommand( 1, 0, 0, ParkCommandCodec.MaximumCoordinate, 0 ),
			new SetPatrolAreaCommand( 1, 3, 0, 0, 0, Clear: true ),
			new SetGameSpeedCommand( (GameSpeed)3 ),
			new QueueCheatCommand( (CheatKind)99 ),
		};
		foreach ( var command in outOfRange )
		{
			Assert.IsFalse( command.IsWellFormed, command.ToString() );
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( ParkCommandCodec.Encode( command ) ), command.ToString() );
		}
		// a string that is not UTF-8, and a cell list longer than the bound
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( Convert.FromHexString( "02010002c3280000000000" ) ) );
		var writer = new ParkCommandWriter();
		writer.Int( 1 ); writer.Byte( (byte)ParkCommandKind.UndoLine ); writer.Int( 0 ); writer.Byte( 1 ); writer.Int( -1 ); writer.Int( 0 ); writer.Int( 0 ); writer.Int( ParkCommandCodec.MaximumCells + 1 );
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( writer.ToArray() ) );
	}

	[TestMethod]
	public void AVarintDecodesOnlyInItsShortestFormAndEveryIntegerRoundTrips()
	{
		foreach ( var value in new long[] { 0, 1, -1, 63, -64, 64, int.MaxValue, int.MinValue, long.MaxValue, long.MinValue } )
		{
			var writer = new ParkCommandWriter();
			writer.Long( value );
			var reader = new ParkCommandReader( writer.ToArray() );
			Assert.AreEqual( value, reader.Long() );
			Assert.IsTrue( reader.AtEnd );
		}
		Assert.ThrowsException<InvalidDataException>( () => new ParkCommandReader( new byte[] { 0x80, 0x00 } ).Long() );
		Assert.ThrowsException<InvalidDataException>( () => new ParkCommandReader( Enumerable.Repeat( (byte)0xff, 11 ).ToArray() ).Long() );
		Assert.ThrowsException<InvalidDataException>( () => new ParkCommandReader( Enumerable.Repeat( (byte)0xff, 9 ).Append( (byte)0x03 ).ToArray() ).Long(), "a tenth byte with bits past 64" );
		Assert.ThrowsException<InvalidDataException>( () => new ParkCommandReader( new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 } ).Int(), "a value past 32 bits" );
	}

	[TestMethod]
	public void ACommandLogRoundTripsAndRefusesAnythingElse()
	{
		var log = new ParkCommandLogFile( "jungle", 0x1234_5678_9ABC_DEF0UL, 600, 0xFEDC_BA98_7654_3210UL,
			new[] { new ParkCommandLogEntry( 600, Schema[1].Command ), new ParkCommandLogEntry( 600, Schema[0].Command ), new ParkCommandLogEntry( 661, Schema[6].Command ) },
			new[] { new ParkCommandHashCheckpoint( 660, 7 ), new ParkCommandHashCheckpoint( 661, 8 ), new ParkCommandHashCheckpoint( 720, 9 ) } );
		var bytes = log.Serialize();
		var read = ParkCommandLogFile.Deserialize( bytes );
		Assert.AreEqual( log.Theme, read.Theme );
		Assert.AreEqual( log.Seed, read.Seed );
		Assert.AreEqual( log.StartTick, read.StartTick );
		Assert.AreEqual( log.StartHash, read.StartHash );
		CollectionAssert.AreEqual( log.Entries.ToArray(), read.Entries.ToArray() );
		CollectionAssert.AreEqual( log.Checkpoints.ToArray(), read.Checkpoints.ToArray() );
		CollectionAssert.AreEqual( bytes, read.Serialize() );
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandLogFile.Deserialize( bytes[..^1] ), "no end mark" );
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandLogFile.Deserialize( bytes.Append( (byte)0 ).ToArray() ), "bytes after the end" );
		var magic = bytes.ToArray();
		magic[0] ^= 1;
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandLogFile.Deserialize( magic ) );
		var backwards = log with { Entries = new[] { new ParkCommandLogEntry( 599, Schema[1].Command ) }, Checkpoints = Array.Empty<ParkCommandHashCheckpoint>() };
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandLogFile.Deserialize( backwards.Serialize() ), "a command before the start" );
	}
}
