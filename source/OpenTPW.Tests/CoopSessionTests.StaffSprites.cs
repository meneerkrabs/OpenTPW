using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// GUEST-016 and UI-092 (world sources) in a level: the staff's sprite sources and the staff tool's sprite step only in the sprite scheduler's pass, which tp.exe runs on the even 31 ms substeps
/// (0x0054F5D7, 0x0054F5FB) at the substep's one clock sample, the pick-up and the drop delete and make the sources, and a save keeps them.
/// </summary>
public partial class CoopSessionTests
{
	[TestMethod]
	public void StaffSourcesAndTheToolSpriteStepOnlyInTheSchedulersPassOnTheEvenSubstepsAtItsClockSample()
	{
		var park = NewPark();
		var member = StandingHandyman( park );
		Assert.IsTrue( park.Level.StaffPlacement!.PickUp( member ) is var picked && picked );
		var placement = park.Level.StaffPlacement;
		var tool = placement.ToolSprite!;
		Assert.IsNotNull( tool, "the carry tool made its dangling person" );
		Assert.IsNull( member.Sprite.Source, "the pick-up (state 7) deleted the member's own source" );
		placement.Cancel();
		var source = member.Sprite.Source!;
		Assert.IsNotNull( source, "the drop made a fresh stand source" );
		var due = source.Due;
		var stepped = 0;
		var drawn = 0;
		for ( var run = 0; run < 400; run++ )
		{
			var tick = park.Economy.Tick;
			var interval = source.Interval;
			park.Level.RunTicks( 1 );
			var substep = SimulationSubstep.ForTick( tick, running: true );
			if ( source.Due != due )
			{
				stepped++;
				Assert.IsTrue( substep.TourDue, $"tick {tick}: a source steps only in an even substep's pass" );
				Assert.AreEqual( GuestSimulation.BodySpriteSampleAtTick( tick + 1 ) + interval, source.Due, $"tick {tick}: the pass's one clock sample plus the interval" );
				due = source.Due;
			}
			if ( member.Sprite.HasOutput )
				drawn++;
		}
		Assert.IsTrue( stepped > 20, $"the source stepped {stepped} times in 400 ticks (a pass is due every 62 ms)" );
		Assert.IsTrue( drawn > 0 && member.Sprite.HasOutput, "the stand shows a frame after its first pass" );
		// the sample stands still between the even substeps: 400 ticks hold 215 substeps and 107 even ones, a source at 62 ms steps at most once in each
		Assert.IsTrue( stepped <= 108, $"{stepped} steps in 400 ticks" );
	}

	[TestMethod]
	public void TheToolsSpriteIsMadeWithTheToolShowsNothingBeforeItsFirstPassAndEndsWithIt()
	{
		var park = NewPark();
		var member = StandingHandyman( park );
		var placement = park.Level.StaffPlacement!;
		Assert.IsNull( placement.ToolSprite );
		Assert.IsTrue( placement.PickUp( member ) );
		var sprite = placement.ToolSprite!;
		Assert.AreEqual( StaffSpriteProgram.Dangling, sprite.RestartPc.Program );
		Assert.AreEqual( (5u, 62), (sprite[StaffSpriteRegister.Kind], sprite.Interval), "the handyman's category, the interval a sprite is made with" );
		Assert.IsNull( placement.ToolSpriteOutput, "a sprite shows nothing before its first scheduler pass" );
		park.Level.RunTicks( 12 );
		Assert.IsNotNull( placement.ToolSpriteOutput, "twelve ticks hold at least two substeps and a pass past the 62 ms due time" );
		Assert.AreEqual( 6, placement.ToolSpriteOutput!.Value.Slot, "slot 6, the dangling person" );
		Assert.IsTrue( placement.ToolSpriteOutput.Value.Frame is >= 0 and < 4 );
		placement.Cancel();
		Assert.IsNull( placement.ToolSprite, "the tool's exit deletes it" );
		Assert.IsNull( placement.ToolSpriteOutput );
	}

	[TestMethod]
	public void ASavedStaffWorldKeepsTheSourcesAndAFreshOneIsMadeForAnOlderSaveAtItsFirstUpdate()
	{
		var park = NewPark();
		var member = StandingHandyman( park );
		park.Level.RunTicks( 150 );
		var world = park.Level.StaffWorld!;
		var snapshot = world.CaptureSnapshot();
		var actor = snapshot.Actors.Single( item => item.Id == member.Id );
		Assert.IsNotNull( actor.Sprite );
		Assert.IsNotNull( actor.Sprite!.Source, "the standing handyman's source is saved with its clock words" );
		Assert.AreEqual( member.Sprite.CaptureSnapshot(), actor.Sprite );
		// the saved words come back as they were
		member.Sprite.Reset();
		world.RestoreSnapshot( snapshot );
		Assert.AreEqual( actor.Sprite, member.Sprite.CaptureSnapshot() );
		// a snapshot with an inconsistent sprite is refused whole, before anything is written
		var broken = new StaffWorldSimulation.StaffWorldStateSnapshot( snapshot.Actors.Select( item => item.Id == member.Id ? item with { Sprite = actor.Sprite with { PendingTemplate = 77 } } : item ).ToArray() );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => world.ValidateSnapshot( broken ) );
		// a save from before the sources were kept has none: the member's wrapper is empty until its first update makes a stand source
		var older = new StaffWorldSimulation.StaffWorldStateSnapshot( snapshot.Actors.Select( item => item with { Sprite = null } ).ToArray() );
		world.RestoreSnapshot( older );
		Assert.IsNull( member.Sprite.Source );
		park.Level.RunTicks( 20 );
		Assert.IsNotNull( member.Sprite.Source, "the first update of the thing sweep makes the stand source of the member's state" );
	}
}
