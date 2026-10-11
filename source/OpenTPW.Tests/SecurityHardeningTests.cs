using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Chat;
using OpenTPW.Online.Packages;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>Regression tests for the findings of the October 2026 security review.</summary>
[TestClass]
public class SecurityHardeningTests
{
	[TestMethod]
	public void ABusyRoomStillFitsOneChatFrame()
	{
		// 20-character non-ASCII names serialize to about 123 bytes each; 300 of them broke the 4096-byte frame.
		var names = Enumerable.Range( 0, 300 ).Select( index => new string( 'Ж', 16 ) + index.ToString( "D4" ) ).ToArray();
		var roster = ChatHub.RosterEvent( ChatProtocol.LobbyRoom, names );
		var bytes = StrictJson.Serialize( roster );
		Assert.IsTrue( bytes.Length <= ChatProtocol.MaximumFrameBytes, $"{bytes.Length} bytes" );
		var parsed = ChatEvent.Parse( bytes );
		var shown = parsed.Args!.Count - 1;
		Assert.IsTrue( shown > 0 );
		CollectionAssert.AreEqual( names.Take( shown ).ToArray(), parsed.Args.Take( shown ).ToArray() );
		Assert.AreEqual( $"+{names.Length - shown}", parsed.Args[^1] );

		var many = Enumerable.Range( 0, 400 ).Select( index => $"p{index}" ).ToArray();
		Assert.IsTrue( ChatEvent.Parse( StrictJson.Serialize( ChatHub.RosterEvent( ChatProtocol.LobbyRoom, many ) ) ).Args!.Count <= ChatProtocol.MaximumArguments );

		var exactly = Enumerable.Range( 0, ChatProtocol.MaximumArguments ).Select( index => $"{index}" ).ToArray();
		CollectionAssert.AreEqual( exactly, ChatHub.RosterEvent( ChatProtocol.LobbyRoom, exactly ).Args!.ToArray(), "256 short names fit whole" );
		var one = exactly.Append( "x" ).ToArray();
		Assert.AreEqual( "+2", ChatHub.RosterEvent( ChatProtocol.LobbyRoom, one ).Args![^1], "257 names: 255 shown, then +2" );

		var few = new[] { "Ann", "Bob" };
		CollectionAssert.AreEqual( few, ChatHub.RosterEvent( ChatProtocol.LobbyRoom, few ).Args!.ToArray(), "a small room is sent whole" );
	}

	[TestMethod]
	public void RateLimitsShareOneBucketPerIpv6Network()
	{
		Assert.AreEqual( "2001:db8:1:2::/64", ServerProgram.RateLimitKey( IPAddress.Parse( "2001:db8:1:2:aaaa:bbbb:cccc:dddd" ) ) );
		Assert.AreEqual( ServerProgram.RateLimitKey( IPAddress.Parse( "2001:db8:1:2::1" ) ), ServerProgram.RateLimitKey( IPAddress.Parse( "2001:db8:1:2:ffff::9" ) ) );
		Assert.AreNotEqual( ServerProgram.RateLimitKey( IPAddress.Parse( "2001:db8:1:2::1" ) ), ServerProgram.RateLimitKey( IPAddress.Parse( "2001:db8:1:3::1" ) ) );
		Assert.AreEqual( "203.0.113.7", ServerProgram.RateLimitKey( IPAddress.Parse( "203.0.113.7" ) ) );
		Assert.AreEqual( "203.0.113.7", ServerProgram.RateLimitKey( IPAddress.Parse( "::ffff:203.0.113.7" ) ), "IPv4-mapped addresses count as IPv4" );
		Assert.AreEqual( "unknown", ServerProgram.RateLimitKey( null ) );
	}

	[TestMethod]
	public void MalformedJsonKeysAndNullChatArgumentsAreInvalidData()
	{
		Assert.ThrowsException<InvalidDataException>( () => ChatRequest.Parse( Encoding.UTF8.GetBytes( "{\"\\ud800\":1}" ) ) );
		Assert.ThrowsException<InvalidDataException>( () => ChatEvent.Parse( Encoding.UTF8.GetBytes( "{\"kind\":\"notice\",\"notice\":1,\"args\":[null]}" ) ) );
	}

	[TestMethod]
	public void InboxCardsWithOneCardIdNoLongerBlockEachOther()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-inbox-" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			var folders = new OnlineFolders( root );
			var id = Guid.NewGuid();
			var first = Postcard.Create( "Mallory", new[] { "Bob" }, "One", "first", "English", cardId: id );
			var second = Postcard.Create( "Mallory", new[] { "Bob" }, "Two", "second", "English", cardId: id );
			var a = folders.Store( folders.Inbox, first, "inbox-a1" );
			var b = folders.Store( folders.Inbox, second, "inbox-b2" );
			Assert.AreNotEqual( a, b );
			Assert.AreEqual( "first", Postcard.Load( a ).Manifest.Text );
			Assert.AreEqual( "second", Postcard.Load( b ).Manifest.Text );
			// The card id alone still refuses a different card, as before.
			folders.Store( folders.Outbox, first );
			Assert.ThrowsException<InvalidDataException>( () => folders.Store( folders.Outbox, second ) );
			foreach ( var name in new[] { "", "../x", "a/b", "x.y", new string( 'a', 81 ) } )
				Assert.ThrowsException<InvalidDataException>( () => folders.Store( folders.Inbox, first, name ), name );
		}
		finally
		{
			if ( Directory.Exists( root ) )
				Directory.Delete( root, true );
		}
	}
}
