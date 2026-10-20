using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// Official extensions switched on the Extensions and mods screen apply at the next park start or load without a restart (docs/MODS.md, "When a change applies"); community mods, their inputs, the online
/// extension, the command line and a mod's dependency still need the next start. Headless: the screen with a stand-in flow, and the flow's own choice handling, with no original game data.
/// </summary>
// [EXT:extensions-apply-at-load] user-approved 2026-10-19 ("Ook ontbrekende extensie aanzetten")
[TestClass]
[DoNotParallelize]
public class ExtensionsApplyAtLoadTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private string root = null!;
	private ModSet previousActive = null!;
	private IReadOnlyList<CatalogContentRoot> previousRoots = null!;
	private Logger previousLog = null!;
	private BaseFileSystem previousFiles = null!;
	private GameEdition previousEdition;

	[TestInitialize]
	public void Initialize()
	{
		previousEdition = GameEditions.Current;
		GameEditions.Select( GameEdition.ThemeParkWorld );
		root = Directory.CreateTempSubdirectory( "opentpw-apply-at-load-" ).FullName;
		previousActive = OpenTpwExtensions.Active;
		previousRoots = ObjectCatalog.ContentRoots;
		previousLog = Log; Log = new Logger();
		previousFiles = FileSystem;
		Directory.CreateDirectory( Path.Combine( root, "data" ) );
		FileSystem = new BaseFileSystem( Path.Combine( root, "data" ) );
		OpenTpwExtensions.Activate( ModSet.Defaults( OfficialExtensions.All ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		OpenTpwExtensions.Activate( previousActive );
		ObjectCatalog.SetContentRoots( previousRoots );
		Log = previousLog;
		FileSystem = previousFiles;
		GameEditions.Select( previousEdition );
		Directory.Delete( root, true );
	}

	/// <summary>Removes the test's mods.json (its folder may not exist yet: <see cref="File.Delete"/> throws for a missing folder on Windows).</summary>
	private void ForgetSettings()
	{
		var path = Path.Combine( root, "config", "mods.json" );
		if ( File.Exists( path ) )
			File.Delete( path );
	}

	private static string Text( OpenTpwText key ) => OriginalUiTests.FakeStrings().Extra( key );
	private static void Click( UiScreen screen, string id ) => ((UiButton)screen.Find( id )!).Clicked!();
	private static string Message( UiScreen screen ) => ((UiLabel)screen.Find( "caption" )!).Text() + "\n\n" + ((UiLabel)screen.Find( "message" )!).Text();
	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, Private )!.SetValue( target, value );

	private string CommunityMod()
	{
		var directory = Path.Combine( root, "mods-src", "community.textures" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			"""{ "schema": 1, "id": "community.textures", "name": "Textures", "version": "1.0.0", "category": "community" }""" );
		return directory;
	}

	private ModsServices Services( Func<IReadOnlyDictionary<string, bool>, ExtensionChoiceTiming>? apply, params string[] mods ) => new()
	{
		Discovered = () => ModDiscovery.Discover( mods, Array.Empty<string>(), null, null ),
		SettingsPath = Path.Combine( root, "config", "mods.json" ),
		Active = () => ModSet.Defaults( OfficialExtensions.All ),
		OpenTpwVersion = null,
		Edition = () => GameEdition.ThemeParkWorld,
		Browser = false,
		ApplyExtensions = apply,
	};

	private static void Pump( UiScreen screen )
	{
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) };
		for ( var attempt = 0; attempt < 200; attempt++ )
		{
			screen.Updating!( context );
			Thread.Sleep( 2 );
		}
	}

	/// <summary>Switches the extension at <paramref name="index"/> of the screen's extensions list and presses OK; returns the notice shown.</summary>
	private static string SwitchAndAccept( UiScreenStack stack, ModsServices services, IOpenTpwExtension extension, Action<UiScreen>? more = null )
	{
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		var list = (UiScrollList)screen.Find( "extensions" )!;
		list.Select( OfficialExtensions.All.ToList().IndexOf( extension ) );
		Pump( screen );
		Click( screen, "extensionsOn" );
		more?.Invoke( screen );
		Pump( screen );
		Click( screen, "ok" );
		Assert.AreEqual( "modsSaved", stack.Top!.Name );
		return Message( stack.Top );
	}

	// ---- The Extensions and mods screen ------------------------------------------------------------------------------

	[TestMethod]
	public void AnExtensionSwitchedOnTheScreenAppliesWithoutARestart()
	{
		var asked = new List<IReadOnlyDictionary<string, bool>>();
		foreach ( var (timing, notice) in new[] { (ExtensionChoiceTiming.NextPark, OpenTpwText.ModsSavedNextPark), (ExtensionChoiceTiming.AfterThisPark, OpenTpwText.ModsSavedAfterThisPark), (ExtensionChoiceTiming.NextStart, OpenTpwText.ModsSaved) } )
		{
			ForgetSettings();
			asked.Clear();
			var stack = new UiScreenStack();
			Assert.AreEqual( Text( notice ), SwitchAndAccept( stack, Services( changes => { asked.Add( changes ); return timing; } ), OfficialExtensions.RainMood ) );
			Assert.AreEqual( 1, asked.Count );
			CollectionAssert.AreEquivalent( new[] { new KeyValuePair<string, bool>( OfficialExtensions.RainMood.Id, true ) }, asked[0].ToArray(), "only what the player switched" );
			Assert.IsTrue( ModSettings.Load( Path.Combine( root, "config", "mods.json" ), new List<string>() ).IsEnabled( OfficialExtensions.RainMood.Id, false ), "mods.json is written first, as before" );
		}
	}

	[TestMethod]
	public void ACommunityModChangeKeepsTheRestartNotice()
	{
		var asked = 0;
		var stack = new UiScreenStack();
		var notice = SwitchAndAccept( stack, Services( _ => { asked++; return ExtensionChoiceTiming.NextPark; }, CommunityMod() ), OfficialExtensions.RainMood, screen =>
		{
			((UiScrollList)screen.Find( "mods" )!).Select( 0 );
			Click( screen, "modsOn" );
		} );
		Assert.AreEqual( Text( OpenTpwText.ModsSaved ), notice );
		Assert.AreEqual( 0, asked, "a community mod needs the next start, and so does everything switched with it" );
		// without the flow (tests, the front-end smoke) every change keeps the restart notice
		ForgetSettings();
		Assert.AreEqual( Text( OpenTpwText.ModsSaved ), SwitchAndAccept( new UiScreenStack(), Services( null ), OfficialExtensions.RainMood ) );
	}

	[TestMethod]
	public void TheNoticesExistInEveryInterfaceLanguage()
	{
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			foreach ( var key in new[] { OpenTpwText.ModsSavedNextPark, OpenTpwText.ModsSavedAfterThisPark } )
			{
				var text = SupplementaryStrings.Get( key, language );
				Assert.IsTrue( text.Contains( "\n\n", StringComparison.Ordinal ), $"{key} in {language} has a caption line" );
				if ( language != "English" )
					Assert.AreNotEqual( SupplementaryStrings.Get( key, "English" ), text );
			}
	}

	// ---- The flow ------------------------------------------------------------------------------------------------------

	private static GameFlow Flow( Level? level = null )
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) );
		Set( flow, "<Strings>k__BackingField", OriginalUiTests.FakeStrings() );
		Set( flow, "<Level>k__BackingField", level );
		flow.ProgressCaptureOverride = () => false;
		return flow;
	}

	private static void ApplyPending( GameFlow flow ) => typeof( GameFlow ).GetMethod( "ApplyPendingExtensions", Private )!.Invoke( flow, null );

	private static bool Fails( Action load )
	{
		try { load(); return false; }
		catch ( Exception error ) when ( error is IOException or InvalidDataException or ArgumentException or InvalidOperationException ) { return true; }
	}

	[TestMethod]
	public void InTheLobbyAChoiceIsActiveAtOnce()
	{
		var flow = Flow();
		Assert.AreEqual( ExtensionChoiceTiming.NextPark, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.RainMood.Id] = true, [OfficialExtensions.Evening.Id] = false } ) );
		Assert.IsTrue( FeatureGates.RainMood );
		Assert.IsFalse( FeatureGates.Evening );
		Assert.AreSame( OpenTpwExtensions.Active, flow.NextExtensions );
	}

	[TestMethod]
	public void InAParkAChoiceWaitsUntilTheParkIsLeftOrReplaced()
	{
		var flow = Flow( (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );
		var before = OpenTpwExtensions.Active;
		Assert.AreEqual( ExtensionChoiceTiming.AfterThisPark, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.WinterCold.Id] = true } ) );
		Assert.AreSame( before, OpenTpwExtensions.Active, "the running park keeps its set" );
		Assert.IsFalse( FeatureGates.WinterCold );
		Assert.IsTrue( flow.NextExtensions.IsEnabled( OfficialExtensions.WinterCold.Id ), "the next park uses the choice" );
		// a second choice adds to the first
		flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = false } );
		Assert.IsTrue( flow.NextExtensions.IsEnabled( OfficialExtensions.WinterCold.Id ) && !flow.NextExtensions.IsEnabled( OfficialExtensions.Pools.Id ) );
		ApplyPending( flow );
		Assert.IsTrue( FeatureGates.WinterCold );
		Assert.IsFalse( FeatureGates.Pools );
		Assert.AreSame( OpenTpwExtensions.Active, flow.NextExtensions );
	}

	[TestMethod]
	public void ALoadAppliesTheWaitingChoiceAndAFailedLoadKeepsItWaiting()
	{
		var flow = Flow( (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );
		var before = OpenTpwExtensions.Active;
		flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.RainMood.Id] = true } );
		var switched = 0;
		void Count( ModSet _ ) => switched++;
		ModStartup.ExtensionsSwitched += Count;
		try
		{
			var snapshot = ParkResumeSaveFileTests.Envelope( 300 );
			// no game data here: the load fails after the choice was applied, which is undone
			Assert.IsTrue( Fails( () => flow.ResumePark( snapshot ) ) );
			Assert.AreEqual( 2, switched, "applied for the load, then switched back" );
			Assert.AreSame( before, OpenTpwExtensions.Active );
			Assert.IsTrue( flow.NextExtensions.IsEnabled( OfficialExtensions.RainMood.Id ), "the choice still waits" );
			// Restart Park reloads the same park with its own set
			Assert.IsTrue( Fails( () => flow.ResumePark( snapshot, restart: true ) ) );
			Assert.AreEqual( 2, switched, "a restart does not apply the waiting choice" );
		}
		finally { ModStartup.ExtensionsSwitched -= Count; }
	}

	[TestMethod]
	public void TheLoadDialogPlansAgainstTheExtensionsTheNextParkUses()
	{
		var flow = Flow( (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );
		var previousSaves = SaveFileSystem;
		try
		{
			SaveFileSystem = new BaseFileSystem( root );
			var path = SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" );
			ParkResumeSaveFile.Save( path, ParkResumeSaveFileTests.Envelope( 300 ) with { Mods = new[] { new ParkModRecord( OfficialExtensions.RainMood.Id, "1.0.0", ModSet.NoFilesSha256 ) } } );
			flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.RainMood.Id] = true } );
			var asked = new List<ParkExtensionSwitch?>();
			flow.ConfirmExtensions( path, asked.Add, new UiScreenStack() );
			CollectionAssert.AreEqual( new ParkExtensionSwitch?[] { null }, asked.ToArray(), "with the waiting choice the save agrees: no question" );
		}
		finally { SaveFileSystem = previousSaves; }
	}

	[TestMethod]
	public void AChoiceTakenBackInTheSameParkLeavesNothingWaiting()
	{
		var flow = Flow( (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );
		flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.RainMood.Id] = true } );
		Assert.AreNotSame( OpenTpwExtensions.Active, flow.NextExtensions );
		Assert.AreEqual( ExtensionChoiceTiming.AfterThisPark, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.RainMood.Id] = false } ) );
		Assert.AreSame( OpenTpwExtensions.Active, flow.NextExtensions, "the running set is what the next park gets: nothing is applied when the park is left" );
	}

	[TestMethod]
	public void AnotherParkStartedFromInsideAParkIsBuiltInOneGoWithTheWaitingChoiceAndAFailureKeepsItWaiting()
	{
		var flow = Flow( (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) );
		var before = OpenTpwExtensions.Active;
		flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.WinterCold.Id] = true } );
		var switched = new List<bool>();
		void Seen( ModSet set ) => switched.Add( set.IsEnabled( OfficialExtensions.WinterCold.Id ) );
		ModStartup.ExtensionsSwitched += Seen;
		try
		{
			var start = typeof( GameFlow ).GetMethods( Private ).Single( method => method.Name == "StartLevel" && method.GetParameters().Any( parameter => parameter.Name == "beforeEnter" ) );
			// over frames was asked for, but with a choice waiting while a park runs the park is built in one go: the choice is applied right before the build, and the build fails here (no game data)
			var error = Assert.ThrowsException<TargetInvocationException>( () => start.Invoke( flow, new object?[] { "jungle", true, false, true, null, ParkStartKind.FullSimulation, null, (Action<Level>)(_ => { }) } ) );
			Assert.IsTrue( error.InnerException is IOException or InvalidDataException or ArgumentException or InvalidOperationException, error.InnerException?.ToString() );
			CollectionAssert.AreEqual( new[] { true, false }, switched.ToArray(), "applied for the build, then switched back while the old park is still live" );
			Assert.AreSame( before, OpenTpwExtensions.Active );
			Assert.IsTrue( flow.NextExtensions.IsEnabled( OfficialExtensions.WinterCold.Id ), "the choice waits again" );
		}
		finally { ModStartup.ExtensionsSwitched -= Seen; }
	}

	[TestMethod]
	public void RevivingARefusedModOrChangingACommandLineChoiceNeedsTheNextStart()
	{
		var flow = Flow();
		var directory = Path.Combine( root, "mods-src", "community.needs-cross" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			"""{ "schema": 1, "id": "community.needs-cross", "name": "Needs cross", "version": "1.0.0", "category": "community", "dependencies": [{ "id": "opentpw.cross-edition-objects" }] }""" );
		var discovered = ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null );
		var refused = ModSet.Resolve( discovered, new ModSettings { Enabled = new Dictionary<string, bool> { ["community.needs-cross"] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		Assert.IsTrue( refused.Refused.Any( refusal => refusal.Id == "community.needs-cross" && refusal.Kind == ModRefusalKind.Dependency ) );
		var discoveredProperty = typeof( ModStartup ).GetProperty( "Discovered", BindingFlags.Static | BindingFlags.Public )!;
		var enabledProperty = typeof( ModStartup ).GetProperty( "EnabledForRun", BindingFlags.Static | BindingFlags.NonPublic )!;
		var previousDiscovered = discoveredProperty.GetValue( null );
		var previousEnabled = enabledProperty.GetValue( null );
		try
		{
			discoveredProperty.SetValue( null, discovered );
			OpenTpwExtensions.Activate( refused );
			Assert.AreEqual( ExtensionChoiceTiming.NextStart, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.CrossEditionObjects.Id] = true } ),
				"the mod would stay refused until a start: no 'no restart' notice" );
			Assert.AreSame( refused, OpenTpwExtensions.Active );
			Assert.AreEqual( ExtensionChoiceTiming.NextPark, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.RainMood.Id] = true } ), "another extension still applies at once" );
			enabledProperty.SetValue( null, new[] { OfficialExtensions.Evening.Id } );
			Assert.AreEqual( ExtensionChoiceTiming.NextStart, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.Evening.Id] = false } ), "--enable-mod wins for the run" );
		}
		finally
		{
			discoveredProperty.SetValue( null, previousDiscovered );
			enabledProperty.SetValue( null, previousEnabled );
		}
	}

	[TestMethod]
	public void TheOnlineExtensionTheCommandLineAndAModsDependencyNeedTheNextStart()
	{
		var flow = Flow();
		var before = OpenTpwExtensions.Active;
		Assert.AreEqual( ExtensionChoiceTiming.NextStart, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.Online.Id] = false } ) );
		Assert.AreEqual( ExtensionChoiceTiming.NextStart, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.Online.Id] = false, [OfficialExtensions.RainMood.Id] = true } ),
			"a change that includes one applies wholly at the next start" );
		Assert.AreSame( before, OpenTpwExtensions.Active );
		var field = typeof( ModStartup ).GetProperty( "DisabledForRun", BindingFlags.Static | BindingFlags.NonPublic )!;
		var previous = field.GetValue( null );
		try
		{
			field.SetValue( null, new[] { OfficialExtensions.Pools.Id } );
			Assert.AreEqual( ExtensionChoiceTiming.NextStart, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = true } ) );
		}
		finally { field.SetValue( null, previous ); }
		Assert.AreSame( before, OpenTpwExtensions.Active );
		// a community mod that needs an extension keeps it on until a new start
		var directory = Path.Combine( root, "mods-src", "community.needs-pools" );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			"""{ "schema": 1, "id": "community.needs-pools", "name": "Needs pools", "version": "1.0.0", "category": "community", "dependencies": [{ "id": "opentpw.pools", "version": ">=1.0.0" }] }""" );
		var withMod = ModSet.Resolve( ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null ), new ModSettings { Enabled = new Dictionary<string, bool> { ["community.needs-pools"] = true } },
			null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		Assert.IsTrue( withMod.IsEnabled( "community.needs-pools" ), string.Join( "; ", withMod.Refused.Select( refusal => refusal.Reason ) ) );
		OpenTpwExtensions.Activate( withMod );
		Assert.AreEqual( ExtensionChoiceTiming.NextStart, flow.ApplyExtensionChoices( new Dictionary<string, bool> { [OfficialExtensions.Pools.Id] = false } ) );
		Assert.IsTrue( FeatureGates.Pools );
	}
}
