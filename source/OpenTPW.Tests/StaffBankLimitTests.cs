using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// GUEST-020: the staff folders handymen, mechanics, guards and researchers load at most the limit of tp.exe 0x41AA40 (1 bank for the preset's NUMKIDS 0, else 2), sorted by name; the entertainers'
/// folders load whole.
/// </summary>
[TestClass]
public class StaffBankLimitTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Init()
	{
		previousFileSystem = FileSystem;
		Log = new();
	}

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	private static Stream? Presets( string path ) => path switch
	{
		"/low.sam" => new MemoryStream( Encoding.ASCII.GetBytes( "GameOptions.NUMKIDS\t0\n" ) ),
		"/med.sam" => new MemoryStream( Encoding.ASCII.GetBytes( "GameOptions.NUMKIDS\t2\n" ) ),
		"/high.sam" => new MemoryStream( Encoding.ASCII.GetBytes( "GameOptions.NUMKIDS\t2\n" ) ),
		_ => null
	};

	[TestMethod]
	public void TheLimitIsOneForTheLowestDetailWordAndTwoOtherwise()
	{
		// [BIN:TPW-EXE:0x41AA84 jump table] 0 -> 1, 1, 2, 3 and above -> 2
		CollectionAssert.AreEqual( new[] { 1, 2, 2, 2, 2 }, new[] { 0, 1, 2, 3, 9 }.Select( StaffSpriteRules.BankLimit ).ToArray() );
		Assert.IsTrue( new[] { StaffType.Handyman, StaffType.Mechanic, StaffType.Guard, StaffType.Researcher }.All( StaffSpriteRules.IsBankLimited ) );
		Assert.IsFalse( StaffSpriteRules.IsBankLimited( StaffType.Entertainer ), "the entertainers' theme folders are unflagged" );
	}

	[TestMethod]
	public void ThePresetsDetailWordGivesTheLimit()
	{
		foreach ( var (preset, expected) in new[] { (GraphicsPreset.Low, 1), (GraphicsPreset.Medium, 2), (GraphicsPreset.High, 2) } )
		{
			var settings = new GraphicsSettingsService( Presets, GraphicsSettings.Default with { Preset = preset }, null, () => CompatibilityFlags.Original );
			Assert.AreEqual( expected, StaffSpriteRules.BankLimitOf( settings ), preset.ToString() );
		}
		Assert.IsNull( StaffSpriteRules.BankLimitOf( null ), "no settings: every bank" );
		var missing = new GraphicsSettingsService( _ => null, GraphicsSettings.Default with { Preset = GraphicsPreset.Low }, null, () => CompatibilityFlags.Original );
		Assert.IsNull( StaffSpriteRules.BankLimitOf( missing ), "a preset without its original file has no word to read" );
	}

	[TestMethod]
	public void TheLoadedBanksFollowTheLimitAndTheDrawnBankIsTheVariantModuloTheLoadedCount()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original staff sprites." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "esprites.wad" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks esprites.wad." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var all = StaffSpriteSet.Load( "jungle" );
		var one = StaffSpriteSet.Load( "jungle", 1 );
		var two = StaffSpriteSet.Load( "jungle", 2 );
		// handymen 1, mechanics 2, guards 1, researchers 1 and the Jungle entertainers' 3 banks
		Assert.AreEqual( 8, all.Atlases.Count );
		Assert.AreEqual( 8, two.Atlases.Count, "two banks change nothing for the shipped folders" );
		Assert.AreEqual( 7, one.Atlases.Count, "the mechanics' second bank is not loaded" );
		CollectionAssert.AreEqual( new[] { "SPR_FA", "SPR_OM" }, Enumerable.Range( 0, 2 ).Select( variant => all.Atlases[all.AtlasFor( StaffType.Mechanic, variant )].Name ).ToArray(), "sorted by name" );
		Assert.AreEqual( one.AtlasFor( StaffType.Mechanic, 0 ), one.AtlasFor( StaffType.Mechanic, 1 ), "variant 1 draws the one loaded bank (the original reduces the id modulo the loaded count, 0x4F8B10)" );
		Assert.AreEqual( "SPR_FA", one.Atlases[one.AtlasFor( StaffType.Mechanic, 1 )].Name );
		Assert.AreEqual( 3, Enumerable.Range( 0, 3 ).Select( variant => one.AtlasFor( StaffType.Entertainer, variant ) ).Distinct().Count(), "entertainers load whole" );
	}
}
