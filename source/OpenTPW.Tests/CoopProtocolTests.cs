using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Coop;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-session] The co-op socket's messages (docs/ONLINE.md, "Shared parks"): every message reads back as it was written, and a truncated,
/// overlong, unknown or out-of-range message is refused rather than read.
/// </summary>
[TestClass]
public class CoopProtocolTests
{
	private static readonly CoopMod Pools = new( "opentpw.pools", "1.0.0", new string( 'a', 64 ) );
	private static readonly CoopMod Evening = new( "opentpw.evening", "2.1", new string( 'b', 64 ) );

	private static CoopSessionState State( uint you = 2, bool seqKnown = true ) => new( "0123456789abcdef", "ABCD23", "Our park", true, 4, you, 1, 41, seqKnown,
		new[] { new CoopMemberInfo( 1, 0, "Host", true ), new CoopMemberInfo( 2, 1, "Guest", false ) } );

	public static IEnumerable<object[]> Messages() => new CoopMessage[]
	{
		new CoopHello( 6, "1.4.0+0123abcd", new[] { Pools, Evening }, Guid.NewGuid(), false, null ),
		new CoopHello( 6, "1.4.0+0123abcd", Array.Empty<CoopMod>(), Guid.NewGuid(), true, new CoopResume( "0123456789abcdef", 3, 1234567890123 ) ),
		new CoopCreate( true, "Our park", 4 ),
		new CoopJoin( "abcd-23" ),
		new CoopLeave( "The host loaded another park." ),
		new CoopKick( 3 ),
		new CoopInvite( "Somebody" ),
		new CoopHostFrame( 7, new byte[] { 1, 2, 3 } ),
		new CoopSend( 0, Array.Empty<byte>() ),
		new CoopPong(),
		new CoopWelcome( 6, State(), null ),
		new CoopWelcome( 6, null, new CoopEnded( "0123456789abcdef", CoopReason.Expired, "The host did not come back." ) ),
		new CoopCreated( State( 1 ) ),
		new CoopWelcome( 6, State( seqKnown: false ), null ),
		new CoopHello( 6, "1.4.0+0123abcd", Enumerable.Range( 0, CoopProtocol.MaximumMods ).Select( index => new CoopMod( $"mod.{index}", "1.0", new string( 'c', 64 ) ) ).ToArray(),
			Guid.NewGuid(), false, null ),
		new CoopJoined( State(), "1.4.0+0123abcd", new[] { Pools } ),
		new CoopMemberChanged( new CoopMemberInfo( 3, 2, "Third", true ), CoopMemberState.Connected ),
		new CoopInvited( "0123456789abcdef", "ABCD23", "Our park", "Host" ),
		new CoopEnded( "0123456789abcdef", CoopReason.Kicked, "" ),
		new CoopFrame( ulong.MaxValue, new byte[] { 9 } ),
		new CoopRelayed( 2, new byte[] { 4, 5 } ),
		new CoopError( CoopMessageType.Join, CoopReason.ModsDiffer, "The host's mods differ from yours." ),
		new CoopPing(),
		new CoopFrameRefused( 8 )
	}.Select( message => new object[] { message } );

	[TestMethod]
	[DynamicData( nameof( Messages ), DynamicDataSourceType.Method )]
	public void EveryMessageReadsBackAsItWasWritten( CoopMessage message )
	{
		var bytes = message.Encode();
		var read = CoopMessage.Decode( bytes );
		Assert.AreEqual( message.Type, read.Type );
		CollectionAssert.AreEqual( bytes, read.Encode(), $"{message.Type} reads back to the same bytes" );
		// every truncation is refused, never read as a shorter message
		for ( var length = 0; length < bytes.Length; length++ )
			Assert.ThrowsException<InvalidDataException>( () => CoopMessage.Decode( bytes.AsSpan( 0, length ) ), $"{message.Type} cut to {length} bytes" );
		// and so is a byte after the fields
		Assert.ThrowsException<InvalidDataException>( () => CoopMessage.Decode( bytes.Concat( new byte[] { 0 } ).ToArray() ) );
	}

	[TestMethod]
	public void MalformedMessagesAreRefused()
	{
		void Refused( byte[] bytes, string why ) => Assert.ThrowsException<InvalidDataException>( () => CoopMessage.Decode( bytes ), why );
		Refused( new byte[] { 0 }, "an unknown type" );
		Refused( new byte[] { 200 }, "another unknown type" );
		Refused( new CoopCreate( true, "Park", 2 ).Encode().Select( ( b, i ) => i == 1 ? (byte)2 : b ).ToArray(), "a flag that is neither 0 nor 1" );
		Refused( new CoopCreate( true, "Park", 5 ).Encode(), "more players than four" );
		Refused( new CoopCreate( true, "Park", 1 ).Encode(), "a session of one" );
		Refused( new CoopHostFrame( 0, new byte[1] ).Encode(), "frame 0" );
		Refused( new CoopHostFrame( ulong.MaxValue, new byte[1] ).Encode(), "the largest frame number, which has no next one" );
		Refused( new CoopHello( 6, "", Array.Empty<CoopMod>(), Guid.NewGuid(), false, null ).Encode(), "no build" );
		Refused( new CoopHello( 6, "1.0", Array.Empty<CoopMod>(), Guid.Empty, false, null ).Encode(), "no instance" );
		Refused( new CoopHello( 6, "1.0", new[] { Pools with { ContentSha256 = "not hex" } }, Guid.NewGuid(), false, null ).Encode(), "a mod digest that is no SHA-256" );
		Refused( new CoopHello( 6, "1.0", Array.Empty<CoopMod>(), Guid.NewGuid(), false, new CoopResume( "0123", 1, 1 ) ).Encode(), "a session id of the wrong length" );
		Refused( new CoopWelcome( 6, State() with { Code = "ABCD0O" }, null ).Encode(), "a code with characters read alike" );
		Refused( new CoopMemberChanged( new CoopMemberInfo( 1, 4, "Fifth", true ), CoopMemberState.Joined ).Encode(), "slot 4" );
		Refused( new CoopMemberChanged( new CoopMemberInfo( 0, 1, "Nobody", true ), CoopMemberState.Joined ).Encode(), "member 0" );
		Refused( new CoopMemberChanged( new CoopMemberInfo( 1, 1, "One", true ), (CoopMemberState)9 ).Encode(), "an unknown member state" );
		var controlCharacter = new CoopLeave( "line break" ).Encode();
		controlCharacter[^6] = (byte)'\n';
		Refused( controlCharacter, "a control character" );
		Refused( Raw( (byte)CoopMessageType.Join, 33, (byte)'A' ), "an overlong code" );
		Refused( new CoopFrameRefused( 0 ).Encode(), "frame 0 expected" );
		var invalidUtf8 = new CoopInvite( "ab" ).Encode();
		invalidUtf8[^1] = 0xFF;
		Refused( invalidUtf8, "invalid UTF-8" );
		var huge = new CoopHostFrame( 1, new byte[1] ).Encode();
		BitConverter.GetBytes( (uint)(CoopProtocol.MaximumMessageBytes + 1) ).CopyTo( huge, 9 );
		Refused( huge, "a payload length over the limit" );
		Refused( new byte[CoopProtocol.MaximumMessageBytes + 1], "a message over the limit" );
	}

	/// <summary>A message of <paramref name="type"/> with one text field of <paramref name="length"/> copies of <paramref name="fill"/>, beyond what a writer writes.</summary>
	private static byte[] Raw( byte type, ushort length, byte fill ) =>
		new[] { type }.Concat( BitConverter.GetBytes( length ) ).Concat( Enumerable.Repeat( fill, length ) ).ToArray();

	[TestMethod]
	public void NothingIsWrittenThatAReaderRefuses()
	{
		Assert.ThrowsException<ArgumentException>( () => new CoopLeave( new string( 'a', CoopProtocol.MaximumReasonLength + 1 ) ).Encode() );
		Assert.ThrowsException<ArgumentException>( () => new CoopError( CoopMessageType.Join, CoopReason.ModsDiffer, new string( 'a', 300 ) ).Encode() );
		Assert.ThrowsException<ArgumentException>( () => new CoopCreate( true, "a\tb", 2 ).Encode() );
		Assert.ThrowsException<ArgumentException>( () => new CoopHello( 6, "1.0", Enumerable.Repeat( Pools, CoopProtocol.MaximumMods + 1 ).ToArray(), Guid.NewGuid(), false, null ).Encode() );
		// a text the server makes is clipped to what a reader takes, never inside a surrogate pair
		var clipped = CoopProtocol.Clip( new string( 'a', 255 ) + "\U0001F600" + new string( 'b', 40 ) + "\n" );
		Assert.AreEqual( 255, clipped.Length );
		Assert.AreEqual( "a b", CoopProtocol.Clip( "a\nb" ) );
		var error = new CoopError( CoopMessageType.Join, CoopReason.ModsDiffer, CoopProtocol.Clip( new string( 'x', 1000 ) ) );
		Assert.AreEqual( CoopProtocol.MaximumReasonLength, ((CoopError)CoopMessage.Decode( error.Encode() )).Text.Length );
	}

	[TestMethod]
	public void CodesAreNormalisedAsTyped()
	{
		Assert.AreEqual( "ABCD23", CoopProtocol.NormalizeCode( "abcd-23" ) );
		Assert.AreEqual( "ABCD23", CoopProtocol.NormalizeCode( " ab cd 23 " ) );
		Assert.IsNull( CoopProtocol.NormalizeCode( "ABCD2" ), "too short" );
		Assert.IsNull( CoopProtocol.NormalizeCode( "ABCD234" ), "too long" );
		Assert.IsNull( CoopProtocol.NormalizeCode( "ABCD0O" ), "0 and O are not in the alphabet" );
		Assert.IsNull( CoopProtocol.NormalizeCode( "ABCD1L" ), "nor 1 and L" );
		Assert.IsNull( CoopProtocol.NormalizeCode( null ) );
		Assert.IsFalse( CoopProtocol.CodeAlphabet.Any( c => "0O1IL".Contains( c ) ) );
	}

	[TestMethod]
	public void TheCloseTableSaysWhichClosesAGameResumesAfter()
	{
		foreach ( var status in new int?[] { null, 1001, 1006, 1011, CoopCloseStatus.Restarting, CoopCloseStatus.TooSlow, CoopCloseStatus.Rate } )
			Assert.IsTrue( CoopCloseStatus.IsResumable( status ), $"{status} resumes" );
		foreach ( var status in new int?[] { 1000, 1008, 1009, CoopCloseStatus.Replaced, CoopCloseStatus.InUse, CoopCloseStatus.ProtocolError } )
			Assert.IsFalse( CoopCloseStatus.IsResumable( status ), $"{status} ends the link" );
	}
}
