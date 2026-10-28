using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;

namespace OpenTPW.Tests;

/// <summary>Synthetic own-format saves retain the still-pending decline, not a visible/reposted offer or an early economy mutation.</summary>
[TestClass, DoNotParallelize]
public class ChallengeOfferSaveTests
{
	private static void Set( object target, string field, object value ) =>
		target.GetType().GetField( field, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	private sealed class Rig
	{
		public long Now;
		public ParkEconomy Economy { get; }
		public Level Level { get; } = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		public ParkHud Hud { get; } = (ParkHud)RuntimeHelpers.GetUninitializedObject( typeof( ParkHud ) );
		public Rig( ParkResumeSnapshot snapshot )
		{
			Economy = ParkSaveFile.Restore( snapshot.Economy, EconomyTestData.Settings(), EconomyTestData.Catalog() );
			Set( Level, "<Park>k__BackingField", ParkEconomyRuntime.ForResume( Economy, ParkStartKind.FullSimulation ) );
			Set( Hud, "level", Level );
			Hud.ChallengeMessageClock = () => Now;
		}
		public void PostOffer()
		{
			Economy.Objectives.Update( 540 * ParkCalendar.SecondsPerDay, 1, _ => 0 ).ToList();
			Assert.IsTrue( Economy.Objectives.Current is { Accepted: false } );
			Assert.IsTrue( Hud.KeepChallengeOfferPosted( Economy ) );
		}
		public void Pump( long now )
		{
			Now = now;
			Hud.QueueChallengeTerminalTimers();
			Hud.DispatchChallengeTerminalTimers();
			Hud.ReleaseChallengeTerminalRows();
		}
	}

	private static ParkResumeSnapshot Capture( ParkResumeSnapshot template, Rig rig ) => template with
	{
		Economy = ParkSaveFile.Capture( rig.Economy, ParkSaveFile.ToWorldState( template.Economy ) ),
		MessageTube = rig.Hud.CaptureMessageTube()
	};

	private static ParkResumeSnapshot Load( byte[] bytes, int slice )
	{
		if ( slice == 0 ) return ParkResumeSaveFile.Deserialize( bytes );
		ParkResumeSnapshot? result = null;
		foreach ( var _ in ParkResumeSaveFile.DeserializeSteps( bytes, value => result = value, slice ) ) { }
		Assert.IsNotNull( result );
		return result!;
	}

	private static long Remove( Rig rig, bool expiry )
	{
		if ( expiry ) rig.Pump( ParkHud.ChallengeOfferLifetime );
		else Assert.IsTrue( rig.Hud.DeletePendingMessage( 0 ) );
		Assert.IsFalse( rig.Hud.IsPendingMessageVisible( 0 ) );
		Assert.IsTrue( rig.Economy.Objectives.Current is { Accepted: false }, "the original 500 ms delay has not elapsed" );
		return rig.Now;
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void DeleteOrExpiryThenSaveBeforeReleaseKeepsTheRemainingDelayAndDeclinesOnce( bool expiry )
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var old = new Rig( template );
		old.PostOffer();
		var removed = Remove( old, expiry );
		old.Now = removed + 200;
		var snapshot = Capture( template, old );
		Assert.AreEqual( (int?)300, snapshot.MessageTube!.Rows.Single().OfferReleaseDelayMs );
		Assert.AreEqual( 0, old.Level.Commands.Log.Count, "capture must not deliver the decline" );
		var bytes = ParkResumeSaveFile.Serialize( snapshot );
		var sliced = ParkResumeSaveFile.BeginSlicedSerialize( snapshot );
		while ( !sliced.Run( TimeSpan.Zero ) ) { }
		CollectionAssert.AreEqual( bytes, sliced.Bytes );
		foreach ( var slice in new[] { 0, 1, 257 } )
		{
			var loaded = Load( bytes, slice );
			var next = new Rig( loaded ) { Now = 500_000 };
			var cues = 0;
			GameAudio.UiSoundObserver = _ => cues++;
			GameAudio.UiGlobalSoundObserver = _ => cues++;
			try { next.Hud.RestoreMessageTube( loaded.MessageTube ); }
			finally { GameAudio.UiSoundObserver = null; GameAudio.UiGlobalSoundObserver = null; }
			Assert.AreEqual( 0, cues, "a hidden removal is not posted again" );
			Assert.IsFalse( next.Hud.IsPendingMessageVisible( 0 ) );
			Assert.IsFalse( next.Hud.OpenPendingMessage( 0 ) );
			Assert.IsFalse( next.Hud.KeepChallengeOfferPosted( next.Economy ), "hidden actions prevent reposting before release" );
			Assert.AreEqual( 0, next.Level.Commands.Log.Count, "restoration issues no command" );
			next.Hud.SetChallengeMessageTimersSuspended( true );
			next.Pump( 500_299 );
			Assert.IsTrue( next.Economy.Objectives.Current is { Accepted: false } );
			next.Pump( 500_300 );
			Assert.IsNull( next.Economy.Objectives.Current, "release runs even while lifetime timers are suspended" );
			Assert.AreEqual( 0, next.Hud.PendingMessages.Count );
			var command = (AnswerChallengeCommand)next.Level.Commands.Log.Single().Command;
			Assert.IsFalse( command.Accept );
			Assert.AreEqual( snapshot.Economy.Objectives.Current!.DefinitionIndex, command.Definition );
			Assert.AreEqual( snapshot.Economy.Objectives.Current.OfferedDay, command.OfferedDay );
			next.Pump( 600_000 );
			Assert.AreEqual( 1, next.Level.Commands.Log.Count, "no duplicate decline" );
			Assert.IsFalse( next.Hud.KeepChallengeOfferPosted( next.Economy ) );
		}
		old.Pump( removed + 499 );
		Assert.IsNotNull( old.Economy.Objectives.Current );
		old.Pump( removed + 500 );
		Assert.IsNull( old.Economy.Objectives.Current, "uninterrupted runtime timing is unchanged" );
	}

	[TestMethod]
	public void ResavingCountsDownAndAnOverdueRemovalKeepsExplicitZero()
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var old = new Rig( template ); old.PostOffer(); Remove( old, false ); old.Now = 200;
		var first = Load( ParkResumeSaveFile.Serialize( Capture( template, old ) ), 257 );
		var next = new Rig( first ) { Now = 10_000 };
		next.Hud.RestoreMessageTube( first.MessageTube );
		next.Now += 100;
		var second = Capture( first, next );
		Assert.AreEqual( (int?)200, second.MessageTube!.Rows.Single().OfferReleaseDelayMs, "not reset to 500" );
		var last = new Rig( second ) { Now = 20_000 };
		last.Hud.RestoreMessageTube( Load( ParkResumeSaveFile.Serialize( second ), 1 ).MessageTube );
		last.Pump( 20_199 ); Assert.IsNotNull( last.Economy.Objectives.Current );
		last.Pump( 20_200 ); Assert.IsNull( last.Economy.Objectives.Current );
		old.Now = 800; // no release pump yet
		var overdue = Capture( template, old );
		Assert.AreEqual( (int?)0, overdue.MessageTube!.Rows.Single().OfferReleaseDelayMs );
		StringAssert.Contains( Encoding.UTF8.GetString( ParkResumeSaveFile.Serialize( overdue ) ), "\"OfferReleaseDelayMs\":0" );
		var due = new Rig( overdue ) { Now = 30_000 };
		due.Hud.RestoreMessageTube( Load( ParkResumeSaveFile.Serialize( overdue ), 1 ).MessageTube );
		Assert.IsNotNull( due.Economy.Objectives.Current );
		Assert.AreEqual( 0, due.Level.Commands.Log.Count );
		due.Pump( due.Now ); Assert.IsNull( due.Economy.Objectives.Current );
	}

	private static ActiveChallenge ReplaceWithNewOffer( Rig rig )
	{
		var current = rig.Economy.Objectives.Current;
		var replacement = new ActiveChallenge( 7, (current?.OfferedDay ?? 540) + 1, false, 0, 0, 0 );
		Set( rig.Economy.Objectives, "<Current>k__BackingField", replacement );
		return replacement;
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void AnsweredCleanupIsNotSavedAndCannotDeclineANewerFollowup( bool accept )
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var rig = new Rig( template ); rig.PostOffer();
		Assert.IsTrue( rig.Hud.OpenPendingMessage( 0 ) );
		Assert.IsTrue( rig.Hud.AnswerChallenge( rig.Economy, accept ) );
		Assert.IsNull( rig.Hud.CaptureMessageTube(), "already answered: only inert hidden cleanup remains" );
		var newer = ReplaceWithNewOffer( rig );
		Assert.IsNull( rig.Hud.CaptureMessageTube(), "a new offer does not rebind old cleanup" );
		rig.Pump( 500 );
		Assert.AreSame( newer, rig.Economy.Objectives.Current );
		Assert.AreEqual( 1, rig.Level.Commands.Log.Count, "only the explicit answer was issued" );
	}

	[TestMethod]
	public void RestoredPendingAndStaleVisibleRowsNeverDeclineADifferentOffer()
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var old = new Rig( template ); old.PostOffer(); Remove( old, false );
		var snapshot = Load( ParkResumeSaveFile.Serialize( Capture( template, old ) ), 1 );
		var next = new Rig( snapshot ); next.Hud.RestoreMessageTube( snapshot.MessageTube );
		var newer = ReplaceWithNewOffer( next );
		Assert.IsNull( next.Hud.CaptureMessageTube() );
		next.Pump( 500 );
		Assert.AreSame( newer, next.Economy.Objectives.Current );
		Assert.AreEqual( 0, next.Level.Commands.Log.Count );
		var visible = new Rig( template ); visible.PostOffer();
		newer = ReplaceWithNewOffer( visible );
		Assert.IsTrue( visible.Hud.DeletePendingMessage( 0 ) );
		Assert.IsNull( visible.Hud.CaptureMessageTube() );
		visible.Pump( 500 );
		Assert.AreSame( newer, visible.Economy.Objectives.Current );
		Assert.AreEqual( 0, visible.Level.Commands.Log.Count );
	}

	[DataTestMethod, DataRow( false ), DataRow( true )]
	public void RestoredRemovalStillRespectsGuestRightsAndReadOnlyVisits( bool readOnly )
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var old = new Rig( template ); old.PostOffer(); Remove( old, false );
		var snapshot = Load( ParkResumeSaveFile.Serialize( Capture( template, old ) ), 257 );
		var next = new Rig( snapshot );
		if ( readOnly ) Set( next.Level, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( ParkVisitInfo ) ) );
		else { next.Level.Commands.Player = 2; next.Level.Commands.Rights = new CoopBuildRights( next.Level ); }
		next.Hud.RestoreMessageTube( snapshot.MessageTube );
		next.Pump( 500 );
		Assert.IsTrue( next.Economy.Objectives.Current is { Accepted: false } );
		Assert.AreEqual( 0, next.Level.Commands.Log.Count );
	}

	[TestMethod]
	public void HiddenOfferOccupiesItsSlotUntilReleaseButHiddenTerminalsStillSaveNothing()
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var old = new Rig( template ); old.PostOffer(); Remove( old, false );
		var snapshot = Load( ParkResumeSaveFile.Serialize( Capture( template, old ) ), 257 );
		var next = new Rig( snapshot ); next.Hud.RestoreMessageTube( snapshot.MessageTube );
		for ( var index = 0; index < 9; index++ ) Assert.IsTrue( next.Hud.PostGameMessage( "plain " + index ) );
		Assert.IsFalse( next.Hud.PostGameMessage( "eleventh" ) );
		next.Pump( 500 );
		Assert.AreEqual( 9, next.Hud.PendingMessages.Count );
		Assert.IsTrue( next.Hud.PostGameMessage( "released slot" ) );
		var terminal = new Rig( template );
		Assert.IsTrue( ChallengeMessageRecords.TryGet( 1, ChallengePresentationKind.Success, out var record ) );
		Assert.IsTrue( terminal.Hud.PostChallengeTerminal( record, "terminal" ) );
		terminal.Hud.DeletePendingMessage( 0 );
		Assert.IsNull( terminal.Hud.CaptureMessageTube() );
	}

	[TestMethod]
	public void InvalidPendingRemovalShapesAndMissingTargetsAreRefusedBeforeRestoration()
	{
		var template = ParkResumeSaveFileTests.Envelope();
		var pending = new MessageTubeRowSave( MessageTubeRowSave.Offer, "offer", 0, 0, 0, 0, false ) { OfferReleaseDelayMs = 200 };
		var tube = new MessageTubeSaveData { Rows = new[] { pending } };
		var empty = new Rig( template );
		Assert.ThrowsException<InvalidDataException>( () => empty.Hud.RestoreMessageTube( tube ) );
		Assert.AreEqual( 0, empty.Hud.PendingMessages.Count );
		var old = new Rig( template ); old.PostOffer(); Remove( old, false );
		var valid = JsonNode.Parse( ParkResumeSaveFile.Serialize( Capture( template, old ) ) )!;
		foreach ( var name in new[] { "negative", "too long", "not offer", "read offer", "unknown", "wrong type", "no target", "accepted target" } )
		{
			var copy = valid.DeepClone();
			var row = copy["MessageTube"]!["Rows"]![0]!;
			switch ( name )
			{
				case "negative": row["OfferReleaseDelayMs"] = -1; break;
				case "too long": row["OfferReleaseDelayMs"] = 501; break;
				case "not offer": row["Kind"] = 0; break;
				case "read offer": row["Read"] = true; break;
				case "unknown": row["Surprise"] = 1; break;
				case "wrong type": row["OfferReleaseDelayMs"] = true; break;
				case "no target": copy["Economy"]!["Objectives"]!["Current"] = null; break;
				case "accepted target": copy["Economy"]!["Objectives"]!["Current"]!["Accepted"] = true; break;
			}
			var bytes = Encoding.UTF8.GetBytes( copy.ToJsonString() );
			foreach ( var slice in new[] { 0, 1, 257 } )
				Assert.ThrowsException<InvalidDataException>( () => Load( bytes, slice ), name );
		}
		foreach ( var value in new[] { "null", "0" } )
		{
			var legacy = "{\"Kind\":1,\"Text\":\"offer\",\"Tag\":0,\"ContextShort\":0,\"ContextInt\":0,\"Lifetime\":0,\"ElapsedMs\":0,\"Active\":false,\"PreExpiryEnabled\":false,\"PreExpiryReached\":false,\"OfferReleaseDelayMs\":" + value + "}";
			Assert.ThrowsException<JsonException>( () => ParkResumeJson.Deserialize<MessageTubeRowSave>( Encoding.UTF8.GetBytes( legacy ) ), "legacy shape must not absorb a new field" );
		}
		var visible = pending with { OfferReleaseDelayMs = null };
		var visibleJson = Encoding.UTF8.GetString( ParkResumeJson.Serialize( visible ) );
		Assert.IsFalse( visibleJson.Contains( nameof( MessageTubeRowSave.OfferReleaseDelayMs ) ), "ordinary rows keep their previous bytes" );
	}
}
