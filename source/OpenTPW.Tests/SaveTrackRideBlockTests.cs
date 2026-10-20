using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class SaveTrackRideBlockTests
{
	private const uint Ride = 0xfffff403; // kind -12, slot 3
	private static byte[] Words( params uint[] words )
	{
		var bytes = new byte[words.Length * 4];
		for ( var i = 0; i < words.Length; i++ ) Put( bytes, i * 4, words[i] );
		return bytes;
	}
	private static void Put( byte[] bytes, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( offset ), value );
	private static byte[] Join( params byte[][] parts ) => parts.SelectMany( part => part ).ToArray();
	private static byte[] Chunk( uint type, byte[] payload, uint word = 0x12345678 ) => Join( Words( type, word, (uint)(12 + payload.Length) ), payload );
	private static byte[] Container( params byte[][] chunks ) => Chunk( 1, Join( chunks ), 0xabcdef01 );
	private static byte[] Header( uint identity = Ride ) => Chunk( 3, Words( identity, 11, 12, 13, 14, 15, 16, 17, 18, 19 ) );
	private static byte[] Raw( uint owner = Ride )
	{
		var payload = new byte[196];
		Put( payload, 0, Ride );
		for ( var i = 0; i < 0xac; i++ ) payload[4 + i] = (byte)i;
		Put( payload, 4, 0x400005 );
		Put( payload, 4 + 0x14, 0xdeadbeef ); // Opaque pointer-looking data is never followed.
		Put( payload, 4 + 0x34, 101 );
		Put( payload, 4 + 0x38, 102 );
		Put( payload, 0xb0, 201 );
		Put( payload, 0xb4, 202 );
		Put( payload, 0xb8, owner );
		Put( payload, 0xbc, 301 );
		Put( payload, 0xc0, 302 );
		return Chunk( 5, payload );
	}

	[TestMethod]
	public void ReadsTypedFieldsAndEveryHeaderWithoutInventingReferenceIndex()
	{
		var input = Container( Header(), Chunk( 4, Words( Ride, 0x10001, 31, 32 ) ), Raw(), Chunk( 6, Words( Ride ) ) );
		var block = SaveTrackRideBlock.ParseContainer( input );
		Assert.AreEqual( 1u, block.Type );
		Assert.AreEqual( 0xabcdef01u, block.HeaderWord );
		Assert.AreEqual( (uint)input.Length, block.Size );
		CollectionAssert.AreEqual( new uint[] { 52, 28, 208, 16 }, block.Chunks.Select( c => c.Size ).ToArray() );
		CollectionAssert.AreEqual( new[] { 12, 64, 92, 300 }, block.Chunks.Select( c => c.Offset ).ToArray() );
		Assert.IsTrue( block.Chunks.All( c => c.HeaderWord == 0x12345678 ) );
		Assert.AreEqual( new SaveTrackRideHeader( new( Ride ), 11, 12, 13, 14, 15, 16, 17, 18, 19 ), block.Chunks[0].Ride );
		Assert.AreEqual( -12, block.Chunks[0].Ride!.Identity.Kind );
		Assert.AreEqual( 3, block.Chunks[0].Ride!.Identity.Slot );
		Assert.AreEqual( new SaveTrackTile( new( Ride ), 0x10001, 31, 32 ), block.Chunks[1].Tile );
		var raw = block.Chunks[2].RawRecord!;
		Assert.AreEqual( 172, raw.Bytes.Length );
		Assert.AreEqual( 0x400005u, raw.Flags );
		Assert.AreEqual( 101, raw.TileCoordinate0 );
		Assert.AreEqual( 102, raw.TileCoordinate1 );
		Assert.AreEqual( 201, raw.FallbackTileCoordinate0 );
		Assert.AreEqual( 202, raw.FallbackTileCoordinate1 );
		Assert.AreEqual( new SaveTrackWaypointReference( new( Ride ), 301, 302 ), raw.Waypoint );
		Assert.AreEqual( SaveTrackWaypointReferenceState.Unresolved, raw.Waypoint.State );
		Assert.AreEqual( 0xdeadbeefu, BinaryPrimitives.ReadUInt32LittleEndian( raw.Bytes[0x14..] ) );
		Assert.AreEqual( new SaveTrackReferenceRequest( new( Ride ) ), block.Chunks[3].ReferenceRequest );
	}

	[TestMethod]
	public void BodyApiMatchesContainerAndHandlesEmptyBody()
	{
		var input = Container( Header(), Raw() );
		var full = SaveTrackRideBlock.ParseContainer( input );
		var body = SaveTrackRideBlock.ParseBody( input.AsSpan( 12 ), full.HeaderWord );
		Assert.AreEqual( full.Size, body.Size );
		Assert.AreEqual( full.HeaderWord, body.HeaderWord );
		Assert.AreEqual( full.Chunks[1].Offset, body.Chunks[1].Offset );
		CollectionAssert.AreEqual( full.Chunks[1].Payload.ToArray(), body.Chunks[1].Payload.ToArray() );
		Assert.AreEqual( 0, SaveTrackRideBlock.ParseContainer( Container() ).Chunks.Count );
		Assert.AreEqual( 12u, SaveTrackRideBlock.ParseBody( Array.Empty<byte>() ).Size );
	}

	[TestMethod]
	public void PreservesUnknownChunksDuplicatesRepeatedRequestsAndMissingOwners()
	{
		var input = Container( Chunk( 6, Words( Ride ) ), Header(), Header(), Header( 0xfffff503 ),
			Raw(), Raw(), Raw( 0 ), Raw( 0xfffff905 ), Chunk( 99, new byte[] { 4, 3, 2, 1, 7 } ), Chunk( 6, Words( Ride ) ) );
		var chunks = SaveTrackRideBlock.ParseContainer( input ).Chunks;
		CollectionAssert.AreEqual( new uint[] { 6, 3, 3, 3, 5, 5, 5, 5, 99, 6 }, chunks.Select( c => c.Type ).ToArray() );
		Assert.AreEqual( chunks[1].Ride, chunks[2].Ride );
		Assert.AreNotEqual( chunks[1].Ride!.Identity, chunks[3].Ride!.Identity );
		Assert.AreEqual( chunks[1].Ride!.Identity.Slot, chunks[3].Ride!.Identity.Slot );
		CollectionAssert.AreEqual( chunks[4].Payload.ToArray(), chunks[5].Payload.ToArray() );
		Assert.AreEqual( SaveTrackWaypointReferenceState.Absent, chunks[6].RawRecord!.Waypoint.State );
		Assert.AreEqual( SaveTrackWaypointReferenceState.Unresolved, chunks[7].RawRecord!.Waypoint.State );
		CollectionAssert.AreEqual( new byte[] { 4, 3, 2, 1, 7 }, chunks[8].Payload.ToArray() );
		Assert.IsNull( chunks[8].Ride );
		Assert.IsNull( chunks[8].Tile );
		Assert.IsNull( chunks[8].RawRecord );
		Assert.IsNull( chunks[8].ReferenceRequest );
		Assert.AreEqual( chunks[0].ReferenceRequest, chunks[9].ReferenceRequest );
	}

	[TestMethod]
	public void InputAndReturnedCopiesCannotMutateParsedDocument()
	{
		var input = Container( Header(), Raw(), Chunk( 99, new byte[] { 7 } ) );
		var block = SaveTrackRideBlock.ParseContainer( input );
		Array.Fill( input, (byte)0 );
		var copy = block.Chunks[1].RawRecord!.Bytes.ToArray();
		Array.Fill( copy, (byte)0 );
		var opaque = block.Chunks[2].Payload.ToArray();
		opaque[0] = 0;
		Assert.AreEqual( Ride, block.Chunks[0].Ride!.Identity.Value );
		Assert.AreEqual( 0x400005u, block.Chunks[1].RawRecord!.Flags );
		Assert.AreEqual( (byte)7, block.Chunks[2].Payload[0] );
		Assert.ThrowsException<NotSupportedException>( () => ((IList<SaveTrackRideChunk>)block.Chunks).Clear() );
	}

	[TestMethod]
	public void StrictTypedSizesRejectShortAndLongShapes()
	{
		foreach ( var (type, length) in new[] { (3u, 40), (4u, 16), (5u, 196), (6u, 4) } )
			foreach ( var delta in new[] { -1, 1 } )
				Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( Container( Chunk( type, new byte[length + delta] ) ) ) );
		// Native ride stride is not chunk 3's wire size; raw172 is not the whole chunk5 payload.
		Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( Container( Chunk( 3, new byte[208] ) ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( Container( Chunk( 5, new byte[172] ) ) ) );
	}

	[TestMethod]
	public void RejectsTruncatedAndOverflowingFramesWithinProvidedSlice()
	{
		for ( var length = 0; length < 12; length++ )
			Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( new byte[length] ) );
		foreach ( var size in new uint[] { 0, 11, 13, uint.MaxValue } )
			Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( Words( 1, 0, size ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( Words( 2, 0, 12 ) ) );
		for ( var length = 1; length < 12; length++ )
			Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseBody( new byte[length] ) );
		foreach ( var size in new uint[] { 0, 11, 13, uint.MaxValue } )
			Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseBody( Words( 99, 0, size ) ) );
		var input = Container( Header(), Raw() );
		Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( input.AsSpan( 0, input.Length - 1 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseBody( input.AsSpan( 12, input.Length - 13 ) ) );
		Assert.ThrowsException<InvalidDataException>( () => SaveTrackRideBlock.ParseContainer( Join( input, new byte[] { 0 } ) ) );
	}

	[TestMethod]
	public void LateFailureLeavesPreviouslyPublishedDocumentUnchanged()
	{
		var document = SaveTrackRideBlock.ParseContainer( Container( Header() ) );
		var original = document;
		Assert.ThrowsException<InvalidDataException>( () => document = SaveTrackRideBlock.ParseContainer( Container( Header(), Chunk( 5, new byte[195] ) ) ) );
		Assert.AreSame( original, document );
		Assert.AreEqual( 1, document.Chunks.Count );
		Assert.AreEqual( Ride, document.Chunks[0].Ride!.Identity.Value );
	}
}
