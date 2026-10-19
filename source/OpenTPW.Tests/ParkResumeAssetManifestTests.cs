using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTPW.Tests;

/// <summary>
/// ParkResumeAssetManifest against synthetic parks (temp roots; no game data needed) and, when
/// <c>OPENTPW_GAME_PATH</c> is set, the original Jungle data (read-only).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParkResumeAssetManifestTests
{
	private static readonly Regex LowercaseSha256 = new( "^[0-9a-f]{64}$", RegexOptions.CultureInvariant );

	private BaseFileSystem? previousFileSystem;
	private readonly List<string> tempRoots = new();

	[TestInitialize]
	public void Initialize()
	{
		previousFileSystem = FileSystem;
		ObjectCatalog.BonusDataRoot = null;
		// the manifest of the base game: the default extension set now includes pools, whose objects are no part of it (a save holds the pool files only for the objects the park places)
		poolsOff = OpenTPW.Mods.OpenTpwExtensions.Use( PoolsTests.PoolsSet( false ) );
		previousRoots = ObjectCatalog.ContentRoots;
		ObjectCatalog.SetContentRoots( Array.Empty<CatalogContentRoot>() );
	}

	private IDisposable? poolsOff;
	private IReadOnlyList<CatalogContentRoot> previousRoots = Array.Empty<CatalogContentRoot>();

	[TestCleanup]
	public void Cleanup()
	{
		ObjectCatalog.SetContentRoots( previousRoots );
		poolsOff?.Dispose();
		FileSystem = previousFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
		foreach ( var root in tempRoots )
		{
			try { Directory.Delete( root, true ); }
			catch ( IOException ) { }
		}
	}

	[TestMethod]
	public void CapturesThePinnedSimulationSetOfASyntheticPark()
	{
		var root = CreateSyntheticPark();
		var manifest = Capture( root, out var catalog, out var settings );
		Assert.ThrowsException<ArgumentNullException>( () => ParkResumeAssetManifest.Capture( null!, settings ) );
		Assert.ThrowsException<ArgumentNullException>( () => ParkResumeAssetManifest.Capture( catalog, null! ) );

		// The pinned set: every .sam/.rse/.md2 member of every catalogue archive (including the Online_
		// overlay the offline game never layers), the category default, the balance layers (also all that
		// GuestSettings ever reads), challenges, global, advisor and the terrain pair.
		var expected = new[]
		{
			"data:/Advisor/Advisor.sam",
			"data:/Challenges.sam",
			"data:/levels/Standard.sam",
			"data:/levels/jungle/Standard.sam",
			"data:/levels/jungle/global.sam",
			"data:/levels/jungle/rides/Rides.sam",
			"data:/levels/jungle/rides/totem/Easy_totem.sam",
			"data:/levels/jungle/rides/totem/Online_totem.sam",
			"data:/levels/jungle/rides/totem/totem.MD2",
			"data:/levels/jungle/rides/totem/totem.rse",
			"data:/levels/jungle/rides/totem/totem.sam",
			"data:/levels/jungle/rides/totem/totemm1.MD2",
			"data:/levels/jungle/terrain/base.MD2",
			"data:/levels/jungle/terrain/base.map"
		};
		CollectionAssert.AreEquivalent( expected, manifest.Keys.ToArray(), "the exact pinned key set" );
		Assert.AreEqual( expected.Length, manifest.Count );
		// Original saves, textures (.wct) and nested archive members never appear.
		Assert.IsFalse( manifest.Keys.Any( key =>
			key.EndsWith( ".tpwi", StringComparison.OrdinalIgnoreCase ) || key.EndsWith( ".tpws", StringComparison.OrdinalIgnoreCase )
			|| key.EndsWith( ".wct", StringComparison.OrdinalIgnoreCase ) || key.Contains( "/textures/", StringComparison.Ordinal ) ), "out-of-scope media" );

		// Values are lowercase hex SHA-256 of content: equal bytes hash equally under different keys.
		foreach ( var value in manifest.Values )
			Assert.IsTrue( LowercaseSha256.IsMatch( value ), $"{value} is not lowercase hex SHA-256" );
		Assert.AreEqual( manifest["data:/levels/jungle/rides/totem/totem.MD2"], manifest["data:/levels/jungle/rides/totem/totemm1.MD2"], "identical members hash identically" );
		var keys = manifest.Keys.ToArray();
		Assert.IsTrue( keys.SequenceEqual( keys.OrderBy( key => key, StringComparer.Ordinal ) ), "keys enumerate ordinal-sorted" );
	}

	[TestMethod]
	public void EasyModePinsTheEasyBalanceLayerOnTopOfTheGuestLayers()
	{
		var root = CreateSyntheticPark();
		var normal = Capture( root, easy: false, out _, out _ );
		Assert.IsFalse( normal.ContainsKey( "data:/levels/jungle/Easy_Standard.sam" ), "no easy balance layer in normal mode" );

		// GuestSettings always reads global+theme Standard.sam (already pinned in both modes); easy mode adds
		// the theme's Easy_Standard.sam to the balance layers. The archive's Easy_ overlay is pinned in both.
		var easy = Capture( root, easy: true, out _, out _ );
		Assert.AreEqual( LowercaseHash( File.ReadAllBytes( Path.Combine( root, "levels/jungle/Easy_Standard.sam" ) ) ), easy["data:/levels/jungle/Easy_Standard.sam"] );
		Assert.IsTrue( normal.ContainsKey( "data:/levels/jungle/rides/totem/Easy_totem.sam" ) && easy.ContainsKey( "data:/levels/jungle/rides/totem/Easy_totem.sam" ), "archive overlays are pinned in both modes" );
		CollectionAssert.AreEqual( new[] { "data:/levels/jungle/Easy_Standard.sam" }, easy.Keys.Except( normal.Keys ).ToArray() );
		Assert.AreEqual( 0, normal.Keys.Except( easy.Keys ).Count(), "easy mode pins everything normal mode pins" );
	}

	[TestMethod]
	public void CaptureIsReproducibleAcrossFreshFileSystemsAndCatalogs()
	{
		var root = CreateSyntheticPark();
		var first = Capture( root, out _, out _ );
		var second = Capture( root, out _, out _ );
		Assert.IsTrue( first.SequenceEqual( second ), "two captures with fresh file systems and catalogs agree" );
	}

	[TestMethod]
	public void ChangedBytesAndChangedMemberSetsChangeTheManifest()
	{
		var root = CreateSyntheticPark();
		var before = Capture( root, out _, out _ );

		// Changed script, category default, balance and terrain bytes: exactly those keys change.
		File.WriteAllBytes( Path.Combine( root, "levels/jungle/rides/totem/totem.rse" ), new byte[] { 0x52, 0x53, 0x45, 0x21 } );
		File.WriteAllText( Path.Combine( root, "levels/jungle/rides/Rides.sam" ), "Upgrades[0].WearRate 9\n" );
		File.WriteAllText( Path.Combine( root, "levels/jungle/Standard.sam" ), EconomyTestData.StandardText( initialCash: 70000 ) );
		File.WriteAllBytes( Path.Combine( root, "levels/jungle/terrain/base.map" ), new byte[] { 0x4d, 0x41, 0x50, 0x32 } );
		var changed = Capture( root, out _, out _ );
		CollectionAssert.AreEquivalent( before.Keys.ToArray(), changed.Keys.ToArray(), "same members" );
		var changedKeys = before.Join( changed, pair => pair.Key, pair => pair.Key, ( a, b ) => (a.Key, Same: a.Value == b.Value) )
			.Where( pair => !pair.Same ).Select( pair => pair.Key ).ToArray();
		CollectionAssert.AreEquivalent( new[]
		{
			"data:/levels/jungle/Standard.sam",
			"data:/levels/jungle/rides/Rides.sam",
			"data:/levels/jungle/rides/totem/totem.rse",
			"data:/levels/jungle/terrain/base.map"
		}, changedKeys );

		// A member leaving or joining an archive changes the key set, even when the offline game never
		// layered the file (Online_ overlays).
		File.Delete( Path.Combine( root, "levels/jungle/rides/totem/Online_totem.sam" ) );
		File.WriteAllBytes( Path.Combine( root, "levels/jungle/rides/totem/totemx.MD2" ), new byte[] { 0x58 } );
		var reshaped = Capture( root, out _, out _ );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/totemx.MD2" }, reshaped.Keys.Except( changed.Keys ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { "data:/levels/jungle/rides/totem/Online_totem.sam" }, changed.Keys.Except( reshaped.Keys ).ToArray() );
	}

	[TestMethod]
	public void VirtualWadMembersAreHashedByContentSoRepackingDoesNotInvalidate()
	{
		var root = CreateTempRoot();
		var wad = Path.Combine( root, "levels/jungle/rides/totem.wad" );
		Directory.CreateDirectory( Path.GetDirectoryName( wad )! );
		var settings = Encoding.ASCII.GetBytes( "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\n" );
		var model = new byte[] { 0x4d, 0x44, 0x32 };
		File.WriteAllBytes( wad, BuildWad( ("totem.sam", settings), ("totem.MD2", model), ("totem.rse", new byte[] { 0x01 }) ) );
		WriteBalanceFiles( root );
		var before = Capture( root, out _, out _ );
		CollectionAssert.AreEquivalent( new[]
		{
			"data:/Advisor/Advisor.sam",
			"data:/Challenges.sam",
			"data:/levels/Standard.sam",
			"data:/levels/jungle/Standard.sam",
			"data:/levels/jungle/global.sam",
			"data:/levels/jungle/rides/Rides.sam",
			"data:/levels/jungle/rides/totem/totem.MD2",
			"data:/levels/jungle/rides/totem/totem.rse",
			"data:/levels/jungle/rides/totem/totem.sam",
			"data:/levels/jungle/terrain/base.MD2",
			"data:/levels/jungle/terrain/base.map"
		}, before.Keys.ToArray(), "WAD members are captured like loose members" );
		Assert.AreEqual( LowercaseHash( new byte[] { 0x01 } ), before["data:/levels/jungle/rides/totem/totem.rse"] );

		// Changed member bytes inside the archive change exactly that key.
		File.WriteAllBytes( wad, BuildWad( ("totem.sam", settings), ("totem.MD2", model), ("totem.rse", new byte[] { 0x02 }) ) );
		var edited = Capture( root, out _, out _ );
		Assert.AreNotEqual( before["data:/levels/jungle/rides/totem/totem.rse"], edited["data:/levels/jungle/rides/totem/totem.rse"] );
		Assert.AreEqual( before["data:/levels/jungle/rides/totem/totem.sam"], edited["data:/levels/jungle/rides/totem/totem.sam"] );

		// The same members re-packed into a differently laid-out archive (other order, other offsets) hash
		// the same: the manifest pins content, not archive packing.
		File.WriteAllBytes( wad, BuildWad( ("totem.rse", new byte[] { 0x02 }), ("totem.MD2", model), ("totem.sam", settings) ) );
		var repacked = Capture( root, out _, out _ );
		Assert.IsTrue( edited.SequenceEqual( repacked ), "repacking an archive does not change the manifest" );
	}

	[TestMethod]
	public void BonusArchivesAreKeyedUnderTheirOwnRoot()
	{
		var root = CreateSyntheticPark();
		var withoutBonus = Capture( root, out _, out _ );

		var bonusRoot = CreateTempRoot();
		var archive = Path.Combine( bonusRoot, "levels/jungle/rides", "_snake_1" );
		Directory.CreateDirectory( archive );
		File.WriteAllText( Path.Combine( archive, "_snake.sam" ), "Info.Id\t9100\nInfo.Name\t\"Snake\"\n" );
		File.WriteAllBytes( Path.Combine( archive, "_snake.MD2" ), new byte[] { 0x4d, 0x44, 0x32 } );
		File.WriteAllBytes( Path.Combine( archive, "_snake.rse" ), new byte[] { 0x09 } );

		ObjectCatalog.BonusDataRoot = bonusRoot;
		var manifest = Capture( root, out var catalog, out _ );
		Assert.AreEqual( 2, catalog.Entries.Count );
		Assert.AreEqual( "/levels/jungle/rides/_snake_1", catalog.Entries.Single( entry => entry.IsBonus ).ArchivePath );

		// The bonus archive's members are keyed under bonus:, the category default stays the game data's
		// (bonus archives read it from there), and the data: half is exactly the no-bonus manifest.
		CollectionAssert.AreEquivalent( new[]
		{
			"bonus:/levels/jungle/rides/_snake_1/_snake.MD2",
			"bonus:/levels/jungle/rides/_snake_1/_snake.rse",
			"bonus:/levels/jungle/rides/_snake_1/_snake.sam"
		}, manifest.Keys.Where( key => key.StartsWith( "bonus:", StringComparison.Ordinal ) ).ToArray() );
		Assert.AreEqual( LowercaseHash( new byte[] { 0x09 } ), manifest["bonus:/levels/jungle/rides/_snake_1/_snake.rse"] );
		CollectionAssert.AreEquivalent( withoutBonus.Keys.ToArray(), manifest.Keys.Where( key => key.StartsWith( "data:", StringComparison.Ordinal ) ).ToArray(), "data keys unchanged by bonus content" );
		Assert.AreEqual( 1, manifest.Keys.Count( key => key == "data:/levels/jungle/rides/Rides.sam" ), "the category default is keyed once" );
	}

	[TestMethod]
	public void OverlongAssetKeysAreRejected()
	{
		var root = CreateTempRoot();
		var longName = new string( 'a', 250 );
		var archive = Path.Combine( root, "levels/jungle/rides", longName );
		Directory.CreateDirectory( archive );
		File.WriteAllText( Path.Combine( archive, longName + ".sam" ), "Info.Id\t1110\n" );
		File.WriteAllBytes( Path.Combine( archive, longName + ".MD2" ), new byte[] { 1 } );
		WriteBalanceFiles( root );

		var catalog = InstallFileSystemAndLoadCatalog( root );
		var settings = BalanceSettings.Load( "jungle", easy: false );
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeAssetManifest.Capture( catalog, settings ) );
	}

	[TestMethod]
	public void TheStepsOfASyntheticParkMakeTheSameManifestInSeveralFrames()
	{
		var root = CreateSyntheticPark();
		var whole = Capture( root, out var catalog, out var settings );
		var manifest = new SortedDictionary<string, string>( StringComparer.Ordinal );
		var steps = 0;
		foreach ( var _ in ParkResumeAssetManifest.CaptureSteps( catalog, settings.Theme, settings.IsEasy, manifest ) )
			steps++;
		Assert.IsTrue( steps >= catalog.Entries.Count, "one step for each catalogue archive" );
		Assert.IsTrue( whole.SequenceEqual( manifest ), "the steps and the one-go capture agree" );
	}

	[TestMethod]
	public void APreparedParkKeepsTheManifestItsStepsMadeForItsSnapshot()
	{
		var root = CreateSyntheticPark();
		var whole = Capture( root, out var catalog, out _ );
		var level = (Level)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
		void Set( string name, object value ) => typeof( Level ).GetField( name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic )!.SetValue( level, value );
		Set( "<Objects>k__BackingField", new ParkObjects( catalog, new SandboxParkGrid() ) );
		Set( "<Park>k__BackingField", ParkEconomyRuntime.ForResume( EconomyTestData.Park(), ParkStartKind.FullSimulation ) );
		var steps = level.PrepareAutosaveAssets().Count();
		Assert.IsTrue( steps >= catalog.Entries.Count, "one step for each catalogue archive" );
		var prepared = level.AutosaveAssets();
		Assert.IsTrue( whole.SequenceEqual( prepared ), "the prepared manifest is the one-go capture" );
		Assert.AreEqual( 0, level.PrepareAutosaveAssets().Count(), "a prepared park has nothing left to hash" );
		Assert.AreSame( prepared, level.AutosaveAssets(), "the snapshot and the autosave reuse it instead of hashing again" );
	}

	/// <summary>Corpus: the real Jungle data (read-only); inconclusive without OPENTPW_GAME_PATH.</summary>
	[TestMethod]
	public void OriginalJungleDataCapturesDeterministicallyOverVirtualMembers()
	{
		if ( !TryUseOriginalData( out var dataRoot ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the asset manifest corpus." );
		ObjectCatalog.BonusDataRoot = null;

		var catalog = InstallFileSystemAndLoadCatalog( dataRoot );
		var settings = BalanceSettings.Load( "jungle", easy: false );
		var first = ParkResumeAssetManifest.Capture( catalog, settings );
		var second = ParkResumeAssetManifest.Capture( catalog, settings );
		Assert.IsTrue( first.SequenceEqual( second ), "two captures agree" );

		Assert.IsTrue( first.Count > 100 && first.Count <= ParkResumeAssetManifest.MaximumEntries, $"{first.Count} keys" );
		Assert.IsTrue( first.Keys.All( key => key.StartsWith( "data:", StringComparison.Ordinal ) ), "no bonus content pinned" );
		Assert.IsTrue( first.Keys.All( key => key.Length <= ParkResumeAssetManifest.MaximumKeyLength ), "keys within the bound" );
		var keys = first.Keys.ToArray();
		Assert.IsTrue( keys.SequenceEqual( keys.OrderBy( key => key, StringComparer.Ordinal ) ), "ordinal-sorted" );

		// Balance, challenges, advisor, terrain and category-default layers of the real theme.
		foreach ( var key in new[]
		{
			"data:/levels/Standard.sam", "data:/levels/jungle/Standard.sam", "data:/Challenges.sam",
			"data:/levels/jungle/global.sam", "data:/Advisor/Advisor.sam",
			"data:/levels/jungle/terrain/base.map", "data:/levels/jungle/terrain/base.MD2",
			"data:/levels/jungle/rides/Rides.sam", "data:/levels/jungle/shops/Shops.sam",
			"data:/levels/jungle/sideshow/SideShow.sam", "data:/levels/jungle/features/Features.sam",
			"data:/levels/jungle/upgrades/Upgrades.sam"
		} )
			Assert.IsTrue( first.ContainsKey( key ), $"{key} is pinned" );

		// Every catalogued archive contributes its model, script and settings (the pinned full set).
		foreach ( var entry in catalog.Entries )
		{
			Assert.IsTrue( first.ContainsKey( "data:" + entry.ModelPath ), $"{entry.ArchivePath} model" );
			Assert.IsTrue( entry.ScriptPath == null || first.ContainsKey( "data:" + entry.ScriptPath ), $"{entry.ArchivePath} script" );
			Assert.IsTrue( first.ContainsKey( "data:" + entry.ArchivePath + "/" + Path.GetFileName( entry.Settings.Sources.Last() ) ), $"{entry.ArchivePath} settings" );
		}

		// WAD member hashes are the SHA-256 of the decompressed content, cross-checked against the raw archive
		// (the member name keeps the archive's own casing, e.g. Totem.sam).
		var totem = catalog.Get( 1110 );
		Assert.AreEqual( LowercaseHash( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/rides/totem.wad", Path.GetFileName( totem.Settings.Sources.Last() ) ) ), first["data:" + totem.Settings.Sources.Last()] );
		Assert.AreEqual( LowercaseHash( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.map" ) ), first["data:/levels/jungle/terrain/base.map"] );
		Assert.AreEqual( LowercaseHash( OriginalParkImportTests.ReadArchiveMember( "levels/jungle/terrain.wad", "base.MD2" ) ), first["data:/levels/jungle/terrain/base.MD2"] );
		// Original saves are excluded by design.
		Assert.IsFalse( first.Keys.Any( key => key.EndsWith( ".tpwi", StringComparison.OrdinalIgnoreCase ) || key.EndsWith( ".tpws", StringComparison.OrdinalIgnoreCase ) ) );
	}

	/// <summary>Corpus: official bonus content (private OPENTPW_BONUS_DATA; inconclusive without it).</summary>
	[TestMethod]
	public void OriginalBonusContentIsKeyedUnderTheBonusRoot()
	{
		if ( !TryUseOriginalData( out var dataRoot ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the asset manifest corpus." );
		var bonusRoot = Environment.GetEnvironmentVariable( "OPENTPW_BONUS_DATA" );
		if ( string.IsNullOrWhiteSpace( bonusRoot ) || ObjectCatalog.FindBonusLevelsParent( bonusRoot ) == null )
			Assert.Inconclusive( "Set OPENTPW_BONUS_DATA to the extracted official bonus content." );

		InstallFileSystemAndLoadCatalog( dataRoot );
		var settings = BalanceSettings.Load( "jungle", easy: false );
		ObjectCatalog.BonusDataRoot = null;
		var withoutBonus = ParkResumeAssetManifest.Capture( ObjectCatalog.Load( "jungle" ), settings );

		ObjectCatalog.BonusDataRoot = bonusRoot;
		var catalog = ObjectCatalog.Load( "jungle" );
		var manifest = ParkResumeAssetManifest.Capture( catalog, settings );
		Assert.IsTrue( catalog.Entries.Any( entry => entry.IsBonus ), "the bonus content merged in" );
		CollectionAssert.AreEquivalent( withoutBonus.Keys.ToArray(), manifest.Keys.Where( key => key.StartsWith( "data:", StringComparison.Ordinal ) ).ToArray(), "data keys unchanged by bonus content" );
		foreach ( var entry in catalog.Entries.Where( entry => entry.IsBonus ) )
			Assert.IsTrue( manifest.ContainsKey( "bonus:" + entry.ModelPath ), $"{entry.ArchivePath} model under bonus:" );
	}

	private IReadOnlyDictionary<string, string> Capture( string root, out ObjectCatalog catalog, out BalanceSettings settings ) =>
		Capture( root, easy: false, out catalog, out settings );

	private IReadOnlyDictionary<string, string> Capture( string root, bool easy, out ObjectCatalog catalog, out BalanceSettings settings )
	{
		InstallFileSystemAndLoadCatalog( root, easy );
		catalog = ObjectCatalog.Load( "jungle", easy );
		settings = BalanceSettings.Load( "jungle", easy );
		return ParkResumeAssetManifest.Capture( catalog, settings );
	}

	private static ObjectCatalog InstallFileSystemAndLoadCatalog( string root, bool easy = false )
	{
		FileSystem = new BaseFileSystem( root );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		return ObjectCatalog.Load( "jungle", easy );
	}

	private static bool TryUseOriginalData( out string dataRoot )
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		dataRoot = "";
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			return false;
		dataRoot = OriginalParkImportTests.OriginalDataPath();
		return Directory.Exists( Path.Combine( dataRoot, "levels", "jungle", "rides" ) );
	}

	/// <summary>A synthetic Jungle park: one loose rides archive plus every theme-level file the manifest pins.</summary>
	private string CreateSyntheticPark()
	{
		var root = CreateTempRoot();
		WriteBalanceFiles( root );
		var archive = Path.Combine( root, "levels/jungle/rides/totem" );
		Directory.CreateDirectory( Path.Combine( archive, "textures" ) );
		File.WriteAllText( Path.Combine( archive, "totem.sam" ), "Info.Id\t1110\nInfo.Name\t\"Inca Totem\"\nInfo.WhichUIType\t0\n" );
		File.WriteAllBytes( Path.Combine( archive, "totem.MD2" ), new byte[] { 0x4d, 0x44, 0x32 } );
		// Identical bytes to the main model: equal content hashes equally under another key.
		File.WriteAllBytes( Path.Combine( archive, "totemm1.MD2" ), new byte[] { 0x4d, 0x44, 0x32 } );
		File.WriteAllBytes( Path.Combine( archive, "totem.rse" ), new byte[] { 0x52, 0x53, 0x45 } );
		File.WriteAllText( Path.Combine( archive, "Easy_totem.sam" ), "Upgrades[0].CostOfUpgrade 1000\n" );
		File.WriteAllText( Path.Combine( archive, "Online_totem.sam" ), "Upgrades[0].CostOfUpgrade 2000\n" );
		// Presentation media: excluded from the manifest, whatever their name.
		File.WriteAllBytes( Path.Combine( archive, "totem.wct" ), new byte[] { 0x57, 0x43, 0x54 } );
		File.WriteAllText( Path.Combine( archive, "textures", "nested.sam" ), "Info.Id 0\n" );
		return root;
	}

	private static void WriteBalanceFiles( string root )
	{
		Write( root, "levels/Standard.sam", EconomyTestData.StandardText() );
		Write( root, "levels/jungle/Standard.sam", EconomyTestData.StandardText( initialCash: 60000 ) );
		Write( root, "levels/jungle/Easy_Standard.sam", EconomyTestData.StandardText( initialCash: 100000 ) );
		Write( root, "levels/jungle/global.sam", "Tickets.CanEarnTicketsInTheme 1\nTickets.CanSpendTicketsInTheme 1\nKeys.CostToEnter 1\n" );
		Write( root, "Challenges.sam", EconomyTestData.ChallengesText );
		Write( root, "Advisor/Advisor.sam", "Welcome.Score 10\n" );
		Write( root, "levels/jungle/Easymode.TPWI", new byte[] { 0x54, 0x50, 0x57, 0x49 } );
		Write( root, "levels/jungle/terrain/base.map", new byte[] { 0x4d, 0x41, 0x50 } );
		Write( root, "levels/jungle/terrain/base.MD2", new byte[] { 0x4d, 0x44, 0x32, 0x21 } );
		Write( root, "levels/jungle/rides/Rides.sam", "Upgrades[0].WearRate 5\n" );
	}

	private static void Write( string root, string relativePath, string text ) => Write( root, relativePath, Encoding.ASCII.GetBytes( text ) );

	private static void Write( string root, string relativePath, byte[] bytes )
	{
		var path = Path.Combine( root, relativePath.Replace( '/', Path.DirectorySeparatorChar ) );
		Directory.CreateDirectory( Path.GetDirectoryName( path )! );
		File.WriteAllBytes( path, bytes );
	}

	private string CreateTempRoot()
	{
		var root = Path.Combine( Path.GetTempPath(), "opentpw-asset-manifest-" + Guid.NewGuid().ToString( "N" ) );
		tempRoots.Add( root );
		return root;
	}

	private static string LowercaseHash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );

	/// <summary>
	/// A minimal DWFB WAD with uncompressed members, in the layout the game's reader expects: 88-byte header,
	/// the 40-byte little-endian list entries directly after it (the reader walks them from offset 88), then
	/// the member data, then the null-terminated ASCII names (whose blob the header's list offset names).
	/// </summary>
	private static byte[] BuildWad( params (string Name, byte[] Data)[] members )
	{
		using var buffer = new MemoryStream();
		void UInt32( uint value ) => buffer.Write( BitConverter.GetBytes( value ), 0, 4 );

		buffer.Write( Encoding.ASCII.GetBytes( "DWFB" ), 0, 4 );
		UInt32( 1 );
		buffer.Write( new byte[64], 0, 64 );
		UInt32( (uint)members.Length );
		UInt32( 0 );
		UInt32( 0 );
		UInt32( 0 );
		var entryListOffset = (uint)buffer.Position;
		var dataOffset = entryListOffset + (uint)(members.Length * 40);
		var nameOffset = dataOffset + (uint)members.Sum( member => member.Data.Length );
		var offsets = new uint[members.Length];
		for ( var member = 0; member < members.Length; member++ )
		{
			UInt32( 0 );
			UInt32( (uint)(nameOffset + members.Take( member ).Sum( previous => previous.Name.Length + 1 )) );
			UInt32( (uint)(members[member].Name.Length + 1) );
			UInt32( (uint)(dataOffset + members.Take( member ).Sum( previous => previous.Data.Length )) );
			UInt32( (uint)members[member].Data.Length );
			UInt32( 0 );
			UInt32( (uint)members[member].Data.Length );
			buffer.Write( new byte[12], 0, 12 );
		}
		foreach ( var (_, data) in members )
			buffer.Write( data, 0, data.Length );
		foreach ( var (name, _) in members )
		{
			var bytes = Encoding.ASCII.GetBytes( name );
			buffer.Write( bytes, 0, bytes.Length );
			buffer.WriteByte( 0 );
		}
		var wad = buffer.ToArray();
		// The header's list offset/length describe the names blob.
		BitConverter.GetBytes( nameOffset ).CopyTo( wad, 76 );
		BitConverter.GetBytes( (uint)members.Sum( member => member.Name.Length + 1 ) ).CopyTo( wad, 80 );
		return wad;
	}
}
