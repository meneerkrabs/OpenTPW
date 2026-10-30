using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A candidate's sprite variant (tp.exe candidate +9, drawn by 0x541F70 as (R >> 2) mod the category's bank count) is kept, saved, taken from an original save's pool sub-type and handed to the member
/// the candidate becomes (SAVE-004, GUEST-014); the member's bank follows it.
/// </summary>
[TestClass]
public class StaffVariantTests
{
	[TestMethod]
	public void ANewPoolsCandidatesHoldADrawnVariantTheirMembersKeep()
	{
		var park = EconomyTestData.Park();
		Assert.IsTrue( park.Staff.Candidates.Count > 0 );
		foreach ( var candidate in park.Staff.Candidates )
		{
			var banks = StaffSpriteRules.Banks( candidate.Type, park.Settings.Theme )!.Banks.Count;
			Assert.IsTrue( candidate.Variant >= 0 && candidate.Variant < banks, $"{candidate.Type} variant {candidate.Variant} of {banks} banks" );
			// a pool made before any bank is loaded takes the draw modulo the folder's banks (a byte, as the original's candidate +9), so it is the variant
			Assert.AreEqual( candidate.Variant, candidate.SpriteDraw );
			Assert.AreEqual( candidate.Variant, StaffSpriteRules.KeyOf( candidate ) % banks );
		}
		var hired = EconomyTestData.HireBest( park, StaffType.Mechanic );
		Assert.IsTrue( hired.Variant >= 0 );
		Assert.AreEqual( hired.Variant, StaffSpriteRules.KeyOf( hired ) % StaffSpriteRules.Banks( StaffType.Mechanic, park.Settings.Theme )!.Banks.Count, "the member draws from the candidate's bank" );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( ParkSaveFile.Serialize( park ) ) ), park.Settings, park.Catalog );
		Assert.AreEqual( hired.Variant, restored.Staff.Members.Single().Variant, "a park save keeps the member's variant" );
		CollectionAssert.AreEqual( park.Staff.Candidates.Select( candidate => candidate.Variant ).ToArray(), restored.Staff.Candidates.Select( candidate => candidate.Variant ).ToArray(), "and the pool's" );
	}

	[TestMethod]
	public void ASaveWithANegativeStaffVariantIsRefused()
	{
		var park = EconomyTestData.Park();
		var hired = EconomyTestData.HireBest( park, StaffType.Mechanic );
		var json = ParkSaveFile.Serialize( park );
		var pattern = new System.Text.RegularExpressions.Regex( "\"Variant\":\\s*" + hired.Variant + "(?=\\D)" );
		Assert.IsTrue( pattern.IsMatch( json ), "the member's variant is saved" );
		var broken = pattern.Replace( json, "\"Variant\": -3" );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( broken ) ) );
	}

	[TestMethod]
	public void ACandidateOrMemberWithoutAVariantDerivesItsBankFromItsNameAndId()
	{
		var candidate = new StaffCandidate( 7, StaffType.Guard, 3, 1, 0 );
		Assert.AreEqual( -1, candidate.Variant );
		Assert.AreEqual( StaffSpriteRules.VariantKey( 3, 7 ), StaffSpriteRules.KeyOf( candidate ) );
		var member = new StaffMember { Id = 8, Type = StaffType.Guard, NameIndex = 4, Grade = 1 };
		Assert.AreEqual( StaffSpriteRules.VariantKey( 4, 8 ), StaffSpriteRules.KeyOf( member ), "a member of an OpenTPW park save from before the variant was kept holds no variant" );
	}

	[TestMethod]
	public void AnOriginalPoolsSubTypesAreTheCandidatesVariantsWhenTheRoleHasThatBank()
	{
		var park = EconomyTestData.Park();
		var entertainerBanks = StaffSpriteRules.Banks( StaffType.Entertainer, park.Settings.Theme )!.Banks.Count;
		park.Staff.RestoreOriginalPool( new[]
		{
			(StaffType.Entertainer, 1, 2, 5L, 100L, 2),
			(StaffType.Entertainer, 2, 2, 5L, 100L, entertainerBanks),
			(StaffType.Handyman, 3, 1, 5L, 100L, 0),
			(StaffType.Handyman, 4, 1, 5L, 100L, 1)
		}, 5 );
		CollectionAssert.AreEqual( new[] { 2, -1, 0, -1 }, park.Staff.Candidates.Select( candidate => candidate.Variant ).ToArray(), "a sub-type past the role's banks is no variant, and the handyman has one bank" );
	}

	[TestMethod]
	public void EveryCorpusSavesPoolKeepsItsSubTypesAsVariants()
	{
		OriginalSaveImportCorpusTests.ForEachSave( ( name, save, economy, import ) =>
		{
			var saved = save.World.StaffPool!.Candidates.Where( candidate => candidate.Valid ).ToList();
			Assert.AreEqual( saved.Count, economy.Staff.Candidates.Count, name );
			for ( var index = 0; index < saved.Count; index++ )
			{
				var banks = StaffSpriteRules.Banks( economy.Staff.Candidates[index].Type, economy.Settings.Theme )!.Banks.Count;
				Assert.AreEqual( saved[index].SubType < banks ? saved[index].SubType : -1, economy.Staff.Candidates[index].Variant, $"{name}: candidate {index}" );
			}
		} );
	}

	[TestMethod]
	public void AMemberOfAnOriginalSaveKeepsTheSavedSpriteVariant()
	{
		// [BIN:TPW-EXE:0x4F8B10] the person record's mSpriteID reduced by the category's bank count: a sprite id past the role's banks wraps
		var park = EconomyTestData.Park();
		var banks = StaffSpriteRules.Banks( StaffType.Mechanic, park.Settings.Theme )!.Banks.Count;
		Assert.IsTrue( banks >= 2 );
		var member = park.Staff.AddImported( StaffType.Mechanic, 1, 0, 50f, StaffState.Idle, 0, thingId: 77, variant: (int)(7u % (uint)banks) );
		Assert.AreEqual( (int)(7u % (uint)banks), member.Variant );
		Assert.AreEqual( member.Variant, StaffSpriteRules.KeyOf( member ) );
		Assert.AreEqual( StaffSpriteRules.BankIndex( StaffSpriteRules.KeyOf( member ), banks ), member.Variant, "the bank is the saved variant's" );
	}

	[TestMethod]
	public void EveryCorpusSavesStaffKeepTheirSavedSpriteVariant()
	{
		OriginalSaveImportCorpusTests.ForEachSave( ( name, save, economy, import ) =>
		{
			foreach ( var saved in save.Staff )
			{
				var member = economy.Staff.Members.Single( candidate => candidate.ThingId == saved.Id );
				var banks = StaffSpriteRules.Banks( member.Type, economy.Settings.Theme )!.Banks.Count;
				Assert.AreEqual( (int)(saved.Person.SpriteId % (uint)banks), member.Variant, $"{name}: staff thing {saved.Id}" );
			}
		} );
	}

	internal sealed class Loaded( params int[] counts ) : IStaffSpriteBanks
	{
		// the counts by role: handyman, mechanic, entertainer, guard, researcher (the StaffType order)
		public SpriteAnimationFile? AnimationsOf( StaffType type, int variantKey ) => null;
		public int BankCount( StaffType type ) => counts[(int)type];
	}

	[TestMethod]
	public void ADrawIsReducedByTheLoadedCountOfItsCategoryAtCreationAndAgainAtEveryLoad()
	{
		// [BIN:TPW-EXE:0x00541F70 / 0x004F93AA] (R >> 2) % the loaded count when a candidate is made, a member's stored id % the loaded count at every load (the person serializer 0x004F8B10): a lower count wraps the draw for good; the staff constructors copy a candidate's +9 raw (0x004D6BED to 0x004D6C17), so a load leaves the pool's draws alone
		var park = EconomyTestData.Park();
		var raw = park.Staff.Candidates.Select( candidate => candidate.SpriteDraw!.Value ).ToArray();
		Assert.IsTrue( raw.All( draw => draw is >= 0 and < 256 ), "a pool made before the banks are loaded holds draws a byte can hold" );
		CollectionAssert.AreEqual( park.Staff.Candidates.Select( candidate => candidate.Variant ).ToArray(), raw, "and they are the variants" );
		var one = new Loaded( Enumerable.Repeat( 1, 5 ).ToArray() );
		var two = new Loaded( Enumerable.Repeat( 2, 5 ).ToArray() );
		park.Staff.AttachSpriteBanks( one );
		CollectionAssert.AreEqual( raw, park.Staff.Candidates.Select( candidate => candidate.SpriteDraw!.Value ).ToArray(), "a load does not reduce the pool's candidates" );
		// a member hired from a pool keeps the candidate's draw as it is; the level's banks then reduce it, and a lower count wraps it for good
		var other = EconomyTestData.Park();
		var mechanic = EconomyTestData.HireBest( other, StaffType.Mechanic );
		other.Staff.AttachSpriteBanks( two );
		Assert.IsTrue( mechanic.SpriteDraw is 0 or 1 );
		Assert.AreEqual( mechanic.SpriteDraw, StaffSpriteRules.KeyOf( mechanic ) );
		other.Staff.AttachSpriteBanks( one );
		Assert.AreEqual( 0, mechanic.SpriteDraw );
		Assert.AreEqual( 0, StaffSpriteRules.KeyOf( mechanic ) );
		other.Staff.AttachSpriteBanks( two );
		Assert.AreEqual( 0, mechanic.SpriteDraw, "the second bank is gone for good" );
		// a candidate made with banks loaded takes (R >> 2) mod the loaded count
		var made = new ParkStaff( EconomyTestData.Settings() );
		made.AttachSpriteBanks( two );
		made.CreateInitialPool( new WorldRng( 0x1234 ), 0 );
		var rawPool = new ParkStaff( EconomyTestData.Settings() );
		rawPool.CreateInitialPool( new WorldRng( 0x1234 ), 0 );
		Assert.IsTrue( made.Candidates.All( candidate => candidate.SpriteDraw is 0 or 1 ), "a candidate made with two banks loaded takes (R >> 2) mod 2" );
		Assert.AreEqual( rawPool.Candidates.Count, made.Candidates.Count );
		// no loaded bank of a role (an unloaded folder) leaves its draws as they are
		var unloaded = EconomyTestData.Park();
		unloaded.Staff.AttachSpriteBanks( new Loaded( 0, 0, 0, 0, 0 ) );
		CollectionAssert.AreEqual( raw, unloaded.Staff.Candidates.Select( candidate => candidate.SpriteDraw!.Value ).ToArray() );
	}

	[TestMethod]
	public void TheStoredDrawIsPresentationAndNeverMovesTheWorldHash()
	{
		var plain = EconomyTestData.Park();
		var reduced = EconomyTestData.Park();
		foreach ( var park in new[] { plain, reduced } )
			park.Staff.AddImported( StaffType.Mechanic, 1, 0, 50f, StaffState.Idle, 0, thingId: 77, variant: 1, spriteDraw: 3 );
		reduced.Staff.AttachSpriteBanks( new Loaded( Enumerable.Repeat( 1, 5 ).ToArray() ) );
		var before = WorldStateHash.Compute( new WorldStateSources { Economy = plain } );
		Assert.AreNotEqual( ParkSaveFile.Serialize( plain ), ParkSaveFile.Serialize( reduced ), "the saves hold different draws" );
		Assert.AreEqual( before, WorldStateHash.Compute( new WorldStateSources { Economy = reduced } ), "peers with other presets hash alike" );
		var hired = EconomyTestData.HireBest( reduced, StaffType.Guard );
		var twin = EconomyTestData.HireBest( plain, StaffType.Guard );
		Assert.AreEqual( WorldStateHash.Compute( new WorldStateSources { Economy = plain } ), WorldStateHash.Compute( new WorldStateSources { Economy = reduced } ) );
		Assert.AreEqual( twin.Variant, hired.Variant, "the hashed variant is the folder's, whatever is loaded" );
	}

	[TestMethod]
	public void AnOriginalSavesSpriteIdIsTheStoredDrawTheLevelReducesByTheLoadedCount()
	{
		var park = EconomyTestData.Park();
		var member = park.Staff.AddImported( StaffType.Mechanic, 1, 0, 50f, StaffState.Idle, 0, thingId: 77, variant: 1, spriteDraw: 3 );
		Assert.AreEqual( 3, StaffSpriteRules.KeyOf( member ), "before the banks are known the id as saved" );
		park.Staff.AttachSpriteBanks( new Loaded( 1, 2, 3, 1, 1 ) );
		Assert.AreEqual( 1, member.SpriteDraw, "3 % the two mechanic banks loaded" );
		Assert.AreEqual( 1, StaffSpriteRules.KeyOf( member ) );
	}

	[TestMethod]
	public void TheStoredDrawIsSavedOptionalAndValidatedAndAnOlderSaveHasNone()
	{
		var park = EconomyTestData.Park();
		park.Staff.AttachSpriteBanks( new Loaded( 1, 2, 3, 1, 1 ) );
		var member = EconomyTestData.HireBest( park, StaffType.Entertainer );
		var json = ParkSaveFile.Serialize( park );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), park.Settings, park.Catalog );
		Assert.AreEqual( member.SpriteDraw, restored.Staff.Members.Single().SpriteDraw );
		CollectionAssert.AreEqual( park.Staff.Candidates.Select( candidate => candidate.SpriteDraw ).ToArray(), restored.Staff.Candidates.Select( candidate => candidate.SpriteDraw ).ToArray() );
		// a save from before the draw was kept has none: the bank comes from the variant, as it did
		string Edit( Action<System.Text.Json.Nodes.JsonObject> change )
		{
			var node = System.Text.Json.Nodes.JsonNode.Parse( json )!.AsObject();
			foreach ( var list in new[] { "Members", "Candidates" } )
				foreach ( var entry in node["Staff"]![list]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>() )
					change( entry );
			return node.ToJsonString();
		}
		var older = Edit( entry => entry.Remove( "SpriteDraw" ) );
		Assert.IsFalse( older.Contains( "SpriteDraw" ) );
		var loaded = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( older ) ), park.Settings, park.Catalog );
		Assert.AreEqual( loaded.Staff.Members.Single().Variant, loaded.Staff.Members.Single().SpriteDraw, "without a saved draw the variant stands in for it (a draw equal to the variant is not written)" );
		Assert.AreEqual( loaded.Staff.Members.Single().Variant, StaffSpriteRules.KeyOf( loaded.Staff.Members.Single() ) );
		Assert.IsTrue( loaded.Staff.Candidates.All( candidate => candidate.SpriteDraw == (candidate.Variant >= 0 ? candidate.Variant : null) ) );
		// a negative draw is refused, a member's and a candidate's
		var broken = Edit( entry => entry["SpriteDraw"] = -2 );
		Assert.ThrowsException<InvalidDataException>( () => ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( broken ) ) );
	}
}
