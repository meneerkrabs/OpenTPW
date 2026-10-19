using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The Extensions and mods screen and its status rows (docs/MODS.md, [EXT:mods-ui]).</summary>
[TestClass]
public class ModsScreenTests
{
	private string root = null!;

	[TestInitialize]
	public void Initialize() => root = Directory.CreateTempSubdirectory( "opentpw-modsui-" ).FullName;

	[TestCleanup]
	public void Cleanup() => Directory.Delete( root, true );

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
	{
		Canvas = new UiCanvas( 1024, 768 )
	};

	private string Mod( string id, string extra = "", params (string Path, string Text)[] files )
	{
		var directory = Path.Combine( root, "mods-src", id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			$$"""{ "schema": 1, "id": "{{id}}", "name": "Mod {{id}}", "version": "1.2.0", "category": "community", "author": "Ann", "description": "Does things."{{extra}} }""" );
		foreach ( var (path, text) in files )
		{
			var full = Path.Combine( directory, path );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			File.WriteAllText( full, text );
		}
		return directory;
	}

	private const string TpiInput = """, "inputs": [{ "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" }]""";

	private string TpiFolder( bool inc = true )
	{
		var folder = Path.Combine( root, inc ? "tpi" : "tpw" );
		Directory.CreateDirectory( Path.Combine( folder, "Data", "levels", inc ? "arabian" : "jungle" ) );
		if ( inc )
			File.WriteAllText( Path.Combine( folder, "Data", "Ver.txt" ), "1" );
		return folder;
	}

	private static IReadOnlyList<DiscoveredMod> Found( params string[] directories ) =>
		ModDiscovery.Discover( directories, Array.Empty<string>(), null, null );

	private ModsServices Services( params string[] directories ) => new()
	{
		Discovered = () => Found( directories ),
		SettingsPath = Path.Combine( root, "config", "mods.json" ),
		Active = () => ModSet.Defaults( OfficialExtensions.All ),
		OpenTpwVersion = null,
		Edition = () => GameEdition.ThemeParkWorld,
		Inspect = ( path, _ ) => Task.FromResult( new InstallationDiscoveryResult( new[] { GameInstallation.Inspect( path ) }, false, null ) ),
	};

	private static void Pump( UiScreen screen, Func<bool>? done = null )
	{
		for ( var attempt = 0; attempt < 400; attempt++ )
		{
			screen.Updating!( Context() );
			if ( done == null || done() )
				return;
			Thread.Sleep( 5 );
		}
		Assert.Fail( "The screen did not settle." );
	}

	private static string Trailing( UiScreen screen, string list, int index ) => ((UiScrollList)screen.Find( list )!).Trailing!( index );
	private static string Label( UiScreen screen, string id ) => ((UiLabel)screen.Find( id )!).Text();
	private static void Click( UiScreen screen, string id ) => ((UiButton)screen.Find( id )!).Clicked!();
	private static string Text( OpenTpwText key ) => OriginalUiTests.FakeStrings().Extra( key );

	// ---- Rows ------------------------------------------------------------------------------------

	[TestMethod]
	public void RowsShowTheStatusTheStartupWouldGive()
	{
		var plain = Mod( "community.plain" );
		var needsInput = Mod( "community.needs-input", TpiInput );
		var wrongEdition = Mod( "community.wrong-edition", """, "edition": "theme-park-inc" """ );
		var versionBound = Mod( "community.version", """, "openTpw": ">=9.0.0" """ );
		var needsOther = Mod( "community.needs-other", """, "dependencies": [{ "id": "community.absent" }]""" );
		var first = Mod( "community.first", "", ("data/a.txt", "x") ).Replace( "community.first", "community.first" );
		File.WriteAllText( Path.Combine( first, ModManifest.FileName ), """{ "schema": 1, "id": "community.first", "name": "First", "version": "1.0.0", "category": "community", "content": { "overrides": "data" } }""" );
		var second = Mod( "community.second", """, "content": { "overrides": "data" }""", ("data/a.txt", "y") );
		var broken = Path.Combine( root, "mods-src", "community.broken" );
		Directory.CreateDirectory( broken );
		File.WriteAllText( Path.Combine( broken, ModManifest.FileName ), "{ not json" );

		var discovered = Found( plain, needsInput, wrongEdition, versionBound, needsOther, first, second, broken );
		var settings = new ModSettings { Enabled = new Dictionary<string, bool>
		{
			["community.plain"] = true, ["community.needs-input"] = true, ["community.wrong-edition"] = true, ["community.version"] = true,
			["community.needs-other"] = true, ["community.first"] = true, ["community.second"] = true, ["opentpw.online"] = false,
		} };
		var set = ModSet.Resolve( discovered, settings, new ModVersion( 1, 0, 0 ), GameEdition.ThemeParkWorld, OfficialExtensions.All );
		var rows = ModRows.Build( discovered, settings, set, OfficialExtensions.All ).ToDictionary( row => row.Id + (row.Manifest == null && !row.IsExtension ? "!" : "") );

		Assert.AreEqual( ModRowStatus.Off, rows["opentpw.online"].Status );
		Assert.IsTrue( rows["opentpw.online"].IsExtension );
		Assert.AreEqual( ModRowStatus.On, rows["community.plain"].Status );
		Assert.AreEqual( 64, rows["community.plain"].Digest!.Length );
		Assert.AreEqual( ModRowStatus.MissingInput, rows["community.needs-input"].Status );
		Assert.AreEqual( ModRowStatus.Incompatible, rows["community.wrong-edition"].Status );
		Assert.AreEqual( ModRowStatus.Incompatible, rows["community.version"].Status );
		Assert.AreEqual( ModRowStatus.NeedsOther, rows["community.needs-other"].Status );
		Assert.AreEqual( ModRowStatus.Conflict, rows["community.first"].Status );
		Assert.AreEqual( ModRowStatus.Conflict, rows["community.second"].Status );
		StringAssert.Contains( rows["community.second"].Reason!, "a.txt" );
		Assert.IsNull( rows["community.first"].Digest );
		var invalid = rows.Values.Single( row => row.Status == ModRowStatus.Unusable );
		Assert.IsFalse( invalid.CanToggle );
		Assert.AreEqual( "community.broken", invalid.Name );
	}

	[TestMethod]
	public void RefusalsOfTheContentRootsShowAsTheirOwnStatus()
	{
		static string Objects( int first, int last ) =>
			$$"""{ "schema": 1, "idRange": { "first": {{first}}, "last": {{last}} }, "objects": [] }""";
		const string Content = ", \"content\": { \"objects\": \"objects.json\" }";
		var one = Mod( "community.one", Content, ("objects.json", Objects( 20000, 20009 )) );
		var two = Mod( "community.two", Content, ("objects.json", Objects( 20005, 20015 )) );
		var bad = Mod( "community.bad", Content, ("objects.json", "{ not json") );
		var discovered = Found( one, two, bad );
		var settings = new ModSettings { Enabled = new Dictionary<string, bool> { ["community.one"] = true, ["community.two"] = true, ["community.bad"] = true } };
		var set = ModSet.Resolve( discovered, settings, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		var rows = ModRows.Build( discovered, settings, set, OfficialExtensions.All ).Where( row => !row.IsExtension ).ToDictionary( row => row.Id );
		Assert.AreEqual( ModRowStatus.Conflict, rows["community.one"].Status );
		Assert.AreEqual( ModRowStatus.Conflict, rows["community.two"].Status );
		Assert.AreEqual( ModRowStatus.Unusable, rows["community.bad"].Status );
		Assert.IsNotNull( rows["community.bad"].Reason );

		// Theme Park Inc does not use Theme Park World objects: incompatible, like a manifest for the other edition.
		var inc = ModSet.Resolve( Found( one ), new ModSettings { Enabled = new Dictionary<string, bool> { ["community.one"] = true } }, null, GameEdition.ThemeParkInc, OfficialExtensions.All );
		Assert.AreEqual( ModRefusalKind.Incompatible, inc.Refused.Single().Kind );
	}

	[TestMethod]
	public void AResolutionOfAnOlderChoiceNeverShowsForTheNewerOne()
	{
		var plain = Mod( "community.plain", "", ("data/a.txt", "x") );
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), Services( plain ) );
		stack.Push( screen );
		var mods = (UiScrollList)screen.Find( "mods" )!;
		mods.Select( 0 );
		Click( screen, "modsOn" );
		screen.Updating!( Context() );   // the resolution of "on" starts
		Click( screen, "modsOn" );       // the player switches it off again before it lands
		Pump( screen, () => Trailing( screen, "mods", 0 ) == Text( OpenTpwText.ModOff ) );
		for ( var i = 0; i < 20; i++ )
		{
			Thread.Sleep( 5 );
			screen.Updating!( Context() );
			Assert.AreEqual( Text( OpenTpwText.ModOff ), Trailing( screen, "mods", 0 ), "a stale result must not flip the row" );
		}
		Click( screen, "modsOn" );
		Pump( screen, () => Trailing( screen, "mods", 0 ) == Text( OpenTpwText.ModOn ) );
		stack.Clear();
	}

	[TestMethod]
	public void AChoiceNotYetResolvedIsPendingNotWrong()
	{
		var plain = Mod( "community.plain" );
		var discovered = Found( plain );
		var set = ModSet.Defaults( OfficialExtensions.All );
		var settings = new ModSettings { Enabled = new Dictionary<string, bool> { ["community.plain"] = true } };
		var row = ModRows.Build( discovered, settings, set, OfficialExtensions.All, current: false ).Single( candidate => !candidate.IsExtension );
		Assert.AreEqual( ModRowStatus.Pending, row.Status );
		Assert.IsTrue( row.Enabled );
	}

	[TestMethod]
	public void InputFoldersAreCheckedForTheEditionTheyAreAskedFor()
	{
		var input = new ModInput( "tpi", "original-data", "theme-park-inc" );
		var tpi = ModInputCheck.Check( TpiFolder(), input );
		Assert.AreEqual( ModInputState.Valid, tpi.State );
		Assert.AreEqual( GameEdition.ThemeParkInc, tpi.Detected );
		Assert.AreEqual( GameEditionEvidence.Structure, tpi.Evidence );

		var tpw = ModInputCheck.Check( TpiFolder( false ), input );
		Assert.AreEqual( ModInputState.WrongEdition, tpw.State );
		Assert.AreEqual( GameEdition.ThemeParkWorld, tpw.Detected );

		Assert.AreEqual( ModInputState.NotFound, ModInputCheck.Check( Path.Combine( root, "empty" ), input ).State );
		var anyEdition = new ModInput( "data", "original-data", null );
		Assert.AreEqual( ModInputState.Valid, ModInputCheck.Check( TpiFolder( false ), anyEdition ).State );
		Assert.AreEqual( ModInputState.Valid, ModInputCheck.Check( TpiFolder( false ), new ModInput( "tpw", "original-data", "theme-park-world" ) ).State );
	}

	[TestMethod]
	public void SettingsKeepInputsPerModAndCanClearThem()
	{
		var settings = ModSettings.Default.WithInput( "community.a", "tpi", "/one" ).WithInput( "community.a", "other", "/two" ).WithInput( "community.b", "tpi", "/three" );
		Assert.AreEqual( "/one", settings.InputPath( "community.a", "tpi" ) );
		Assert.AreEqual( "/three", settings.InputPath( "community.b", "tpi" ) );
		var cleared = settings.WithInput( "community.a", "tpi", null ).WithInput( "community.b", "tpi", null );
		Assert.IsNull( cleared.InputPath( "community.a", "tpi" ) );
		Assert.AreEqual( "/two", cleared.InputPath( "community.a", "other" ) );
		Assert.IsFalse( cleared.Inputs.ContainsKey( "community.b" ) );
		Assert.AreEqual( "/one", settings.InputPath( "community.a", "tpi" ), "The original settings are not changed." );
		var round = ModSettings.Parse( settings.Serialize(), new List<string>() );
		Assert.AreEqual( "/two", round.InputPath( "community.a", "other" ) );
	}

	// ---- Screen ----------------------------------------------------------------------------------

	[TestMethod]
	public void BothGuestExtensionsAreOfferedToPlayers()
	{
		Assert.IsTrue( OfficialExtensions.SmarterGuests.PlayerSelectable && !OfficialExtensions.SmarterGuests.DefaultEnabled, "released as opt-in in 1.6.0: a measured effect and the CPU gate met (docs/SMARTER-GUESTS.md, Cost and release)" );
		Assert.IsTrue( OfficialExtensions.GuestInsights.PlayerSelectable, "it only reads the park, so no save is locked" );
		Assert.IsTrue( OfficialExtensions.Online.PlayerSelectable && OfficialExtensions.CrossEditionObjects.PlayerSelectable );
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), Services() );
		stack.Push( screen );
		var rows = ((UiScrollList)screen.Find( "extensions" )!).Rows();
		CollectionAssert.AreEqual( new[] { "OpenTPW online  1.0.0", "Cross-edition objects  1.0.0", Text( OpenTpwText.ExtensionSmarterGuestsName ) + "  1.6.0", Text( OpenTpwText.ExtensionGuestInsightsName ) + "  1.0.0", "Pools  1.0.0", Text( OpenTpwText.ExtensionPoolGuestArtName ) + "  1.0.0", Text( OpenTpwText.ExtensionUmbrellasName ) + "  1.0.0", Text( OpenTpwText.ExtensionRainMoodName ) + "  1.0.0", Text( OpenTpwText.ExtensionWinterName ) + "  1.0.0", Text( OpenTpwText.ExtensionWinterWeatherName ) + "  1.0.0", Text( OpenTpwText.ExtensionWinterColdName ) + "  1.0.0", Text( OpenTpwText.ExtensionWinterAlwaysName ) + "  1.0.0", Text( OpenTpwText.ExtensionRestoredAdviceName ) + "  1.0.0", Text( OpenTpwText.ExtensionEveningName ) + "  1.1.0", Text( OpenTpwText.ExtensionWinterSnowName ) + "  1.0.0", Text( OpenTpwText.ExtensionCheatsName ) + "  1.0.0" }, rows.ToArray() );
		stack.Clear();
	}

	[TestMethod]
	public void TheModsScreenListsGuestInsightsWithItsDescriptionAndNoSimulationFlag()
	{
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), Services() );
		stack.Push( screen );
		var extensions = (UiScrollList)screen.Find( "extensions" )!;
		var index = Array.FindIndex( extensions.Rows().ToArray(), row => row.StartsWith( Text( OpenTpwText.ExtensionGuestInsightsName ), StringComparison.Ordinal ) );
		Assert.IsTrue( index >= 0, "the mods screen lists opentpw.guest-insights" );
		extensions.Select( index );
		Pump( screen );
		var details = Label( screen, "details" );
		StringAssert.Contains( details, "opentpw.guest-insights" );
		StringAssert.Contains( details, Text( OpenTpwText.ExtensionGuestInsightsDescription ) );
		StringAssert.Contains( details, Text( OpenTpwText.ModLeavesSimulation ) );
		StringAssert.Contains( Text( OpenTpwText.ExtensionGuestInsightsDescription ), "park information window" );
		stack.Clear();
	}

	[TestMethod]
	public void ASelectableGuestExtensionShowsItsTranslatedNameAndDescription()
	{
		// the screen is ready for the day an extension becomes selectable: the same registry entry, switched to selectable
		var smarter = new OpenTpwExtension( "opentpw.smarter-guests", "Smarter guests", new ModVersion( 1, 0, 0 ), false, true, PlayerSelectable: true );
		var services = new ModsServices
		{
			Extensions = new IOpenTpwExtension[] { OfficialExtensions.Online, smarter },
			Discovered = () => Found(),
			SettingsPath = Path.Combine( root, "config", "mods.json" ),
			Active = () => ModSet.Defaults( new IOpenTpwExtension[] { OfficialExtensions.Online, smarter } ),
			OpenTpwVersion = null,
			Edition = () => GameEdition.ThemeParkWorld,
		};
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		var extensions = (UiScrollList)screen.Find( "extensions" )!;
		Assert.AreEqual( Text( OpenTpwText.ExtensionSmarterGuestsName ) + "  1.0.0", extensions.Rows()[1] );
		extensions.Select( 1 );
		Pump( screen );
		var details = Label( screen, "details" );
		StringAssert.Contains( details, "opentpw.smarter-guests" );
		StringAssert.Contains( details, Text( OpenTpwText.ExtensionSmarterGuestsDescription ) );
		StringAssert.Contains( details, Text( OpenTpwText.ModAffectsSimulation ) );
		stack.Clear();
	}

	[TestMethod]
	public void SettingsAndTheCommandLineSwitchOnTheReleasedSmarterExtension()
	{
		var diagnostics = new List<string>();
		var settings = ModStartup.WithCommandLine( ModSettings.Default.WithEnabled( "opentpw.guest-insights", true ), Array.Empty<DiscoveredMod>(), new[] { "opentpw.smarter-guests" }, Array.Empty<string>() );
		Assert.IsTrue( settings.IsEnabled( "opentpw.smarter-guests", false ), "the request itself is a valid id" );
		var set = ModStartup.Resolve( Array.Empty<DiscoveredMod>(), settings, Array.Empty<string>(), diagnostics );
		Assert.IsTrue( set.IsEnabled( "opentpw.smarter-guests" ), "released as opt-in in 1.6.0, so --enable-mod and mods.json switch it on" );
		Assert.IsTrue( set.IsEnabled( "opentpw.guest-insights" ) );
		Assert.IsFalse( diagnostics.Any( line => line.Contains( "ignored" ) ), string.Join( "; ", diagnostics ) );
		Assert.IsFalse( FeatureGates.SmarterGuests, "resolving a set does not switch the running game's gate" );
		// tests and development code still switch them on directly
		using var scope = OpenTpwExtensions.Use( SmarterGuestsTests.On() );
		Assert.IsTrue( FeatureGates.SmarterGuests );
	}

	[TestMethod]
	public void TheGuestExtensionStringsExistInAllNineLanguages()
	{
		foreach ( var key in new[] { OpenTpwText.ExtensionSmarterGuestsName, OpenTpwText.ExtensionSmarterGuestsDescription, OpenTpwText.ExtensionGuestInsightsName, OpenTpwText.ExtensionGuestInsightsDescription,
			OpenTpwText.ExtensionRestoredAdviceName, OpenTpwText.ExtensionRestoredAdviceDescription, OpenTpwText.RefusalsTitle, OpenTpwText.RefusalTooExpensive, OpenTpwText.RefusalQueueTooLong, OpenTpwText.RefusalTooScary, OpenTpwText.RefusalTooBoring, OpenTpwText.RefusalRideUnavailable } )
		{
			var texts = SupplementaryStrings.SupportedLanguages.Select( language => SupplementaryStrings.Get( key, language ) ).ToList();
			Assert.AreEqual( SupplementaryStrings.SupportedLanguages.Count, texts.Count, $"{key}: every translated language" );
			Assert.IsTrue( texts.All( text => !string.IsNullOrWhiteSpace( text ) ), $"{key} has a text in every language" );
			Assert.AreEqual( texts.Count, texts.Distinct().Count(), $"{key}: every language has its own text" );
		}
		// the mods screen's details label holds about 300 characters of description beside the name, version and flags
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			Assert.IsTrue( SupplementaryStrings.Get( OpenTpwText.ExtensionSmarterGuestsDescription, language ).Length <= 300, $"{language}: the smarter guests description fits the details label" );
	}

	[TestMethod]
	public void SwitchingAnExtensionOffSavesModsJsonAndAsksForARestart()
	{
		var services = Services();
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		var extensions = (UiScrollList)screen.Find( "extensions" )!;
		Assert.AreEqual( "OpenTPW online  1.0.0", extensions.Rows()[0] );
		Assert.AreEqual( Text( OpenTpwText.ModOn ), Trailing( screen, "extensions", 0 ) );
		Assert.AreEqual( Text( OpenTpwText.ModNoneInstalled ), ((UiScrollList)screen.Find( "mods" )!).Rows().Single() );
		Assert.AreEqual( Text( OpenTpwText.ModSelectHint ), Label( screen, "details" ) );

		extensions.Select( 0 );
		Pump( screen );
		StringAssert.Contains( Label( screen, "details" ), "opentpw.online" );
		StringAssert.Contains( Label( screen, "details" ), Text( OpenTpwText.ModBuiltIn ) );
		Click( screen, "extensionsOn" );
		Assert.AreEqual( Text( OpenTpwText.ModOff ), Trailing( screen, "extensions", 0 ) );
		Assert.IsFalse( File.Exists( services.SettingsPath ), "Nothing is written before OK." );

		Click( screen, "ok" );
		Assert.AreEqual( "modsSaved", stack.Top!.Name );
		Assert.AreEqual( Text( OpenTpwText.ModsSaved ), Label( stack.Top, "caption" ) + "\n\n" + Label( stack.Top, "message" ) );
		Assert.IsFalse( ModSettings.Load( services.SettingsPath, new List<string>() ).IsEnabled( "opentpw.online", true ) );
		Click( stack.Top, "choice0" );
		Assert.IsNull( stack.Top );
	}

	[TestMethod]
	public void ToggleBackOrCancelWritesNothingAndAsksNothing()
	{
		var services = Services();
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		((UiScrollList)screen.Find( "extensions" )!).Select( 0 );
		Click( screen, "extensionsOn" );
		Click( screen, "extensionsOn" );
		Click( screen, "ok" );
		Assert.IsNull( stack.Top, "The same choices close the screen without a notice." );
		Assert.IsFalse( File.Exists( services.SettingsPath ) );

		screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		((UiScrollList)screen.Find( "extensions" )!).Select( 0 );
		Click( screen, "extensionsOn" );
		Click( screen, "cancel" );
		Assert.IsNull( stack.Top );
		Assert.IsFalse( File.Exists( services.SettingsPath ) );
	}

	[TestMethod]
	public void ACommunityModNeedsItsInputFolderAndTheScreenChecksTheFolder()
	{
		var rides = Mod( "community.rides", TpiInput );
		var plain = Mod( "community.plain" );
		var services = Services( rides, plain );
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		var mods = (UiScrollList)screen.Find( "mods" )!;
		CollectionAssert.AreEqual( new[] { "Mod community.plain  1.2.0", "Mod community.rides  1.2.0" }, mods.Rows().ToArray() );

		mods.Select( 1 );
		Pump( screen );
		StringAssert.Contains( Label( screen, "details" ), "Ann" );
		StringAssert.Contains( Label( screen, "details" ), Text( OpenTpwText.ModDigestPending ) );
		Assert.IsFalse( screen.Find( "inputType" )!.Visible is false, "A mod with an input shows the folder chooser." );
		Assert.AreEqual( Text( OpenTpwText.ModInputNotChosen ), Label( screen, "inputStatus" ) );
		Assert.AreEqual( $"{Text( OpenTpwText.ModInputTpi )}: {Text( OpenTpwText.NoFolder )}", Label( screen, "inputPath" ) );

		Click( screen, "modsOn" );
		Pump( screen, () => Trailing( screen, "mods", 1 ) == Text( OpenTpwText.ModMissingInput ) );

		// A Theme Park World folder is refused and not saved.
		Click( screen, "inputType" );
		var field = (UiTextField)stack.Top!.Find( "path" )!;
		field.Text = TpiFolder( false );
		field.Submitted!();
		Pump( screen );
		StringAssert.Contains( Label( screen, "inputStatus" ), Text( OpenTpwText.EditionThemeParkWorld ) );
		Assert.AreEqual( Text( OpenTpwText.ModMissingInput ), Trailing( screen, "mods", 1 ) );

		// A Theme Park Inc folder is accepted and the mod turns on.
		var tpi = TpiFolder();
		Click( screen, "inputType" );
		field = (UiTextField)stack.Top!.Find( "path" )!;
		field.Text = tpi;
		field.Submitted!();
		Pump( screen, () => Trailing( screen, "mods", 1 ) == Text( OpenTpwText.ModOn ) );
		Assert.AreEqual( Text( OpenTpwText.ModInputFoundTpiStructure ), Label( screen, "inputStatus" ) );
		StringAssert.Contains( Label( screen, "inputPath" ), tpi );
		Assert.IsFalse( Label( screen, "details" ).Contains( Text( OpenTpwText.ModDigestPending ) ), "An accepted mod shows its digest." );

		Click( screen, "ok" );
		var saved = ModSettings.Load( services.SettingsPath, new List<string>() );
		Assert.IsTrue( saved.IsEnabled( "community.rides", false ) );
		Assert.AreEqual( Path.GetFullPath( tpi ), saved.InputPath( "community.rides", "tpi" ) );
		StringAssert.Contains( File.ReadAllText( services.SettingsPath ), "\"inputs\"" );
		stack.Clear();

		// Reopened: the saved folder is checked again when its mod is selected, and Remove clears it.
		screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		Pump( screen, () => Trailing( screen, "mods", 1 ) == Text( OpenTpwText.ModOn ) );
		((UiScrollList)screen.Find( "mods" )!).Select( 1 );
		Pump( screen, () => Label( screen, "inputStatus" ) == Text( OpenTpwText.ModInputFoundTpiStructure ) );
		Click( screen, "inputRemove" );
		Pump( screen, () => Trailing( screen, "mods", 1 ) == Text( OpenTpwText.ModMissingInput ) );
		Assert.AreEqual( Text( OpenTpwText.ModInputNotChosen ), Label( screen, "inputStatus" ) );
		stack.Clear();
	}

	[TestMethod]
	public void AModThatCannotBeReadCannotBeSwitchedOn()
	{
		var broken = Path.Combine( root, "mods-src", "community.broken" );
		Directory.CreateDirectory( broken );
		File.WriteAllText( Path.Combine( broken, ModManifest.FileName ), "{ not json" );
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), Services( broken ) );
		stack.Push( screen );
		var mods = (UiScrollList)screen.Find( "mods" )!;
		mods.Select( 0 );
		Pump( screen );
		Assert.AreEqual( Text( OpenTpwText.ModUnusable ), Trailing( screen, "mods", 0 ) );
		Assert.IsFalse( screen.Find( "modsOn" )!.Enabled );
		StringAssert.Contains( Label( screen, "details" ), Text( OpenTpwText.ModReasonLabel ) );
		stack.Clear();
	}

	[TestMethod]
	public void TheOptionsPageOffersTheScreenBesideGameFiles()
	{
		var source = File.ReadAllText( Path.Combine( RepositoryRoot(), "source", "OpenTPW", "UI", "Original", "Options", "OptionsScreen.cs" ) );
		// [EXT:extensions-apply-at-load] the screen gets the flow's way to apply extension choices without a restart
		Assert.IsTrue( source.Contains( "ModsScreen.Create( stack, strings, new ModsServices { ApplyExtensions = services.ApplyExtensions } )", StringComparison.Ordinal ), "the options page opens the screen with the flow's services" );
		StringAssert.Contains( source, "OpenTpwText.ModsPage" );
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo( AppContext.BaseDirectory );
		while ( directory != null && !File.Exists( Path.Combine( directory.FullName, "source", "OpenTPW", "OpenTPW.csproj" ) ) )
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException( "The repository root was not found." );
	}

	[TestMethod]
	public void EveryNewTextIsTranslatedIntoEveryShippedLanguage()
	{
		foreach ( var key in Enum.GetValues<OpenTpwText>().Where( key => key.ToString().StartsWith( "Mod" ) ) )
		{
			var english = SupplementaryStrings.Get( key, "English" );
			Assert.IsTrue( english.Length > 0, key.ToString() );
			foreach ( var language in SupplementaryStrings.SupportedLanguages )
				Assert.IsFalse( string.IsNullOrWhiteSpace( SupplementaryStrings.Get( key, language ) ), $"{key} in {language}" );
		}
		Assert.AreEqual( "Extensies en mods", SupplementaryStrings.Get( OpenTpwText.ModsPage, "Dutch" ) );
		foreach ( var language in SupplementaryStrings.SupportedLanguages.Where( language => language != "English" ) )
			Assert.AreNotEqual( SupplementaryStrings.Get( OpenTpwText.ModsPage, "English" ), SupplementaryStrings.Get( OpenTpwText.ModsPage, language ), language );
		// The wrong-edition message keeps both placeholders in every language.
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
		{
			var text = SupplementaryStrings.Get( OpenTpwText.ModInputWrongEdition, language );
			StringAssert.Contains( text, "{0}" );
			StringAssert.Contains( text, "{1}" );
		}
	}
}
