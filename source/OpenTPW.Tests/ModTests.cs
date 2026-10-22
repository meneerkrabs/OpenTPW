using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>The mod core (docs/MODS.md): strict manifests, versions, discovery, settings and resolution.</summary>
[TestClass]
public class ModTests
{
	private string root = null!;

	[TestInitialize]
	public void Initialize() => root = Directory.CreateTempSubdirectory( "opentpw-mods-" ).FullName;

	[TestCleanup]
	public void Cleanup() => Directory.Delete( root, true );

	private static string Manifest( string id, string version = "1.0.0", string extra = "" ) =>
		$$"""{ "schema": 1, "id": "{{id}}", "name": "Test {{id}}", "version": "{{version}}", "category": "{{(id.StartsWith( "opentpw." ) ? "official" : "community")}}"{{extra}} }""";

	private static ModManifest Parse( string json ) => ModManifest.Parse( Encoding.UTF8.GetBytes( json ) );

	private static void AssertInvalid( string json, string expected )
	{
		var exception = Assert.ThrowsException<InvalidDataException>( () => Parse( json ) );
		StringAssert.Contains( exception.Message, expected );
	}

	/// <summary>Writes a mod folder <c>root/name</c> with its manifest and files.</summary>
	private string Mod( string id, string version = "1.0.0", string extra = "", string? folder = null, params (string Path, string Text)[] files )
	{
		var directory = Path.Combine( root, folder ?? id );
		Directory.CreateDirectory( directory );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ), Manifest( id, version, extra ) );
		foreach ( var (path, text) in files )
		{
			var full = Path.Combine( directory, path );
			Directory.CreateDirectory( Path.GetDirectoryName( full )! );
			File.WriteAllText( full, text );
		}
		return directory;
	}

	private static DiscoveredMod Found( string directory ) =>
		ModDiscovery.Discover( new[] { directory }, Array.Empty<string>(), null, null ).Single();

	private static ModSettings Enable( params string[] ids ) => new() { Enabled = ids.ToDictionary( id => id, _ => true ) };

	private static readonly IOpenTpwExtension Core = new OpenTpwExtension( "opentpw.core-test", "Core test", new ModVersion( 2, 1, 0 ), true, false );

	private static ModSet Resolve( IEnumerable<string> directories, ModSettings settings, ModVersion? version = null, GameEdition edition = GameEdition.ThemeParkWorld, params string[] disabled ) =>
		ModSet.Resolve( directories.Select( Found ).ToList(), settings, version, edition, new[] { Core }, disabled );

	// ---- Manifest -----------------------------------------------------------------------------

	[TestMethod]
	public void AFullManifestParses()
	{
		var manifest = Parse( """
			{ "schema": 1, "id": "community.tpi-rides", "name": "TPI rides", "version": "1.2.3-beta.1", "category": "community",
			  "openTpw": ">=0.9.0 <0.10.0", "edition": "theme-park-world", "author": "Ann", "description": "Two\nlines",
			  "dependencies": [{ "id": "opentpw.cross-edition-objects", "version": ">=1.0.0" }], "after": ["community.other"],
			  "inputs": [{ "id": "tpi", "kind": "original-data", "edition": "theme-park-inc" }],
			  "content": { "overrides": "data/", "objects": "objects.json", "strings": "strings\\" } }
			""" );
		Assert.AreEqual( "community.tpi-rides", manifest.Id );
		Assert.AreEqual( new ModVersion( 1, 2, 3, "beta.1" ), manifest.Version );
		Assert.AreEqual( ModCategory.Community, manifest.Category );
		Assert.AreEqual( GameEdition.ThemeParkWorld, manifest.Edition );
		Assert.IsTrue( manifest.OpenTpw!.Contains( new ModVersion( 0, 9, 4 ) ) );
		Assert.AreEqual( "opentpw.cross-edition-objects", manifest.Dependencies.Single().Id );
		Assert.AreEqual( "tpi", manifest.Inputs.Single().Id );
		Assert.AreEqual( new ModContent( "data", "objects.json", "strings" ), manifest.Content );
		CollectionAssert.AreEqual( new[] { "community.other" }, manifest.After.ToArray() );
	}

	[TestMethod]
	public void UnknownDuplicateAndLooseJsonIsRefused()
	{
		AssertInvalid( Manifest( "community.a", extra: ", \"script\": \"run.dll\"" ), "malformed" );
		AssertInvalid( Manifest( "community.a", extra: ", \"content\": { \"code\": \"x\" }" ), "malformed" );
		AssertInvalid( """{ "schema": 1, "schema": 1, "id": "community.a", "name": "A", "version": "1.0.0", "category": "community" }""", "more than once" );
		AssertInvalid( Manifest( "community.a", extra: "," ), "malformed" );
		AssertInvalid( "// comment\n" + Manifest( "community.a" ), "malformed" );
		AssertInvalid( Manifest( "community.a" ).Replace( "\"schema\": 1", "\"schema\": 2" ), "schema" );
		AssertInvalid( """{ "schema": 1, "name": "A", "version": "1.0.0", "category": "community" }""", "'id' is required" );
	}

	[TestMethod]
	public void OversizedManifestsAndFieldsAreRefused()
	{
		var padding = new string( ' ', ModManifest.MaximumBytes );
		AssertInvalid( Manifest( "community.a" ) + padding, "larger than" );
		AssertInvalid( Manifest( "community.a" ).Replace( "Test community.a", new string( 'n', ModManifest.MaximumNameLength + 1 ) ), "'name'" );
		AssertInvalid( Manifest( "community." + new string( 'a', ModManifest.MaximumIdLength ) ), "'id'" );
		var dependencies = string.Join( ",", Enumerable.Range( 0, ModManifest.MaximumDependencies + 1 ).Select( index => $"{{ \"id\": \"community.d{index}\" }}" ) );
		AssertInvalid( Manifest( "community.a", extra: $", \"dependencies\": [{dependencies}]" ), "more than" );
		var path = Path.Combine( root, ModManifest.FileName );
		File.WriteAllText( path, Manifest( "community.a" ) + padding );
		StringAssert.Contains( Assert.ThrowsException<InvalidDataException>( () => ModManifest.Load( path ) ).Message, "larger than" );
	}

	[DataTestMethod]
	[DataRow( "../outside" )]
	[DataRow( "data/../../outside" )]
	[DataRow( "/etc" )]
	[DataRow( "\\\\server\\share" )]
	[DataRow( "C:/Windows" )]
	[DataRow( "data\\..\\..\\x" )]
	[DataRow( "a//b" )]
	[DataRow( "./data" )]
	[DataRow( "" )]
	public void ContentPathsCannotLeaveTheModFolder( string path )
	{
		var escaped = path.Replace( "\\", "\\\\" );
		AssertInvalid( Manifest( "community.a", extra: $", \"content\": {{ \"overrides\": \"{escaped}\" }}" ), "content.overrides" );
	}

	[TestMethod]
	public void IdsCategoriesAndEditionsAreChecked()
	{
		AssertInvalid( Manifest( "Community.A" ), "'id'" );
		AssertInvalid( Manifest( "other.mod" ), "'id'" );
		AssertInvalid( Manifest( "community.a" ).Replace( "\"community\" }", "\"official\" }" ), "does not match" );
		AssertInvalid( Manifest( "community.a", extra: ", \"edition\": \"sim-coaster\"" ), "edition" );
		AssertInvalid( Manifest( "community.a", extra: ", \"dependencies\": [{ \"id\": \"community.a\" }]" ), "dependency" );
		AssertInvalid( Manifest( "community.a", extra: ", \"inputs\": [{ \"id\": \"tpi\", \"kind\": \"executable\" }]" ), "kind" );
		AssertInvalid( Manifest( "community.a", version: "1.0" ), "'version'" );
		AssertInvalid( Manifest( "community.a", extra: ", \"openTpw\": \"~0.9\"" ), "'openTpw'" );
	}

	// ---- Versions ------------------------------------------------------------------------------

	[TestMethod]
	public void VersionsCompareNumericallyWithPrereleasesFirst()
	{
		Assert.IsTrue( ModVersion.Parse( "0.10.0" ) > ModVersion.Parse( "0.9.12" ) );
		Assert.IsTrue( ModVersion.Parse( "1.0.0-rc.1" ) < ModVersion.Parse( "1.0.0" ) );
		Assert.IsTrue( ModVersion.Parse( "1.0.0-alpha" ) < ModVersion.Parse( "1.0.0-beta" ) );
		Assert.AreEqual( "1.2.3-x", ModVersion.Parse( "1.2.3-x" ).ToString() );
		foreach ( var invalid in new[] { "1", "1.0", "01.0.0", "1.0.0-", "-1.0.0", "1.0.0 ", "1.0.0+build", "a.b.c", "1.0.0.0" } )
			Assert.IsFalse( ModVersion.TryParse( invalid, out _ ), invalid );
	}

	[TestMethod]
	public void RangesHoldEveryComparator()
	{
		var range = ModVersionRange.Parse( ">=0.9.0 <0.10.0" );
		Assert.IsTrue( range.Contains( ModVersion.Parse( "0.9.0" ) ) );
		Assert.IsTrue( range.Contains( ModVersion.Parse( "0.9.99" ) ) );
		Assert.IsFalse( range.Contains( ModVersion.Parse( "0.10.0" ) ) );
		Assert.IsFalse( range.Contains( ModVersion.Parse( "0.8.9" ) ) );
		Assert.IsTrue( ModVersionRange.Parse( "1.2.3" ).Contains( ModVersion.Parse( "1.2.3" ) ) );
		Assert.IsFalse( ModVersionRange.Parse( "=1.2.3" ).Contains( ModVersion.Parse( "1.2.4" ) ) );
		Assert.IsTrue( ModVersionRange.Parse( ">1.0.0 <=2.0.0" ).Contains( ModVersion.Parse( "2.0.0" ) ) );
		Assert.IsTrue( ModVersionRange.Parse( "*" ).Contains( ModVersion.Parse( "99.0.0" ) ) );
		foreach ( var invalid in new[] { "", "~1.0.0", "^1.0.0", ">= 1.0.0", "1.0.0 || 2.0.0", "<1.0" } )
			Assert.IsFalse( ModVersionRange.TryParse( invalid, out _ ), invalid );
	}

	// ---- Discovery -----------------------------------------------------------------------------

	[TestMethod]
	public void DiscoveryFollowsPrecedenceAndReportsShadowedAndMisnamedFolders()
	{
		var config = Path.Combine( root, "config" );
		var program = Path.Combine( root, "program" );
		var commandLine = Mod( "community.shared", "2.0.0", folder: "cli" );
		void At( string modsRoot, string id, string folder, string version = "1.0.0" )
		{
			var directory = Path.Combine( modsRoot, folder );
			Directory.CreateDirectory( directory );
			File.WriteAllText( Path.Combine( directory, ModManifest.FileName ), Manifest( id, version ) );
		}
		At( ModDiscovery.ConfigModsDirectory( config ), "community.shared", "community.shared", "1.0.0" );
		At( ModDiscovery.ConfigModsDirectory( config ), "community.named", "wrong-name" );
		At( ModDiscovery.ConfigModsDirectory( config ), "opentpw.reserved", "opentpw.reserved" );
		At( ModDiscovery.ProgramModsDirectory( program ), "opentpw.bundled", "opentpw.bundled" );
		Directory.CreateDirectory( Path.Combine( ModDiscovery.ConfigModsDirectory( config ), "community.empty" ) );

		var found = ModDiscovery.Discover( new[] { commandLine }, Array.Empty<string>(), config, program );
		var valid = found.Where( mod => mod.IsValid ).ToList();
		Assert.AreEqual( ModSource.CommandLine, valid.Single( mod => mod.Id == "community.shared" ).Source );
		Assert.AreEqual( new ModVersion( 2, 0, 0 ), valid.Single( mod => mod.Id == "community.shared" ).Manifest!.Version );
		Assert.AreEqual( ModSource.Program, valid.Single( mod => mod.Id == "opentpw.bundled" ).Source );
		StringAssert.Contains( found.Single( mod => mod.Id == "community.shared" && !mod.IsValid ).Error, "shadowed" );
		StringAssert.Contains( found.Single( mod => mod.Id == "community.named" ).Error, "named after its id" );
		StringAssert.Contains( found.Single( mod => mod.Id == "opentpw.reserved" ).Error, "reserved" );
		Assert.IsFalse( found.Single( mod => mod.Id == "community.empty" ).IsValid );
		CollectionAssert.AreEqual( new[] { "a", "b" }, ModDiscovery.SplitEnvironment( $"a{Path.PathSeparator} {Path.PathSeparator}b" ).ToArray() );
	}

	// ---- Resolution ----------------------------------------------------------------------------

	[TestMethod]
	public void NoModsLeavesOnlyTheOfficialDefaults()
	{
		var set = Resolve( Array.Empty<string>(), ModSettings.Default );
		CollectionAssert.AreEqual( new[] { Core.Id }, set.Order.ToArray() );
		Assert.AreEqual( 0, set.Mods.Count );
		Assert.AreEqual( "", set.Digest );
		Assert.AreEqual( 0, set.Refused.Count );
		Assert.IsFalse( Resolve( Array.Empty<string>(), new ModSettings { Enabled = new Dictionary<string, bool> { [Core.Id] = false } } ).IsEnabled( Core.Id ) );
		Assert.IsFalse( Resolve( Array.Empty<string>(), ModSettings.Default, disabled: Core.Id ).IsEnabled( Core.Id ) );
	}

	[TestMethod]
	public void FilesModsAreOptInAndOrderedByDependenciesThenOfficialThenUserOrderThenId()
	{
		var directories = new[]
		{
			Mod( "community.zeta" ),
			Mod( "community.alpha" ),
			Mod( "community.base" ),
			Mod( "community.needs-base", extra: ", \"dependencies\": [{ \"id\": \"community.base\", \"version\": \">=1.0.0\" }]" ),
			Mod( "community.needs-core", extra: ", \"dependencies\": [{ \"id\": \"opentpw.core-test\", \"version\": \">=2.0.0 <3.0.0\" }]" ),
			Mod( "community.off" ),
		};
		var settings = Enable( "community.zeta", "community.alpha", "community.base", "community.needs-base", "community.needs-core" ) with
		{
			Order = new[] { "community.needs-base", "community.zeta" },
		};
		var set = Resolve( directories, settings );
		CollectionAssert.AreEqual( new[] { Core.Id, "community.zeta", "community.alpha", "community.base", "community.needs-base", "community.needs-core" }, set.Order.ToArray() );
		Assert.IsFalse( set.IsEnabled( "community.off" ) );
		Assert.AreEqual( 0, set.Refused.Count );
		CollectionAssert.AreEqual( set.Order.Skip( 1 ).ToArray(), set.Mods.Select( mod => mod.Id ).ToArray() );
	}

	[TestMethod]
	public void MissingDisabledAndWrongVersionDependenciesAreRefusedTransitively()
	{
		var directories = new[]
		{
			Mod( "community.base", "1.5.0" ),
			Mod( "community.wants-two", extra: ", \"dependencies\": [{ \"id\": \"community.base\", \"version\": \">=2.0.0\" }]" ),
			Mod( "community.on-top", extra: ", \"dependencies\": [{ \"id\": \"community.wants-two\" }]" ),
			Mod( "community.wants-missing", extra: ", \"dependencies\": [{ \"id\": \"community.nowhere\" }]" ),
			Mod( "community.wants-core", extra: ", \"dependencies\": [{ \"id\": \"opentpw.core-test\" }]" ),
		};
		var set = Resolve( directories, Enable( "community.base", "community.wants-two", "community.on-top", "community.wants-missing", "community.wants-core" ), disabled: Core.Id );
		CollectionAssert.AreEqual( new[] { "community.base" }, set.Order.ToArray() );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.wants-two" ).Reason, "not 1.5.0" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.on-top" ).Reason, "disabled or refused" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.wants-missing" ).Reason, "not installed" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.wants-core" ).Reason, "disabled or refused" );
	}

	[TestMethod]
	public void DependencyAndAfterCyclesAreRefused()
	{
		var directories = new[]
		{
			Mod( "community.a", extra: ", \"dependencies\": [{ \"id\": \"community.b\" }]" ),
			Mod( "community.b", extra: ", \"dependencies\": [{ \"id\": \"community.a\" }]" ),
			Mod( "community.c", extra: ", \"dependencies\": [{ \"id\": \"community.a\" }]" ),
			Mod( "community.x", extra: ", \"after\": [\"community.y\"]" ),
			Mod( "community.y", extra: ", \"after\": [\"community.x\"]" ),
			Mod( "community.free" ),
		};
		var set = Resolve( directories, Enable( "community.a", "community.b", "community.c", "community.x", "community.y", "community.free" ) );
		CollectionAssert.AreEqual( new[] { Core.Id, "community.free" }, set.Order.ToArray() );
		foreach ( var id in new[] { "community.a", "community.b", "community.x", "community.y" } )
			StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == id ).Reason, "cycle" );
		Assert.IsFalse( set.IsEnabled( "community.c" ) );
	}

	[TestMethod]
	public void ConflictingOverridesAreRefusedUnlessOneIsOrderedAfterTheOther()
	{
		const string overrides = ", \"content\": { \"overrides\": \"data\" }";
		var first = Mod( "community.first", extra: overrides, files: new[] { ("data/levels/jungle/terrain.wct", "first") } );
		var second = Mod( "community.second", extra: overrides, files: new[] { ("data/LEVELS/Jungle/terrain.WCT", "second") } );
		var elsewhere = Mod( "community.elsewhere", extra: overrides, files: new[] { ("data/levels/jungle/other.wct", "other") } );
		var set = Resolve( new[] { first, second, elsewhere }, Enable( "community.first", "community.second", "community.elsewhere" ) );
		CollectionAssert.AreEqual( new[] { Core.Id, "community.elsewhere" }, set.Order.ToArray() );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.first" ).Reason, "levels/jungle/terrain.wct" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.second" ).Reason, "community.first" );

		var after = Mod( "community.later", extra: overrides + ", \"after\": [\"community.first\"]", files: new[] { ("data/levels/jungle/terrain.wct", "later") } );
		var ordered = Resolve( new[] { after, first }, Enable( "community.first", "community.later" ) with { Order = new[] { "community.later" } } );
		CollectionAssert.AreEqual( new[] { Core.Id, "community.first", "community.later" }, ordered.Order.ToArray() );
		Assert.AreEqual( 0, ordered.Refused.Count );
	}

	[TestMethod]
	public void IncompatibleVersionsEditionsAndMissingInputsAreRefused()
	{
		var directories = new[]
		{
			Mod( "community.future", extra: ", \"openTpw\": \">=0.9.0 <0.10.0\"" ),
			Mod( "community.inc-only", extra: ", \"edition\": \"theme-park-inc\"" ),
			Mod( "community.needs-tpi", extra: ", \"inputs\": [{ \"id\": \"tpi\", \"kind\": \"original-data\", \"edition\": \"theme-park-inc\" }]" ),
			Mod( "community.has-tpi", extra: ", \"inputs\": [{ \"id\": \"tpi\", \"kind\": \"original-data\" }]" ),
		};
		var settings = Enable( directories.Select( Path.GetFileName ).ToArray()! ) with
		{
			Inputs = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["community.has-tpi"] = new Dictionary<string, string> { ["tpi"] = root } },
		};
		var set = Resolve( directories, settings, new ModVersion( 0, 8, 1 ) );
		CollectionAssert.AreEqual( new[] { Core.Id, "community.has-tpi" }, set.Order.ToArray() );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.future" ).Reason, "needs OpenTPW" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.inc-only" ).Reason, "theme-park-inc" );
		StringAssert.Contains( set.Refused.Single( refusal => refusal.Id == "community.needs-tpi" ).Reason, "'tpi'" );
		// a development build without a version does not check openTpw ranges
		Assert.IsTrue( Resolve( directories.Take( 1 ), Enable( "community.future" ) ).IsEnabled( "community.future" ) );
	}

	[TestMethod]
	public void FilesModsCannotTakeAnExtensionsIdAndInvalidFoldersAreReported()
	{
		var broken = Path.Combine( root, "broken" );
		Directory.CreateDirectory( broken );
		File.WriteAllText( Path.Combine( broken, ModManifest.FileName ), "{" );
		var set = Resolve( new[] { broken }, Enable( "community.broken" ) );
		Assert.AreEqual( 1, set.Refused.Count );
		var bundled = Path.Combine( root, "program", "content", "mods", Core.Id );
		Directory.CreateDirectory( bundled );
		File.WriteAllText( Path.Combine( bundled, ModManifest.FileName ), Manifest( Core.Id ) );
		var discovered = ModDiscovery.Discover( Array.Empty<string>(), Array.Empty<string>(), null, Path.Combine( root, "program" ) );
		var clash = ModSet.Resolve( discovered, ModSettings.Default, null, GameEdition.ThemeParkWorld, new[] { Core } );
		StringAssert.Contains( clash.Refused.Single().Reason, "belongs to an OpenTPW extension" );
		Assert.IsTrue( clash.IsEnabled( Core.Id ) );
	}

	[TestMethod]
	public void ContentDigestIsStableAndCoversEveryFileAndTheManifest()
	{
		var directory = Mod( "community.digest", files: new[] { ("b/one.txt", "1"), ("a.txt", "2") } );
		var first = Resolve( new[] { directory }, Enable( "community.digest" ) ).Mods.Single().ContentSha256;
		Assert.AreEqual( 64, first.Length );
		Assert.AreEqual( first, Resolve( new[] { directory }, Enable( "community.digest" ) ).Mods.Single().ContentSha256 );
		// the documented canonical form: sorted "path NUL sha256 LF" lines
		var expected = ModSet.ContentDigest( Directory.EnumerateFiles( directory, "*", SearchOption.AllDirectories )
			.Select( path => (Path.GetRelativePath( directory, path ).Replace( '\\', '/' ),
				Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( path ) ) ).ToLowerInvariant()) ) );
		Assert.AreEqual( expected, first );
		File.WriteAllText( Path.Combine( directory, "b", "one.txt" ), "changed" );
		Assert.AreNotEqual( first, Resolve( new[] { directory }, Enable( "community.digest" ) ).Mods.Single().ContentSha256 );
		File.WriteAllText( Path.Combine( directory, "b", "one.txt" ), "1" );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ), Manifest( "community.digest", extra: ", \"author\": \"Ann\"" ) );
		Assert.AreNotEqual( first, Resolve( new[] { directory }, Enable( "community.digest" ) ).Mods.Single().ContentSha256, "the manifest is part of the digest" );
		Assert.AreEqual( "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", ModSet.ContentDigest( Array.Empty<(string, string)>() ) );
	}

	[TestMethod]
	public void IdsCannotEndInALineFeed()
	{
		Assert.IsFalse( ModManifest.IsValidId( "community.a\n" ) );
		Assert.IsFalse( ModManifest.IsValidInputId( "tpi\n" ) );
		AssertInvalid( Manifest( "community.a\\n" ), "'id'" );
		AssertInvalid( Manifest( "community.a", extra: ", \"inputs\": [{ \"id\": \"tpi\\n\", \"kind\": \"original-data\" }]" ), "input id" );
		var diagnostics = new List<string>();
		var settings = ModSettings.Parse( Encoding.UTF8.GetBytes( "{ \"enabled\": { \"community.a\\n\": true }, \"order\": [\"community.a\\n\"], \"inputs\": { \"community.a\": { \"tpi\\n\": \"/x\" } } }" ), diagnostics );
		Assert.AreEqual( 0, settings.Enabled.Count );
		Assert.AreEqual( 0, settings.Order.Count );
		Assert.AreEqual( 0, settings.Inputs["community.a"].Count );
		Assert.AreEqual( 3, diagnostics.Count, string.Join( "; ", diagnostics ) );
	}

	[DataTestMethod]
	[DataRow( "data/CON" )]
	[DataRow( "data/nul.txt" )]
	[DataRow( "Com1" )]
	[DataRow( "data./x" )]
	[DataRow( "data/x " )]
	public void ContentPathsCannotUseNamesWindowsCannotHold( string path ) =>
		AssertInvalid( Manifest( "community.a", extra: $", \"content\": {{ \"overrides\": \"{path}\" }}" ), "reserved" );

	[TestMethod]
	public void ThreeModsOverridingTheSameFileAreAllRefused()
	{
		const string overrides = ", \"content\": { \"overrides\": \"data\" }";
		var directories = new[] { "community.a", "community.b", "community.c" }
			.Select( id => Mod( id, extra: overrides, files: new[] { ("data/x.txt", id) } ) ).ToList();
		var set = Resolve( directories, Enable( "community.a", "community.b", "community.c" ) );
		CollectionAssert.AreEqual( new[] { Core.Id }, set.Order.ToArray() );
		CollectionAssert.AreEquivalent( new[] { "community.a", "community.b", "community.c" }, set.Refused.Select( refusal => refusal.Id ).ToArray() );

		// a fourth mod ordered after all three keeps its override; the three still conflict among themselves
		var last = Mod( "community.d", extra: overrides + ", \"after\": [\"community.a\", \"community.b\", \"community.c\"]", files: new[] { ("data/X.txt", "d") } );
		var withLast = Resolve( directories.Append( last ), Enable( "community.a", "community.b", "community.c", "community.d" ) );
		CollectionAssert.AreEqual( new[] { Core.Id, "community.d" }, withLast.Order.ToArray() );
	}

	[TestMethod]
	public void TheGoldenDigestIsTheSameOnEveryPlatform()
	{
		var directory = Path.Combine( root, "golden" );
		Directory.CreateDirectory( Path.Combine( directory, "data" ) );
		File.WriteAllText( Path.Combine( directory, ModManifest.FileName ), """{ "schema": 1, "id": "community.golden", "name": "Golden", "version": "1.0.0", "category": "community" }""" );
		File.WriteAllText( Path.Combine( directory, "data", "A.txt" ), "alpha\n" );
		File.WriteAllText( Path.Combine( directory, "data", "b.txt" ), "beta\n" );
		// written in decomposed form (as macOS file systems hand names out); hashed in composed form
		File.WriteAllText( Path.Combine( directory, "cafe\u0301.txt" ), "cafe\n" );
		// operating-system clutter is not part of the mod
		File.WriteAllText( Path.Combine( directory, ".DS_Store" ), "junk" );
		File.WriteAllText( Path.Combine( directory, "._cafe.txt" ), "junk" );
		File.WriteAllText( Path.Combine( directory, "data", "Thumbs.db" ), "junk" );
		File.WriteAllText( Path.Combine( directory, "data", "desktop.ini" ), "junk" );
		var set = Resolve( new[] { directory }, Enable( "community.golden" ) );
		Assert.AreEqual( 0, set.Refused.Count, string.Join( "; ", set.Refused ) );
		Assert.AreEqual( "08fcdf68c516d55a900c6167694d9ae2c8dcc2eda87186509e5c5920109fe618", set.Mods.Single().ContentSha256 );
	}

	[TestMethod]
	public void NamesThatDifferOnlyByCaseRefuseTheMod()
	{
		var directory = Mod( "community.cased", files: new[] { ("data/Foo.txt", "1") } );
		File.WriteAllText( Path.Combine( directory, "data", "foo.txt" ), "2" );
		if ( Directory.GetFiles( Path.Combine( directory, "data" ) ).Length < 2 )
			return; // a case-insensitive file system holds one file: nothing to refuse
		StringAssert.Contains( Resolve( new[] { directory }, Enable( "community.cased" ) ).Refused.Single().Reason, "differ only by case" );
	}

	[TestMethod]
	public void HostileManifestsAndFilesAreRefusedWithoutReadingThem()
	{
		if ( OperatingSystem.IsWindows() )
			return;
		// a manifest linked to an endless device: refused before any read
		var linked = Path.Combine( root, "linked" );
		Directory.CreateDirectory( linked );
		File.CreateSymbolicLink( Path.Combine( linked, ModManifest.FileName ), "/dev/zero" );
		StringAssert.Contains( Found( linked ).Error, "symbolic link" );
		// a FIFO would block the first read forever
		var piped = Path.Combine( root, "piped" );
		Directory.CreateDirectory( piped );
		MakeFifo( Path.Combine( piped, ModManifest.FileName ) );
		StringAssert.Contains( Found( piped ).Error, "not a regular file" );
		var withFifo = Mod( "community.fifo" );
		MakeFifo( Path.Combine( withFifo, "data.bin" ) );
		StringAssert.Contains( Resolve( new[] { withFifo }, Enable( "community.fifo" ) ).Refused.Single().Reason, "not a regular file" );
		// mods.json the same way
		var settings = Path.Combine( root, ModSettings.FileName );
		MakeFifo( settings );
		var diagnostics = new List<string>();
		Assert.AreSame( ModSettings.Default, ModSettings.Load( settings, diagnostics ) );
		StringAssert.Contains( diagnostics.Single(), "not a regular file" );
	}

	private static void MakeFifo( string path )
	{
		using var process = System.Diagnostics.Process.Start( "mkfifo", new[] { path } );
		process.WaitForExit();
		Assert.AreEqual( 0, process.ExitCode );
	}

	[TestMethod]
	public void AMalformedEnvironmentFolderIsARefusedEntry()
	{
		var found = ModDiscovery.Discover( Array.Empty<string>(), new[] { "bad\0folder" }, null, null ).Single();
		Assert.IsFalse( found.IsValid );
		StringAssert.Contains( found.Error, "not a usable folder path" );
	}

	[TestMethod]
	public void SymbolicLinksInsideAModAreRefused()
	{
		if ( OperatingSystem.IsWindows() )
			return;
		var directory = Mod( "community.linked", files: new[] { ("data.txt", "x") } );
		File.CreateSymbolicLink( Path.Combine( directory, "escape" ), "/etc/hosts" );
		var set = Resolve( new[] { directory }, Enable( "community.linked" ) );
		StringAssert.Contains( set.Refused.Single().Reason, "symbolic link" );
	}

	// ---- Settings ------------------------------------------------------------------------------

	[TestMethod]
	public void SettingsRoundTrip()
	{
		var path = Path.Combine( root, "config", ModSettings.FileName );
		var settings = new ModSettings
		{
			Enabled = new Dictionary<string, bool> { ["opentpw.online"] = false, ["community.a"] = true },
			Order = new[] { "community.a", "community.b" },
			Inputs = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["community.a"] = new Dictionary<string, string> { ["tpi"] = "/games/tpi" } },
		};
		settings.Save( path );
		var diagnostics = new List<string>();
		var loaded = ModSettings.Load( path, diagnostics );
		Assert.AreEqual( 0, diagnostics.Count, string.Join( "; ", diagnostics ) );
		Assert.IsFalse( loaded.IsEnabled( "opentpw.online", true ) );
		Assert.IsTrue( loaded.IsEnabled( "community.a", false ) );
		Assert.IsTrue( loaded.IsEnabled( "community.unlisted", true ) );
		CollectionAssert.AreEqual( settings.Order.ToArray(), loaded.Order.ToArray() );
		Assert.AreEqual( "/games/tpi", loaded.Inputs["community.a"]["tpi"] );
		CollectionAssert.AreEqual( File.ReadAllBytes( path ), loaded.Serialize(), "saving what was loaded writes the same bytes" );
		CollectionAssert.AreEqual( new[] { ModSettings.FileName }, Directory.GetFiles( Path.GetDirectoryName( path )! ).Select( Path.GetFileName ).ToArray(), "no temporary file is left behind" );
		Assert.AreSame( ModSettings.Default, ModSettings.Load( Path.Combine( root, "missing.json" ), diagnostics ) );
		Assert.AreEqual( 0, diagnostics.Count );
	}

	[DataTestMethod]
	[DataRow( "{ \"enabled\": { \"opentpw.online\": false" )]
	[DataRow( "{ \"enabled\": {}, \"theme\": \"dark\" }" )]
	[DataRow( "{ \"enabled\": { \"opentpw.online\": \"no\" } }" )]
	[DataRow( "[1, 2]" )]
	[DataRow( "" )]
	public void ACorruptSettingsFileFallsBackToTheDefaultsWithADiagnostic( string json )
	{
		var path = Path.Combine( root, ModSettings.FileName );
		File.WriteAllText( path, json );
		var diagnostics = new List<string>();
		Assert.AreSame( ModSettings.Default, ModSettings.Load( path, diagnostics ) );
		Assert.AreEqual( 1, diagnostics.Count );
		StringAssert.Contains( diagnostics[0], "default mods and extensions" );
	}

	[TestMethod]
	public void InvalidEntriesAreDroppedOneByOne()
	{
		var diagnostics = new List<string>();
		var settings = ModSettings.Parse( Encoding.UTF8.GetBytes( """
			{ "enabled": { "community.good": true, "../evil": true }, "order": ["community.good", "community.good", "Bad"],
			  "inputs": { "community.good": { "tpi": "", "other": "/x" } } }
			""" ), diagnostics );
		Assert.AreEqual( 4, diagnostics.Count, string.Join( "; ", diagnostics ) );
		Assert.IsTrue( settings.IsEnabled( "community.good", false ) );
		Assert.IsFalse( settings.Enabled.ContainsKey( "../evil" ) );
		CollectionAssert.AreEqual( new[] { "community.good" }, settings.Order.ToArray() );
		Assert.AreEqual( "/x", settings.Inputs["community.good"].Single().Value );
		var oversized = Path.Combine( root, "big.json" );
		File.WriteAllText( oversized, "{}" + new string( ' ', ModSettings.MaximumBytes ) );
		diagnostics.Clear();
		Assert.AreSame( ModSettings.Default, ModSettings.Load( oversized, diagnostics ) );
		StringAssert.Contains( diagnostics.Single(), "larger than" );
	}

	[TestMethod]
	public void StartupOptionsAreRepeatableAndUnknownDisabledIdsFail()
	{
		CollectionAssert.AreEqual( new[] { "a", "b" }, ModStartup.Values( new[] { "--mod-dir", "a", "--smoke-test", "--mod-dir", "b" }, "--mod-dir", "a folder" ).ToArray() );
		Assert.ThrowsException<ArgumentException>( () => ModStartup.Values( new[] { "--mod-dir" }, "--mod-dir", "a folder" ) );
		Assert.ThrowsException<ArgumentException>( () => ModStartup.Values( new[] { "--mod-dir", "--smoke-test" }, "--mod-dir", "a folder" ) );
		var diagnostics = new List<string>();
		Assert.ThrowsException<ArgumentException>( () => ModStartup.Resolve( Array.Empty<DiscoveredMod>(), ModSettings.Default, new[] { "opentpw.nothing" }, diagnostics ) );
		ModStartup.Resolve( Array.Empty<DiscoveredMod>(), Enable( "community.gone" ), Array.Empty<string>(), diagnostics );
		StringAssert.Contains( diagnostics.Single(), "community.gone" );
	}

	[TestMethod]
	public void AnUnsetInformationalVersionMeansADevelopmentBuild()
	{
		Assert.IsNull( ModStartup.OpenTpwVersion( typeof( ModTests ).Assembly ), "the test assembly carries the SDK's default 1.0.0" );
	}

	[TestMethod]
	public void TheSmokesIgnoreTheSavedModChoices()
	{
		Assert.IsTrue( ModStartup.IgnoresSavedChoices( new[] { "--smoke-test" } ) );
		// the co-op join smoke's two phases run the default extensions, whatever mods.json holds
		Assert.IsTrue( ModStartup.IgnoresSavedChoices( new[] { "--game-path", "x", "--coop-join-smoke", "join", "--coop-join-evidence", "y" } ) );
		Assert.IsFalse( ModStartup.IgnoresSavedChoices( new[] { "--game-path", "x", "--load-original-level", "jungle" } ) );
	}
}
