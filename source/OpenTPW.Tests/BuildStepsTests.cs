using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// The park build's long steps split into smaller ones (docs/BROWSER-PERFORMANCE.md, "Build steps"): the economy's settings and catalogues, the
/// gate sign's composition, the ride script handlers and the advisor's message tables. Each split must make what the work made at once.
/// </summary>
[TestClass, DoNotParallelize]
public class BuildStepsTests
{
	[TestInitialize]
	public void Setup() => Log ??= new();

	[TestMethod]
	public void TheEconomyMadeInStepsIsTheEconomyMadeAtOnce()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		var original = FileSystem;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			foreach ( var kind in new[] { ParkStartKind.FullSimulation, ParkStartKind.InstantAction } )
			{
				var park = OriginalPark.Load( "jungle", readShippedSave: ParkStart.ReadsShippedSave( kind ) );
				var atOnce = ParkEconomyRuntime.ForOriginalLevel( park, kind );
				var steps = 0;
				ParkEconomyRuntime? stepped = null;
				foreach ( var created in ParkEconomyRuntime.ForOriginalLevelSteps( park, kind ) )
				{
					if ( created == null )
						steps++;
					else
						stepped = created;
				}
				Assert.IsNotNull( stepped );
				Assert.IsTrue( steps > 20, $"{kind}: the economy takes {steps} steps, one per object archive and more" );
				Assert.AreEqual( ParkSaveFile.Serialize( atOnce.Economy ), ParkSaveFile.Serialize( stepped!.Economy ), $"{kind}: the same economy" );
				// ForOriginalLevel runs the same steps, so the economy is also pinned to what origin/main made at once before the steps (6ecadcbd9; round 11 package A moves the FullSimulation one from D2F1349AA44D6545 to 542DB340B6DA71FC: the stored sprite draw (SpriteDraw, GUEST-020) is saved where it differs from the variant, and the headless seed's pool candidates hold the raw (R >> 2) draw (no banks loaded), where InstantAction's imported pool holds none that differs; Instant Action re-pinned at 6aac37d9f and again by the round 10 quick ports (E19E5729D4A8EE5A to 4DCABA51A693C7D7: the seed's staff now carry their saved sprite variant, GUEST-014), after main's advisor construction stamps changed its serialization)
				Assert.AreEqual( kind == ParkStartKind.FullSimulation ? "542DB340B6DA71FC" : "4DCABA51A693C7D7", Digest( ParkSaveFile.Serialize( stepped.Economy ) ), $"{kind}: the economy of origin/main" );
				Assert.AreEqual( atOnce.Start, stepped.Start );
				CollectionAssert.AreEqual( atOnce.Import?.Evidence.ToArray() ?? Array.Empty<string>(), stepped.Import?.Evidence.ToArray() ?? Array.Empty<string>() );
				Assert.AreEqual( atOnce.Economy.Catalog.Objects.Count, stepped.Economy.Catalog.Objects.Count );
			}
		}
		finally
		{
			FileSystem = original;
		}
	}

	[TestMethod]
	public void SignsComposedInStepsAndInterleavedAreTheSignsComposedAtOnce()
	{
		var data = StringTableTests.OriginalDataDirectory();
		using var stream = File.OpenRead( Path.Combine( data, "fonts.wad" ) );
		var fonts = SignFontLibrary.Load( stream );
		// the jungle gate and a sign of each other kind: their steps alternate, so no state may pass from one composition to another between steps
		var signs = SignFileTests.OriginalSignMembers( data ).Where( member => member.Path.Contains( "gates", StringComparison.OrdinalIgnoreCase ) ).Take( 2 )
			.Concat( SignFileTests.OriginalSignMembers( data ).Take( 2 ) ).Select( member => new SignFile( member.Data ) ).ToList();
		Assert.IsTrue( signs.Count >= 3 );
		var lines = new[] { "Theme Park", "Wereld" };
		var atOnce = signs.Select( sign => SignSurfaceComposer.Compose( sign, fonts, lines, new List<string>() ) ).ToList();
		var compositions = signs.Select( _ => new SignSurfaceComposer.Composition() ).ToList();
		var steps = signs.Select( ( sign, index ) => SignSurfaceComposer.ComposeSteps( sign, fonts, lines, new List<string>(), compositions[index] ).GetEnumerator() ).ToList();
		var counts = new int[signs.Count];
		for ( var more = true; more; )
		{
			more = false;
			for ( var index = 0; index < steps.Count; index++ )
				if ( steps[index].MoveNext() )
				{
					counts[index]++;
					more = true;
				}
		}
		for ( var index = 0; index < signs.Count; index++ )
		{
			CollectionAssert.AreEqual( atOnce[index].Left, compositions[index].Result.Left, $"sign {index}: left half" );
			CollectionAssert.AreEqual( atOnce[index].Right, compositions[index].Result.Right, $"sign {index}: right half" );
			Assert.AreEqual( atOnce[index].TextPixelCount, compositions[index].Result.TextPixelCount );
		}
		Assert.IsTrue( counts.Max() >= 8, $"a sign with text takes several steps ({string.Join( ", ", counts )})" );
		// Compose runs the same steps, so the gate signs are also pinned to what origin/main (6ecadcbd9, before the steps) composed with these lines
		var pins = new Dictionary<string, string> { ["fantasy"] = "500402231F5D37BF", ["hallow"] = "D0A8029C55F3647B", ["jungle"] = "6D50EB67C27DA267", ["space"] = "77610221247E4FC3" };
		foreach ( var (path, bytes) in SignFileTests.OriginalSignMembers( data ).Where( member => member.Path.Contains( "gates", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var theme = pins.Keys.Single( key => path.Contains( $"/{key}/", StringComparison.OrdinalIgnoreCase ) );
			var composition = new SignSurfaceComposer.Composition();
			foreach ( var _ in SignSurfaceComposer.ComposeSteps( new SignFile( bytes ), fonts, lines, new List<string>(), composition ) ) { }
			Assert.AreEqual( pins[theme], Digest( composition.Result.Left.Concat( composition.Result.Right ).ToArray() ), $"{path}: the pixels of origin/main" );
		}
	}

	private static string Digest( string text ) => Digest( Encoding.UTF8.GetBytes( text ) );
	private static string Digest( byte[] bytes ) => Convert.ToHexString( SHA256.HashData( bytes ) )[..16];

	[TestMethod]
	public void TheEconomyCatalogueReadsTheModsObjectCatalogueInItsOwnSteps()
	{
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "Original levels/Standard.sam is missing." );
		// the evening extension's torches as a content root: the branch the official extensions take in the game (they count as mods)
		var folder = Path.Combine( RepositoryFolder(), "content", "evening" );
		var original = FileSystem;
		var roots = ObjectCatalog.ContentRoots;
		ObjectCatalog.SetContentRoots( new[] { new CatalogContentRoot( ContentRoot.Mod( "opentpw.evening" ), folder, ModObjectsFile.Load( Path.Combine( folder, "objects.json" ), Array.Empty<string>() ).Objects ) } );
		try
		{
			// a fresh file system each: the object catalogue is cached per file system, so each read below loads it itself
			FileSystem = new BaseFileSystem( dataPath );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			var atOnce = EconomyObjectCatalog.Load( "jungle", easy: false );
			FileSystem = new BaseFileSystem( dataPath );
			FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
			var archives = 0;
			EconomyObjectCatalog? stepped = null;
			foreach ( var loaded in EconomyObjectCatalog.LoadSteps( "jungle", easy: false ) )
			{
				if ( loaded == null )
					archives++;
				else
					stepped = loaded;
			}
			static string Row( EconomyObjectInfo info ) => $"{info.InfoId} {info.Name} {info.Kind}";
			CollectionAssert.AreEqual( atOnce.Objects.Select( Row ).ToList(), stepped!.Objects.Select( Row ).ToList() );
			Assert.IsTrue( stepped.Objects.Any( info => info.InfoId == 63900 ), "the extension's torch is in the economy's catalogue" );
			Assert.IsTrue( archives > stepped.Objects.Count( info => info.InfoId != 63900 ), $"the object catalogue's archives are steps of their own too ({archives})" );
		}
		finally
		{
			ObjectCatalog.SetContentRoots( roots );
			FileSystem = original;
		}
	}

	private static string RepositoryFolder()
	{
		for ( var directory = new DirectoryInfo( AppContext.BaseDirectory ); directory != null; directory = directory.Parent )
			if ( Directory.Exists( Path.Combine( directory.FullName, "content", "evening" ) ) )
				return directory.FullName;
		throw new DirectoryNotFoundException( "The repository's content folder was not found." );
	}

	[TestMethod]
	public void TheOpcodeTableBuiltInStepsIsTheTableBuiltAtOnce()
	{
		// the table as it was built at once: every OpcodeHandler method, in the assembly's order
		var expected = typeof( RideOpcodes ).Assembly.GetTypes().SelectMany( type => type.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
			.Select( method => (Method: method, Attribute: method.GetCustomAttribute<OpcodeHandlerAttribute>()) ).Where( item => item.Attribute != null )
			.Select( item => (item.Attribute!.Opcode, item.Attribute.Status, item.Attribute.Evidence, Operands: item.Method.GetParameters().Length - 1) ).ToList();
		var builder = new RideOpcodes.TableBuilder();
		var steps = 0;
		while ( builder.Step() )
			steps++;
		Assert.IsTrue( steps > 5, $"the table takes {steps} steps" );
		Assert.IsFalse( builder.Step() );
		var built = builder.Result!.Values.Select( definition => (definition.Opcode, definition.Status, definition.Evidence, Operands: definition.OperandCount) ).ToList();
		CollectionAssert.AreEqual( expected, built );
		CollectionAssert.AreEqual( RideOpcodes.Definitions.Keys.ToList(), builder.Result.Keys.ToList(), "the shared table has the same order" );
		Assert.IsFalse( RideOpcodes.PrepareStep(), "a built table needs no step" );

		// a table prepared part of the way, then finished at once by its first use, is the same table in the same order
		var table = new RideOpcodes.Table();
		for ( var step = 0; step < steps / 2; step++ )
			Assert.IsTrue( table.PrepareStep() );
		CollectionAssert.AreEqual( expected.Select( item => item.Opcode ).ToList(), table.Definitions.Keys.ToList() );
		Assert.IsFalse( table.PrepareStep() );
	}

	[TestMethod]
	public void TheUnproducedMessagesAreTheDescriptorsNoMessageUses()
	{
		var produced = AdvisorTables.AllMessages.ToList();
		var expected = AdvisorDescriptorTable.Rows.Where( row => !produced.Any( message => message.MessageId == row.MessageId ) ).Select( row => row.MessageId ).ToList();
		CollectionAssert.AreEqual( expected, AdvisorTables.UnproducedMessages.Select( message => message.MessageId ).ToList() );
		Assert.AreEqual( 4, AdvisorTables.WarmSteps().Count() );
	}
}
