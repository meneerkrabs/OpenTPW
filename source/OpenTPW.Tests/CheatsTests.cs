using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.Mods;
using OpenTPW.UI.Original;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>
/// The park's cheats (docs/CHEATS.md): the record in the park economy, the turn-boundary queue, the save and its validation, the used-only recording of <c>opentpw.cheats</c>, the cheated mark
/// and the pins that must not move for a park that never cheated.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CheatsTests
{
	internal static ModSet On() => ModSet.Resolve( Array.Empty<DiscoveredMod>(),
		new ModSettings { Enabled = new Dictionary<string, bool> { [OfficialExtensions.Cheats.Id] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );

	private static ParkEconomy Park() => EconomyTestData.Park();

	/// <summary>Advances to the first tick of the next park turn; returns that turn.</summary>
	private static long NextTurn( ParkEconomy park )
	{
		var turn = park.Turn;
		while ( park.Turn == turn )
			park.Advance( 1 );
		return park.Turn;
	}

	// ---- The extension ------------------------------------------------------------------------------

	[TestMethod]
	public void TheExtensionIsRegisteredOffSimulatingAndSelectable()
	{
		var cheats = OfficialExtensions.Cheats;
		Assert.AreEqual( "opentpw.cheats", cheats.Id );
		Assert.IsFalse( cheats.DefaultEnabled );
		Assert.IsTrue( cheats.AffectsSimulation );
		Assert.IsTrue( cheats.PlayerSelectable, "selectable since K3 shipped its window" );
		Assert.AreSame( cheats, OfficialExtensions.Find( "opentpw.cheats" ) );
		Assert.IsFalse( ModSet.Defaults( OfficialExtensions.All ).IsEnabled( cheats.Id ) );
		Assert.IsTrue( ParkResumeMods.UsedOnlyExtensionIds.Contains( cheats.Id ) );
		using ( OpenTpwExtensions.Use( On() ) )
			Assert.IsTrue( FeatureGates.Cheats );
		Assert.IsFalse( FeatureGates.Cheats );
	}

	// ---- The record and the queue ---------------------------------------------------------------------

	[TestMethod]
	public void AParkThatNeverCheatedHasAnEmptyRecordThatItsSaveLeavesOut()
	{
		var park = Park();
		Assert.IsTrue( park.Cheats.IsEmpty );
		Assert.IsFalse( park.Cheats.Cheated );
		Assert.IsNull( ParkSaveFile.Capture( park ).Cheats );
		StringAssert.DoesNotMatch( ParkSaveFile.Serialize( park ), new System.Text.RegularExpressions.Regex( "Cheat" ) );
	}

	[TestMethod]
	public void AQueuedCheatAppliesAtTheStartOfTheNextTurnAndTheModeStaysOn()
	{
		var park = Park();
		var applied = new List<CheatLogEntry>();
		park.Cheats.Applied += applied.Add;
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Mode ) ) );
		Assert.IsFalse( park.Cheats.Mode, "nothing applies before the turn" );
		Assert.AreEqual( 1, park.Cheats.Pending.Count );
		Assert.IsFalse( park.Cheats.IsEmpty, "a queued cheat is saved" );
		var turn = NextTurn( park );
		Assert.IsTrue( park.Cheats.Mode );
		Assert.IsTrue( park.Cheats.Cheated );
		Assert.AreEqual( 0, park.Cheats.Pending.Count );
		CollectionAssert.AreEqual( new[] { new CheatLogEntry( turn, CheatKind.Mode, 0 ) }, park.Cheats.Log.ToArray() );
		CollectionAssert.AreEqual( park.Cheats.Log.ToArray(), applied.ToArray() );
		park.Advance( 2000 );
		Assert.IsTrue( park.Cheats.Mode, "nothing turns the mode off (no 0x4053C0(0) exists)" );
		Assert.IsFalse( park.Cheats.Extension, "the mode is the original's" );
		Assert.IsFalse( park.Cheats.UsesExtension );
	}

	[TestMethod]
	public void ApplyingACheatTakesNoDrawAndAPausedParkKeepsItsQueue()
	{
		var plain = Park();
		var cheated = Park();
		plain.Speed = GameSpeed.Paused;
		cheated.Speed = GameSpeed.Paused;
		Assert.IsTrue( cheated.QueueCheat( new CheatAction( CheatKind.Mode ) ) );
		Assert.IsFalse( cheated.NextTickStartsTurn, "a paused park enters no turn" );
		plain.Speed = GameSpeed.Normal;
		cheated.Speed = GameSpeed.Normal;
		plain.Advance( 600 );
		cheated.Advance( 600 );
		Assert.IsTrue( cheated.Cheats.Mode );
		Assert.AreEqual( plain.World.State, cheated.World.State, "the world generator moved the same" );
		Assert.AreEqual( plain.Ledger.Balance, cheated.Ledger.Balance );
	}

	[TestMethod]
	public void TheQueueIsBoundedAndRefusesInvalidActionsAndThemeParkIncParks()
	{
		var park = Park();
		for ( var index = 0; index < ParkCheats.MaximumPending; index++ )
			Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Mode ) ) );
		Assert.IsFalse( park.QueueCheat( new CheatAction( CheatKind.Mode ) ), "full" );
		Assert.IsFalse( Park().QueueCheat( new CheatAction( CheatKind.Mode, 3 ) ), "the mode takes no argument" );
		Assert.IsFalse( Park().QueueCheat( new CheatAction( (CheatKind)999 ) ) );
		var tpi = new ParkEconomy( EconomyTestData.Settings(), EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 7, EconomyRules.ThemeParkIncMoney );
		Assert.IsFalse( tpi.QueueCheat( new CheatAction( CheatKind.Mode ) ), "Theme Park World's cheats only" );
		NextTurn( park );
		Assert.AreEqual( ParkCheats.MaximumPending, park.Cheats.Log.Count, "a batch applies in one turn" );
	}

	// ---- Saves -----------------------------------------------------------------------------------------

	[TestMethod]
	public void TheRecordAndAPendingCheatRoundTripThroughTheParkSave()
	{
		var park = Park();
		park.QueueCheat( new CheatAction( CheatKind.Mode ) );
		NextTurn( park );
		park.QueueCheat( new CheatAction( CheatKind.Mode ) );
		var data = ParkSaveFile.Capture( park );
		Assert.IsNotNull( data.Cheats );
		Assert.IsTrue( data.Cheats!.Mode );
		Assert.AreEqual( 1, data.Cheats.Log.Count );
		Assert.AreEqual( 1, data.Cheats.Pending!.Count );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ), "the same bytes again" );
		Assert.IsTrue( restored.Cheats.Mode );
		CollectionAssert.AreEqual( park.Cheats.Log.ToArray(), restored.Cheats.Log.ToArray() );
		CollectionAssert.AreEqual( park.Cheats.Pending.ToArray(), restored.Cheats.Pending.ToArray() );
		// the resumed park applies the waiting cheat at its next turn, as the uninterrupted one does
		var turn = NextTurn( park );
		Assert.AreEqual( turn, NextTurn( restored ) );
		CollectionAssert.AreEqual( park.Cheats.Log.ToArray(), restored.Cheats.Log.ToArray() );
		Assert.AreEqual( ParkSaveFile.Serialize( park ), ParkSaveFile.Serialize( restored ) );
	}

	[TestMethod]
	public void ASavedRecordThatDoesNotHoldTogetherIsRefused()
	{
		var park = Park();
		park.QueueCheat( new CheatAction( CheatKind.Mode ) );
		NextTurn( park );
		var good = ParkSaveFile.Serialize( park );
		var turn = park.Turn;
		void Refused( string from, string to, string because )
		{
			Assert.IsTrue( good.Contains( from, StringComparison.Ordinal ), from );
			Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( good.Replace( from, to, StringComparison.Ordinal ) ) ), because );
		}
		Refused( $"\"Turn\": {turn}", $"\"Turn\": {turn + 1}", "an entry after the park turn" );
		Refused( "\"Kind\": \"Mode\"", "\"Kind\": \"Weather\"", "an unknown kind" );
		Refused( "\"Argument\": 0", "\"Argument\": 7", "an argument the kind does not take" );
		Refused( "\"Mode\": true", "\"Mode\": false", "applied cheats with the mode off" );
		Refused( "\"Log\": [", "\"Pending\": [], \"Log\": [", "an empty pending list (none is left out)" );
		Refused( "\"Log\": [", "\"DroppedLog\": 3, \"Log\": [", "entries dropped from a log that is not full" );
		Assert.IsTrue( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( good ) ).Cheats!.Mode );
	}

	[TestMethod]
	public void TheRecordJoinsTheRunHashOnlyOnceTheParkCheats()
	{
		var plain = Park();
		var cheated = Park();
		var before = WorldStateHash.Compute( new WorldStateSources { Economy = plain } );
		Assert.AreEqual( before, WorldStateHash.Compute( new WorldStateSources { Economy = cheated } ) );
		cheated.QueueCheat( new CheatAction( CheatKind.Mode ) );
		Assert.AreNotEqual( before, WorldStateHash.Compute( new WorldStateSources { Economy = cheated } ), "a waiting cheat is park state" );
		NextTurn( plain );
		NextTurn( cheated );
		Assert.AreNotEqual( WorldStateHash.Compute( new WorldStateSources { Economy = plain } ), WorldStateHash.Compute( new WorldStateSources { Economy = cheated } ) );
	}

	// ---- The extension's recording -------------------------------------------------------------------

	[TestMethod]
	public void TheExtensionIsRecordedOnlyForAParkThatUsedItsOwnCheats()
	{
		var on = On();
		Assert.IsNull( ParkResumeMods.Capture( on, ParkResumeMods.InUse( null ) ), "on but unused: nothing recorded" );
		var recorded = ParkResumeMods.Capture( on, ParkResumeMods.InUse( null, cheats: true ) );
		Assert.AreEqual( OfficialExtensions.Cheats.Id, recorded!.Single().Id );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( null, on ).Count, "a park without the record loads with the extension on" );
		var off = ModSet.Defaults( OfficialExtensions.All );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( null, off ).Count );
		StringAssert.Contains( string.Join( "; ", ParkResumeMods.Unmet( recorded, off ) ), "needs opentpw.cheats 1.0.0" );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( recorded, on ).Count );
		// a park cheated through the original's cheat mode only does not need the extension
		var park = Park();
		park.QueueCheat( new CheatAction( CheatKind.Mode ) );
		NextTurn( park );
		Assert.IsTrue( park.Cheats.Cheated );
		Assert.IsFalse( park.Cheats.UsesExtension );
	}

	/// <summary>A world save whose park used the extension's own cheats must record the extension, so it cannot load with it off; a park cheated through the cheat mode only need not.</summary>
	[TestMethod]
	public void AWorldSaveOfAParkThatUsedTheExtensionsCheatsMustRecordTheExtension()
	{
		var snapshot = ParkResumeSaveFileTests.Envelope();
		ParkSaveFile.Data WithCheats( string cheats )
		{
			var options = new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
			var json = System.Text.Json.JsonSerializer.Serialize( snapshot.Economy, options );
			Assert.IsTrue( json.EndsWith( "}", StringComparison.Ordinal ) );
			return ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json[..^1] + ",\"Cheats\":" + cheats + "}" ) );
		}
		var modeOnly = snapshot with { Economy = WithCheats( "{\"Mode\":true,\"Log\":[]}" ) };
		ParkResumeSaveFile.ValidateShape( modeOnly );
		var extension = snapshot with { Economy = WithCheats( "{\"Mode\":true,\"Extension\":true,\"Log\":[]}" ) };
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.ValidateShape( extension ) ).Message,
			"used the extension's cheats but the save does not record opentpw.cheats" );
		var recorded = extension with { Mods = ParkResumeMods.Capture( On(), ParkResumeMods.InUse( null, cheats: true ) ) };
		ParkResumeSaveFile.ValidateShape( recorded );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( recorded.Mods, ModSet.Defaults( OfficialExtensions.All ) ) ).Message, "needs opentpw.cheats 1.0.0" );
	}

	// ---- Pins --------------------------------------------------------------------------------------------

	[TestMethod]
	public void EveryExistingPinAndTheSaveBytesAreUnchangedWithTheExtensionOnOrOff()
	{
		string Run( ModSet set, out ulong hash )
		{
			using var scope = OpenTpwExtensions.Use( set );
			var park = new DeterminismTests.SyntheticPark( WorldSeed.Default );
			park.Run( DeterminismTests.PinnedRunTicks );
			hash = park.Hash;
			return ParkSaveFile.Serialize( park.Economy );
		}
		var off = Run( ModSet.Defaults( OfficialExtensions.All ), out var offHash );
		var on = Run( On(), out var onHash );
		Assert.AreEqual( DeterminismTests.FixedRunHash, offHash, $"0x{offHash:X16}" );
		Assert.AreEqual( DeterminismTests.FixedRunHash, onHash, $"0x{onHash:X16}" );
		Assert.AreEqual( off, on, "the same save bytes" );
		Assert.IsFalse( on.Contains( "Cheat", StringComparison.Ordinal ) );
	}

	// ---- The mark ----------------------------------------------------------------------------------------

	[TestMethod]
	public void TheLoadMenuSaysWhenAParkIsCheatedAndASharedParkIsFlaggedButNotRefused()
	{
		static UiStringTable Table( string language ) => new( language, new string[1], new[] { "" }, new string[1], new string[1] );
		var strings = Table( "English" );
		Assert.AreEqual( " (cheated)", FrontEndMenu.CheatedSuffix( strings, true ) );
		Assert.AreEqual( "", FrontEndMenu.CheatedSuffix( strings, false ) );
		Assert.AreEqual( " (vals gespeeld)", FrontEndMenu.CheatedSuffix( Table( "Dutch" ), true ) );
		Assert.AreEqual( "cheated", OpenTPW.Online.Packages.CompatibilityFlags.Cheated );
		Assert.IsTrue( OpenTPW.Online.Packages.CompatibilityFlags.Known.Contains( "cheated" ) );
		// a package that carries the flag is a valid package: the server and older clients accept any well-formed flag
		var snapshot = new OpenTPW.Online.Packages.ParkSnapshot( OpenTPW.Online.Packages.MinimalParkPayload.Format, 1, "jungle", new byte[] { 1, 2, 3 },
			Array.Empty<OpenTPW.Online.Packages.RequiredContent>(), new[] { OpenTPW.Online.Packages.CompatibilityFlags.Cheated } );
		var package = OpenTPW.Online.Packages.ParkPackage.Create( snapshot, new OpenTPW.Online.Packages.ParkInfo( "Cheaters", "", "me", "jungle" ),
			new OpenTPW.Online.Packages.GameInfo( "test", "English" ), null );
		var read = OpenTPW.Online.Packages.ParkPackage.Read( package.ToBytes() );
		CollectionAssert.Contains( read.Manifest.Flags.ToArray(), "cheated" );
		// the export marks a cheated park's snapshot and leaves any other alone; nothing refuses it (ParkSharing.ModsRefusal does not count the cheats extension)
		var plain = snapshot with { Flags = new[] { OpenTPW.Online.Packages.CompatibilityFlags.ReadOnlyVisit } };
		Assert.AreSame( plain, ParkSharing.MarkCheated( plain, false ) );
		CollectionAssert.AreEqual( new[] { "read-only-visit", "cheated" }, ParkSharing.MarkCheated( plain, true ).Flags.ToArray() );
		using ( OpenTpwExtensions.Use( On() ) )
			Assert.IsNull( ParkSharing.ModsRefusal(), "the cheats extension on: still nothing to refuse" );
	}
}
