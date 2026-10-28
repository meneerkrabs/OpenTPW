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
			Assert.AreEqual( candidate.Variant, StaffSpriteRules.KeyOf( candidate ) );
		}
		var hired = EconomyTestData.HireBest( park, StaffType.Mechanic );
		Assert.IsTrue( hired.Variant >= 0 );
		Assert.AreEqual( hired.Variant, StaffSpriteRules.KeyOf( hired ), "the member draws from the candidate's bank" );
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
}
