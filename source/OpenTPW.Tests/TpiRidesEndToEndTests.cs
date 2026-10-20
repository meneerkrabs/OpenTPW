using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Mods;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc attractions in Theme Park World through the data mod OpenTPW ships (<c>content/mods/opentpw.tpi-rides</c>, docs/MODS.md): the mod
/// is the shipped one (<see cref="TpiRidesFixture"/>, no Theme Park Inc bytes) and reads the install named by <c>OPENTPW_TPI_PATH</c>
/// (an installation or its Data folder) next to the Theme Park World data of <c>OPENTPW_GAME_PATH</c>. Inconclusive without both.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TpiRidesEndToEndTests
{
	private string root = null!;
	private BaseFileSystem? previousFileSystem;
	private IDisposable? scope;
	private ModSet set = null!;
	private string tpiFolder = null!;

	private const int Bounce = 40001, Robot = 40002, Bin = 40003, Drinks = 40004;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		var tpi = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( tpi ) || !Directory.Exists( tpi ) )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		tpiFolder = tpi!;
		var data = OriginalParkImportTests.OriginalDataPath();
		root = Directory.CreateTempSubdirectory( "opentpw-tpi-rides-" ).FullName;
		previousFileSystem = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		ObjectCatalog.BonusDataRoot = null;
		set = TpiRidesFixture.Resolve( TpiRidesFixture.Settings( tpiFolder ) );
		Assert.AreEqual( 0, set.Refused.Count, string.Join( "; ", set.Refused.Select( refusal => refusal.Reason ) ) );
		scope = ModContentRoots.Use( set, FileSystem );
	}

	[TestCleanup]
	public void Cleanup()
	{
		scope?.Dispose();
		if ( previousFileSystem != null )
			FileSystem = previousFileSystem;
		ObjectCatalog.BonusDataRoot = null;
		if ( root != null )
			Directory.Delete( root, true );
	}

	private static ObjectCatalogEntry Entry( string theme, int infoId ) =>
		ObjectCatalog.Load( theme ).Find( infoId ) ?? throw new AssertFailedException( $"{theme} lists no object {infoId}" );

	// ---- Loading ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheCatalogueListsTheModObjectsUnderTheirThemesWithTheirIdsAndNames()
	{
		var bounce = Entry( "jungle", Bounce );
		var robot = Entry( "space", Robot );
		var bin = Entry( "space", Bin );
		var drinks = Entry( "jungle", Drinks );
		Assert.AreEqual( (ObjectCategory.Ride, ObjectCategory.Ride, ObjectCategory.Feature, ObjectCategory.Shop), (bounce.Category, robot.Category, bin.Category, drinks.Category) );
		Assert.AreEqual( "Turban Bounce", bounce.DisplayName );
		Assert.AreEqual( "Giant Robot Swing", robot.DisplayName );
		foreach ( var entry in new[] { bounce, robot, bin, drinks } )
		{
			Assert.AreEqual( TpiRidesFixture.Id, entry.ModId );
			Assert.IsTrue( entry.IsExternal && !entry.IsBonus );
			StringAssert.StartsWith( entry.Root.Key( "/levels/x" ), "mod:opentpw.tpi-rides:/levels/" );
			Assert.IsNotNull( entry.ScriptPath, $"{entry} has its original script" );
			Assert.IsTrue( entry.Animations.Count > 0 || entry.Category != ObjectCategory.Ride, $"{entry} has its clips" );
		}
		// the game's own objects are unchanged and keep their ids (the mod's ids sit above every original one)
		Assert.AreEqual( "Belly Bounce", ObjectCatalog.Load( "jungle" ).Find( 1100 )!.DisplayName );
		Assert.IsTrue( ObjectCatalog.Load( "jungle" ).Entries.Where( entry => !entry.IsExternal ).All( entry => entry.InfoId < CrossEditionObjects.FirstReservableId ) );
		// counted by the mod's id: the pools extension (on by default) adds its own objects to every Theme Park World theme
		foreach ( var theme in new[] { "jungle", "space", "fantasy" } )
			Assert.AreEqual( TpiRidesFixture.Items.Count( item => item.Theme == theme ), ObjectCatalog.Load( theme ).Entries.Count( entry => entry.ModId == TpiRidesFixture.Id ), $"every archive the shipped mod lists is in {theme}" );
		Assert.AreEqual( 0, ObjectCatalog.Load( "hallow" ).Entries.Count( entry => entry.ModId == TpiRidesFixture.Id ), "the mod adds nothing to the other themes" );
	}

	[TestMethod]
	public void TheEconomyPricesResearchesAndListsTheModObjects()
	{
		foreach ( var (theme, id, kind, category) in new[] { ("jungle", Bounce, ParkObjectKind.Ride, BuildCategory.Rides), ("space", Robot, ParkObjectKind.Ride, BuildCategory.Rides),
			("space", Bin, ParkObjectKind.Feature, BuildCategory.Features), ("jungle", Drinks, ParkObjectKind.Shop, BuildCategory.Shops) } )
		{
			var entry = Entry( theme, id );
			var economy = ParkEconomy.CreateForTheme( theme, false );
			Assert.IsTrue( economy.Catalog.TryGet( id, out var info ), $"{theme} economy knows {id}" );
			Assert.AreEqual( kind, info.Kind );
			Assert.AreEqual( entry.InfoId, info.InfoId );
			Assert.AreEqual( entry.SettingsName, info.Name, "the economy names it as the mod does" );
			Assert.AreEqual( entry.BuildCost, info.PurchaseCost, "one price in the catalogue and the economy" );
			Assert.IsTrue( info.PurchaseCost > 0, $"{entry} has a price from its own .sam" );
			Assert.IsTrue( economy.Research.IsAvailable( id ), $"{entry} needs no research" );
			Assert.AreEqual( ParkEconomy.PurchaseResult.Ok, economy.CheckBuild( id ) );
			// the buy list (the build catalogue's tabs, priced and listed through the economy status)
			var status = new EconomyParkStatus( () => economy );
			var listed = new OriginalBuildCatalog( ObjectCatalog.Load( theme ) ).GetItems( category ).SingleOrDefault( item => item.InfoId == id );
			Assert.IsNotNull( listed, $"{entry} is in the {category} tab" );
			Assert.AreEqual( info.PurchaseCost, status.PriceOf( listed! ) );
			Assert.IsTrue( status.IsAvailable( listed! ) );
			Assert.AreSame( entry, listed!.Entry );
		}
		// a ride that needs research stays unavailable until it is researched, like the game's own
		var unresearched = ParkEconomy.CreateForTheme( "space", false );
		Assert.IsFalse( unresearched.Research.Items.Where( item => item.Level == 0 && item.InfoId >= CrossEditionObjects.FirstReservableId ).Any( item => item.Cost > 0 && unresearched.Research.IsAvailable( item.InfoId ) ) );
	}

	[TestMethod]
	public void WithoutTheModNothingOfTheInstallIsListedOrRouted()
	{
		using var plain = ModContentRoots.Use( ModSet.Defaults( OfficialExtensions.All ), FileSystem );
		// the default set includes the pools extension (on by default), whose own objects are external too and sit in their reserved ids: nothing else is listed
		Assert.AreEqual( 0, ObjectCatalog.Load( "jungle" ).Entries.Count( entry => entry.IsExternal && entry.ModId != OfficialExtensions.Pools.Id && entry.ModId != OfficialExtensions.Umbrellas.Id && entry.ModId != OfficialExtensions.Winter.Id && entry.ModId != OfficialExtensions.Evening.Id ) );
		Assert.IsFalse( ParkEconomy.CreateForTheme( "jungle", false ).Catalog.Objects.Any( info => info.InfoId >= CrossEditionObjects.FirstReservableId
			&& info.InfoId is not (>= EveningContent.FirstReservedId and <= PoolContent.LastReservedId) ) );
		Assert.IsNull( ContentSoundRoots.SourceOf( Entry( "jungle", 1100 ) ) );
	}

	[TestMethod]
	public void EveryArchiveTheShippedModListsLoadsIntoItsThemeWithItsEconomyEntry()
	{
		var items = TpiRidesFixture.Items;
		var loaded = 0;
		foreach ( var theme in ObjectCatalog.Themes )
		{
			var catalog = ObjectCatalog.Load( theme );
			var economy = ParkEconomy.CreateForTheme( theme, false );
			foreach ( var item in items.Where( item => item.Theme == theme ) )
			{
				var entry = catalog.Find( item.InfoId ) ?? throw new AssertFailedException( $"{item.Archive} is not listed in {theme}" );
				loaded++;
				Assert.AreEqual( TpiRidesFixture.Id, entry.ModId );
				Assert.AreEqual( item.English, entry.SettingsName );
				Assert.AreEqual( item.Category switch { "rides" => ObjectCategory.Ride, "shops" => ObjectCategory.Shop, "sideshow" => ObjectCategory.Sideshow, "features" => ObjectCategory.Feature, _ => ObjectCategory.Upgrade }, entry.Category );
				Assert.IsTrue( economy.Catalog.TryGet( item.InfoId, out _ ), $"{item.Archive} is in the {theme} economy catalogue" );
			}
		}
		Assert.AreEqual( items.Count, loaded );
		// the retail install: 182 archives of its three worlds' rides, shops, sideshow, features and upgrades folders load, plus the science litter bin
		Assert.AreEqual( 183, items.Count );
	}

	/// <summary>Whether a .sam of the archive declares an Info.Id: Theme Park Inc's ptract holds none, so no object can be made of it (the catalogue says so and skips it).</summary>
	private static bool HasInfoId( BaseFileSystem install, string archive ) =>
		install.GetFiles( archive ).Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) )
			.Any( file => ObjectSettingsFile.Load( install, install.GetRelativePath( install.GetAbsolutePath( file ) ) ).Values.ContainsKey( "Info.Id" ) );

	// ---- Textures and sounds ---------------------------------------------------------------------------------

	[TestMethod]
	public void EveryModelTextureResolvesInTheInstallsOwnSharedFolders()
	{
		foreach ( var (theme, id) in new[] { ("jungle", Bounce), ("space", Robot), ("space", Bin), ("jungle", Drinks) } )
		{
			var entry = Entry( theme, id );
			// the shared folders are those of the theme the archive was authored in, in the install (levels/arabian/sharetex, not the park's jungle)
			var locations = ObjectAssets.TextureDirectories( entry ).ToArray();
			Assert.IsTrue( locations.All( location => ReferenceEquals( location.FileSystem, entry.FileSystem ) ), $"{entry}: every folder is read from the install" );
			var source = entry.ArchivePath.Split( '/' )[2];
			CollectionAssert.Contains( locations.Select( location => location.Directory ).ToArray(), $"/levels/{source}/sharetex" );
			var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
			var missing = model.Textures.SelectMany( texture => texture.FrameNames ).Where( name => ObjectAssets.ResolveTexture( entry, name ) == null ).Distinct().ToArray();
			// Theme Park Inc's own science data holds the Giant Robot Swing's eyes.tga only in the low-detail ssharete.wad (sharetex.wad has eyes1 to eyes3), so the full set misses it there too
			CollectionAssert.AreEqual( id == Robot ? new[] { "eyes.tga" } : Array.Empty<string>(), missing, $"{entry}" );
		}
	}

	[TestMethod]
	public void TheScriptsEventsPlayFromTheInstallsOwnSoundBanks()
	{
		var checkedEvents = 0;
		foreach ( var (theme, id) in new[] { ("jungle", Bounce), ("space", Robot), ("space", Bin), ("jungle", Drinks) } )
		{
			var entry = Entry( theme, id );
			var source = ContentSoundRoots.SourceOf( entry );
			Assert.IsNotNull( source, $"{entry} is routed to the install's sound root" );
			Assert.AreEqual( entry.ArchivePath.Split( '/' )[2], source!.Value.Theme, "the theme folder of its archive in the install" );
			using var stream = entry.FileSystem.OpenRead( entry.ScriptPath! );
			var events = new RideScriptFile( stream ).Instructions.Where( instruction => instruction.Opcode == (ushort)Opcode.EVENT && instruction.Operands.Count >= 3
				&& instruction.Operands[0].Kind == RideScriptOperandKind.Literal && instruction.Operands[0].Value is >= 3 and <= 9 ).ToArray();
			foreach ( var instruction in events )
			{
				var (type, eventId) = (instruction.Operands[0].Value, instruction.Operands[2].Value);
				if ( instruction.Operands[2].Kind != RideScriptOperandKind.Literal )
					continue;
				var found = GameAudio.FindRseEvent( type, (uint)eventId, source );
				Assert.IsNotNull( found, $"{entry}: EVENT {type} {eventId} is in the install's categories" );
				checkedEvents++;
			}
		}
		Assert.IsTrue( checkedEvents >= 3, $"{checkedEvents} literal EVENT instructions checked" );
	}

	// ---- Running ---------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheLitterBinRunsTheInstallsWaitAbsoluteOpcodeWithoutFaulting()
	{
		var entry = Entry( "space", Bin );
		// its script declares VAR_USE (0), VAR_EMPTY, VAR_BOMB, ...: a guest's litter sets variable 0 whatever its name (Level.SetBinTrigger)
		var used = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: null, open: true );
		CollectionAssert.AreEqual( new[] { "VAR_USE", "VAR_EMPTY", "VAR_BOMB" }, used.Script!.Script.VariableNames.Take( 3 ).ToArray() );
		var usedOpcodes = new HashSet<ushort>();
		used.Script.InstructionExecuting = ( _, instruction ) => usedOpcodes.Add( instruction.RawOpcode );
		Level.SetBinTrigger( used );
		for ( var tick = 0; tick < 10 * FixedStepClock.TicksPerSecond; tick++ )
			used.Simulate( FixedStepClock.TickDuration );
		Assert.IsTrue( usedOpcodes.Contains( (ushort)Opcode.ADDOBJ ), "a binned item runs the bin's effect" );
		Assert.AreNotEqual( RideVMState.Faulted, used.Script.State, used.Script.FaultMessage );
		// the Theme Park Inc only opcode (45) sits in the VAR_BOMB branch, which nothing in Theme Park World drives; setting the variable runs it
		var bomb = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: null, open: true );
		var executed = new HashSet<ushort>();
		bomb.Script!.InstructionExecuting = ( _, instruction ) => executed.Add( instruction.RawOpcode );
		Assert.IsTrue( bomb.SetVariable( "VAR_BOMB", 1 ) );
		for ( var tick = 0; tick < 10 * FixedStepClock.TicksPerSecond; tick++ )
			bomb.Simulate( FixedStepClock.TickDuration );
		CollectionAssert.Contains( executed.ToArray(), (ushort)Opcode.WAITABS, "WAITABS ran inside the placed object" );
		Assert.AreNotEqual( RideVMState.Faulted, bomb.Script.State, bomb.Script.FaultMessage );
	}

	[TestMethod]
	public void ATpiRideInTheJungleTakesGuestsThroughTheGate()
	{
		var report = RunGate( "jungle", Bounce );
		Assert.AreEqual( M3GateVerdict.Pass, report["build.attraction"].Verdict, report.ToSummary() );
		StringAssert.Contains( report["build.attraction"].Evidence["object"]!.ToString(), "Turban Bounce" );
		Assert.IsTrue( (int)report["guests.flow"].Evidence["attractionBoarded"]! > 0, report.ToSummary() );
		Assert.IsTrue( (int)report["build.queue"].Evidence["boardedFromQueueCells"]! > 0, report.ToSummary() );
	}

	[TestMethod]
	public void ATpiRideInTheSpaceParkTakesGuestsThroughTheGate()
	{
		var report = RunGate( "space", Robot );
		StringAssert.Contains( report["build.attraction"].Evidence["object"]!.ToString(), "Giant Robot Swing" );
		Assert.IsTrue( (int)report["guests.flow"].Evidence["attractionBoarded"]! > 0, report.ToSummary() );
	}

	/// <summary>The gate's attraction is the TPI ride: bought, queued, registered with the guests, run for three minutes with a shop, a toilet and staff.</summary>
	private static M3GateReport RunGate( string level, int infoId )
	{
		var report = M3Gate.Run( new M3GateOptions { Level = level, Minutes = 3, ArrivalTimeBetween = 1, AttractionFilter = entry => entry.InfoId == infoId } );
		// no row fails (the queue row stays unresolved: its boarding bound is traced for Theme Park World's bounce rides only)
		Assert.IsFalse( report.HasFailures, report.ToSummary() );
		foreach ( var id in new[] { "build.entrance", "build.paths", "build.attraction", "build.queue", "guests.flow", "rides.scripts-run", "economy.ledger-consistent", "determinism.same-seed" } )
			Assert.AreEqual( M3GateVerdict.Pass, report[id].Verdict, $"{id}: {report.ToSummary()}" );
		Assert.IsTrue( (bool)report["determinism.same-seed"].Evidence["guestHashMatches"]! );
		return report;
	}

	// ---- Saves -----------------------------------------------------------------------------------------------

	[TestMethod]
	public void AParkWithTheModRecordsItAndATpiObjectNeedsItToLoad()
	{
		var records = ParkResumeMods.Capture( set );
		CollectionAssert.AreEqual( new[] { "opentpw.cross-edition-objects", TpiRidesFixture.Id }, records!.Select( record => record.Id ).ToArray() );
		Assert.AreEqual( 0, ParkResumeMods.Unmet( records, set ).Count );
		var without = ModSet.Defaults( OfficialExtensions.All );
		var unmet = ParkResumeMods.Unmet( records, without );
		StringAssert.Contains( string.Join( "; ", unmet ), "needs opentpw.tpi-rides 1.0.0" );
		var message = Assert.ThrowsException<InvalidDataException>( () => ParkResumeMods.Require( records, without ) ).Message;
		StringAssert.Contains( message, TpiRidesFixture.Id );
		// the asset manifest pins the install's members the park uses, keyed by the mod's root
		var manifest = ParkResumeAssetManifest.Capture( ObjectCatalog.Load( "jungle" ), BalanceSettings.Load( "jungle", false ) );
		Assert.IsTrue( manifest.Keys.Any( key => key.StartsWith( "mod:opentpw.tpi-rides:/levels/arabian/rides/tbounce/", StringComparison.Ordinal ) ), "the TPI archive's members are pinned" );
		Assert.IsTrue( manifest.Keys.Any( key => key.StartsWith( "data:/levels/jungle/rides/", StringComparison.Ordinal ) ) );
		// as a save holds it: the pools extension (on by default) keeps only the keys of the pools objects a park placed (none here), ParkResumeMods.AssetsForUse
		Assert.IsNull( ParkResumeMods.ShapeProblem( records, ParkResumeMods.AssetsForUse( manifest, Array.Empty<ObjectCatalogEntry>() ) ) );
		// a park saved without the mod, loaded with it, is refused for the mod that was not there
		StringAssert.Contains( string.Join( "; ", ParkResumeMods.Unmet( null, set ) ), "was saved without" );
	}
}
