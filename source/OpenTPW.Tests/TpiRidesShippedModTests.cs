using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Keeps the shipped <c>content/mods/opentpw.tpi-rides/objects.json</c> in step with the retail Theme Park Inc install: it lists exactly the
/// archives of the three worlds that the Theme Park World parks can load (docs/MODS.md). Needs <c>OPENTPW_GAME_PATH</c> and
/// <c>OPENTPW_TPI_PATH</c>. With <c>OPENTPW_WRITE_TPI_RIDES=1</c> it rewrites the shipped file instead of comparing (existing archives keep
/// their ids; new ones take the next free id), so the tests and the shipped mod have one source.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TpiRidesShippedModTests
{
	/// <summary>Archives outside the five object folders that the first version of the mod carried (the TPI litter bin, whose script runs the Theme Park Inc only opcode WAITABS).</summary>
	private static readonly string[] Extra = { "levels/science/Facilities/litbin" };

	private string root = null!;
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize()
	{
		previousFileSystem = FileSystem;
		Log ??= new();
		root = Directory.CreateTempSubdirectory( "opentpw-tpi-shipped-" ).FullName;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		FileSystem = previousFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
		Directory.Delete( root, true );
	}

	[TestMethod]
	public void TheShippedObjectsHaveStableUniqueIdsInTheReservedRangeAndTheFixedFirstFour()
	{
		var items = TpiRidesFixture.Items;
		CollectionAssert.AreEqual( new[] { 40001, 40002, 40003, 40004 }, items.Take( 4 ).Select( item => item.InfoId ).ToArray() );
		CollectionAssert.AreEqual( new[] { "levels/arabian/rides/tbounce", "levels/science/rides/robot", "levels/science/Facilities/litbin", "levels/arabian/shops/drinks" }, items.Take( 4 ).Select( item => item.Archive ).ToArray() );
		Assert.AreEqual( items.Count, items.Select( item => item.InfoId ).Distinct().Count() );
		Assert.AreEqual( items.Count, items.Select( item => item.Archive.ToLowerInvariant() ).Distinct().Count() );
		Assert.IsTrue( items.All( item => item.English.Length is > 0 and <= ModObjectsFile.MaximumNameLength ) );
		Assert.IsTrue( items.Count > 100, "the shipped mod lists the install's archives, not just a few" );
	}

	/// <summary>
	/// The mod's identity in park saves is a hash of its files' bytes, so the shipped files are pinned: any change (a new archive, a renamed object, a
	/// different line ending after a checkout) fails here and must be a deliberate new version. Always on, without any install.
	/// </summary>
	private const string GoldenDigest = "e6f0a719c5d75cd23630dade1b4f9e5f92c3952eed1ade8cf11f97506bd06d4c";

	[TestMethod]
	public void TheShippedModHasLfLineEndingsAndAFixedContentDigest()
	{
		foreach ( var file in Directory.GetFiles( TpiRidesFixture.ShippedFolder ) )
			Assert.IsFalse( File.ReadAllBytes( file ).Contains( (byte)'\r' ), $"{Path.GetFileName( file )} has a CR: a Windows checkout would change the mod's digest" );
		if ( TpiRidesFixture.RepositoryFolder() is { } repository )
		{
			var attributes = File.ReadAllText( Path.Combine( repository, "..", "..", "..", ".gitattributes" ) ).Replace( "\r\n", "\n" );
			Assert.IsTrue( attributes.Contains( "content/mods/** text eol=lf" ), ".gitattributes pins LF for the shipped mods" );
		}
		var input = Directory.CreateDirectory( Path.Combine( root, "any", "Data" ) ).Parent!.FullName;
		var set = TpiRidesFixture.Resolve( TpiRidesFixture.Settings( input ) );
		Assert.AreEqual( 0, set.Refused.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		Assert.AreEqual( GoldenDigest, set.Mods.Single().ContentSha256, "the shipped mod's files changed: update the golden digest and the mod's version together" );
		// the same files with CRLF would be another mod
		var crlf = Directory.CreateDirectory( Path.Combine( root, "crlf", "content", "mods", TpiRidesFixture.Id ) );
		foreach ( var file in Directory.GetFiles( TpiRidesFixture.ShippedFolder ) )
			File.WriteAllText( Path.Combine( crlf.FullName, Path.GetFileName( file ) ), File.ReadAllText( file ).Replace( "\n", "\r\n" ) );
		Assert.AreNotEqual( GoldenDigest, TpiRidesFixture.Resolve( TpiRidesFixture.Settings( input ), Path.Combine( root, "crlf" ) ).Mods.Single().ContentSha256 );
	}

	[TestMethod]
	public void TheShippedObjectsListExactlyTheArchivesTheInstallLoads()
	{
		var tpi = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( tpi ) || !Directory.Exists( tpi ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var game = OriginalParkImportTests.OriginalDataPath();
		FileSystem = new BaseFileSystem( game );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var data = DataRoots.FindDataDirectory( tpi! )!;
		var install = new BaseFileSystem( data );
		install.RegisterArchiveHandler<WadArchive>( ".wad" );

		var shipped = TpiRidesFixture.Items;
		var candidates = TpiRidesFixture.ArchivesOf( data ).ToList();
		foreach ( var archive in Extra.Where( archive => candidates.All( item => !item.Archive.Equals( archive, StringComparison.OrdinalIgnoreCase ) ) ) )
		{
			var parts = archive.Split( '/' );
			var folder = Path.Combine( data, parts[0], parts[1], parts[2] );
			if ( Directory.Exists( folder ) && Directory.GetFiles( folder, parts[3] + ".wad" ).Length > 0 )
				candidates.Add( new TpiRidesFixture.Item( archive, TpiRidesFixture.WorldThemes[parts[1]], "features", 0, parts[^1] ) );
		}

		// every candidate with a distinct id, in the reserved range, through the real loader
		var numbered = candidates.Select( ( item, index ) => item with { InfoId = 40001 + index } ).ToList();
		var program = TpiRidesFixture.Write( Path.Combine( root, "program" ), numbered );
		var set = TpiRidesFixture.Resolve( TpiRidesFixture.Settings( tpi! ), program );
		Assert.AreEqual( 0, set.Refused.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		var loadable = new List<TpiRidesFixture.Item>();
		using ( ModContentRoots.Use( set, FileSystem ) )
		{
			foreach ( var theme in ObjectCatalog.Themes )
			{
				var catalog = ObjectCatalog.Load( theme );
				foreach ( var item in numbered.Where( item => item.Theme == theme ) )
					if ( catalog.Find( item.InfoId ) != null )
						loadable.Add( item );
					else
					{
						// the original keeps the first archive of a name (the game's own gates, bus, ...), and an archive whose .sam has no Info.Id makes no object
						var name = ObjectCatalog.ArchiveKey( Path.GetFileName( item.Archive ) );
						var gameArchives = FileSystem.GetDirectories( $"/levels/{theme}/{item.Category}" ).Select( archive => ObjectCatalog.ArchiveKey( Path.GetFileName( archive ) ) );
						Assert.IsTrue( gameArchives.Contains( name, StringComparer.OrdinalIgnoreCase ) || !HasInfoId( install, "/" + item.Archive ),
							$"{item.Archive} is not listed in {theme}, though the game has no archive of that name and it declares an Info.Id" );
					}
			}
		}
		Assert.IsTrue( loadable.Count > 100, $"{loadable.Count} archives load" );

		// the shipped list: the first four as the example had them, every other loadable archive after, ids never reused
		var next = shipped.Count == 0 ? 40001 : shipped.Max( item => item.InfoId ) + 1;
		var known = shipped.ToDictionary( item => item.Archive, StringComparer.OrdinalIgnoreCase );
		var merged = shipped.Where( item => loadable.Any( candidate => candidate.Archive.Equals( item.Archive, StringComparison.OrdinalIgnoreCase ) ) ).ToList();
		foreach ( var item in loadable.Where( item => !known.ContainsKey( item.Archive ) ) )
			merged.Add( item with { InfoId = next++ } );

		if ( Environment.GetEnvironmentVariable( "OPENTPW_WRITE_TPI_RIDES" ) == "1" )
		{
			var repository = TpiRidesFixture.RepositoryFolder() ?? throw new AssertFailedException( "The repository's content folder is not above the test binaries." );
			File.WriteAllText( Path.Combine( repository, "objects.json" ), TpiRidesFixture.ObjectsJson( merged ), new UTF8Encoding( false ) );
			Console.WriteLine( $"Wrote {merged.Count} objects to {repository}" );
			return;
		}
		CollectionAssert.AreEquivalent( merged.Select( item => item.Archive.ToLowerInvariant() ).ToArray(), shipped.Select( item => item.Archive.ToLowerInvariant() ).ToArray(),
			"the shipped objects.json differs from the archives the install loads; regenerate it with OPENTPW_WRITE_TPI_RIDES=1" );
	}

	/// <summary>Whether a .sam of the archive declares an Info.Id: Theme Park Inc's ptract holds none, so no object can be made of it (the catalogue says so and skips it).</summary>
	private static bool HasInfoId( BaseFileSystem install, string archive ) =>
		install.GetFiles( archive ).Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) )
			.Any( file => ObjectSettingsFile.Load( install, install.GetRelativePath( install.GetAbsolutePath( file ) ) ).Values.ContainsKey( "Info.Id" ) );
}
