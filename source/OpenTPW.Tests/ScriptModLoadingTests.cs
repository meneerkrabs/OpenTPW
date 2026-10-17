using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;
using OpenTPW.Scripting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Phase 3 of the script mods (docs/SCRIPT-MODS.md): the manifest's scripts, the retained and compiled bytes, classification, safe mode and the shipped example.</summary>
[TestClass]
[DoNotParallelize]
public class ScriptModLoadingTests
{
	private const string Script = "function leaveQueue(guest, attraction, prev)\n  return prev\nend\n";
	private const string ScriptsJson = """, "scriptApi": ">=1.0.0 <2.0.0", "content": { "scripts": { "files": ["main.tps"], "hooks": { "leaveQueue": "leaveQueue" } } }""";

	private string root = null!;

	[TestInitialize]
	public void Initialize() => root = Directory.CreateTempSubdirectory( "opentpw-scriptmods-" ).FullName;

	[TestCleanup]
	public void Cleanup()
	{
		ScriptSafeMode.Enter( Array.Empty<string>(), Path.Combine( root, "reset" ), smokeTest: true );
		Directory.Delete( root, true );
	}

	private string Mod( string id, string extra = ScriptsJson, params (string Path, string Text)[] files )
	{
		var directory = Path.Combine( root, "mods", id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ),
			$$"""{ "schema": 1, "id": "{{id}}", "name": "Mod {{id}}", "version": "1.0.0", "category": "community"{{extra}} }""" );
		foreach ( var (path, text) in files.Length == 0 ? new[] { ("main.tps", Script) } : files )
		{
			var full = Path.Combine( directory, path );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			File.WriteAllText( full, text );
		}
		return directory;
	}

	private static ModSet Resolve( string directory, bool scriptsOff = false, ModVersion? version = null )
	{
		var found = ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null );
		var id = found.Single().Id;
		return ModSet.Resolve( found, new ModSettings { Enabled = new Dictionary<string, bool> { [id] = true } }, version, GameEdition.ThemeParkWorld,
			OfficialExtensions.All, null, scriptsOff );
	}

	private static void AssertInvalid( string extra, string expected )
	{
		var json = $$"""{ "schema": 1, "id": "community.x", "name": "X", "version": "1.0.0", "category": "community"{{extra}} }""";
		var exception = Assert.ThrowsException<InvalidDataException>( () => ModManifest.Parse( Encoding.UTF8.GetBytes( json ) ) );
		StringAssert.Contains( exception.Message, expected );
	}

	private static string Refusal( ModSet set ) => string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) );

	// ---- Manifest ---------------------------------------------------------------------------------

	[TestMethod]
	public void AManifestWithScriptsParsesFilesHooksAndTheApiRange()
	{
		var manifest = ModManifest.Parse( Encoding.UTF8.GetBytes( $$"""{ "schema": 1, "id": "community.x", "name": "X", "version": "1.0.0", "category": "community"{{ScriptsJson.Replace( "\"main.tps\"", "\"scripts\\\\main.tps\", \"b.tps\"" )}} }""" ) );
		CollectionAssert.AreEqual( new[] { "scripts/main.tps", "b.tps" }, manifest.Content.Scripts!.Files.ToArray() );
		Assert.AreEqual( "leaveQueue", manifest.Content.Scripts.Hooks["leaveQueue"] );
		Assert.IsTrue( manifest.ScriptApi!.Contains( ModVersion.Parse( ScriptLimits.ScriptApiVersion ) ) );
		Assert.IsNull( ModManifest.Parse( Encoding.UTF8.GetBytes( """{ "schema": 1, "id": "community.x", "name": "X", "version": "1.0.0", "category": "community" }""" ) ).Content.Scripts );
	}

	[TestMethod]
	public void OnlyTheKnownHooksAreAccepted()
	{
		foreach ( var hook in ScriptLimits.HookParameters.Keys )
			Assert.IsNotNull( ModManifest.Parse( Encoding.UTF8.GetBytes( $$"""{ "schema": 1, "id": "community.x", "name": "X", "version": "1.0.0", "category": "community", "scriptApi": "*", "content": { "scripts": { "files": ["a.tps"], "hooks": { "{{hook}}": "f" } } } }""" ) ).Content.Scripts );
		AssertInvalid( ScriptsJson.Replace( "leaveQueue\": \"", "onTick\": \"" ), "unknown script hook 'onTick'" );
		AssertInvalid( ScriptsJson.Replace( "\"leaveQueue\": \"leaveQueue\"", "\"leaveQueue\": \"not a name\"" ), "must name a script function" );
		AssertInvalid( ScriptsJson.Replace( "\"leaveQueue\": \"leaveQueue\"", "" ), "needs at least one hook" );
	}

	[TestMethod]
	public void ScriptPathsFollowTheContentPathRules()
	{
		foreach ( var bad in new[] { "../x.tps", "/x.tps", "C:/x.tps", "a/../../x.tps", "con.tps", "x.tps.", "" } )
			AssertInvalid( ScriptsJson.Replace( "\"main.tps\"", $"\"{bad}\"" ), "content.scripts.files entry" );
		AssertInvalid( ScriptsJson.Replace( "\"main.tps\"", "\"a.tps\", \"A.TPS\"" ), "listed more than once" );
		AssertInvalid( ScriptsJson.Replace( "\"main.tps\"", "" ), "needs at least one script file" );
	}

	[TestMethod]
	public void TheManifestCapsTheNumberOfScriptFilesAndNeedsAMatchingScriptApi()
	{
		var files = string.Join( ", ", Enumerable.Range( 0, ModManifest.MaximumScriptFiles + 1 ).Select( index => $"\"s{index}.tps\"" ) );
		AssertInvalid( ScriptsJson.Replace( "\"main.tps\"", files ), "more than 8 entries" );
		AssertInvalid( ScriptsJson.Replace( "\"scriptApi\": \">=1.0.0 <2.0.0\", ", "" ), "'scriptApi' is required" );
		AssertInvalid( ScriptsJson.Replace( ">=1.0.0 <2.0.0", "soon" ), "'scriptApi' 'soon' is not a version range" );
		AssertInvalid( """, "scriptApi": "*" """, "only for mods with" );
		Assert.IsTrue( ModManifest.MaximumScriptBytes < 1024 * 1024 && ModManifest.MaximumScriptFiles < 20, "far below the general mod allowance" );
	}

	[TestMethod]
	public void AScriptApiRangeThatExcludesThisBuildRefusesTheMod()
	{
		var set = Resolve( Mod( "community.future", ScriptsJson.Replace( ">=1.0.0 <2.0.0", ">=2.0.0" ) ) );
		Assert.AreEqual( 0, set.Mods.Count );
		var refusal = set.Refused.Single();
		Assert.AreEqual( ModRefusalKind.Incompatible, refusal.Kind );
		StringAssert.Contains( refusal.Reason, $"needs script API >=2.0.0, not {ScriptLimits.ScriptApiVersion}" );
	}

	// ---- Loading ----------------------------------------------------------------------------------

	[TestMethod]
	public void AScriptModResolvesWithItsFilesCompiled()
	{
		var set = Resolve( Mod( "community.ok" ) );
		Assert.AreEqual( 0, set.Refused.Count, Refusal( set ) );
		var scripts = set.Mods.Single().Scripts!;
		Assert.AreEqual( "main.tps", scripts.Files.Single().Path );
		Assert.IsTrue( scripts.Files.Single().Program.HasFunction( "leaveQueue" ) );
		Assert.AreEqual( (0, "leaveQueue"), scripts.Hooks["leaveQueue"] );
		Assert.IsNull( Resolve( Mod( "community.plain", "", ("a.txt", "x") ) ).Mods.Single().Scripts );
	}

	[TestMethod]
	public void ACompileErrorRefusesTheModWithTheFileAndLine()
	{
		var set = Resolve( Mod( "community.broken", ScriptsJson, ("main.tps", "function leaveQueue(guest, attraction, prev)\n  return prev\nend\nlocal bad = 1 / 2\n") ) );
		Assert.AreEqual( 0, set.Mods.Count );
		var refusal = set.Refused.Single();
		Assert.AreEqual( ModRefusalKind.Script, refusal.Kind );
		StringAssert.Contains( refusal.Reason, "script 'main.tps' line 4" );
	}

	[TestMethod]
	public void HooksMustNameAFunctionWithTheRightParameters()
	{
		var missing = Resolve( Mod( "community.nofunction", ScriptsJson, ("main.tps", "function other(a, b, c)\n  return a\nend\n") ) );
		StringAssert.Contains( Refusal( missing ), "no script defines" );
		var arity = Resolve( Mod( "community.arity", ScriptsJson, ("main.tps", "function leaveQueue(guest)\n  return true\nend\n") ) );
		StringAssert.Contains( Refusal( arity ), "needs a function with 3 parameters, but 'leaveQueue' has 1" );
		var twice = Resolve( Mod( "community.twice", ScriptsJson.Replace( "\"main.tps\"", "\"main.tps\", \"b.tps\"" ), ("main.tps", Script), ("b.tps", Script) ) );
		StringAssert.Contains( Refusal( twice ), "more than one script defines" );
		var missingFile = Resolve( Mod( "community.nofile", ScriptsJson.Replace( "\"main.tps\"", "\"gone.tps\"" ) ) );
		StringAssert.Contains( Refusal( missingFile ), "'gone.tps' is not a file of the mod" );
	}

	[TestMethod]
	public void ScriptSizesAreCappedPerFileAndInTotal()
	{
		var padding = "-- " + new string( 'x', 60_000 ) + "\n";
		var big = Resolve( Mod( "community.big", ScriptsJson, ("main.tps", Script + "-- " + new string( 'x', ScriptLimits.MaxSourceBytes ) + "\n") ) );
		StringAssert.Contains( Refusal( big ), $"larger than {ScriptLimits.MaxSourceBytes} bytes" );
		var files = Enumerable.Range( 0, 5 ).Select( index => ($"s{index}.tps", index == 0 ? Script + padding : padding) ).ToArray();
		var many = Resolve( Mod( "community.many", ScriptsJson.Replace( "\"main.tps\"", string.Join( ", ", files.Select( file => $"\"{file.Item1}\"" ) ) ), files ) );
		StringAssert.Contains( Refusal( many ), $"larger than {ModManifest.MaximumScriptBytes} bytes together" );
	}

	[TestMethod]
	public void TheExecutedBytesAreTheHashedBytes()
	{
		var directory = Mod( "community.exact" );
		var set = Resolve( directory );
		var mod = set.Mods.Single();
		var original = Encoding.UTF8.GetBytes( Script );
		var hashed = mod.Files.Single( file => file.Path == "main.tps" ).Sha256;

		// the file changes after the mod was resolved: the retained bytes and the program still are the hashed ones
		File.WriteAllText( Path.Combine( directory, "main.tps" ), "function leaveQueue(guest, attraction, prev)\n  return false\nend\n" );
		var file = mod.Scripts!.Files.Single();
		CollectionAssert.AreEqual( original, file.Source.ToArray() );
		Assert.AreEqual( hashed, Convert.ToHexString( SHA256.HashData( file.Source.Span ) ).ToLowerInvariant() );
		Assert.AreEqual( hashed, file.Program.Sha256 );
		Assert.AreEqual( ModSet.ContentDigest( mod.Files ), mod.ContentSha256 );
		// recompiling the retained bytes gives the same program identity
		Assert.AreEqual( hashed, ScriptProgram.Compile( file.Source.Span ).Sha256 );
	}

	// ---- Classification and saves -----------------------------------------------------------------

	[TestMethod]
	public void AModWithScriptsChangesTheSimulationAndParkSavesRecordIt()
	{
		var set = Resolve( Mod( "community.sim" ) );
		var mod = set.Mods.Single();
		Assert.IsTrue( mod.AffectsSimulation );
		Assert.IsTrue( ModSet.AffectsSimulation( mod.Manifest, new[] { "main.tps" } ) );
		Assert.AreEqual( "community.sim", set.SimulationMods.Single().Id );
		var saved = ParkResumeMods.Capture( set );
		Assert.AreEqual( mod.ContentSha256, saved!.Single().ContentSha256 );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( saved, set ).Count );
		StringAssert.Contains( ParkResumeMods.Unmet( saved, ModSet.Defaults( OfficialExtensions.All ) ).Single(), "needs community.sim" );
		Assert.IsTrue( new ModScripts( new[] { "a.tps" }, new Dictionary<string, string> { ["leaveQueue"] = "f" } ).AffectsSimulation );
		Assert.AreEqual( 0, ScriptLimits.UiOnlyHooks.Count, "v1 has no UI-only hooks" );
	}

	// ---- Safe mode ---------------------------------------------------------------------------------

	[TestMethod]
	public void TheSafeModeFlagSwitchesScriptModsOffWithoutReadingThem()
	{
		var config = Path.Combine( root, "config" );
		Assert.IsFalse( ScriptSafeMode.Enter( Array.Empty<string>(), config, smokeTest: false ) );
		Assert.IsTrue( ScriptSafeMode.Enter( new[] { "--safe-mode" }, config, smokeTest: false ) );
		StringAssert.Contains( ScriptSafeMode.Reason!, "--safe-mode" );
		// a script that does not even compile is not compiled, so the mod is not refused for it
		var directory = Mod( "community.safe", ScriptsJson, ("main.tps", "function (((") );
		var set = Resolve( directory, scriptsOff: ScriptSafeMode.Active );
		Assert.AreEqual( 0, set.Mods.Count );
		Assert.AreEqual( ModRefusalKind.SafeMode, set.Refused.Single().Kind );
		// a mod without scripts is unaffected
		Assert.AreEqual( 1, Resolve( Mod( "community.plain", "", ("a.txt", "x") ), scriptsOff: true ).Mods.Count );
	}

	[TestMethod]
	public void AMarkerLeftByAnUnfinishedStartSwitchesScriptModsOffOnceAndIsRemoved()
	{
		var config = Path.Combine( root, "config" );
		var set = Resolve( Mod( "community.ok" ) );
		var marker = ScriptSafeMode.Begin( set, config, smokeTest: false );
		Assert.IsNotNull( marker );
		Assert.IsTrue( File.Exists( ScriptSafeMode.MarkerPath( config ) ), "written before the first frame" );

		// the process died before the marker was removed: the next start is in safe mode, and sees the marker only once
		Assert.IsTrue( ScriptSafeMode.Enter( Array.Empty<string>(), config, smokeTest: false ) );
		StringAssert.Contains( ScriptSafeMode.Reason!, "did not finish" );
		Assert.IsFalse( File.Exists( ScriptSafeMode.MarkerPath( config ) ) );
		Assert.IsNull( ScriptSafeMode.Begin( set, config, smokeTest: false ), "no new marker in a safe run" );
		Assert.IsFalse( ScriptSafeMode.Enter( Array.Empty<string>(), config, smokeTest: false ) );
		Assert.IsNull( ScriptSafeMode.Reason );
	}

	[TestMethod]
	public void TheMarkerIsRemovedAfterACleanStartAndNeverWrittenWithoutScriptMods()
	{
		var config = Path.Combine( root, "config" );
		var now = 0.0;
		var set = Resolve( Mod( "community.ok" ) );
		var marker = ScriptSafeMode.Begin( set, config, smokeTest: false, () => now )!;
		now = ScriptSafeMode.CleanStartSeconds - 1;
		marker.Update();
		Assert.IsTrue( File.Exists( ScriptSafeMode.MarkerPath( config ) ) );
		now = ScriptSafeMode.CleanStartSeconds;
		marker.Update();
		Assert.IsFalse( File.Exists( ScriptSafeMode.MarkerPath( config ) ) );
		Assert.IsTrue( marker.Cleared );
		marker.Clear();

		Assert.IsNull( ScriptSafeMode.Begin( ModSet.Defaults( OfficialExtensions.All ), config, smokeTest: false ), "no script mods, no marker" );
		Assert.IsNull( ScriptSafeMode.Begin( set, config, smokeTest: true ), "smoke tests leave the real marker alone" );
		Assert.IsFalse( File.Exists( ScriptSafeMode.MarkerPath( config ) ) );
		// and a smoke test does not read it either
		File.WriteAllText( ScriptSafeMode.MarkerPath( config ), "x" );
		Assert.IsFalse( ScriptSafeMode.Enter( Array.Empty<string>(), config, smokeTest: true ) );
		Assert.IsTrue( File.Exists( ScriptSafeMode.MarkerPath( config ) ) );
	}

	[TestMethod]
	public void StartupResolvesWithScriptModsOffInSafeMode()
	{
		var directory = Mod( "community.startup" );
		var found = ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null );
		var settings = new ModSettings { Enabled = new Dictionary<string, bool> { ["community.startup"] = true } };
		Assert.AreEqual( 1, ModStartup.Resolve( found, settings, Array.Empty<string>(), new List<string>() ).Mods.Count );
		Assert.AreEqual( 0, ModStartup.Resolve( found, settings, Array.Empty<string>(), new List<string>(), scriptModsOff: true ).Mods.Count );
	}

	// ---- The shipped example -----------------------------------------------------------------------

	private const string ExampleId = "opentpw.example.patient-guests";
	private const string ExampleGoldenDigest = "3e3621368d1572124841f1154546a8997718186b7bae9d8cb9ac26bb4a7226f9";

	private static string ExampleFolder => Path.Combine( ModDiscovery.ProgramModsDirectory( AppContext.BaseDirectory ), ExampleId );

	[TestMethod]
	public void TheExampleModCompilesHasLfEndingsAndAFixedDigest()
	{
		foreach ( var file in Directory.GetFiles( ExampleFolder, "*", SearchOption.AllDirectories ) )
			Assert.IsFalse( File.ReadAllBytes( file ).Contains( (byte)'\r' ), $"{Path.GetFileName( file )} has a CR: a Windows checkout would change the mod's digest" );
		var found = ModDiscovery.Discover( Array.Empty<string>(), Array.Empty<string>(), null, AppContext.BaseDirectory ).Single( mod => mod.Id == ExampleId );
		Assert.IsTrue( found.IsValid, found.Error );
		Assert.AreEqual( ModCategory.Official, found.Manifest!.Category );
		var set = ModSet.Resolve( new[] { found }, new ModSettings { Enabled = new Dictionary<string, bool> { [ExampleId] = true } }, null, GameEdition.ThemeParkWorld, OfficialExtensions.All );
		Assert.AreEqual( 0, set.Refused.Count, Refusal( set ) );
		var mod = set.Mods.Single();
		CollectionAssert.AreEquivalent( new[] { "leaveQueue", "joinQueue" }, mod.Scripts!.Hooks.Keys.ToArray() );
		Assert.IsTrue( mod.AffectsSimulation );
		Assert.AreEqual( ExampleGoldenDigest, mod.ContentSha256, "the example's files changed: update the golden digest and the mod's version together" );
		Assert.IsFalse( ModSettings.Default.IsEnabled( ExampleId, false ), "off by default" );
	}

	[TestMethod]
	public void AnOfficialIdOutsideTheProgramFolderIsRefusedLikeAnyOtherOfficialMod()
	{
		var copy = Path.Combine( root, "mods", ExampleId );
		Directory.CreateDirectory( copy );
		foreach ( var file in Directory.GetFiles( ExampleFolder, "*", SearchOption.AllDirectories ) )
		{
			var target = Path.Combine( copy, Path.GetRelativePath( ExampleFolder, file ) );
			Directory.CreateDirectory( Path.GetDirectoryName( target )! );
			File.Copy( file, target );
		}
		var found = ModDiscovery.Discover( new[] { copy }, Array.Empty<string>(), null, null ).Single();
		Assert.IsFalse( found.IsValid );
		StringAssert.Contains( found.Error!, "reserved for content shipped with OpenTPW" );
	}

	// ---- Mods screen --------------------------------------------------------------------------------

	[TestMethod]
	public void TheModsScreenSaysContainsScriptsAndShowsTheCompileErrorAsTheReason()
	{
		var good = Mod( "community.good" );
		var broken = Mod( "community.bad", ScriptsJson, ("main.tps", "function leaveQueue(a, b, c)\n  return 1 / 2\nend\n") );
		var services = new ModsServices
		{
			Discovered = () => ModDiscovery.Discover( new[] { good, broken }, Array.Empty<string>(), null, null ),
			SettingsPath = Path.Combine( root, "config", "mods.json" ),
			Active = () => ModSet.Defaults( OfficialExtensions.All ),
			OpenTpwVersion = null,
			Edition = () => GameEdition.ThemeParkWorld,
			ScriptModsOff = () => false,
		};
		var stack = new UiScreenStack();
		var screen = ModsScreen.Create( stack, OriginalUiTests.FakeStrings(), services );
		stack.Push( screen );
		var mods = (UiScrollList)screen.Find( "mods" )!;
		var label = OriginalUiTests.FakeStrings().Extra( OpenTpwText.ModContainsScripts );
		Assert.AreEqual( "Contains scripts", label );

		// rows are sorted by id: community.bad, community.good
		mods.Select( 1 );
		screen.Updating!( Context() );
		StringAssert.Contains( ((UiLabel)screen.Find( "details" )!).Text(), label );
		// switch both on and wait for the resolution
		foreach ( var index in new[] { 0, 1 } )
		{
			mods.Select( index );
			((UiButton)screen.Find( "modsOn" )!).Clicked!();
		}
		for ( var attempt = 0; attempt < 400 && ((UiScrollList)screen.Find( "mods" )!).Trailing!( 0 ) == OriginalUiTests.FakeStrings().Extra( OpenTpwText.ModChecking ); attempt++ )
		{
			screen.Updating!( Context() );
			System.Threading.Thread.Sleep( 5 );
		}
		screen.Updating!( Context() );
		mods.Select( 0 );
		screen.Updating!( Context() );
		var details = ((UiLabel)screen.Find( "details" )!).Text();
		StringAssert.Contains( details, "script 'main.tps' line 2" );
		Assert.AreEqual( OriginalUiTests.FakeStrings().Extra( OpenTpwText.ModUnusable ), mods.Trailing!( 0 ) );
		Assert.AreEqual( OriginalUiTests.FakeStrings().Extra( OpenTpwText.ModOn ), mods.Trailing!( 1 ) );
		stack.Clear();
	}

	private static UiContext Context() => new( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) )
	{
		Canvas = new UiCanvas( 1024, 768 )
	};
}
