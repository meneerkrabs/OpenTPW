using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The kid-set follow-up of GUEST-017 and GUEST-020: the limit of tp.exe 0x41A9D0, the order the kid folders load in (0x541310, table 0x764030) and the draw a guest keeps (0x541F70, 0x4F93AA).
/// </summary>
[TestClass]
public class KidSetLoadTests
{
	private static readonly string[] Alphabetical = { "SPR_BE", "SPR_BI", "SPR_CH", "SPR_FR", "SPR_KI", "SPR_SA", "SPR_SU", "SPR_TA" };

	private static Stream? Presets( string path ) => path switch
	{
		"/low.sam" => new MemoryStream( Encoding.ASCII.GetBytes( "GameOptions.NUMKIDS\t0\n" ) ),
		"/med.sam" => new MemoryStream( Encoding.ASCII.GetBytes( "GameOptions.NUMKIDS\t2\n" ) ),
		"/high.sam" => new MemoryStream( Encoding.ASCII.GetBytes( "GameOptions.NUMKIDS\t2\n" ) ),
		_ => null
	};

	[TestMethod]
	public void TheLimitIsTwoFourSixOrEightByTheDetailWord()
	{
		// [BIN:TPW-EXE:0x41AA24 jump table] words 0, 1, 2, 3 -> 2, 4, 6, 8 and any word above 3 (unsigned) -> 8
		CollectionAssert.AreEqual( new[] { 2, 4, 6, 8, 8, 8, 8 }, new[] { 0, 1, 2, 3, 4, 99, -1 }.Select( KidSetLoad.Limit ).ToArray() );
	}

	[TestMethod]
	public void ThePresetsDetailWordGivesTheLimit()
	{
		foreach ( var (preset, expected) in new[] { (GraphicsPreset.Low, 2), (GraphicsPreset.Medium, 6), (GraphicsPreset.High, 6) } )
		{
			var settings = new GraphicsSettingsService( Presets, GraphicsSettings.Default with { Preset = preset }, null, () => CompatibilityFlags.Original );
			Assert.AreEqual( expected, KidSetLoad.LimitOf( settings ), preset.ToString() );
		}
		Assert.IsNull( KidSetLoad.LimitOf( null ), "no settings: every set" );
		var missing = new GraphicsSettingsService( _ => null, GraphicsSettings.Default with { Preset = GraphicsPreset.Low }, null, () => CompatibilityFlags.Original );
		Assert.IsNull( KidSetLoad.LimitOf( missing ), "a preset without its original file has no word to read" );
	}

	[TestMethod]
	public void ACustomPresetsNumKidsFallsBackToHighUnlessTheEnhancedOptionsFixIsOn()
	{
		var flags = CompatibilityFlags.Original;
		var service = new GraphicsSettingsService( Presets, GraphicsSettings.Default with { Preset = GraphicsPreset.High }, null, () => flags );
		service.Apply( new GraphicsSettings { Preset = GraphicsPreset.Custom, Detail = new OriginalDetailSettings { NumKids = 3 } } );
		Assert.AreEqual( 6, KidSetLoad.LimitOf( service ), "the raw custom value 3 is reset to High's 2 without the fix" );
		Assert.AreEqual( 2, StaffSpriteRules.BankLimitOf( service ) );
		flags = CompatibilityFlags.Custom( new[] { CompatibilityFixes.EnhancedGameOptions } );
		Assert.AreEqual( 8, KidSetLoad.LimitOf( service ), "with the fix the custom value stands" );
	}

	[TestMethod]
	public void TheFourNamedSetsLoadFirstAndTheRestFollowInNameOrder()
	{
		// [BIN:TPW-EXE:0x764030 table] BI, KI, TA, SU first; the second pass takes the others by name (BE, CH, FR, SA)
		var expected = new[] { "SPR_BI", "SPR_KI", "SPR_TA", "SPR_SU", "SPR_BE", "SPR_CH", "SPR_FR", "SPR_SA" };
		CollectionAssert.AreEqual( expected, KidSetLoad.Order( Alphabetical, name => name, null ) );
		foreach ( var limit in new[] { 2, 4, 6, 8 } )
			CollectionAssert.AreEqual( expected.Take( limit ).ToArray(), KidSetLoad.Order( Alphabetical, name => name, limit ), $"limit {limit}" );
		CollectionAssert.AreEqual( new[] { "SPR_BI" }, KidSetLoad.Order( Alphabetical, name => name, 0 ), "the test follows a load, so a limit of 0 still loads one set" );
		CollectionAssert.AreEqual( expected, KidSetLoad.Order( Alphabetical, name => name, 20 ), "a limit above the count keeps them all" );
	}

	[TestMethod]
	public void TheNameCompareIsCaseInsensitiveAndAFolderWithoutTheNamedSetsKeepsItsNameOrder()
	{
		CollectionAssert.AreEqual( new[] { "spr_ki", "SPR_AA" }, KidSetLoad.Order( new[] { "SPR_AA", "spr_ki" }, name => name, null ) );
		var other = new[] { "SPR_BD", "SPR_GB", "SPR_GK", "SPR_KB" };
		CollectionAssert.AreEqual( other, KidSetLoad.Order( other, name => name, null ) );
		CollectionAssert.AreEqual( other.Take( 2 ).ToArray(), KidSetLoad.Order( other, name => name, 2 ) );
	}

	[TestMethod]
	public void AGuestKeepsItsDrawReducedByTheSetsLoadedAndTheRendererReducesItAgain()
	{
		// [BIN:TPW-EXE:0x541F70 kid set draw] the function returns (word >> 2) mod the loaded count and 0x4FB1B5 stores that; a load reduces it again (0x4F93AA): (raw mod 8) mod 6
		GuestSimulation Park( int? sets )
		{
			var grid = new GuestPathGrid( 20, 10 );
			for ( var x = 0; x < 20; x++ ) grid.SetPath( x, 5, true );
			return new GuestSimulation( grid, GuestTests.CreateSettings(), 11 ) { ArrivalsEnabled = false, KidSetCount = sets };
		}
		var loaded8 = Park( 8 );
		var headless = Park( null );
		var withEight = Enumerable.Range( 0, 40 ).Select( _ => loaded8.SpawnArrival() ).OfType<Guest>().ToArray();
		var raw = Enumerable.Range( 0, 40 ).Select( _ => headless.SpawnArrival() ).OfType<Guest>().ToArray();
		Assert.IsTrue( withEight.Length > 20 && raw.Length == withEight.Length );
		foreach ( var guest in withEight )
		{
			var draw = GuestAppearance.RawKidDraw( guest.Id );
			Assert.AreEqual( draw % 8, guest.KidSetDraw, $"guest {guest.Id}: the stored bank is the draw modulo the eight loaded" );
			Assert.AreEqual( (int)((draw % 8) % 6), GuestAppearance.KidSet( guest, 6 ), "and a park that loads six reduces it again" );
			Assert.AreEqual( GuestAppearance.KidSet( guest.Id, 8 ), GuestAppearance.KidSet( guest, 8 ) );
		}
		foreach ( var guest in raw )
			Assert.AreEqual( GuestAppearance.RawKidDraw( guest.Id ), guest.KidSetDraw, "without a known count the raw draw is kept" );
		// 6 does not divide 8: reducing twice is not reducing once
		Assert.IsTrue( withEight.Any( guest => GuestAppearance.KidSet( guest, 6 ) != (int)(GuestAppearance.RawKidDraw( guest.Id ) % 6) ), "the double reduction differs from a single one" );
		Assert.AreEqual( 0, GuestAppearance.KidSet( withEight[0], 0 ) );
	}

	[TestMethod]
	public void TheKeptDrawIsSavedAndAnOlderSaveFallsBackToTheIdsDraw()
	{
		var grid = new GuestPathGrid( 20, 10 );
		for ( var x = 0; x < 20; x++ ) grid.SetPath( x, 5, true );
		var park = new GuestSimulation( grid, GuestTests.CreateSettings(), 11 ) { ArrivalsEnabled = false };
		var guest = park.SpawnArrival()!;
		guest.KidSetDraw = 12345;
		var hashBefore = Canonical( park );
		var bytes = ParkResumeJson.Serialize( park.CaptureSnapshot() );
		var restored = new GuestSimulation( new GuestPathGrid( 20, 10 ), GuestTests.CreateSettings(), 11 ) { ArrivalsEnabled = false };
		restored.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( bytes ) );
		Assert.AreEqual( 12345u, restored.Find( guest.Id )!.KidSetDraw );
		Assert.AreEqual( hashBefore, Canonical( restored ), "the draw is presentation: not hashed" );
		guest.KidSetDraw = 99999;
		Assert.AreEqual( hashBefore, Canonical( park ), "the draw does not reach the canonical state" );

		var json = JsonNode.Parse( bytes )!;
		foreach ( var item in json["Guests"]!.AsArray() )
			item!.AsObject().Remove( "KidSetDraw" );
		var old = new GuestSimulation( new GuestPathGrid( 20, 10 ), GuestTests.CreateSettings(), 11 ) { ArrivalsEnabled = false };
		old.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( json.ToJsonString() ) ) );
		Assert.AreEqual( GuestAppearance.RawKidDraw( guest.Id ), old.Find( guest.Id )!.KidSetDraw, "an older save has no draw: the id's own" );

		var tooBig = JsonNode.Parse( bytes )!;
		tooBig["Guests"]![0]!.AsObject()["KidSetDraw"] = 4294967296L;
		Assert.IsFalse( park.ValidateSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( tooBig.ToJsonString() ) ) ).IsValid );
		tooBig["Guests"]![0]!.AsObject()["KidSetDraw"] = -1L;
		Assert.IsFalse( park.ValidateSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( Encoding.UTF8.GetBytes( tooBig.ToJsonString() ) ) ).IsValid );
	}

	[TestMethod]
	public void ALoadedBankIsReducedBeforeResavingAndDoesNotReturnWhenMoreSetsLoad()
	{
		GuestSimulation Park( int? count )
		{
			var grid = new GuestPathGrid( 20, 10 );
			for ( var x = 0; x < 20; x++ ) grid.SetPath( x, 5, true );
			return new GuestSimulation( grid, GuestTests.CreateSettings(), 11 ) { ArrivalsEnabled = false, KidSetCount = count };
		}
		var source = Park( 8 );
		var guest = source.SpawnArrival()!;
		guest.KidSetDraw = 7;
		var snapshot = source.CaptureSnapshot();
		foreach ( var count in new int?[] { 6, 2, null, 0 } )
		{
			var loaded = Park( count );
			loaded.RestoreSnapshot( snapshot );
			var expected = count is > 0 ? 7u % (uint)count.Value : 7u;
			Assert.AreEqual( expected, loaded.Find( guest.Id )!.KidSetDraw, "a known count reduces the stored bank; headless/empty counts leave it alone" );
			Assert.AreEqual( Canonical( source ), Canonical( loaded ), "presentation normalization must not change canonical state" );
			var bytes = ParkResumeJson.Serialize( loaded.CaptureSnapshot() );
			var moreSets = Park( 8 );
			moreSets.RestoreSnapshot( ParkResumeJson.Deserialize<GuestSimulationSnapshot>( bytes ) );
			Assert.AreEqual( expected, moreSets.Find( guest.Id )!.KidSetDraw, "the second save keeps the reduced bank" );
			Assert.AreEqual( (int)expected, GuestAppearance.KidSet( moreSets.Find( guest.Id )!, 8 ) );
		}
		guest.InCostume = true;
		var costume = Park( 6 );
		costume.RestoreSnapshot( source.CaptureSnapshot() );
		Assert.AreEqual( 7u, costume.Find( guest.Id )!.KidSetDraw, "the saved kid value is not a costume guest's current sprite bank" );
	}

	private static ulong Canonical( GuestSimulation park )
	{
		var hash = new StateHasher();
		park.AddCanonicalState( hash );
		return hash.Value;
	}
}

