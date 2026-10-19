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
		(new PlacePrototypeCommand( 1.5f, -2.25f, 0f ), "0214000000c03f000010c000000000"),
		(new RemovePrototypeCommand(), "021500"),
	};

	[TestMethod]
	public void EveryCommandKindHasPinnedBytesUnderTheCurrentVersion()
	{
		Assert.AreEqual( 1, ParkCommandCodec.Version, "a new version adds a new schema table beside this one" );
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
			otherVersion[0] = 4;
			Assert.ThrowsException<InvalidDataException>( () => ParkCommandCodec.Decode( otherVersion ), $"{command.Kind} as version 2" );
		}
		var bad = new[]
		{
			"020000", // kind 0
			"02ff00", // kind 255
			"02160000", // a kind no command has
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
