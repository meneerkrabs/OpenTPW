using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Veldrid;

namespace OpenTPW.Tests;

/// <summary>
/// The original's cheat mode (docs/CHEATS.md, phase K2): the secret park name, the Alt-only key table, Alt+R, Alt+1 and the ten ticket cheats, the ticket provenance in the player's ledger, and the
/// <c>CHTS</c> chunk of an original save in and out.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CheatsOriginalTests
{
	private static ParkEconomy Park( ParkGameMode mode = ParkGameMode.FullSimulation ) => EconomyTestData.Park( mode: mode );

	private static long NextTurn( ParkEconomy park )
	{
		var turn = park.Turn;
		while ( park.Turn == turn )
			park.Advance( 1 );
		return park.Turn;
	}

	private static List<ParkEvent> Events( ParkEconomy park )
	{
		var events = new List<ParkEvent>();
		park.EventRaised += events.Add;
		return events;
	}

	// ---- The secret park name ----------------------------------------------------------------------------

	[TestMethod]
	public void TheSecretNameIsKevinTheCosmicCowInTheStoredXorForm()
	{
		Assert.AreEqual( SecretParkName.Line1Stored, SecretParkName.Transform( "Kevin the" ) );
		Assert.AreEqual( SecretParkName.Line2Stored, SecretParkName.Transform( "Cosmic Cow" ) );
		Assert.IsTrue( SecretParkName.Matches( "Kevin the", "Cosmic Cow" ) );
		Assert.IsFalse( SecretParkName.Matches( "kevin the", "Cosmic Cow" ), "case-sensitive (wcscmp)" );
		Assert.IsFalse( SecretParkName.Matches( "Kevin the ", "Cosmic Cow" ) );
		Assert.IsFalse( SecretParkName.Matches( "Cosmic Cow", "Kevin the" ), "the lines in order" );
		Assert.IsFalse( SecretParkName.Matches( "Kevin the", "" ) );
		// the transform is its own inverse and re-reads the length after every unit: a unit equal to its index becomes 0 and ends the string there
		Assert.AreEqual( "Kevin the", SecretParkName.Transform( SecretParkName.Line1Stored ) );
		Assert.AreEqual( "a", SecretParkName.Transform( "a\u0001bc" ) );
		Assert.AreEqual( "", SecretParkName.Transform( "" ) );
	}

	[TestMethod]
	public void RenamingTheParkToTheSecretNameTurnsTheCheatModeOnAtTheNextTurnAndTheSignKeepsTheName()
	{
		var economy = Park();
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		typeof( Level ).GetField( "<Park>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!
			.SetValue( level, ParkEconomyRuntime.ForResume( economy, ParkStartKind.FullSimulation ) );
		typeof( Level ).GetProperty( nameof( Level.ParkName ) )!.SetValue( level, new ParkNameLines( "LOST", "Kingdom" ) );
		Assert.IsTrue( level.RenamePark( "Kevin", "Cosmic Cow" ) );
		Assert.AreEqual( 0, economy.Cheats.Pending.Count, "another name is just a name" );
		Assert.IsTrue( level.RenamePark( "Kevin the", "Cosmic Cow" ) );
		Assert.AreEqual( new ParkNameLines( "Kevin the", "Cosmic Cow" ), level.ParkName, "the sign keeps the name" );
		Assert.AreEqual( new CheatAction( CheatKind.Mode ), economy.Cheats.Pending.Single() );
		Assert.IsFalse( economy.Cheats.Mode );
		NextTurn( economy );
		Assert.IsTrue( economy.Cheats.Mode );
		Assert.IsFalse( economy.Cheats.UsesExtension, "the original's cheat mode needs no extension" );
		Assert.IsTrue( level.RenamePark( "Theme", "Park" ) );
		NextTurn( economy );
		Assert.IsTrue( economy.Cheats.Mode, "renaming it back does not turn the mode off" );
	}

	// ---- The key table ---------------------------------------------------------------------------------

	[TestMethod]
	public void TheCheatKeysAreTheOriginalsTwelveAltOnlyEntriesAndNeedTheCheatMode()
	{
		CollectionAssert.AreEqual( new[] { Key.R, Key.Number1, Key.G, Key.Keypad1, Key.Keypad2, Key.Keypad3, Key.Keypad4, Key.Keypad5, Key.Keypad6, Key.Keypad7, Key.Keypad8, Key.Keypad9 },
			ParkHud.CheatKeys.Select( entry => entry.Key ).ToArray() );
		Assert.AreEqual( new CheatAction( CheatKind.Research ), ParkHud.CheatKeys[0].Action );
		Assert.AreEqual( new CheatAction( CheatKind.Money ), ParkHud.CheatKeys[1].Action );
		CollectionAssert.AreEqual( Enumerable.Range( 0, 10 ).ToArray(), ParkHud.CheatKeys.Skip( 2 ).Select( entry => entry.Action.Argument ).ToArray(), "Alt+G, then Alt+Num1..9 in the award order" );
		Assert.IsTrue( ParkHud.CheatKeys.Skip( 2 ).All( entry => entry.Action.Kind == CheatKind.Ticket ) );
		Assert.IsTrue( ParkHud.CheatKeys.All( entry => ParkCheats.IsValid( entry.Action ) && !ParkCheats.IsExtensionKind( entry.Action.Kind ) ) );
		var alt = Input.ModifierMask( false, false, true );
		Assert.AreEqual( ParkHud.CheatModifierMask, alt );
		var pressed = new[] { Key.G, Key.Keypad9 }; // released this frame
		CollectionAssert.AreEqual( new[] { new CheatAction( CheatKind.Ticket, 0 ), new CheatAction( CheatKind.Ticket, 9 ) }, ParkHud.CheatKeyActions( true, alt, pressed ).ToArray() );
		Assert.AreEqual( 0, ParkHud.CheatKeyActions( false, alt, pressed ).Count, "the cheat context opens only in cheat mode" );
		Assert.AreEqual( 0, ParkHud.CheatKeyActions( true, Input.ModifierMask( true, false, true ), pressed ).Count, "Alt+Shift is not Alt" );
		Assert.AreEqual( 0, ParkHud.CheatKeyActions( true, Input.ModifierMask( false, true, true ), pressed ).Count, "nor is Ctrl+Alt" );
		Assert.AreEqual( 0, ParkHud.CheatKeyActions( true, 0, pressed ).Count, "the plain keys are shortcuts" );
	}

	[TestMethod]
	public void AltRAndAltGDoNotOpenTheResearchAndEntryPriceWindows()
	{
		try
		{
			using ( Input.InjectKeyDown( Key.R ) )
			{
				Input.UpdateButtons();
				Assert.IsTrue( ParkHud.PressedNow( HudScreenShortcut.ResearchWindow ) );
			}
			Input.UpdateButtons();
			using ( Input.InjectKeyDown( Key.AltLeft, Key.R, Key.G ) )
			{
				Input.UpdateButtons();
				Assert.IsFalse( ParkHud.PressedNow( HudScreenShortcut.ResearchWindow ), "an exact modifier match, 0x40C990" );
				Assert.IsFalse( ParkHud.PressedNow( HudScreenShortcut.EntryPrice ) );
			}
		}
		finally
		{
			Input.UpdateButtons();
			Input.UpdateButtons();
		}
	}

	/// <summary>The real HUD frame queues a cheat when its key is released with Alt alone held, only in cheat mode, once per release.</summary>
	[TestMethod]
	public void TheHudQueuesACheatOnTheKeysReleaseWithAltHeldInCheatModeOnly()
	{
		var previousCamera = Camera.Mode;
		var previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		Camera.Mode = new ParkCameraMode();
		Input.Keyboard = new Input.KeyboardInfo();
		Input.KeysDown.Clear(); Input.LastKeysDown.Clear(); Input.TextEntryActive = false;
		try
		{
			var hud = VisitorWorldSelectionTests.Fixture( out _, out _ );
			var economy = Park();
			hud.GetType().GetField( "<Status>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( hud, new EconomyParkStatus( () => economy ) );
			CommandTestLevel.AttachPark( (Level)typeof( OpenTPW.Hud.ParkHud ).GetField( "level", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.GetValue( hud )!, economy );
			var font = new FontAtlas( FontAtlasTests.CreateFont( 8, new FontAtlasTests.SyntheticGlyph( 'A', 2, 2, 0, 0, 3 ) ) );
			var context = new UiContext( OriginalUiTests.FakeStrings(), new UiFonts( _ => font ), new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1000, 1000 ) };
			void Frame( params Key[] held )
			{
				Input.Keyboard = new Input.KeyboardInfo( held.ToList() );
				Input.UpdateButtons();
				hud.Update( context, UiInput.Idle( new( -1, -1 ) ), false, false, false );
			}
			Frame( Key.AltLeft, Key.Number1 );
			Frame( Key.AltLeft );
			Assert.AreEqual( 0, economy.Cheats.Pending.Count, "no cheat mode yet: the cheat context is closed" );
			economy.QueueCheat( new CheatAction( CheatKind.Mode ) );
			NextTurn( economy );
			Frame( Key.AltLeft, Key.Number1 );
			Assert.AreEqual( 0, economy.Cheats.Pending.Count, "the press does nothing" );
			Frame( Key.AltLeft, Key.Number1 );
			Frame( Key.AltLeft );
			Assert.AreEqual( new CheatAction( CheatKind.Money ), economy.Cheats.Pending.Single(), "the release fires, once" );
			Frame( Key.AltLeft, Key.ShiftLeft, Key.Keypad1 );
			Frame( Key.AltLeft, Key.ShiftLeft );
			Frame();
			Assert.AreEqual( 1, economy.Cheats.Pending.Count, "Alt+Shift is not a cheat key" );
			Frame( Key.Number1 );
			Frame();
			Assert.AreEqual( 1, economy.Cheats.Pending.Count, "1 released without Alt is no cheat" );
			// a quick release: the key and Alt let go in the same frame still fire (the original reads the mask at the key's own release message)
			Frame( Key.AltLeft, Key.Keypad5 );
			Assert.IsFalse( ParkCameraMode.KeypadScroll, "Alt alone held: the keypad does not scroll (Alt+Num4 and Alt+Num6 are ticket cheats)" );
			Frame();
			Assert.AreEqual( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.RecentVisitors ), economy.Cheats.Pending.Last() );
			Assert.AreEqual( 2, economy.Cheats.Pending.Count );
			Frame( Key.Keypad4 );
			Assert.IsTrue( ParkCameraMode.KeypadScroll, "without Alt the keypad scrolls" );
			Frame();
		}
		finally
		{
			Camera.Mode = previousCamera;
			GameEditions.Select( previousEdition );
			Input.Keyboard = new Input.KeyboardInfo();
			Input.KeysDown.Clear(); Input.LastKeysDown.Clear(); Input.TextEntryActive = false;
		}
	}

	// ---- What the cheats do ------------------------------------------------------------------------------

	[TestMethod]
	public void AltOneCreditsTenThousandAsAPrizeToTheBalanceMoneyInAndTheYearsProfit()
	{
		var park = Park();
		var balance = park.Ledger.Balance;
		var profit = park.Ledger.ProfitThisYear;
		var income = park.Ledger.CurrentTotals.GetValueOrDefault( LedgerCategory.OtherIncome );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Money ) ) );
		var turn = NextTurn( park );
		Assert.AreEqual( balance + 10000, park.Ledger.Balance );
		Assert.AreEqual( profit + 10000, park.Ledger.ProfitThisYear );
		Assert.AreEqual( income + 10000, park.Ledger.CurrentTotals[LedgerCategory.OtherIncome] );
		Assert.IsTrue( park.Cheats.Mode, "a cheat marks the park" );
		Assert.AreEqual( new CheatLogEntry( turn, CheatKind.Money, 0 ), park.Cheats.Log.Single() );
	}

	[TestMethod]
	public void AltRMakesEveryResearchItemsObjectBuildableAndLeavesTheResearchAlone()
	{
		var park = Park();
		var research = park.Research;
		Assert.IsFalse( research.IsAvailable( 1180 ) );
		Assert.IsFalse( research.IsAvailable( 1106 ) );
		Assert.IsFalse( research.IsAvailable( 1500 ) );
		var completed = research.Completed.ToArray();
		var allResearched = research.IsAllResearched;
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Research ) ) );
		var events = Events( park );
		NextTurn( park );
		Assert.IsTrue( research.IsAvailable( 1180 ) && research.IsAvailable( 1106 ) && research.IsAvailable( 1500 ) );
		Assert.IsFalse( research.IsAvailable( 1100, 1 ), "an upgrade level stays a research project: the item names its ride's object control" );
		CollectionAssert.AreEquivalent( completed, research.Completed.ToArray(), "nothing is researched" );
		Assert.AreEqual( allResearched, research.IsAllResearched );
		CollectionAssert.AreEquivalent( new[] { 1106, 1180, 1500 }, research.CheatAvailable.ToArray() );
		Assert.IsFalse( events.Any( item => item.Kind is ParkEventKind.ItemResearched or ParkEventKind.ResearchAllDone ) );
		Assert.AreEqual( "3", events.Single( item => item.Kind == ParkEventKind.CheatApplied ).Detail );
		Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, park.TryBuild( 1106, out _ ), "the object can be bought" );
		// saved and restored with the park
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.IsTrue( restored.Research.IsAvailable( 1180 ) );
		Assert.AreEqual( json, ParkSaveFile.Serialize( restored ) );
		var forged = System.Text.Json.Nodes.JsonNode.Parse( json )!;
		forged["Research"]!["CheatAvailable"] = new System.Text.Json.Nodes.JsonArray( 9999 );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( forged.ToJsonString() ) ), park.Settings, park.Catalog ),
			"an object that is no research item" );
		// a park that never used it saves no list
		Assert.IsFalse( ParkSaveFile.Serialize( Park() ).Contains( "CheatAvailable", StringComparison.Ordinal ) );
	}

	[TestMethod]
	public void ATicketCheatAwardsTheTicketSilentlyAndOnlyOnce()
	{
		var park = Park();
		var events = Events( park );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.Visitors ) ) );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.Visitors ) ) );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.CoasterHeight ) ) );
		NextTurn( park );
		CollectionAssert.AreEquivalent( new[] { GoldenTicketKind.Visitors, GoldenTicketKind.CoasterHeight }, park.Objectives.GoldenTickets.ToArray() );
		Assert.IsFalse( events.Any( item => item.Kind is ParkEventKind.GoldenTicketWon or ParkEventKind.GlobalTicketMoved ), "silent: no win, so no advisor line" );
		CollectionAssert.AreEqual( new[] { "Awarded", "None", "Awarded" }, events.Where( item => item.Kind == ParkEventKind.CheatApplied ).Select( item => item.Detail ).ToArray() );
		Assert.AreEqual( 0, park.Objectives.LastWonTickets.Count );
		Assert.IsFalse( ParkCheats.IsValid( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.CamerasEverywhere ) ), "the secret tickets have no cheat" );
		Assert.IsFalse( ParkCheats.IsValid( new CheatAction( CheatKind.Ticket, -1 ) ) );
	}

	[TestMethod]
	public void TicketCheatsWorkInInstantActionAsInTheOriginal()
	{
		var park = Park( ParkGameMode.InstantAction );
		Assert.IsTrue( park.QueueCheat( new CheatAction( CheatKind.Ticket, (int)GoldenTicketKind.ProfitYear ) ) );
		NextTurn( park );
		Assert.IsTrue( park.Objectives.GoldenTickets.Contains( GoldenTicketKind.ProfitYear ), "no game mode check in 0x40C740" );
	}

	[TestMethod]
	public void AGlobalTicketCheatClaimsRecordHundredThroughThePlayersLedger()
	{
		var ledger = new PlayerTicketLedger();
		Assert.AreEqual( GlobalTicketOutcome.Awarded, ledger.RecordGlobal( "hallow", GoldenTicketKind.CoasterHeight, 99 ) );
		Assert.AreEqual( GlobalTicketOutcome.Awarded, ledger.RecordGlobal( "hallow", GoldenTicketKind.WaterLength, 100 ) );
		var park = Park();
		park.Objectives.GlobalTicketAuthority = ( kind, record ) => ledger.RecordGlobal( "jungle", kind, record );
		var events = Events( park );
		foreach ( var kind in new[] { GoldenTicketKind.CoasterHeight, GoldenTicketKind.WaterLength, GoldenTicketKind.BigPark } )
			park.QueueCheat( new CheatAction( CheatKind.Ticket, (int)kind ) );
		NextTurn( park );
		CollectionAssert.AreEqual( new[] { "Moved", "None", "Awarded" }, events.Where( item => item.Kind == ParkEventKind.CheatApplied ).Select( item => item.Detail ).ToArray(),
			"99 < 100 moves (outcome 4), 100 is not below 100, a free ticket is earned" );
		Assert.AreEqual( "jungle", ledger.HolderOf( GoldenTicketKind.CoasterHeight )!.Theme );
		Assert.AreEqual( 100, ledger.HolderOf( GoldenTicketKind.CoasterHeight )!.Record );
		Assert.AreEqual( "hallow", ledger.HolderOf( GoldenTicketKind.WaterLength )!.Theme );
		CollectionAssert.AreEquivalent( new[] { GoldenTicketKind.CoasterHeight, GoldenTicketKind.BigPark }, park.Objectives.GoldenTickets.ToArray() );
	}

	[TestMethod]
	public void CheatTicketsCountTowardKeysAndTheProgressFileRecordsThatACheatAwardedThem()
	{
		var ledger = new PlayerTicketLedger();
		Assert.IsTrue( ledger.RecordEarned( "jungle", GoldenTicketKind.Happiness ), "won honestly first" );
		Assert.IsFalse( ledger.RecordCheated( "jungle", GoldenTicketKind.Happiness, globalAwarded: false ), "an honest ticket keeps its provenance" );
		Assert.IsTrue( ledger.RecordCheated( "jungle", GoldenTicketKind.Visitors, globalAwarded: false ) );
		Assert.AreEqual( GlobalTicketOutcome.Awarded, ledger.RecordGlobal( "jungle", GoldenTicketKind.BigPark, ParkCheats.TicketRecord ) );
		Assert.IsTrue( ledger.RecordCheated( "jungle", GoldenTicketKind.BigPark, globalAwarded: true ) );
		Assert.AreEqual( 3, ledger.EarnedTickets );
		Assert.AreEqual( 1, ledger.Keys, "three tickets make a key, cheated or not" );
		Assert.IsTrue( ledger.IsCheated( "jungle", GoldenTicketKind.Visitors ) && ledger.IsCheated( "space", GoldenTicketKind.BigPark ) );
		Assert.IsFalse( ledger.IsCheated( "jungle", GoldenTicketKind.Happiness ) );
		var bytes = PlayerTicketFile.Serialize( ledger.Capture() );
		var text = Encoding.UTF8.GetString( bytes );
		Assert.AreEqual( 2, text.Split( "\"Cheated\":true" ).Length - 1, text );
		var back = PlayerTicketFile.Deserialize( bytes );
		Assert.AreEqual( 2, back.Cheated.Count );
		Assert.AreEqual( 1, back.Keys );
		CollectionAssert.AreEqual( bytes, PlayerTicketFile.Serialize( back.Capture() ) );
		// a ledger without cheats writes no field, so older files and their digests stay as they are
		var honest = new PlayerTicketLedger();
		honest.RecordEarned( "jungle", GoldenTicketKind.Visitors );
		Assert.IsFalse( Encoding.UTF8.GetString( PlayerTicketFile.Serialize( honest.Capture() ) ).Contains( "Cheated", StringComparison.Ordinal ) );
		Assert.ThrowsException<InvalidDataException>( () => PlayerTicketLedger.Restore( honest.Capture() with { Cheated = new[] { new EarnedTicketIdentity( "jungle", GoldenTicketKind.Happiness ) } } ),
			"a cheated ticket must be earned" );
	}

	[TestMethod]
	public void ThePlayersProgressOwnerRecordsTheTicketCheatsAndFlushesTheFile()
	{
		var root = Directory.CreateTempSubdirectory( "opentpw-cheat-owner-" ).FullName;
		try
		{
			var path = Path.Combine( root, PlayerTicketFile.FileName );
			PlayerTicketFile.Save( path, new PlayerTicketLedger().Capture() );
			var runtime = ParkEconomyRuntime.ForResume( Park(), ParkStartKind.FullSimulation );
			using var owner = new PlayerProgressRuntimeOwner( path, "jungle", PlayerTicketFile.ReadIfPresent( path )!, runtime, () => true, message => Assert.Fail( message ) );
			// another park of the player's holds the water ticket with a weaker record, so the cheat's record 100 moves it here
			Assert.AreEqual( GlobalTicketOutcome.Awarded, owner.Ledger.RecordGlobal( "hallow", GoldenTicketKind.WaterLength, 50 ) );
			var economy = runtime.Economy;
			Assert.IsTrue( economy.QueueCheat( new CheatAction( CheatKind.Mode ) ) );
			foreach ( var kind in new[] { GoldenTicketKind.Visitors, GoldenTicketKind.BigPark, GoldenTicketKind.WaterLength } )
				Assert.IsTrue( economy.QueueCheat( new CheatAction( CheatKind.Ticket, (int)kind ) ) );
			NextTurn( economy );
			Assert.IsFalse( owner.Dirty, "the owner flushed the cheat tickets" );
			var file = PlayerTicketFile.LoadIfPresent( path )!;
			Assert.IsTrue( file.IsCheated( "jungle", GoldenTicketKind.Visitors ), "a local cheat ticket is earned and marked" );
			Assert.IsTrue( file.IsCheated( "jungle", GoldenTicketKind.BigPark ), "a global ticket the cheat awarded is earned and marked" );
			Assert.IsFalse( file.IsCheated( "jungle", GoldenTicketKind.WaterLength ), "a moved global was earned honestly before: its provenance stays" );
			Assert.AreEqual( "jungle", file.HolderOf( GoldenTicketKind.WaterLength )?.Theme, "the moved global's holder is the cheated park" );
			Assert.AreEqual( ParkCheats.TicketRecord, file.HolderOf( GoldenTicketKind.WaterLength )?.Record );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void TheHudNotesEveryCheatThatChangedSomethingInTheLanguageOfTheTable()
	{
		var strings = new UiStringTable( "English", new string[1], new[] { "" }, new string[1], new string[1] );
		ParkEvent Applied( CheatKind kind, string detail = "" ) => new( 0, ParkEventKind.CheatApplied, 0, (int)kind, 0, detail );
		Assert.AreEqual( "Cheat mode on", ParkHud.CheatNote( strings, Applied( CheatKind.Mode ) ) );
		Assert.AreEqual( "Cheat: 10,000 added", ParkHud.CheatNote( strings, Applied( CheatKind.Money ) ) );
		Assert.AreEqual( "Cheat: everything can be built", ParkHud.CheatNote( strings, Applied( CheatKind.Research, "3" ) ) );
		Assert.AreEqual( "Cheat: golden ticket", ParkHud.CheatNote( strings, Applied( CheatKind.Ticket, "Awarded" ) ) );
		Assert.AreEqual( "Cheat: golden ticket", ParkHud.CheatNote( strings, Applied( CheatKind.Ticket, "Moved" ) ) );
		Assert.IsNull( ParkHud.CheatNote( strings, Applied( CheatKind.Ticket, "None" ) ), "a ticket the park held: nothing to say" );
		Assert.AreEqual( "Valsspelen: gouden kaartje", ParkHud.CheatNote( new UiStringTable( "Dutch", new string[1], new[] { "" }, new string[1], new string[1] ), Applied( CheatKind.Ticket, "Awarded" ) ) );
	}

	// ---- The CHTS chunk of an original save --------------------------------------------------------------

	[TestMethod]
	public void AnOriginalSavesCheatModeMarksTheImportedParkAndTheExportWritesTheParksOwnChunk()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		var easymode = Path.Combine( dataPath, "levels", "jungle", "Easymode.TPWI" );
		if ( !File.Exists( easymode ) )
			Assert.Inconclusive( "The original jungle Easymode.TPWI is missing." );
		var previous = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			using var reader = new SaveReader( easymode );
			var payload = reader.ReadFile();
			var template = OriginalSavePayload.Read( payload );
			CollectionAssert.AreEqual( new byte[] { 1, 1 }, template.Cheat, "the shipped seed was saved with the cheat mode on" );
			var map = new MapFile( new MemoryStream( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ) );
			ParkEconomy Import( byte[] cheat, out OriginalEconomyImport import, bool shippedSeed = false, int researchedByCheat = 0 )
			{
				var copy = OriginalSavePayload.Read( template.Write() );
				copy.Cheat = cheat;
				// the research cheat's footprint: the object control's researched byte +0x10 set while the project is unfinished (0x503FF0)
				for ( var index = 0; researchedByCheat != 0 && index < copy.World.Controls.Count; index++ )
					if ( BitConverter.ToInt32( copy.World.Controls.Records, index * SaveObjectControl.Bytes ) == researchedByCheat )
						copy.World.Controls.Records[index * SaveObjectControl.Bytes + 0x10] = 1;
				var economy = new ParkEconomy( BalanceSettings.Load( "jungle", true ), EconomyObjectCatalog.Load( "jungle", true ), ParkGameMode.FullSimulation, 1 );
				import = OriginalEconomyImport.Apply( economy, OriginalParkImport.Import( copy.Write(), map ), shippedSeed );
				return economy;
			}
			// the shipped seed starts a new park: its bytes are not applied, so the player's park is not cheated
			var seeded = Import( new byte[] { 1, 1 }, out var seededImport, shippedSeed: true );
			Assert.IsFalse( seeded.Cheats.Mode );
			Assert.IsNull( ParkSaveFile.Capture( seeded ).Cheats );
			StringAssert.Contains( string.Join( "\n", seededImport.Evidence ), "Cheat bytes 0101 of the shipped seed not applied" );
			var clean = Import( new byte[] { 0, 0 }, out var cleanImport );
			Assert.IsFalse( clean.Cheats.Mode );
			Assert.IsNull( ParkSaveFile.Capture( clean ).Cheats );
			StringAssert.Contains( string.Join( "\n", cleanImport.Evidence ), "Cheat mode off (CHTS byte 0 = 0)" );
			var cheated = Import( new byte[] { 1, 1 }, out var cheatedImport );
			Assert.IsTrue( cheated.Cheats.Mode );
			Assert.AreEqual( 0, cheated.Cheats.Log.Count, "loading sets the mode; no cheat was applied" );
			StringAssert.Contains( string.Join( "\n", cheatedImport.Evidence ), "Cheat mode on: the park is marked as cheated (CHTS byte 0 = 1); byte 1 = 1" );
			Assert.IsTrue( Import( new byte[] { 2, 0 }, out _ ).Cheats.Mode, "0x405560 tests the whole byte" );
			// the export writes the park's own chunk: 01 01 for a cheated park, 00 00 for a clean one, whatever the template holds
			var cheatedTemplate = OriginalSavePayload.Read( template.Write() );
			cheatedTemplate.Cheat = new byte[] { 1, 1 };
			CollectionAssert.AreEqual( new byte[] { 0, 0 }, OriginalParkExport.Export( cheatedTemplate, ParkSaveFile.Capture( clean ), out var evidence ).Cheat );
			StringAssert.Contains( string.Join( "\n", evidence ), "Cheat chunk: 0000 (no cheats)" );
			CollectionAssert.AreEqual( new byte[] { 1, 1 }, OriginalParkExport.Export( template, ParkSaveFile.Capture( cheated ), out _ ).Cheat );
			// Alt+R in the original's save: in a cheated park an object marked researched without its finished project is the research cheat's, and the export writes the mark back
			var item = clean.Research.Items.First( candidate => candidate.Level == 0 && !clean.Research.IsAvailable( candidate.InfoId ) ).InfoId;
			var researched = Import( new byte[] { 1, 1 }, out var researchedImport, researchedByCheat: item );
			Assert.IsTrue( researched.Research.IsAvailable( item ) && researched.Research.CheatAvailable.Contains( item ) );
			Assert.IsFalse( researched.Research.Completed.Contains( (item, 0) ), "nothing is researched" );
			StringAssert.Contains( string.Join( "\n", researchedImport.Evidence ), $"Research cheat: 1 objects the cheated save marks researched without a finished project are available to build ({item})" );
			var honestFlag = Import( new byte[] { 0, 0 }, out var honestImport, researchedByCheat: item );
			Assert.IsFalse( honestFlag.Research.IsAvailable( item ), "without the cheat mode the flag is a disagreement, as before" );
			StringAssert.Contains( string.Join( "\n", honestImport.Evidence ), $"{item} (saved researched)" );
			var exported = OriginalParkExport.Export( template, ParkSaveFile.Capture( researched ), out _, new OriginalParkExportOptions( Research: researched.Research ) );
			var record = Enumerable.Range( 0, (int)exported.World.Controls.Count ).Single( index => BitConverter.ToInt32( exported.World.Controls.Records, index * SaveObjectControl.Bytes ) == item );
			Assert.AreEqual( 1, exported.World.Controls.Records[record * SaveObjectControl.Bytes + 0x10], "the export marks the cheat's object researched" );
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
