using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Staff sprites: the folder and bank per role and theme, and the clip per state (tp.exe 0x763f88, 0x75a418; docs/STAFF-PLACEMENT.md). No game data needed.</summary>
[TestClass]
public class StaffSpriteTests
{
	private static StaffMember Member( StaffType type, StaffState state = StaffState.Idle ) =>
		new() { Id = 1, Type = type, NameIndex = 0, Grade = 0, State = state };

	[TestMethod]
	public void EachRoleHasItsFolderAndBanksAndEntertainersFollowTheTheme()
	{
		var expected = new Dictionary<(StaffType, string), (string, string)>
		{
			[(StaffType.Handyman, "jungle")] = ("/esprites/Generic/Handymen", "SPR_HA"),
			[(StaffType.Mechanic, "space")] = ("/esprites/Generic/Mechanics", "SPR_FA,SPR_OM"),
			[(StaffType.Guard, "fantasy")] = ("/esprites/Generic/Guards", "SPR_GU"),
			[(StaffType.Researcher, "hallow")] = ("/esprites/Generic/Researchers", "SPR_RE"),
			[(StaffType.Entertainer, "jungle")] = ("/esprites/Jungle/Entertainers", "SPR_DI,SPR_EX,SPR_NA"),
			[(StaffType.Entertainer, "fantasy")] = ("/esprites/Fantasy/Entertainers", "SPR_FL,SPR_GN,SPR_LA"),
			[(StaffType.Entertainer, "hallow")] = ("/esprites/Hallow/Entertainers", "SPR_DR,SPR_FK,SPR_HU"),
			[(StaffType.Entertainer, "space")] = ("/esprites/Space/Entertainers", "SPR_MO,SPR_SM,SPR_SP"),
		};
		foreach ( var ((type, level), (folder, banks)) in expected )
		{
			var set = StaffSpriteRules.Banks( type, level );
			Assert.IsNotNull( set, $"{type} in {level}" );
			Assert.AreEqual( folder, set.Folder, $"{type} in {level}" );
			Assert.AreEqual( banks, string.Join( ",", set.Banks ), $"{type} in {level}" );
		}
		Assert.IsNull( StaffSpriteRules.Banks( StaffType.Entertainer, "sandbox" ), "an unknown level has no theme entertainers" );
		Assert.IsNotNull( StaffSpriteRules.Banks( StaffType.Handyman, "sandbox" ) );
	}

	[TestMethod]
	public void TheVariantPicksTheBankDeterministically()
	{
		Assert.AreEqual( StaffSpriteRules.VariantKey( 7, 3 ), StaffSpriteRules.VariantKey( 7, 3 ) );
		Assert.AreEqual( 0, StaffSpriteRules.BankIndex( StaffSpriteRules.VariantKey( 4, 2 ), 3 ) );
		Assert.AreEqual( 1, StaffSpriteRules.BankIndex( 4, 3 ) );
		Assert.AreEqual( 0, StaffSpriteRules.BankIndex( 999, 1 ), "a single bank always" );
		Assert.AreEqual( -1, StaffSpriteRules.BankIndex( 5, 0 ) );
		Assert.IsTrue( StaffSpriteRules.VariantKey( int.MaxValue, int.MaxValue ) >= 0, "never negative" );
	}

	[TestMethod]
	public void HandymenPlayTheirScriptsByJob()
	{
		var handyman = Member( StaffType.Handyman );
		StaffClip? Clip( HandymanJob job, bool moving, StaffState state = StaffState.Working )
		{
			handyman.Job = job;
			handyman.State = state;
			return StaffSpriteRules.SelectClip( handyman, moving );
		}
		Assert.AreEqual( StaffSpriteRules.Stand, Clip( HandymanJob.Idle, false, StaffState.Idle ) );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( HandymanJob.Patrol, true, StaffState.Patrolling ), "the patrol walks at half rate (script 9)" );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( HandymanJob.ToLitter, true ) );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( HandymanJob.ToToilet, true ) );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( HandymanJob.ToStrike, true ) );
		Assert.AreEqual( StaffSpriteRules.Stand, Clip( HandymanJob.Patrol, false, StaffState.Patrolling ), "between two walks" );
		Assert.AreEqual( StaffSpriteRules.Sweep, Clip( HandymanJob.Sweeping, false ) );
		Assert.AreEqual( StaffSpriteRules.Sweep, Clip( HandymanJob.Cleaning, false ) );
		Assert.AreEqual( StaffSpriteRules.Strike, Clip( HandymanJob.Striking, false, StaffState.OnStrike ) );
		Assert.IsNull( Clip( HandymanJob.Resting, false, StaffState.Resting ), "a resting member has no sprite (state 3)" );
		Assert.IsNull( Clip( HandymanJob.PickedUp, false, StaffState.PickedUp ), "a carried member is the dangling sprite instead" );
		Assert.AreEqual( 5, StaffSpriteRules.Strike.Slot );
		Assert.AreEqual( 4, StaffSpriteRules.Sweep.Slot );
		Assert.AreEqual( 1, StaffSpriteRules.WalkHalf.Slot );
	}

	[TestMethod]
	public void OtherRolesPlayWalkWorkResearchAndGuardClips()
	{
		var mechanic = Member( StaffType.Mechanic, StaffState.Working );
		mechanic.BusyTicks = 5;
		Assert.AreEqual( StaffSpriteRules.Repair, StaffSpriteRules.SelectClip( mechanic, false ), "repairing: slot 4, 8 frames looping" );
		mechanic.AwaitingJobArrival = true;
		Assert.AreEqual( StaffSpriteRules.Walk, StaffSpriteRules.SelectClip( mechanic, true ) );
		Assert.AreEqual( StaffSpriteRules.Stand, StaffSpriteRules.SelectClip( mechanic, false ), "waiting for the arrival is not working yet" );
		mechanic.AwaitingJobArrival = false;
		mechanic.BusyTicks = 0;
		mechanic.State = StaffState.Idle;
		Assert.AreEqual( StaffSpriteRules.Stand, StaffSpriteRules.SelectClip( mechanic, false ) );
		Assert.AreEqual( 8, StaffSpriteRules.Repair.MaxFrames );

		var researcher = Member( StaffType.Researcher );
		Assert.AreEqual( StaffSpriteRules.Research, StaffSpriteRules.SelectClip( researcher, false ), "researching: slot 4, 4 frames then hold" );
		Assert.AreEqual( StaffSpriteRules.Walk, StaffSpriteRules.SelectClip( researcher, true ) );
		Assert.AreEqual( StaffPlayback.OnceThenHold, StaffSpriteRules.Research.Playback );

		var guard = Member( StaffType.Guard );
		Assert.AreEqual( StaffSpriteRules.Stand, StaffSpriteRules.SelectClip( guard, false ) );
		guard.Escorting = true;
		Assert.AreEqual( StaffSpriteRules.CatchAndCarry, StaffSpriteRules.SelectClip( guard, false ), "slot 7 once, then slot 8 looping" );
		guard.Escorting = false;
		guard.State = StaffState.OnStrike;
		Assert.AreEqual( StaffSpriteRules.Strike, StaffSpriteRules.SelectClip( guard, false ) );
		guard.State = StaffState.Resting;
		Assert.IsNull( StaffSpriteRules.SelectClip( guard, false ) );
	}

	[TestMethod]
	public void EntertainersPerformWhatTheirBankHasAndStandWhenIdle()
	{
		var entertainer = Member( StaffType.Entertainer );
		Assert.AreEqual( StaffSpriteRules.Stand, StaffSpriteRules.SelectClip( entertainer, false ) );
		entertainer.State = StaffState.Working;
		var bank = new[]
		{
			new SpriteAnimation( 0, 0, 1, 5 ), new SpriteAnimation( 1, 5, 8, 5 ), new SpriteAnimation( 2, 0, 0, 0 ), new SpriteAnimation( 3, 45, 1, 5 ),
			new SpriteAnimation( 4, 50, 8, 5 ), new SpriteAnimation( 5, 90, 4, 5 ), new SpriteAnimation( 6, 110, 4, 5 ),
		}.Concat( Enumerable.Range( 7, 13 ).Select( slot => new SpriteAnimation( slot, 0, 0, 0 ) ) ).ToArray();
		var clip = StaffSpriteRules.SelectClip( entertainer, false, bank )!.Value;
		Assert.AreEqual( 4, clip.Slot, "the one multi-frame performance slot of this bank" );
		var rich = bank.ToArray();
		rich[10] = new SpriteAnimation( 10, 130, 8, 5 );
		var slots = Enumerable.Range( 0, 8 ).Select( id => StaffSpriteRules.Perform( id, rich ).Slot ).Distinct().OrderBy( slot => slot ).ToArray();
		CollectionAssert.AreEqual( new[] { 4, 10 }, slots, "members pick among the bank's performances" );
		Assert.AreEqual( StaffSpriteRules.Perform( 3, rich ), StaffSpriteRules.Perform( 3, rich ), "deterministic per member" );
		Assert.AreEqual( StaffSpriteRules.Walk, StaffSpriteRules.SelectClip( entertainer, true, bank ) );
	}

	[TestMethod]
	public void ClipsPlayTheirFramesAtTheirRates()
	{
		var animations = Enumerable.Range( 0, 20 ).Select( slot => new SpriteAnimation( slot, 0, slot switch { 0 => 1, 1 => 8, 4 => 8, 5 => 4, 6 => 5, 7 => 8, 8 => 8, _ => 0 }, 5 ) ).ToArray();
		Assert.AreEqual( (0, 0), StaffSpriteRules.Resolve( StaffSpriteRules.Stand, 12f, animations ) );
		Assert.AreEqual( (1, 3), StaffSpriteRules.Resolve( StaffSpriteRules.Walk, 0.3f, animations ) );
		Assert.AreEqual( (1, 6), StaffSpriteRules.Resolve( StaffSpriteRules.Walk, 2.2f, animations ), "8-frame loop at 10 per second" );
		Assert.AreEqual( (1, 1), StaffSpriteRules.Resolve( StaffSpriteRules.WalkHalf, 0.3f, animations ), "half rate" );
		Assert.AreEqual( (5, 3), StaffSpriteRules.Resolve( StaffSpriteRules.Strike, 99f, animations ), "4 frames then hold" );
		Assert.AreEqual( (4, 7), StaffSpriteRules.Resolve( StaffSpriteRules.Repair, 0.7f, animations ) );
		Assert.AreEqual( (4, 0), StaffSpriteRules.Resolve( StaffSpriteRules.Repair, 0.8f, animations ), "the repair loops its 8 frames" );
		Assert.AreEqual( (6, 3), StaffSpriteRules.Resolve( StaffSpriteRules.Dangle, 0.3f, animations ), "the dangling sprite loops frames 0-3 of a 5-frame slot" );
		Assert.AreEqual( (6, 0), StaffSpriteRules.Resolve( StaffSpriteRules.Dangle, 0.4f, animations ) );
		Assert.AreEqual( (7, 5), StaffSpriteRules.Resolve( StaffSpriteRules.CatchAndCarry, 0.5f, animations ) );
		Assert.AreEqual( (8, 2), StaffSpriteRules.Resolve( StaffSpriteRules.CatchAndCarry, 1.0f, animations ), "the catch plays once, then the carry loops" );
		Assert.AreEqual( 6, StaffSpriteRules.Dangle.Slot );
		Assert.AreEqual( 4, StaffSpriteRules.Dangle.MaxFrames );
	}
}
