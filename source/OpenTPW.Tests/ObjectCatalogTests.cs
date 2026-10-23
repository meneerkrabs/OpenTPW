using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTPW.Tests;

[TestClass]
public class ObjectSettingsTests
{
	[TestMethod]
	public void ParsesQuotedNamesBlocksCommentsAndLayers()
	{
		var defaults = new ObjectSettingsFile( "Info.HasQueue\t1\t\tBoolean comment\r\nUpgrades[0].CostOfUpgrade\t1000\tcash cost\r\nUsageInfo.ExcitementLevel 30 // Ben's fault\r\n", "Rides.sam" );
		var main = new ObjectSettingsFile( "#\n#\tRide\n#\nInfo.Id\t\t\t1110\nInfo.Name\t\t\"Inca Totem\"\nInfo.Shape\n---\n*S*\n***\n*2*\n---\n\n\nInfo.Hoarding\n---\nF.7\nL.J\n---\nUpgrades[0].CostOfUpgrade\t\t3250\nUpgrades[1].InitCapacity 8\nInfo.PreviewAnimType\t\"m\"\t\tm = main loop\n", "Totem.sam" );
		Assert.AreEqual( "Inca Totem", main.Values["Info.Name"] );
		CollectionAssert.AreEqual( new[] { "*S*", "***", "*2*" }, main.Blocks["Info.Shape"].ToArray() );
		CollectionAssert.AreEqual( new[] { "F.7", "L.J" }, main.Blocks["Info.Hoarding"].ToArray() );
		Assert.AreEqual( "30", defaults.Values["UsageInfo.ExcitementLevel"] );
		var settings = new ObjectSettings( new[] { defaults, main } );
		Assert.AreEqual( 1110, settings.GetInt( "info.id" ) );
		Assert.AreEqual( 3250, settings.GetInt( "Upgrades[0].CostOfUpgrade" ) );
		Assert.IsTrue( settings.GetBool( "Info.HasQueue" ) );
		Assert.AreEqual( "m", settings["Info.PreviewAnimType"] );
		Assert.AreEqual( 7, settings.GetInt( "Missing", 7 ) );
		Assert.ThrowsException<InvalidDataException>( () => new ObjectSettingsFile( "Info.Shape\n---\n**\n" ) );
	}

	[TestMethod]
	public void BonusNameFilesAreUtf16Sections()
	{
		var text = "NAME\r\nSlither\r\n\r\nSIGNA\r\nSlither\r\n\r\nSIGNB\r\n\r\n";
		var bytes = new byte[] { 0xFF, 0xFE }.Concat( Encoding.Unicode.GetBytes( text ) ).ToArray();
		var sections = BonusNames.Parse( bytes );
		Assert.AreEqual( "Slither", sections["NAME"] );
		Assert.AreEqual( "Slither", sections["SIGNA"] );
		Assert.AreEqual( "", sections["SIGNB"] );
		Assert.AreEqual( 0, BonusNames.Parse( Array.Empty<byte>() ).Count );
		Assert.AreEqual( "Caf\u00e9", BonusNames.Parse( new byte[] { 0xFF, 0xFE }.Concat( Encoding.Unicode.GetBytes( "NAME\nCaf\u00e9\n" ) ).ToArray() )["NAME"] );
	}

	[TestMethod]
	public void ShapeRowsCountFromTheLastRowAndClassifySymbols()
	{
		var shape = new ObjectShape( new[] { "**", "*2" } );
		Assert.AreEqual( (2, 2), (shape.Width, shape.Height) );
		var entrance = shape.Entrances.Single();
		Assert.AreEqual( (1, 0), (entrance.U, entrance.V) );
		var coaster = new ObjectShape( new[] { "****", "<**>", "*2N*" } );
		Assert.AreEqual( ObjectCellKind.Exit, coaster.Exits.Single().Kind );
		Assert.AreEqual( (2, 0), (coaster.Exits.Single().U, coaster.Exits.Single().V) );
		Assert.AreEqual( 2, coaster.Cells.Count( cell => cell.Kind == ObjectCellKind.TrackConnection ) );
		Assert.AreEqual( 0, new ObjectShape( new[] { "..", ".." } ).OccupiedCells.Count() );
		Assert.ThrowsException<InvalidDataException>( () => new ObjectShape( new[] { "*x" } ) );
	}

	[TestMethod]
	public void FootprintRotationMatchesTheEasymodeRecords()
	{
		// Staff Room "**/*2" saved at (58, 16) rotation 90 covers x 58..59, y 15..16 with the entrance at (58, 15).
		var staff = new ObjectShape( new[] { "**", "*2" } );
		var cells = ObjectFootprint.GetCells( staff, 58, 16, 90 ).ToArray();
		CollectionAssert.AreEquivalent( new[] { (58, 15), (58, 16), (59, 15), (59, 16) }, cells.Select( cell => (cell.X, cell.Y) ).ToArray() );
		Assert.AreEqual( (58, 15), cells.Where( cell => cell.Cell.Kind == ObjectCellKind.Entrance ).Select( cell => (cell.X, cell.Y) ).Single() );
		var staffAccess = ObjectFootprint.GetAccessPoints( staff, 58, 16, 90 ).Single();
		Assert.AreEqual( (58, 15, 57, 15), (staffAccess.X, staffAccess.Y, staffAccess.OutsideX, staffAccess.OutsideY) );
		// Small Toilet "2" at rotation 270 opens towards +X (path at x + 1).
		var toilet = ObjectFootprint.GetAccessPoints( new ObjectShape( new[] { "2" } ), 55, 17, 270 ).Single();
		Assert.AreEqual( (56, 17), (toilet.OutsideX, toilet.OutsideY) );
		// Belly Bounce: entrance on the last text row joins the queue at -Y, exit "S" on the first row the path at +Y.
		var bouncy = ObjectFootprint.GetAccessPoints( new ObjectShape( new[] { "*S*", "***", "***", "*2*" } ), 51, 23, 0 ).ToArray();
		Assert.AreEqual( (52, 23, 52, 22), bouncy.Where( point => point.Kind == ObjectCellKind.Entrance ).Select( point => (point.X, point.Y, point.OutsideX, point.OutsideY) ).Single() );
		Assert.AreEqual( (52, 26, 52, 27), bouncy.Where( point => point.Kind == ObjectCellKind.Exit ).Select( point => (point.X, point.Y, point.OutsideX, point.OutsideY) ).Single() );
		// Drinks Shop "**/2*" at (43, 30) rotation 0: entrance (43, 30), reached from the path at (43, 29).
		var shop = new ObjectShape( new[] { "**", "2*" } );
		Assert.AreEqual( (43, 30), ObjectFootprint.GetCells( shop, 43, 30, 0 ).Single( cell => cell.Cell.Kind == ObjectCellKind.Entrance ) is var e ? (e.X, e.Y) : default );
		// Rotations of a non-square shape keep its cell count and stay rigid.
		var log = new ObjectShape( new[] { "***", "*2*" } );
		foreach ( var rotation in new[] { 0, 90, 180, 270 } )
		{
			var placed = ObjectFootprint.GetCells( log, 10, 10, rotation ).Select( cell => (cell.X, cell.Y) ).ToArray();
			Assert.AreEqual( 6, placed.Distinct().Count() );
			var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds( log, 10, 10, rotation );
			Assert.AreEqual( rotation % 180 == 0 ? (2, 1) : (1, 2), (maxX - minX, maxY - minY) );
			Assert.IsTrue( placed.All( cell => cell.X >= minX && cell.X <= maxX && cell.Y >= minY && cell.Y <= maxY ) );
			// Continuous mapping agrees with the cell mapping at cell centres.
			foreach ( var cell in log.OccupiedCells )
			{
				var centre = ObjectFootprint.ToGrid( 10, 10, rotation, new System.Numerics.Vector2( cell.U + 0.5f, cell.V + 0.5f ) );
				Assert.AreEqual( ObjectFootprint.ToGrid( 10, 10, rotation, cell.U, cell.V ), ((int)MathF.Floor( centre.X ), (int)MathF.Floor( centre.Y )) );
			}
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ObjectFootprint.ToGrid( 0, 0, 45, 0, 0 ) );
	}

	[TestMethod]
	public void AnimationNumbersSelectSuffixLettersAndVariants()
	{
		var members = new[] { "/a/totem.MD2", "/a/totemc.MD2", "/a/totemm1.MD2", "/a/totemm2.MD2", "/a/totemm3.MD2", "/a/totemm4.MD2", "/a/totemm5.MD2", "/a/totemm6.MD2", "/a/totemm7.MD2", "/a/totemm8.MD2", "/a/totemm9.MD2", "/a/totemm10.MD2", "/a/totemb1.MD2", "/a/totemr.MD2", "/a/Ptotem.MD2", "/a/totemx.MD2" };
		var animations = ObjectAnimations.Find( "totem", members );
		Assert.AreEqual( 13, animations.Count );
		Assert.AreEqual( "/a/totemm1.MD2", ObjectAnimations.Resolve( animations, 5, 0 )!.Path );
		Assert.AreEqual( "/a/totemm10.MD2", ObjectAnimations.Resolve( animations, 5, 9 )!.Path );
		Assert.AreEqual( "/a/totemc.MD2", ObjectAnimations.Resolve( animations, 0, 0 )!.Path );
		Assert.AreEqual( "/a/totemb1.MD2", ObjectAnimations.Resolve( animations, 9, 0 )!.Path );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 0, 1 ) );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 1, 0 ) );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 2, 0 ) );
		foreach ( var value in Enum.GetValues<ScriptDefs.Animations>() )
			Assert.AreEqual( (int)value, ObjectAnimations.GetAnimation( ObjectAnimations.GetLetter( (int)value )!.Value ) );
	}

	/// <summary>RIDES-008 (PC 0x6fe6bc, loader 0x461f10): twelve letters C D I L S M E U W B R O by AnimID; numbered members load until the first gap; the unnumbered one only without number 1.</summary>
	[TestMethod]
	public void AnimationLettersFollowTheOriginalTableAndStopAtGaps()
	{
		Assert.AreEqual( "cdilsmeuwbro", new string( Enumerable.Range( 0, 12 ).Select( id => ObjectAnimations.GetLetter( id )!.Value ).ToArray() ) );
		Assert.IsNull( ObjectAnimations.GetLetter( 12 ) );
		Assert.IsNull( ObjectAnimations.GetLetter( -1 ) );
		Assert.AreEqual( 1, ObjectAnimations.GetAnimation( 'd' ) );
		Assert.AreEqual( 8, ObjectAnimations.GetAnimation( 'W' ) );
		var members = new[] { "/a/xm1.MD2", "/a/xm2.MD2", "/a/xm4.MD2", "/a/xm.MD2", "/a/xd.MD2", "/a/xw2.MD2", "/a/xs.MD2", "/a/xs2.MD2" };
		var animations = ObjectAnimations.Find( "x", members );
		Assert.AreEqual( "/a/xm1.MD2", ObjectAnimations.Resolve( animations, 5, 0 )!.Path );
		Assert.AreEqual( "/a/xm2.MD2", ObjectAnimations.Resolve( animations, 5, 1 )!.Path );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 5, 2 ), "m3 is missing" );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 5, 3 ), "m4 lies behind the gap" );
		Assert.AreEqual( "/a/xd.MD2", ObjectAnimations.Resolve( animations, 1, 0 )!.Path );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 8, 0 ), "w1 is missing, so only the unnumbered w would load" );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 8, 1 ), "w2 is behind the gap" );
		Assert.AreEqual( "/a/xs.MD2", ObjectAnimations.Resolve( animations, 4, 0 )!.Path, "no s1: the unnumbered clip plays" );
		Assert.IsNull( ObjectAnimations.Resolve( animations, 4, 1 ), "s2 is behind the missing s1" );
	}
}

/// <summary>Catalog cache behaviour on a synthetic data root (no game data needed).</summary>
[TestClass]
[DoNotParallelize]
public class ObjectCatalogCacheTests
{
	[TestMethod]
	public void ReplacingTheFileSystemWithTheSameRootLoadsEntriesFromTheNewInstance()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-catalog-" + Guid.NewGuid().ToString( "N" ) );
		var archive = Path.Combine( root, "levels", "jungle", "rides", "totem" );
		Directory.CreateDirectory( Path.Combine( archive, "textures" ) );
		Directory.CreateDirectory( Path.Combine( root, "levels", "jungle", "sharetex" ) );
		File.WriteAllText( Path.Combine( archive, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\nUpgrades[0].CostOfUpgrade\t3250\n" );
		File.WriteAllBytes( Path.Combine( archive, "totem.MD2" ), Array.Empty<byte>() );
		var previous = FileSystem;
		try
		{
			ObjectCatalog.BonusDataRoot = null;
			foreach ( var pass in new[] { "first", "replaced" } )
			{
				// Tests and setup changes install a new instance with the same root; entries must follow it.
				FileSystem = new BaseFileSystem( root );
				var entry = ObjectCatalog.Load( "jungle" ).Get( 1110 );
				Assert.AreSame( FileSystem, entry.FileSystem, $"{pass} file system" );
				var item = new OriginalBuildCatalog( ObjectCatalog.Load( "jungle" ) ).GetItems( BuildCategory.Rides ).Single();
				CollectionAssert.Contains( item.TextureDirectories.ToArray(), "/levels/jungle/rides/totem/textures", $"{pass} file system" );
				CollectionAssert.Contains( item.TextureDirectories.ToArray(), "/levels/jungle/sharetex", $"{pass} file system" );
			}
		}
		finally
		{
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = null;
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void ArchiveKeyFollowsTheOriginalsArchiveNaming()
	{
		// tp.exe 0x0041EC30: strip .wad (0x00424D30); exactly two underscores drop the second and what follows, then a leading 'p' (0x0041EB90).
		Assert.AreEqual( "_snake", ObjectCatalog.ArchiveKey( "_snake_3" ) );
		Assert.AreEqual( "_snake", ObjectCatalog.ArchiveKey( "_snake_3.WAD" ) );
		Assert.AreEqual( "_snake", ObjectCatalog.ArchiveKey( "p_snake_12" ) );
		Assert.AreEqual( "P_snake", ObjectCatalog.ArchiveKey( "P_snake_12" ), "the p test is case-sensitive" );
		Assert.AreEqual( "a_b", ObjectCatalog.ArchiveKey( "pa_b_x" ), "the suffix need not be a number" );
		Assert.AreEqual( "cat_co", ObjectCatalog.ArchiveKey( "cat_co" ), "one underscore keeps the name" );
		Assert.AreEqual( "a_b_c_d", ObjectCatalog.ArchiveKey( "a_b_c_d" ), "three underscores keep the name" );
		Assert.AreEqual( "pumpkin", ObjectCatalog.ArchiveKey( "pumpkin" ) );
	}

	[TestMethod]
	public void BonusArchivesLoadOncePerNameAndKeepTakenIdsBehindTheFirst()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-catalog-" + Guid.NewGuid().ToString( "N" ) );
		var game = Path.Combine( root, "game" );
		var bonus = Path.Combine( root, "bonus" );
		void Archive( string data, string archive, string member, int id, string category = "rides" )
		{
			var directory = Path.Combine( data, "levels", "jungle", category, archive );
			Directory.CreateDirectory( directory );
			File.WriteAllText( Path.Combine( directory, member + ".sam" ), $"Info.Id\t{id}\nInfo.Name\t\"{archive}\"\nInfo.WhichUIType\t0\n" );
			File.WriteAllBytes( Path.Combine( directory, member + ".MD2" ), Array.Empty<byte>() );
		}
		Archive( game, "totem", "totem", 1110 );
		Archive( game, "zebra", "zebra", 1111 );
		Archive( bonus, "_totem_1", "_totem", 1110 );
		Archive( bonus, "_totem_2", "_totem", 1300 );
		Archive( bonus, "p_snake_3", "_snake", 1301 );
		Archive( game, "totem", "totem", 1600, "features" );
		Archive( game, "cafe", "cafe", 1500, "shops" );
		Archive( bonus, "_cafe_9", "_cafe", 1500, "features" );
		var previous = FileSystem;
		try
		{
			FileSystem = new BaseFileSystem( game );
			ObjectCatalog.BonusDataRoot = bonus;
			var catalog = ObjectCatalog.Load( "jungle" );
			// One rides directory in name order (ordinal, upper-cased: P < T < Z < _); _totem_2 has the name _totem_1 already holds.
			CollectionAssert.AreEqual( new[] { "p_snake_3", "totem", "zebra", "_totem_1" }, catalog.Entries.Where( entry => entry.Category == ObjectCategory.Ride ).Select( entry => entry.ArchiveName ).ToArray() );
			Assert.AreEqual( "totem", catalog.Get( 1600 ).ArchiveName, "each category keeps its own archive names" );
			Assert.AreEqual( ObjectCategory.Feature, catalog.Get( 1500 ).Category, "ids resolve in the original's category order: features before shops" );
			Assert.IsNull( catalog.Find( 1300 ), "the second _totem archive is not read" );
			Assert.AreEqual( "totem", catalog.Get( 1110 ).ArchiveName, "a taken Info.Id resolves to the first definition" );
			Assert.AreEqual( 2, catalog.Entries.Count( entry => entry.InfoId == 1110 ), "the bonus definition with a taken id stays loaded" );
			Assert.AreEqual( 3, catalog.Get( 1301 ).BonusNumber );
			Assert.AreEqual( 1, catalog.Entries.Single( entry => entry.ArchiveName == "_totem_1" ).BonusNumber );
			Assert.IsNull( catalog.Get( 1111 ).BonusNumber );
		}
		finally
		{
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = null;
			Directory.Delete( root, true );
		}
	}
}

/// <summary>Catalog of the original objects; inconclusive without OPENTPW_GAME_PATH.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectCatalogCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	internal static BaseFileSystem? UseOriginalData( out BaseFileSystem previous )
	{
		previous = FileSystem;
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			return null;
		var dataPath = OriginalParkImportTests.OriginalDataPath();
		if ( !Directory.Exists( Path.Combine( dataPath, "levels", "jungle", "rides" ) ) )
			return null;
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		return FileSystem;
	}

	[TestInitialize]
	public void Initialize()
	{
		if ( UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the object catalog corpus." );
		originalFileSystem = previous;
		initialized = true;
		// The pinned counts are for the game data alone; bonus content is tested separately.
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	/// <summary>
	/// WAITANIM converts <c>ms − 300</c> unsigned (0xafc88), so a member returning under 300 ms (at most 8 ticks)
	/// would make the original script wait INT32_MAX ms. This scans every WAITANIM/WAITANIM_CH of every object script
	/// with literal operands for such a member, counts the requests whose operands are variables, and reports both
	/// (ANIM-plan §8, "Degenerate case").
	/// </summary>
	[TestMethod]
	public void WaitAnimShortClipWrapIsReportedForTheCorpus()
	{
		int requests = 0, literal = 0, variable = 0, missing = 0, shortClips = 0;
		var wraps = new List<string>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				foreach ( var file in entry.Animations )
				{
					var clip = ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip;
					if ( clip != null && ObjectAnimator.Fctiwz( 1000f * (clip.Duration - clip.FirstTick) / ObjectAnimator.DefaultTicksPerSecond ) < RideAnimationTiming.MinimumWaitMilliseconds )
						shortClips++;
				}
				foreach ( var member in entry.Scripts )
				{
					RideScriptFile script;
					using ( var stream = entry.FileSystem.OpenRead( member ) )
						script = new RideScriptFile( stream );
					foreach ( var instruction in script.Instructions.Where( instruction => instruction.Opcode is (ushort)Opcode.WAITANIM or (ushort)Opcode.WAITANIM_CH ) )
					{
						requests++;
						if ( instruction.Operands.Take( 2 ).Any( operand => operand.Kind != RideScriptOperandKind.Literal ) )
						{
							variable++;
							continue;
						}
						literal++;
						var animation = (short)instruction.Operands[0].Value;
						var variant = (short)instruction.Operands[1].Value;
						var resolved = entry.ResolveAnimation( animation, variant );
						var clip = resolved == null ? null : ObjectAssets.LoadModel( entry.FileSystem, resolved.Path ).Clip;
						if ( clip == null )
						{
							missing++;
							continue;
						}
						var milliseconds = ObjectAnimator.Fctiwz( 1000f * (clip.Duration - clip.FirstTick) / ObjectAnimator.DefaultTicksPerSecond );
						if ( RideAnimationTiming.WaitAnimDelay( milliseconds, RideVM.DefaultScriptSpeed ) == int.MaxValue )
							wraps.Add( $"{member} word {instruction.WordOffset}: {(Opcode)instruction.Opcode} {animation} {variant} → {resolved!.Path} ({clip.Duration} ticks, {milliseconds} ms)" );
					}
				}
			}
		}
		Assert.IsTrue( requests > 0 && literal > 0 );
		Console.WriteLine( $"WAITANIM requests {requests}: {literal} literal ({missing} without a member, {wraps.Count} wrapping), {variable} with variable operands; object clips under 300 ms: {shortClips}" );
		foreach ( var wrap in wraps )
			Console.WriteLine( wrap );
	}

	[TestMethod]
	public void EveryObjectScriptRunsInItsParkWithOriginalAnimations()
	{
		const int seconds = 60;
		var report = new StringBuilder();
		int objects = 0, animating = 0, played = 0;
		var unresolved = new SortedSet<string>( StringComparer.Ordinal );
		foreach ( var theme in ObjectCatalog.Themes )
		{
			// Every scripted object of a theme in one shared script world, like objects in one park.
			var world = new RideScriptWorld();
			var runtimes = ObjectCatalog.Load( theme ).Entries.Where( entry => entry.ScriptPath != null )
				.Select( ( entry, index ) => new OriginalObjectRuntime( entry, world, seed: index + 1, open: true ) ).ToArray();
			for ( var tick = 0; tick < seconds * FixedStepClock.TicksPerSecond; tick++ )
			{
				foreach ( var runtime in runtimes )
					runtime.Simulate( FixedStepClock.TickDuration );
			}
			foreach ( var runtime in runtimes )
			{
				objects++;
				Assert.AreNotEqual( RideVMState.Faulted, runtime.Script!.State, $"{runtime.Entry}: {runtime.Script.FaultMessage}" );
				if ( runtime.AnimationsPlayed > 0 )
					animating++;
				played += runtime.AnimationsPlayed;
				foreach ( var request in runtime.UnresolvedAnimations )
					unresolved.Add( $"{runtime.Entry.ArchivePath} {request}" );
				report.AppendLine( $"{runtime.Entry} {runtime.Script.State} anims {runtime.AnimationsPlayed} channels {runtime.Animator.ActiveChannels} unresolved [{string.Join( " ", runtime.UnresolvedAnimations )}] cycles {runtime.CompletedCycles}" );
			}
			foreach ( var runtime in runtimes )
				runtime.Stop();
		}
		Assert.AreEqual( (262, 258), (objects, animating) );
		// Members these shops/features request but their archives do not ship.
		CollectionAssert.AreEqual( new[] { "/levels/fantasy/features/royaloo 5/0", "/levels/fantasy/shops/fries 5/0", "/levels/fantasy/shops/icecream 5/0", "/levels/fantasy/shops/purse 2/0", "/levels/fantasy/shops/purse 5/0", "/levels/space/features/crys_b 5/0" }, unresolved.ToArray() );
		report.Insert( 0, $"{objects} scripted objects, {animating} played original animations ({played} plays), {unresolved.Count} unresolved requests: {string.Join( ", ", unresolved )}\n" );
		var output = Environment.GetEnvironmentVariable( "OPENTPW_OBJECT_REPORT_OUT" );
		if ( !string.IsNullOrEmpty( output ) )
			File.WriteAllText( output + ".scripts", report.ToString() );
	}

	[TestMethod]
	public void CatalogCountsPerThemeArePinned()
	{
		// Named = objects with an OBJECT_NAMES binding: all but the three tools of each theme (RIDES-010: the Info.Id table binds the gates, signs, lights, loudspeakers and more).
		var expected = new Dictionary<string, (int Entries, int Buildable, int Rides, int Shops, int Sideshows, int Features, int Upgrades, int Named, int Scripts)>
		{
			["jungle"] = (70, 58, 18, 8, 5, 36, 3, 67, 67),
			["hallow"] = (70, 58, 20, 8, 4, 35, 3, 67, 67),
			["space"] = (68, 56, 21, 8, 4, 32, 3, 65, 65),
			["fantasy"] = (66, 55, 17, 8, 4, 35, 2, 63, 63)
		};
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			int Count( ObjectCategory category ) => catalog.Entries.Count( entry => entry.Category == category );
			Assert.AreEqual( expected[theme], (catalog.Entries.Count, catalog.Buildable.Count(), Count( ObjectCategory.Ride ), Count( ObjectCategory.Shop ), Count( ObjectCategory.Sideshow ),
				Count( ObjectCategory.Feature ), Count( ObjectCategory.Upgrade ), catalog.Entries.Count( entry => entry.ObjectNameIndex != null ), catalog.Entries.Count( entry => entry.ScriptPath != null )), theme );
			// Six fixed items per theme (bus, gates, seaplane, lights, ferry, end); three tools (mystery, buy/clear land).
			Assert.AreEqual( 6, catalog.Entries.Count( entry => entry.IsFixedItem ), theme );
			Assert.AreEqual( 3, catalog.Entries.Count( entry => entry.IsTool ), theme );
			Assert.IsTrue( catalog.Buildable.All( entry => entry.WhichUIType is >= 0 and <= 3 && entry.Shape.OccupiedCells.Any() ), theme );
		}
		var jungle = ObjectCatalog.Load( "jungle" );
		var totem = jungle.Get( 1110 );
		Assert.AreEqual( ("Inca Totem", 28, 2, "Inca Totem"), (totem.SettingsName, totem.ObjectNameIndex, totem.ObjectNameLength, totem.DisplayName) );
		Assert.AreEqual( (3, 4, 3250, 6, 12, 4), (totem.Shape.Width, totem.Shape.Height, totem.BuildCost, totem.InitialCapacity, totem.RideTypeIndex, totem.NumSimultaneousAnimations) );
		Assert.AreEqual( "/levels/jungle/rides/totem/totemm1.MD2", totem.ResolveAnimation( 5, 0 )!.Path );
		Assert.AreEqual( "/levels/jungle/rides/totem/Ptotemm.MD2", totem.PreviewAnimationPath );
		Assert.AreEqual( "/levels/jungle/rides/totem/Totem.RSE", totem.ScriptPath );
		// Category defaults apply under the object's own values (Rides.sam HasQueue, Totem.sam capacity).
		Assert.IsTrue( totem.HasQueue && totem.IsChoosable && totem.WhichUIType == 0 );
		Assert.AreEqual( ("The Hot Pot", 34, 2), (jungle.Get( 1140 ).DisplayName, jungle.Get( 1140 ).ObjectNameIndex, jungle.Get( 1140 ).ObjectNameLength) );
		var drinks = jungle.Get( 1203 ).Economy;
		Assert.AreEqual( (650, 30, 20), (drinks.BuildCost, drinks.PricePerUse, drinks.CostOfGoods) );
		Assert.AreEqual( ObjectCategory.Shop, jungle.Get( 1203 ).Category );
		Assert.IsTrue( jungle.Get( 1601 ).IsFixedItem && !jungle.Get( 1601 ).IsBuildable );
		Assert.ThrowsException<KeyNotFoundException>( () => jungle.Get( 17302 ) );
	}

	/// <summary>RIDES-003 (PC 0x473f32): instance flag 0x10 is set exactly when no clip exists for AnimIDs 1 to 11.</summary>
	[TestMethod]
	public void OnlyBaseClipsSetsTheHoldFlag()
	{
		ObjectAnimationFile[] Clips( params string[] names ) => ObjectAnimations.Find( "x", names.Select( name => $"/a/{name}.MD2" ) ).ToArray();
		Assert.IsTrue( ObjectAnimations.HasOnlyBaseClips( Clips() ), "no clips at all" );
		Assert.IsTrue( ObjectAnimations.HasOnlyBaseClips( Clips( "xc" ) ) );
		Assert.IsTrue( ObjectAnimations.HasOnlyBaseClips( Clips( "xc1", "xc2" ) ) );
		Assert.IsFalse( ObjectAnimations.HasOnlyBaseClips( Clips( "xc", "xm1" ) ) );
		Assert.IsFalse( ObjectAnimations.HasOnlyBaseClips( Clips( "xo" ) ), "O is AnimID 11" );
		Assert.IsFalse( ObjectAnimations.HasOnlyBaseClips( Clips( "xd" ) ) );
		Assert.IsTrue( ObjectAnimations.HasOnlyBaseClips( Clips( "xm2" ) ), "m2 lies behind the missing m1, so no clip loads for AnimID 5" );
	}

	/// <summary>The names a type's first use reads ahead (<see cref="ObjectRenderParts.TextureNames"/>, a co-op join's type steps) are its parts' names, over every entry of every theme.</summary>
	[TestMethod]
	public void EveryEntrysTextureNamesAreTheNamesItsPartsDraw()
	{
		var entries = 0;
		foreach ( var theme in ObjectCatalog.Themes )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				entries++;
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				var drawn = ObjectRenderParts.Build( entry, model ).SelectMany( part => part.Textures ).ToHashSet( StringComparer.OrdinalIgnoreCase );
				var names = ObjectRenderParts.TextureNames( model );
				Assert.AreEqual( names.Count, names.Distinct( StringComparer.OrdinalIgnoreCase ).Count(), $"{entry}: each name once" );
				Assert.IsTrue( drawn.SetEquals( names ), $"{entry}: {string.Join( ", ", names )} / {string.Join( ", ", drawn )}" );
			}
		Assert.IsTrue( entries > 250, $"every theme's entries ({entries})" );
	}

	[TestMethod]
	public void EveryEntryResolvesItsModelAnimationsAndTextures()
	{
		var missingTextures = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
		int entries = 0, slots = 0, animations = 0, split = 0, holders = 0, withoutClips = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				entries++;
				var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
				Assert.AreEqual( ModelFileKind.Geometry, model.Kind, entry.ToString() );
				Assert.IsTrue( model.Meshes.Count > 0, entry.ToString() );
				var parts = ObjectRenderParts.Build( entry, model );
				Assert.IsTrue( parts.Count > 0 && parts.All( part => part.Textures.Length is > 0 and <= 16 && part.Indices.All( index => index < part.Vertices.Length ) ), entry.ToString() );
				foreach ( var mesh in model.Meshes )
				{
					Assert.IsTrue( ObjectAssets.GetUnrenderableReason( mesh ) is null or "more than 16 texture slots", $"{entry.ModelPath}:{mesh.Name}" );
					if ( mesh.Materials.Length > 16 )
						split++;
					foreach ( var material in mesh.Materials )
					{
						slots++;
						if ( ObjectAssets.ResolveTexturePath( entry, material.Name ) == null )
							missingTextures.Add( $"{entry.ArchivePath}:{material.Name}" );
					}
				}
				if ( ObjectAnimations.HasOnlyBaseClips( entry.Animations ) )
				{
					holders++;
					if ( entry.Animations.Count == 0 )
						withoutClips++;
				}
				foreach ( var animation in entry.Animations )
				{
					var clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path );
					Assert.AreEqual( ModelFileKind.Animation, clip.Kind, animation.Path );
					// Every clip binds to the main model's nodes.
					new ObjectAnimator( model ).Play( 0, clip.Clip!, animation.Name, false );
					animations++;
				}
				if ( entry.PreviewModelPath != null )
					Assert.AreEqual( ModelFileKind.Geometry, ObjectAssets.LoadModel( entry.FileSystem, entry.PreviewModelPath ).Kind, entry.PreviewModelPath );
			}
		}
		Assert.AreEqual( (274, 911, 31), (entries, animations, split) );
		// RIDES-003: the objects whose only clip is the construction clip C (or that have none) take the hold route at a clip end.
		Assert.AreEqual( (88, 8), (holders, withoutClips) );
		Assert.AreEqual( 5629, slots );
		// The ogre upgrade has an unnamed slot and the space rocket a slot whose texture is not shipped.
		CollectionAssert.AreEqual( new[] { "/levels/hallow/upgrades/ogre:", "/levels/space/rides/rocket:s_leg3" }, missingTextures.ToArray() );
	}

	[TestMethod]
	public void EasymodeObjectsMatchTheirCatalogShapesAndAccessCells()
	{
		var park = OriginalPark.Load( "jungle" );
		var save = park.Save!;
		var catalog = ObjectCatalog.Load( "jungle" );
		foreach ( var item in save.PlacedObjects )
		{
			var entry = catalog.Get( item.Record.InfoId );
			// The saved size equals the shape size; the catalog footprint equals the verified saved footprint.
			Assert.AreEqual( (entry.Shape.Width, entry.Shape.Height), (item.Record.Width, item.Record.Height), entry.ToString() );
			var cells = ObjectFootprint.GetCells( entry.Shape, item.Record.X, item.Record.Y, item.Record.Rotation ).Select( cell => (cell.X, cell.Y) ).OrderBy( cell => cell ).ToArray();
			var expected = Enumerable.Range( item.MinX, item.MaxX - item.MinX + 1 ).SelectMany( x => Enumerable.Range( item.MinY, item.MaxY - item.MinY + 1 ).Select( y => (x, y) ) ).OrderBy( cell => cell ).ToArray();
			CollectionAssert.AreEqual( expected, cells, entry.ToString() );
			// Every entrance/exit opens onto a path cell or an occupied non-object cell (the Belly Bounce queue).
			foreach ( var point in ObjectFootprint.GetAccessPoints( entry.Shape, item.Record.X, item.Record.Y, item.Record.Rotation ) )
			{
				var outside = save.Cells[point.OutsideX, point.OutsideY];
				Assert.IsTrue( outside.IsPath || (outside.IsOccupied && !save.IsOccupiedByObject( point.OutsideX, point.OutsideY )), $"{entry} {point}" );
			}
		}
		Assert.AreEqual( 9, save.PlacedObjects.Sum( item => ObjectFootprint.GetAccessPoints( catalog.Get( item.Record.InfoId ).Shape, item.Record.X, item.Record.Y, item.Record.Rotation ).Count() ) );
		Assert.IsTrue( save.FixedItems.All( item => catalog.Get( item.InfoId ).IsFixedItem ) );
	}

	[TestMethod]
	public void OriginalGridAppliesMapSaveAndShapeRules()
	{
		var park = OriginalPark.Load( "jungle" );
		var grid = new OriginalParkGrid( park );
		var catalog = ObjectCatalog.Load( "jungle" );
		var objects = new ParkObjects( catalog, grid );
		var totem = catalog.Get( 1110 );
		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( totem, 20, 60, 0 ) );
		var bin = catalog.Get( 1406 );
		Assert.AreEqual( OriginalPlacementResult.Water, objects.Check( bin, 51, 50, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, objects.Check( bin, 47, 17, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Path, objects.Check( totem, 38, 20, 0 ) );
		// The Belly Bounce queue stays blocked; the imported Belly Bounce cells are free until its object is placed.
		Assert.AreEqual( OriginalPlacementResult.Occupied, grid.CheckTerrain( 50, 22 ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, grid.CheckTerrain( 52, 25 ) );
		Assert.AreEqual( OriginalPlacementResult.OutsideTerrain, objects.Check( totem, 95, 84, 0 ) );
		Assert.AreEqual( OriginalPlacementResult.Blocked, objects.Check( catalog.Get( 100 ), 20, 60, 0 ), "the Mystery ride is a tool" );
		// Blocked ground with a surface is not refused by its MAP bit (tp.exe's validator 0x535670 reads the cell type, 0 there): only the land decides
		var blocked = Enumerable.Range( 0, park.Heightfield.CellCountX * park.Heightfield.CellCountZ ).Select( index => (X: index % park.Heightfield.CellCountX, Y: index / park.Heightfield.CellCountX) )
			.First( cell => cell.X >= 2 && cell.Y >= 2 && park.Map.GetFlagsAt( cell.X, cell.Y ) == MapCellFlags.Blocked && !park.Heightfield.IsHole( cell.X, cell.Y ) );
		Assert.AreEqual( OriginalPlacementResult.Allowed, objects.Check( bin, blocked.X, blocked.Y, 0 ) );
		var settings = GuestSettings.Load( "jungle" );
		var land = ParkCellMap.FromOriginal( park.Map, null, ParkLandSetup.FromLevel( settings.EntranceA, settings.EntranceB, FileSystem.ReadAllText( "/levels/jungle/Hoardings.sam" ) ), park.Heightfield );
		Assert.IsTrue( land.FlagsAt( blocked.X, blocked.Y ).HasFlag( ParkCellFlags.Unowned ), "Jungle's starting land holds no Blocked ground" );
		objects.IsUnowned = ( x, y ) => land.FlagsAt( x, y ).HasFlag( ParkCellFlags.Unowned );
		Assert.AreEqual( OriginalPlacementResult.NotOwned, objects.Check( bin, blocked.X, blocked.Y, 0 ) );
		objects.IsUnowned = null;
		objects.IsReserved = ( x, y ) => x == 21 && y == 61;
		Assert.AreEqual( OriginalPlacementResult.Occupied, objects.Check( totem, 20, 60, 0 ) );
		// Placement matrices agree with the terrain mapping: a rotation-0 model point lands on its cell.
		var placement = new ObjectPlacement( 20, 60, 0, grid.Origin, 0 );
		var engine = System.Numerics.Vector3.Transform( new System.Numerics.Vector3( 5, 5, 0 ), placement.ModelToEngine );
		Assert.IsTrue( OriginalParkPlacement.TryGetCell( park.Heightfield, engine.X, engine.Y, out var cellX, out var cellY ) );
		Assert.AreEqual( (20, 60), (cellX, cellY) );
		Assert.AreEqual( OriginalParkPlacement.ToEngine( park.Heightfield, new System.Numerics.Vector3( 205, 0, 605 ) ).X, engine.X, 1e-4 );
	}

	/// <summary>
	/// The overwrite priorities and access cells the build rules read (docs/OBJECTS.md "Build rules"): rides, shops, sideshows, upgrades,
	/// toilets, staff rooms and the fixed items have priority 5, the small scenery 1, the other features 0; every type with a queue has one
	/// entrance and one exit, every other type at most one entrance and no exit (so tp.exe's exit test, which only queue types reach, always has a cell).
	/// </summary>
	[TestMethod]
	public void OverwritePrioritiesAndAccessCellsFollowTheTypes()
	{
		var counts = new Dictionary<int, int>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				counts[entry.OverwritePriority] = counts.GetValueOrDefault( entry.OverwritePriority ) + 1;
				if ( entry.Category is not ObjectCategory.Feature || entry.IsFixedItem )
					Assert.AreEqual( 5, entry.OverwritePriority, entry.ToString() );
				var entrances = entry.Shape.Cells.Count( cell => cell.Kind == ObjectCellKind.Entrance );
				var exits = entry.Shape.Cells.Count( cell => cell.Kind == ObjectCellKind.Exit );
				Assert.AreEqual( entry.HasQueue ? (1, 1) : (Math.Min( entrances, 1 ), 0), (entrances, exits), entry.ToString() );
			}
		}
		Assert.AreEqual( 3, counts.Count, "priorities 0, 1 and 5 only" );
		Assert.AreEqual( (48, 45), (counts[0], counts[1]) );
	}

	/// <summary>Official bonus content (private: OPENTPW_BONUS_DATA, inconclusive without it).</summary>
	[TestMethod]
	public void BonusContentMergesIntoTheCatalogsAndRuns()
	{
		var root = Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" );
		if ( string.IsNullOrWhiteSpace( root ) || ObjectCatalog.FindBonusLevelsParent( root ) == null )
			Assert.Inconclusive( "Set OPENTPW_BONUS_DATA to the extracted official bonus content for the bonus corpus." );
		var baseCounts = ObjectCatalog.Themes.ToDictionary( theme => theme, theme => ObjectCatalog.Load( theme ).Entries.Count );
		ObjectCatalog.BonusDataRoot = root;
		var expected = new Dictionary<string, (int Rides, int Sideshows, int Features)>
		{
			["jungle"] = (4, 0, 4),
			["hallow"] = (4, 1, 4),
			["space"] = (3, 2, 5),
			["fantasy"] = (3, 1, 4)
		};
		var bonus = new List<ObjectCatalogEntry>();
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			var added = catalog.Entries.Where( entry => entry.IsBonus ).ToArray();
			Assert.AreEqual( baseCounts[theme] + added.Length, catalog.Entries.Count, theme );
			Assert.AreEqual( expected[theme], (added.Count( entry => entry.Category == ObjectCategory.Ride ), added.Count( entry => entry.Category == ObjectCategory.Sideshow ), added.Count( entry => entry.Category == ObjectCategory.Feature )), theme );
			bonus.AddRange( added );
			// Every bonus object runs its script in the park's shared world next to the base objects.
			var world = new RideScriptWorld();
			var runtimes = added.Select( ( entry, index ) => new OriginalObjectRuntime( entry, world, seed: index + 1 ) ).ToArray();
			for ( var tick = 0; tick < 30 * FixedStepClock.TicksPerSecond; tick++ )
			{
				foreach ( var runtime in runtimes )
					runtime.Simulate( FixedStepClock.TickDuration );
			}
			foreach ( var runtime in runtimes )
			{
				Assert.IsTrue( runtime.Script == null || runtime.Script.State != RideVMState.Faulted, $"{runtime.Entry}: {runtime.Script?.FaultMessage}" );
				runtime.Stop();
			}
		}
		Assert.AreEqual( 35, bonus.Count );
		Assert.AreEqual( 35, bonus.Select( entry => entry.BonusNumber ).Distinct().Count() );
		foreach ( var entry in bonus )
		{
			Assert.IsTrue( entry.ArchiveName.StartsWith( "_" ) && entry.IsBuildable && entry.InfoId > 0 && entry.DisplayName.Length > 0, entry.ToString() );
			Assert.IsNotNull( entry.ScriptPath, entry.ToString() );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			Assert.AreEqual( ModelFileKind.Geometry, model.Kind, entry.ToString() );
			Assert.IsTrue( ObjectRenderParts.Build( entry, model ).Count > 0, entry.ToString() );
			foreach ( var animation in entry.Animations )
				new ObjectAnimator( model ).Play( 0, ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip!, animation.Name, false );
		}
		var snake = ObjectCatalog.Load( "jungle" ).Get( 1118 );
		Assert.AreEqual( ("_snake_1", 1, "Snake", "Slither"), (snake.ArchiveName, snake.BonusNumber, snake.SettingsName, snake.DisplayName) );
		Assert.AreEqual( "Rutschbahn", BonusNames.Read( snake, "German" ) );
		Assert.AreEqual( "Slither", BonusNames.Read( snake, "Klingon" ), "unknown languages fall back to English" );
		// [BIN:TPW-EXE:0x00413600 object text] the sign lines come from the selected language's file only; no file means no lines (ObjectSigns then draws the display name; the original's NULL-line handling is untraced)
		Assert.IsNotNull( BonusNames.ReadSignLines( snake, "German" ) );
		Assert.IsNull( BonusNames.ReadSignLines( snake, "Klingon" ), "the sign lines do not fall back to English" );
		Assert.IsTrue( snake.HasQueue, "bonus rides use the game's Rides.sam defaults" );
	}
}
