using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>Raw own-format #888 rows migrate through both world loaders without restoring the superseded timer-continuation policy.</summary>
[TestClass, DoNotParallelize]
public class MessageTubeSaveCompatibilityTests
{
	private const string LegacyPlain = """{"Kind":0,"Text":"plain","Tag":0,"ContextShort":0,"ContextInt":0,"Lifetime":0,"ElapsedMs":0,"Active":false,"PreExpiryEnabled":false,"PreExpiryReached":false}""";
	private const string LegacyTerminal = """{"Kind":3,"Text":"terminal","Tag":77,"ContextShort":3,"ContextInt":9,"Lifetime":30000,"ElapsedMs":26000,"Active":true,"PreExpiryEnabled":true,"PreExpiryReached":false}""";
	private const string CurrentTerminal = """{"Kind":3,"Text":"terminal","Tag":77,"ContextShort":3,"ContextInt":9,"Lifetime":30000,"Read":false}""";

	private static byte[] WorldWithRows( JsonNode world, params string[] rows )
	{
		var copy = world.DeepClone();
		copy["MessageTube"] = JsonNode.Parse( "{\"Rows\":[" + string.Join( ",", rows ) + "]}" );
		return Encoding.UTF8.GetBytes( copy.ToJsonString() );
	}

	private static JsonNode World() => JsonNode.Parse( ParkResumeSaveFile.Serialize( ParkResumeSaveFileTests.Envelope() ) )!;

	private static ParkResumeSnapshot Stepped( byte[] bytes, int slice )
	{
		ParkResumeSnapshot? result = null;
		foreach ( var _ in ParkResumeSaveFile.DeserializeSteps( bytes, value => result = value, slice ) ) { }
		Assert.IsNotNull( result );
		return result!;
	}

	private static IEnumerable<ParkResumeSnapshot> ReadBoth( byte[] bytes )
	{
		yield return ParkResumeSaveFile.Deserialize( bytes );
		yield return Stepped( bytes, 1 );
		yield return Stepped( bytes, 257 );
	}

	private static void RejectBoth( byte[] bytes, string name )
	{
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.Deserialize( bytes ), name );
		foreach ( var slice in new[] { 1, 257 } )
			Assert.ThrowsException<InvalidDataException>( () => Stepped( bytes, slice ), $"{name}, slice {slice}" );
	}

	private sealed class Clock
	{
		public long Now = 500_000;
		public ParkHud Hud { get; } = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		public Clock() => Hud.ChallengeMessageClock = () => Now;
		public void Pump( long now )
		{
			Now = now;
			Hud.QueueChallengeTerminalTimers();
			Hud.DispatchChallengeTerminalTimers();
			Hud.ReleaseChallengeTerminalRows();
		}
	}

	[TestMethod]
	public void RawLegacyRowsLoadAndRewriteOnlyTheCurrentShape()
	{
		var offer = JsonNode.Parse( LegacyPlain )!;
		offer["Kind"] = MessageTubeRowSave.Offer;
		var progress = JsonNode.Parse( LegacyPlain )!;
		progress["Kind"] = MessageTubeRowSave.Progress;
		progress["Tag"] = 78;
		var bytes = WorldWithRows( World(), LegacyPlain, offer.ToJsonString(), progress.ToJsonString(), LegacyTerminal );
		foreach ( var snapshot in ReadBoth( bytes ) )
		{
			var rows = snapshot.MessageTube!.Rows;
			CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, rows.Select( row => row.Kind ).ToArray() );
			Assert.IsTrue( rows.All( row => !row.Read ) );
			Assert.AreEqual( (77, (short)3, 9), (rows[3].Tag, rows[3].ContextShort, rows[3].ContextInt) );
			var rewritten = ParkResumeSaveFile.Serialize( snapshot );
			var outputRows = JsonNode.Parse( rewritten )!["MessageTube"]!["Rows"]!.AsArray();
			foreach ( var row in outputRows )
				CollectionAssert.AreEqual( new[] { "Kind", "Text", "Tag", "ContextShort", "ContextInt", "Lifetime", "Read" }, row!.AsObject().Select( property => property.Key ).ToArray() );
			Assert.IsTrue( ParkResumeSaveFile.Serialize( ParkResumeSaveFile.Deserialize( rewritten ) ).SequenceEqual( rewritten ) );
			var sliced = ParkResumeSaveFile.BeginSlicedSerialize( snapshot );
			while ( !sliced.Run( TimeSpan.Zero ) ) { }
			CollectionAssert.AreEqual( rewritten, sliced.Bytes, "the sliced writer also emits only current rows" );
		}
	}

	[TestMethod]
	public void LegacyActiveRowsRestartTheFullLifetimeWithoutRestoringPendingDelivery()
	{
		var world = World();
		foreach ( var state in new[] { "running", "suspended", "pending warning", "pending expiry", "delivered warning" } )
		{
			var row = JsonNode.Parse( LegacyTerminal )!;
			if ( state == "suspended" ) row["ElapsedMs"] = 0;
			if ( state == "pending warning" ) row["PendingPreExpiry"] = true;
			if ( state == "pending expiry" ) { row["ElapsedMs"] = 30000; row["PendingPreExpiry"] = true; row["PendingExpiry"] = true; }
			if ( state == "delivered warning" ) { row["PreExpiryEnabled"] = false; row["PreExpiryReached"] = true; }
			foreach ( var snapshot in ReadBoth( WorldWithRows( world, row.ToJsonString() ) ) )
			{
				Assert.IsFalse( snapshot.MessageTube!.Rows[0].Read, state );
				var clock = new Clock();
				clock.Hud.RestoreMessageTube( snapshot.MessageTube );
				clock.Hud.DispatchChallengeTerminalTimers();
				Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ), state );
				Assert.IsFalse( clock.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached, state );
				clock.Pump( 524_999 );
				Assert.IsFalse( clock.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached, state );
				clock.Pump( 525_000 );
				Assert.IsTrue( clock.Hud.ChallengeTerminalState( 0 )!.Value.PreExpiryReached, state );
				clock.Pump( 529_999 );
				Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ), state );
				clock.Pump( 530_000 );
				Assert.IsFalse( clock.Hud.IsPendingMessageVisible( 0 ), state );
			}
		}
	}

	[TestMethod]
	public void LegacyReadRowsStayStoppedAndNoTubeStillLoads()
	{
		var world = World();
		var row = JsonNode.Parse( LegacyTerminal )!;
		row["Active"] = false;
		row["PreExpiryEnabled"] = false;
		row["ElapsedMs"] = 0;
		foreach ( var snapshot in ReadBoth( WorldWithRows( world, row.ToJsonString() ) ) )
		{
			Assert.IsTrue( snapshot.MessageTube!.Rows[0].Read );
			var clock = new Clock();
			clock.Hud.RestoreMessageTube( snapshot.MessageTube );
			clock.Pump( 9_000_000 );
			Assert.IsTrue( clock.Hud.IsPendingMessageVisible( 0 ) );
			Assert.IsFalse( clock.Hud.ChallengeTerminalState( 0 )!.Value.Active );
		}
		foreach ( var snapshot in ReadBoth( Encoding.UTF8.GetBytes( world.ToJsonString() ) ) )
			Assert.IsNull( snapshot.MessageTube );
	}

	[TestMethod]
	public void CurrentRowsKeepTheirExactBytesAndTheirReadState()
	{
		foreach ( var read in new[] { false, true } )
		{
			var raw = CurrentTerminal.Replace( "false", read ? "true" : "false" );
			var row = ParkResumeJson.Deserialize<MessageTubeRowSave>( Encoding.UTF8.GetBytes( raw ) );
			Assert.AreEqual( read, row.Read );
			Assert.AreEqual( raw, Encoding.UTF8.GetString( ParkResumeJson.Serialize( row ) ) );
			foreach ( var snapshot in ReadBoth( WorldWithRows( World(), raw ) ) )
				Assert.AreEqual( read, snapshot.MessageTube!.Rows[0].Read );
		}
	}

	[TestMethod]
	public void LegacyCompatibilityDoesNotAdmitPartialMixedOrMalformedRows()
	{
		var world = World();
		var corruptions = new (string Name, Action<JsonObject> Change)[]
		{
			("unknown field", row => row["Surprise"] = 1),
			("mixed Read false", row => row["Read"] = false),
			("mixed Read true", row => row["Read"] = true),
			("null active", row => row["Active"] = null),
			("string active", row => row["Active"] = "true"),
			("null pending", row => row["PendingExpiry"] = null),
			("negative elapsed", row => row["ElapsedMs"] = -1),
			("unbounded elapsed", row => row["ElapsedMs"] = 630001),
			("excessive lifetime", row => row["Lifetime"] = 600001),
			("stopped pending expiry", row => { row["Active"] = false; row["PendingExpiry"] = true; }),
			("disabled pending warning", row => { row["PreExpiryEnabled"] = false; row["PendingPreExpiry"] = true; }),
			("delivered pending warning", row => { row["PreExpiryReached"] = true; row["PendingPreExpiry"] = true; }),
			("plain timer state", row => { row["Kind"] = 0; row["Tag"] = 0; row["ContextShort"] = 0; row["ContextInt"] = 0; row["Lifetime"] = 0; })
		};
		foreach ( var (name, corrupt) in corruptions )
		{
			var row = JsonNode.Parse( LegacyTerminal )!.AsObject();
			corrupt( row );
			RejectBoth( WorldWithRows( world, row.ToJsonString() ), name );
		}
		foreach ( var property in JsonNode.Parse( LegacyTerminal )!.AsObject().Select( property => property.Key ).ToArray() )
		{
			var row = JsonNode.Parse( LegacyTerminal )!.AsObject();
			row.Remove( property );
			RejectBoth( WorldWithRows( world, row.ToJsonString() ), "missing " + property );
		}
		var current = JsonNode.Parse( CurrentTerminal )!;
		current["Surprise"] = 1;
		RejectBoth( WorldWithRows( world, current.ToJsonString() ), "unknown current field" );
		RejectBoth( WorldWithRows( world, "null" ), "null row" );
	}
}
