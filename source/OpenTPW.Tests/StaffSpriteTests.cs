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
		StaffClip? Clip( StaffJob job, bool moving, StaffState state = StaffState.Working )
		{
			handyman.Job = job;
			handyman.State = state;
			return StaffSpriteRules.SelectClip( handyman, moving );
		}
		Assert.AreEqual( StaffSpriteRules.Stand, Clip( StaffJob.Idle, false, StaffState.Idle ) );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( StaffJob.Patrol, true, StaffState.Patrolling ), "the patrol walks at half rate (script 9)" );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( StaffJob.ToLitter, true ) );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( StaffJob.ToToilet, true ) );
		Assert.AreEqual( StaffSpriteRules.WalkHalf, Clip( StaffJob.ToStrike, true ) );
		Assert.AreEqual( StaffSpriteRules.Stand, Clip( StaffJob.Patrol, false, StaffState.Patrolling ), "between two walks" );
		Assert.AreEqual( StaffSpriteRules.Sweep, Clip( StaffJob.Sweeping, false ) );
		Assert.AreEqual( StaffSpriteRules.Sweep, Clip( StaffJob.Cleaning, false ) );
		Assert.AreEqual( StaffSpriteRules.Strike, Clip( StaffJob.Striking, false, StaffState.OnStrike ) );
		Assert.IsNull( Clip( StaffJob.Resting, false, StaffState.Resting ), "a resting member has no sprite (state 3)" );
		Assert.IsNull( Clip( StaffJob.PickedUp, false, StaffState.PickedUp ), "a carried member is the dangling sprite instead" );
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
		Assert.AreEqual( 8, StaffScripts.Get( StaffSpriteRules.Repair.Script )!.Frames.Count );

		var researcher = Member( StaffType.Researcher );
		Assert.AreEqual( StaffSpriteRules.Stand, StaffSpriteRules.SelectClip( researcher, false ) );
		researcher.Job = StaffJob.Researching;
		Assert.AreEqual( StaffSpriteRules.Research, StaffSpriteRules.SelectClip( researcher, false ), "researching: slot 4, four frames then four steps of the first, looping" );
		Assert.AreEqual( StaffSpriteRules.Walk, StaffSpriteRules.SelectClip( researcher, true ) );

		var guard = Member( StaffType.Guard );
		Assert.AreEqual( StaffSpriteRules.Stand, StaffSpriteRules.SelectClip( guard, false ) );
		guard.Job = StaffJob.Escorting;
		Assert.AreEqual( StaffSpriteRules.CatchAndCarry, StaffSpriteRules.SelectClip( guard, false ), "slot 7 once, then slot 8 looping" );
		guard.Job = StaffJob.Chasing;
		Assert.AreEqual( StaffSpriteRules.Run, StaffSpriteRules.SelectClip( guard, true ), "a chasing guard runs (script 2)" );
		guard.Job = StaffJob.Idle;
		guard.State = StaffState.OnStrike;
		Assert.AreEqual( StaffSpriteRules.Strike, StaffSpriteRules.SelectClip( guard, false ) );
		guard.State = StaffState.Resting;
		Assert.IsNull( StaffSpriteRules.SelectClip( guard, false ) );
	}

	[TestMethod]
	public void EntertainersPerformTheirBanksEntryAndStandWhenIdle()
	{
		var entertainer = Member( StaffType.Entertainer );
		Assert.AreEqual( StaffSpriteRules.Stand, StaffSpriteRules.SelectClip( entertainer, false ) );
		entertainer.Job = StaffJob.Performing;
		// Every shipped entertainer bank: one entry, 01 05 00 00 (program 0 on slot 4); tp.exe's script choice is 13 when n is 1.
		var shipped = new[] { new SpritePerformance( 0, 4, 0, 0 ), new SpritePerformance( -1, -1, 0, 0 ), new SpritePerformance( -1, -1, 0, 0 ), new SpritePerformance( -1, -1, 0, 0 ) };
		var clip = StaffSpriteRules.SelectClip( entertainer, false, shipped, 1 )!.Value;
		Assert.AreEqual( (13, 4), (clip.Script, clip.Slot) );
		Assert.IsTrue( Enumerable.Range( 0, 8 ).All( id => StaffSpriteRules.Perform( (uint)id * 2654435761u, shipped, 1 ).Slot == 4 ), "every draw plays the one entry" );
		// A bank without entries performs slot 4 too.
		Assert.AreEqual( 4, StaffSpriteRules.SelectClip( entertainer, false )!.Value.Slot );
		// Several entries (none shipped): script 13 + (r mod (n - 1)), so the last entry is never played.
		var four = new[] { new SpritePerformance( 0, 4, 0, 0 ), new SpritePerformance( 0, 10, 0, 0 ), new SpritePerformance( 0, 3, 0, 0 ), new SpritePerformance( 0, 11, 0, 0 ) };
		var slots = Enumerable.Range( 0, 9 ).Select( draw => StaffSpriteRules.Perform( (uint)draw, four, 4 ).Slot ).Distinct().OrderBy( slot => slot ).ToArray();
		CollectionAssert.AreEqual( new[] { 3, 4, 10 }, slots, "entries 0..2 of four" );
		// the entry is the performance's own draw (tp.exe 0x4D47D8), not the member: two entertainers with the same draw play the same entry and one member's next performance may change it
		entertainer.PerformanceDraw = 1;
		var second = Member( StaffType.Entertainer );
		second.Job = StaffJob.Performing;
		second.PerformanceDraw = 1;
		Assert.AreEqual( (14, 10), (StaffSpriteRules.SelectClip( entertainer, false, four, 4 )!.Value.Script, StaffSpriteRules.SelectClip( entertainer, false, four, 4 )!.Value.Slot) );
		Assert.AreEqual( StaffSpriteRules.SelectClip( entertainer, false, four, 4 ), StaffSpriteRules.SelectClip( second, false, four, 4 ) );
		entertainer.PerformanceDraw = 5;
		Assert.AreEqual( (15, 3), (StaffSpriteRules.SelectClip( entertainer, false, four, 4 )!.Value.Script, StaffSpriteRules.SelectClip( entertainer, false, four, 4 )!.Value.Slot), "5 mod 3 = 2" );
		Assert.AreEqual( StaffSpriteRules.Walk, StaffSpriteRules.SelectClip( entertainer, true, shipped, 1 ) );
	}

	[TestMethod]
	public void ClipsPlayTheScriptsFramesStepByStep()
	{
		var animations = Enumerable.Range( 0, 20 ).Select( slot => new SpriteAnimation( slot, 0, slot switch { 0 => 1, 1 => 8, 4 => 8, 5 => 4, 6 => 5, 7 => 8, 8 => 8, _ => 0 }, 5 ) ).ToArray();
		(int, int) At( StaffClip clip, int steps ) => StaffSpriteRules.ResolveSteps( clip, steps, animations );
		Assert.AreEqual( (0, 0), At( StaffSpriteRules.Stand, 99 ) );
		Assert.AreEqual( (1, 3), At( StaffSpriteRules.Walk, 3 ) );
		Assert.AreEqual( (1, 0), At( StaffSpriteRules.Walk, 8 ), "8 frames, looping" );
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 1, 2, 2, 3, 3 }, Enumerable.Range( 0, 8 ).Select( step => At( StaffSpriteRules.WalkHalf, step ).Item2 ).ToArray(), "the patrol walk shows each frame twice (script 9)" );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 0, 0, 0, 0, 0, 1 }, Enumerable.Range( 0, 10 ).Select( step => At( StaffSpriteRules.Research, step ).Item2 ).ToArray(), "researching loops: 4 frames then four steps of frame 0" );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 1 }, Enumerable.Range( 0, 12 ).Select( step => At( StaffSpriteRules.Strike, step ).Item2 ).ToArray(), "striking loops: 4 frames then six steps of frame 0" );
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 1, 2, 2, 3, 3, 0 }, Enumerable.Range( 0, 9 ).Select( step => At( StaffSpriteRules.Sweep, step ).Item2 ).ToArray() );
		Assert.AreEqual( (4, 7), At( StaffSpriteRules.Repair, 7 ) );
		Assert.AreEqual( (4, 0), At( StaffSpriteRules.Repair, 8 ), "the repair loops its 8 frames" );
		Assert.AreEqual( (6, 3), At( StaffSpriteRules.Dangle, 3 ), "the dangling sprite shows frames 0-3 of a 5-frame slot" );
		Assert.AreEqual( (6, 0), At( StaffSpriteRules.Dangle, 4 ) );
		Assert.AreEqual( (7, 5), At( StaffSpriteRules.CatchAndCarry, 5 ) );
		Assert.AreEqual( (8, 2), At( StaffSpriteRules.CatchAndCarry, 10 ), "the catch plays once, then the carry (script 0x17) loops" );
		Assert.AreEqual( (8, 0), At( StaffSpriteRules.CatchAndCarry, 16 ) );
		Assert.AreEqual( 6, StaffSpriteRules.Dangle.Slot );
		Assert.AreEqual( (6, 3), StaffSpriteRules.Resolve( StaffSpriteRules.Dangle, 0.2f, animations ), "62 ms a step when the sprite keeps the interval it was created with" );
	}

	[TestMethod]
	public void ASpritePlayerStepsOncePerTickPastItsDueTimeWithoutCatchingUp()
	{
		var player = new SpritePlayer( 1000 );
		player.Tick( 1062 );
		Assert.AreEqual( 0, player.Steps, "the due time is the creation plus 62 ms and the clock must pass it" );
		player.Tick( 1063 );
		Assert.AreEqual( 1, player.Steps );
		player.Tick( 1300 );
		Assert.AreEqual( 2, player.Steps, "one step per tick however late" );
		player.Tick( 1301 );
		Assert.AreEqual( 2, player.Steps, "the next due time is the tick's clock plus the interval" );
		player.SetInterval( 0 );
		Assert.AreEqual( 62, player.Interval, "0 asks for no change" );
		player.SetInterval( 100 );
		player.Tick( 1363 );
		Assert.AreEqual( 3, player.Steps );
		player.Tick( 1400 );
		Assert.AreEqual( 3, player.Steps, "the 100 ms interval is due at 1463" );
		player.Restart();
		Assert.AreEqual( 0, player.Steps );
	}

	[TestMethod]
	public void AWalkStepAsksForSixtyTwoOverTheSpeedTruncatedAndCapped()
	{
		// v = length (16.16, a park turn) x 3.829657e-05, doubled for the walk script: 0.4 cell = 26214 units, v = 1.0039 (run) or 2.0078 (walk)
		Assert.AreEqual( 61, SpritePlayer.IntervalFor( 26214f, run: true ) );
		Assert.AreEqual( 30, SpritePlayer.IntervalFor( 26214f, run: false ) );
		Assert.AreEqual( 250, SpritePlayer.IntervalFor( 100f, run: false ), "capped at 250 ms" );
		Assert.AreEqual( 0, SpritePlayer.IntervalFor( 0f, run: false ), "no movement asks for no change" );
		Assert.AreEqual( 0, SpritePlayer.IntervalFor( 1e7f, run: true ), "62 over a speed above 62 truncates to 0: no change" );
		// steps per cell: a turn is 248 ms, so a walk shows 248 / 30 = 8.3 steps for 0.4 cell: about 20 a cell; a run about 10
		Assert.AreEqual( 20f, 0.248f / (SpritePlayer.IntervalFor( 26214f, run: false ) / 1000f) / 0.4f, 1f );
	}

	[TestMethod]
	public void APerformanceProgramPlaysItsIntroOnceThenLoopsTheFramesAndTheHold()
	{
		int[] Frames( int intro, int hold, int count, int steps ) => Enumerable.Range( 0, steps ).Select( step => StaffScripts.PerformanceFrame( intro, hold, count, step ) ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 0, 1, 2, 3 }, Frames( 0, 0, 4, 8 ), "every shipped entry but one: the slot's frames, looping" );
		// Hallow SPR_DR: intro 2, hold 40 on an 8-frame slot: frames 0-2 once, then 2-7 and 41 steps of frame 0, round and round
		var dr = Frames( 2, 40, 8, 3 + 47 + 7 );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, dr.Take( 3 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 2, 3, 4, 5, 6, 7 }, dr.Skip( 3 ).Take( 6 ).ToArray() );
		Assert.IsTrue( dr.Skip( 9 ).Take( 41 ).All( frame => frame == 0 ), "41 steps of frame 0 (the register runs 0 to 40 inclusive)" );
		CollectionAssert.AreEqual( new[] { 2, 3, 4, 5, 6, 7 }, dr.Skip( 50 ).Take( 6 ).ToArray(), "the next pass starts at the intro frame again" );
		Assert.AreEqual( 0, StaffScripts.PerformanceFrame( 0, 0, 0, 5 ) );
	}
}
