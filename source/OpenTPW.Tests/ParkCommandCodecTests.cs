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
		(new EndMoveCommand() { Player = 1 }, "020602"),
		(new LayLineCommand( CellToolMode.Queue, 9, 1, 2, 1, 9 ), "020a00031202040212"),
		(new UndoLineCommand( CellToolMode.Path, -1, 4, 5, new[] { (4, 5), (5, 5) }, new[] { (6, 5) } ) { Player = 1 }, "020b020101080a04080a0a0a020c0a"),
		(new RemovePathCellCommand( 8, 9 ), "020c001012"),
		(new RemoveQueueCellCommand( 10, 11 ), "020d001416"),
		(new BacktrackCommand( 1, 2 ), "020e000204"),
		(new EditQueueCommand( 3, 3 ), "020f000606"),
		(new ClearAreaCommand( 1, 2, 7, 9 ) { Player = 1 }, "02120202040e12"),
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
		// the park's tools (C1 part 3)
		(new BuyLandCommand( 25, 11, 33, 21 ), "0236003216422a"),
		(new BeginStaffHireCommand( 7 ), "023c000e"),
		(new CancelStaffHireCommand() { Player = 1 }, "023d02"),
		(new PlaceStaffCommand( 7, 30, 12 ), "023e000e3c18"),
		(new PickUpStaffCommand( 3 ) { Player = 2 }, "023f0406"),
		(new DropStaffCommand( 3, 10, 64 ), "02400006148001"),
		(new PaintPoolCellCommand( 20, 21, true ), "024600282a01"),
		(new ErasePoolCellCommand( 20, 21 ) { Player = 1 }, "024702282a"),
		(new UndoPoolStrokeCommand( false, new[] { (20, 21), (21, 21) }, new[] { (22, 21) } ), "0248000004282a2a2a022c2a"),
		(new SetPoolWavesCommand( 17, true, 3, 1 ), "02490022010602"),
		(new LayPoolSlidePieceCommand( 30, 40, 31, 40 ), "024a003c503e50"),
		(new UndoPoolSlidePieceCommand( 30, 40 ), "024b003c50"),
		(new PlacePoolBridgeCommand( 12, 14, 1 ), "024c00181c02"),
		(new RemovePoolBridgeCommand( 12, 14 ), "024d00181c"),
		(new LayTrackLineCommand( 17, 40, 50, 40, 56 ), "0250002250645070"),
		(new UndoTrackLineCommand( 17, 40, 50, 40, 56 ), "0251002250645070"),
		(new CutTrackCommand( 17, 40, 50, 44, 50 ) { Player = 1 }, "0252022250645864"),
		(new OpenTrackLineToolCommand( 17 ), "02530022"),
		// the coaster tool (C1 part 3b)
		(new BeginCoasterToolCommand( 17 ), "025a0022"),
		(new EndCoasterToolCommand( 17 ) { Player = 1 }, "025b0222"),
		(new CoasterClickCommand( 17, 30, 40, 10f, true, 2 ), "025c00223c50000020410104"),
		(new RemoveLastCoasterPylonCommand( 17 ), "025d0022"),
		(new BeginCoasterNodeEditCommand( 17, 5, CoasterDrag.Rotate ), "025e00220a02"),
		(new EditCoasterNodeCommand( 17, CoasterNodeField.Loft, 0.25f, 0, 0 ), "025f0022000000803e0000"),
		(new EndCoasterNodeEditCommand( 17 ) { Player = 2 }, "02600422"),
		(new SwapCoasterStackCommand( 17, 6, false ), "026100220c00"),
		(new CutCoasterCommand( 17, 4, 9 ), "026200220812"),
		// [EXT:online-coop-lockstep] the session clock: 21 October 2026, 15:00:00 in whole seconds since year 1
		(new SetSessionClockCommand( 63928191600 ), "026400e0a9d6a6dc03"),
		// [EXT:online-coop-permissions] the host grants seat 3 building
		(new SetBuildGrantCommand( 3, true ), "0265000601"),
		(new ReleaseSeatCommand( 3 ), "02660006"),
		// [EXT:online-coop-commands] a guest's pool batches (C3b-3)
		(new PoolCellsCommand( false, true, new[] { (20, 21), (21, 21) } ) { Player = 1 }, "024e02000104282a2a2a"),
		(new LayPoolSlidePiecesCommand( 30, 40, new[] { (31, 40), (32, 40) } ), "024f003c50043e504050"),
		// [EXT:online-coop-claims] seat 2 claims attraction 17, and the host lets the claim go (C3c-1)
		(new ClaimRideCommand( 17 ) { Player = 2 }, "02670422"),
		(new ReleaseRideCommand( 17 ), "02680022"),
		// [EXT:online-coop-permissions] the host gives seat 3 building through the seat's rights (C4a)
		(new SetSeatRightsCommand( 3, CoopRights.Build ), "0269000602"),
		// [EXT:online-coop-undo] seat 2's Backspace on its path step of record 41, its selected ride 7, as it leaves the guest: undecided, no inverse (C5)
		(new TakeBackCommand( false, 41, -1, -1, 7 ) { Player = 2 }, "026a04005201010e0000"),
	};

	/// <summary>
	/// [EXT:online-coop-undo] The take-back as the host decided it (C5): taken with its inverse nested as a whole command of its own bytes, and refused with each verdict, a slide piece's part
	/// and a claimed ride; version 1 like the request.
	/// </summary>
	internal static readonly (ParkCommand Command, string Hex)[] TakeBackSchema =
	{
		(new TakeBackCommand( false, 41, -1, -1, 7, TakeBackVerdict.Taken,
			new UndoLineCommand( CellToolMode.Path, -1, 4, 5, new[] { (4, 5), (5, 5) }, new[] { (6, 5) } ) { Player = 1 } ) { Player = 1 }, "026a02005201010e011e020b020101080a04080a0a0a020c0a"),
		(new TakeBackCommand( false, 300, 2, -1, -1, TakeBackVerdict.Taken, new UndoPoolSlidePieceCommand( 30, 40 ) { Player = 3 } ) { Player = 3 }, "026a0600d804040101010a024b063c50"),
		(new TakeBackCommand( false, 41, -1, 17, -1, TakeBackVerdict.Changed ) { Player = 2 }, "026a0400520122010200"),
		(new TakeBackCommand( true, 0, -1, -1, -1, TakeBackVerdict.TooLarge ) { Player = 1 }, "026a0201000101010700"),
	};

	/// <summary>
	/// [EXT:online-coop-commands] The three stale-checked kinds with their expectation (C4b): version 3, the expectation after the kind's own fields; without one they keep the schema's
	/// version-1 bytes.
	/// </summary>
	internal static readonly (ParkCommand Command, string Hex)[] ExpectationSchema =
	{
		(new BuyUpgradeCommand( 17, 2 ), "062b002204"),
		(new CancelUpgradeCommand( 17, 2 ), "062c002204"),
		(new MoveResearchCommand( ResearchCategory.Ride, -1, 1100, 1 ), "0628000001981102"),
	};

	/// <summary>
	/// [EXT:online-coop-commands] A guest's upgrade cancellation that names its purchase too (C4b-2): version 4, the pending level and the purchase after the ride. C4b-1's version-3 form
	/// with the level alone keeps its bytes (<see cref="ExpectationSchema"/>).
	/// </summary>
	internal static readonly (ParkCommand Command, string Hex)[] PurchaseSchema =
	{
		(new CancelUpgradeCommand( 17, 2, 1 ), "082c00220402"),
	};

	[TestMethod]
	public void EveryCommandKindHasPinnedBytesUnderTheCurrentVersion()
	{
		Assert.AreEqual( (4, 2, 3, 4), (ParkCommandCodec.Version, ParkCommandCodec.QueueVersion, ParkCommandCodec.ExpectationVersion, ParkCommandCodec.PurchaseVersion),
			"each generation adds its own pinned table beside this one, which holds versions 1 and 2" );
		CollectionAssert.AreEquivalent( Enum.GetValues<ParkCommandKind>(), Schema.Select( row => row.Command.Kind ).ToArray(), "one schema row per kind, no kind without one" );
		var types = typeof( ParkCommand ).Assembly.GetTypes().Where( type => type.IsSubclassOf( typeof( ParkCommand ) ) && !type.IsAbstract ).ToArray();
		CollectionAssert.AreEquivalent( types, Schema.Select( row => row.Command.GetType() ).ToArray(), "every command type has a schema row" );
		foreach ( var (command, hex) in Schema )
			Assert.AreEqual( hex, ParkCommandCodec.ToHex( command ), command.Kind.ToString() );
	}

	[TestMethod]
	public void ATakeBackCarriesItsDecisionAndItsInverseAsAWholeCommand()
	{
		foreach ( var (command, hex) in TakeBackSchema )
		{
			Assert.AreEqual( hex, ParkCommandCodec.ToHex( command ), command.ToString() );
			var bytes = Convert.FromHexString( hex );
			Assert.AreEqual( command, ParkCommandCodec.Decode( bytes ), command.ToString() );
			CollectionAssert.AreEqual( bytes, ParkCommandCodec.Encode( ParkCommandCodec.Decode( bytes ) ) );
			for ( var length = 0; length < bytes.Length; length++ )
				Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.AsSpan( 0, length ) ), $"{command} cut to {length} bytes" );
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.Append( (byte)0 ).ToArray() ), $"{command} with a trailing byte" );
		}
		// a guest's request is a few bytes, whatever its step
		Assert.IsTrue( ParkCommandCodec.Encode( new TakeBackCommand( false, long.MaxValue, 4095, int.MaxValue, int.MaxValue ) { Player = 64 } ).Length <= 32 );
		// an inverse of another kind than the tools' undo, of another player than the take-back's, and a taken verdict without one or a refusal with one are no take-back
		var place = ParkCommandCodec.Encode( new PlaceObjectCommand( "x", 1, 1, 90 ) { Player = 1 } );
		var writer = new ParkCommandWriter();
		writer.Int( 1 ); writer.Byte( (byte)ParkCommandKind.TakeBack ); writer.Int( 1 );
		writer.Bool( false ); writer.Long( 41 ); writer.Int( -1 ); writer.Int( -1 ); writer.Int( -1 ); writer.Byte( (byte)TakeBackVerdict.Taken ); writer.Bytes( place );
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( writer.ToArray() ), "a foreign inner kind" );
		var inverse = new UndoLineCommand( CellToolMode.Path, -1, 4, 5, new[] { (4, 5) }, Array.Empty<(int X, int Y)>() );
		foreach ( var malformed in new[]
		{
			new TakeBackCommand( false, 41, -1, -1, -1, TakeBackVerdict.Taken, inverse with { Player = 2 } ) { Player = 1 },
			new TakeBackCommand( false, 41, -1, -1, -1, TakeBackVerdict.Taken ) { Player = 1 },
			new TakeBackCommand( false, 41, -1, -1, -1, TakeBackVerdict.Changed, inverse with { Player = 1 } ) { Player = 1 },
			new TakeBackCommand( false, -1, -1, -1, -1 ),
			new TakeBackCommand( false, 1, -2, -1, -1 ),
			new TakeBackCommand( false, 1, -1, -2, -1 ),
			new TakeBackCommand( false, 1, -1, -1, -1, (TakeBackVerdict)8 ),
		} )
		{
			Assert.IsFalse( malformed.IsWellFormed, malformed.ToString() );
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( ParkCommandCodec.Encode( malformed ) ), malformed.ToString() );
		}
		// the reasons name their verdicts for the tools that read a refusal
		foreach ( var verdict in Enum.GetValues<TakeBackVerdict>().Where( verdict => verdict != TakeBackVerdict.Taken ) )
			Assert.AreEqual( verdict, TakeBackCommand.VerdictOf( TakeBackCommand.ReasonOf( verdict ) ) );
		Assert.IsNull( TakeBackCommand.VerdictOf( "Not enough money." ) );
	}

	[TestMethod]
	public void ThePoolBridgeCommandOfEveryDirectionKeepsItsBytes()
	{
		// one bridge entry builds in all four directions; saved and replicated commands of either direction decode as before
		foreach ( var (direction, hex) in new[] { (0, "024c00181c00"), (1, "024c00181c02"), (2, "024c00181c04"), (3, "024c00181c06") } )
		{
			var command = new PlacePoolBridgeCommand( 12, 14, direction );
			Assert.AreEqual( hex, ParkCommandCodec.ToHex( command ) );
			Assert.AreEqual( command, ParkCommandCodec.Decode( Convert.FromHexString( hex ) ) );
		}
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
	public void SeatRightsDecodeRefusesUnofferedBitsBeforeNarrowing()
	{
		// Write the raw integer: constructing CoopRights first would already lose its high bits. 32 is the first bit past the five rights, all offered since C4c-2.
		foreach ( var rights in new[] { 32, 63, 255, 256, 257, -1, -255, -256, int.MinValue, int.MinValue + 1, int.MaxValue } )
		{
			var writer = new ParkCommandWriter();
			writer.Int( 1 );
			writer.Byte( (byte)ParkCommandKind.SetSeatRights );
			writer.Int( ParkCommands.LocalPlayer );
			writer.Int( 2 );
			writer.Int( rights );
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( writer.ToArray() ), $"raw rights {rights}" );
		}
	}

	[TestMethod]
	public void OfferedSeatRightsKeepTheirCanonicalBytes()
	{
		foreach ( var (rights, hex) in new[] { (CoopRights.None, "0269000400"), (CoopRights.Build, "0269000402"), (CoopRights.Staff, "0269000408"), ((CoopRights)0x1F, "026900043E") } )
		{
			var command = new SetSeatRightsCommand( 2, rights );
			var bytes = Convert.FromHexString( hex );
			Assert.AreEqual( command, ParkCommandCodec.Decode( bytes ) );
			CollectionAssert.AreEqual( bytes, ParkCommandCodec.Encode( command ) );
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
			// a mod ride's window may go past the shipped speed, capacity and duration ranges: the setters take any value, as they did before the commands
			new SetRideSettingsCommand( int.MaxValue, int.MinValue, int.MaxValue, 300 ),
		};
		foreach ( var command in edges )
		{
			var bytes = ParkCommandCodec.Encode( command );
			Assert.AreEqual( command, ParkCommandCodec.Decode( bytes ) );
			CollectionAssert.AreEqual( bytes, ParkCommandCodec.Encode( ParkCommandCodec.Decode( bytes ) ) );
		}
		Assert.AreEqual( BitConverter.SingleToInt32Bits( -0f ), BitConverter.SingleToInt32Bits( ((PlacePrototypeCommand)ParkCommandCodec.Decode( ParkCommandCodec.Encode( edges[2] ) )).Y ), "floats keep their bit pattern" );
	}

	[TestMethod]
	public void TheStaleCheckedKindsCarryTheirExpectationInVersionThreeAlone()
	{
		foreach ( var (command, hex) in ExpectationSchema )
		{
			Assert.AreEqual( hex, ParkCommandCodec.ToHex( command ), command.Kind.ToString() );
			var bytes = Convert.FromHexString( hex );
			Assert.AreEqual( command, ParkCommandCodec.Decode( bytes ), command.Kind.ToString() );
			CollectionAssert.AreEqual( bytes, ParkCommandCodec.Encode( ParkCommandCodec.Decode( bytes ) ) );
			// a truncated expectation (a research move's project cut after its id included) and a trailing byte are no command
			for ( var length = 0; length < bytes.Length; length++ )
				Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.AsSpan( 0, length ) ), $"{command.Kind} cut to {length} bytes" );
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.Append( (byte)0 ).ToArray() ), $"{command.Kind} with a trailing byte" );
		}
		// version 3 is these three kinds' alone, and the same kinds without an expectation (the host's) keep version 1, decoding with none
		foreach ( var (command, hex) in Schema.Where( row => row.Command is not IStaleChecked ) )
		{
			var bytes = Convert.FromHexString( hex );
			bytes[0] = 6;
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes ), $"{command.Kind} as version 3" );
		}
		foreach ( var (command, hex) in Schema.Where( row => row.Command is IStaleChecked ) )
			Assert.AreEqual( command, ParkCommandCodec.Decode( Convert.FromHexString( hex ) ), $"{command.Kind} of version 1 has no expectation" );
		Assert.AreEqual( 3, Schema.Count( row => row.Command is IStaleChecked ) );
		// a research move names its whole project or none; an expectation is never negative
		foreach ( var partial in new ParkCommand[] { new MoveResearchCommand( ResearchCategory.Ride, 1, 1100, null ), new MoveResearchCommand( ResearchCategory.Ride, 1, null, 1 ),
			new MoveResearchCommand( ResearchCategory.Ride, 1, -1, 1 ), new BuyUpgradeCommand( 17, -1 ), new CancelUpgradeCommand( 17, -1 ) } )
			Assert.IsFalse( partial.IsWellFormed, partial.ToString() );
	}

	[TestMethod]
	public void AGuestsUpgradeCancellationNamesItsPurchaseInVersionFourAlone()
	{
		foreach ( var (command, hex) in PurchaseSchema )
		{
			Assert.AreEqual( hex, ParkCommandCodec.ToHex( command ), command.Kind.ToString() );
			var bytes = Convert.FromHexString( hex );
			Assert.AreEqual( command, ParkCommandCodec.Decode( bytes ), command.Kind.ToString() );
			CollectionAssert.AreEqual( bytes, ParkCommandCodec.Encode( ParkCommandCodec.Decode( bytes ) ) );
			for ( var length = 0; length < bytes.Length; length++ )
				Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.AsSpan( 0, length ) ), $"{command.Kind} cut to {length} bytes" );
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes.Append( (byte)0 ).ToArray() ), $"{command.Kind} with a trailing byte" );
		}
		// version 4 is the cancellation's alone
		foreach ( var (command, hex) in Schema.Concat( ExpectationSchema ).Where( row => row.Command is not CancelUpgradeCommand ) )
		{
			var bytes = Convert.FromHexString( hex );
			bytes[0] = 8;
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( bytes ), $"{command.Kind} as version 4" );
		}
		// C4b-1's version-3 cancellation decodes with the level alone, and the host's version 1 with neither
		Assert.AreEqual( new CancelUpgradeCommand( 17, 2 ), ParkCommandCodec.Decode( Convert.FromHexString( "062c002204" ) ) );
		Assert.AreEqual( new CancelUpgradeCommand( 17 ), ParkCommandCodec.Decode( Convert.FromHexString( Schema.Single( row => row.Command is CancelUpgradeCommand ).Hex ) ) );
		// a purchase names its level too, and neither is negative
		foreach ( var partial in new ParkCommand[] { new CancelUpgradeCommand( 17, null, 1 ), new CancelUpgradeCommand( 17, 2, -1 ), new CancelUpgradeCommand( 17, -1, 1 ) } )
			Assert.IsFalse( partial.IsWellFormed, partial.ToString() );
	}

	/// <summary>A log recorded before the undo's last-line flag decodes as it was written, and a flag byte that is present must be 1.</summary>
	[TestMethod]
	public void TheUndoTrackLineBytesOfAnOlderLogStillDecodeAndAPresentZeroFlagIsRefused()
	{
		var old = ParkCommandCodec.Decode( Convert.FromHexString( "0251002250645070" ) );
		Assert.AreEqual( new UndoTrackLineCommand( 17, 40, 50, 40, 56 ), old );
		var last = new UndoTrackLineCommand( 17, 40, 50, 40, 56, true );
		Assert.AreEqual( "025100225064507001", Convert.ToHexString( ParkCommandCodec.Encode( last ) ).ToLowerInvariant() );
		Assert.AreEqual( last, ParkCommandCodec.Decode( Convert.FromHexString( "025100225064507001" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( Convert.FromHexString( "025100225064507000" ) ) );
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
			otherVersion[0] = 8;
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( otherVersion ), $"{command.Kind} as version 4" );
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
			new SetRideSettingsCommand( -1, 0, 0, 0 ),
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
			new BuyLandCommand( -1, 0, 0, 0 ),
			new BuyLandCommand( 0, 0, ParkCommandCodec.MaximumCoordinate, 0 ),
			new BeginStaffHireCommand( 0 ),
			new PlaceStaffCommand( 1, -1, 0 ),
			new PlaceStaffCommand( 0, 1, 1 ),
			new PickUpStaffCommand( -3 ),
			new DropStaffCommand( 1, 0, -1 ),
			new PaintPoolCellCommand( -1, 0, false ),
			new ErasePoolCellCommand( 0, ParkCommandCodec.MaximumCoordinate ),
			new UndoPoolStrokeCommand( true, new[] { (1, 1) }, new[] { (2, 2) } ),
			new UndoPoolStrokeCommand( false, new[] { (-1, 1) }, Array.Empty<(int X, int Y)>() ),
			new SetPoolWavesCommand( 1, true, 0, 1 ),
			new SetPoolWavesCommand( 1, true, 2, 3 ),
			new SetPoolWavesCommand( -1, false, 2, 1 ),
			new LayPoolSlidePieceCommand( 0, 0, -1, 0 ),
			new UndoPoolSlidePieceCommand( -1, 0 ),
			new PlacePoolBridgeCommand( 1, 1, 4 ),
			new RemovePoolBridgeCommand( 1, -1 ),
			new PoolCellsCommand( false, false, Array.Empty<(int X, int Y)>() ),
			new PoolCellsCommand( true, true, new[] { (1, 1) } ),
			new PoolCellsCommand( false, false, Enumerable.Repeat( (1, 1), PoolCellsCommand.MaximumCells + 1 ).ToArray() ),
			new PoolCellsCommand( false, false, new[] { (1, -1) } ),
			new LayPoolSlidePiecesCommand( 1, 1, Array.Empty<(int X, int Y)>() ),
			new LayPoolSlidePiecesCommand( -1, 1, new[] { (1, 1) } ),
			new LayPoolSlidePiecesCommand( 1, 1, Enumerable.Repeat( (1, 1), PoolCellsCommand.MaximumCells + 1 ).ToArray() ),
			new LayTrackLineCommand( -1, 0, 0, 2, 0 ),
			new UndoTrackLineCommand( 1, 0, 0, 2, 2 ),
			new UndoTrackLineCommand( 1, 0, 0, 3, 0 ),
			new CutTrackCommand( 1, 0, ParkCommandCodec.MaximumCoordinate, 0, 0 ),
			new OpenTrackLineToolCommand( -1 ),
			new BeginCoasterToolCommand( -1 ),
			new EndCoasterToolCommand( -1 ),
			new CoasterClickCommand( 1, -1, 0, 10f, false, 0 ),
			new CoasterClickCommand( 1, 0, 0, float.NaN, false, 0 ),
			new CoasterClickCommand( 1, 0, 0, 10f, false, 1 ),
			new CoasterClickCommand( 1, 0, 0, 10f, true, 3 ),
			new RemoveLastCoasterPylonCommand( -1 ),
			new BeginCoasterNodeEditCommand( 1, -1, CoasterDrag.Height ),
			new BeginCoasterNodeEditCommand( 1, 3, (CoasterDrag)5 ),
			new EditCoasterNodeCommand( 1, (CoasterNodeField)6, 0, 0, 0 ),
			new EditCoasterNodeCommand( 1, CoasterNodeField.Yaw, float.PositiveInfinity, 0, 0 ),
			new EditCoasterNodeCommand( 1, CoasterNodeField.Move, 0, -1, 0 ),
			new EndCoasterNodeEditCommand( -1 ),
			new SwapCoasterStackCommand( -1, 0, true ),
			new CutCoasterCommand( 1, -1, 2 ),
			new CutCoasterCommand( 1, 2, -1 ),
			new SwapCoasterStackCommand( 1, -1, true ),
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

		// [EXT:online-coop-lockstep] a session's log says it applied under the session's rules (format 3's flags byte)
		var session = ParkCommandLogFile.Deserialize( ( log with { SessionRules = true } ).Serialize() );
		Assert.IsTrue( session.SessionRules );
		Assert.IsFalse( read.SessionRules );
		// a log of format 2 (no flags byte) reads as recorded without the session's rules, and holds no session log
		var old = ParkCommandLogFile.Deserialize( log.Serialize( 2 ) );
		Assert.IsFalse( old.SessionRules );
		CollectionAssert.AreEqual( log.Entries.ToArray(), old.Entries.ToArray() );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ( log with { SessionRules = true } ).Serialize( 2 ) );
		// a flag this version does not know is refused
		var header = new ParkCommandWriter();
		foreach ( var value in ParkCommandLogFile.Magic )
			header.Byte( value );
		header.Int( 3 );
		header.String( "jungle" );
		header.UInt64( 5 );
		header.Long( 600 );
		header.UInt64( 6 );
		header.Byte( 2 );
		header.Byte( 0 );
		Assert.ThrowsException<InvalidDataException>( () => ParkCommandLogFile.Deserialize( header.ToArray() ), "an unknown flag" );
	}
}
