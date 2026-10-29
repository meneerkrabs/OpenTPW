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
	public void AGuestsKeptDrawIsTheCreationDrawAndTheRendererTakesItModuloTheLoadedCount()
	{
		var grid = new GuestPathGrid( 20, 10 );
		for ( var x = 0; x < 20; x++ ) grid.SetPath( x, 5, true );
		var settings = GuestTests.CreateSettings();
		var park = new GuestSimulation( grid, settings, 11 ) { ArrivalsEnabled = false };
		var guests = Enumerable.Range( 0, 40 ).Select( _ => park.SpawnArrival() ).OfType<Guest>().ToArray();
		Assert.IsTrue( guests.Length > 20 );
		foreach ( var guest in guests )
		{
			Assert.AreEqual( GuestAppearance.RawKidDraw( guest.Id ), guest.KidSetDraw, $"guest {guest.Id}: the reseed makes the creation draw a function of the id" );
			Assert.AreEqual( (int)(guest.KidSetDraw % 6), GuestAppearance.KidSet( guest, 6 ) );
			Assert.AreEqual( GuestAppearance.KidSet( guest.Id, 8 ), GuestAppearance.KidSet( guest, 8 ) );
		}
		// 6 does not divide 8: a value reduced by 8 at creation and again by 6 would be a different set for some guests
		Assert.IsTrue( guests.Any( guest => (int)(guest.KidSetDraw % 6) != (int)((guest.KidSetDraw % 8) % 6) ), "the raw draw, not its remainder, is kept" );
		Assert.AreEqual( 0, GuestAppearance.KidSet( guests[0], 0 ) );
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

	private static ulong Canonical( GuestSimulation park )
	{
		var hash = new StateHasher();
		park.AddCanonicalState( hash );
		return hash.Value;
	}
}
