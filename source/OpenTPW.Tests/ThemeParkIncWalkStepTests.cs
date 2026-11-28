using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Theme Park Inc's walk step 0x60B460 and the native stamp counter 0x5D81F0 (NV3, docs/reverse/TPI-NAVIGATOR.md): the heading word on arrival (D1), only kids and guards run (D2), the interval at
/// the control word 0x7F, the counter that counts every simulation call with Theme Park Inc's navigator and changes no comparison of a Theme Park World run, and the level's switch.
/// </summary>
[TestClass]
public class ThemeParkIncWalkStepTests
{
	private sealed record Golden( int Walk, int Kind, int Generation, int Bonus, string Digest, string First );

	/// <summary>
	/// Game.exe's walk step on the navigator tests' map (<c>tools/ppc-analysis/oracle_x86/tpi_walk_step_golden.py</c>): per step "result:heading:template:interval" in hex, '-' where nothing is
	/// written, the SHA-256 of all steps and the first twelve. The restart test answers 0, so every walking step queues its template.
	/// </summary>
	private static readonly Golden[] Expected =
	[
		new( 0, 1, 0, 25, "79f9bb9274da80aebccf2fb6f7be2cc403b102ce704d90b1e350865a2fc28e1a", "1:47b:2:7f,1:468:2:7a,1:45f:2:7a,1:45b:2:79,1:459:2:79,1:458:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a" ),
		new( 0, 1, 1, 25, "bbcca602fd3919992bf52dc6f5cfb1de8a783bb93d50bfa2fc1c1c57e4b54833", "1:47b:1:3f,1:468:1:3d,1:45f:1:3d,1:45b:1:3c,1:459:1:3c,1:458:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d" ),
		new( 0, 7, 0, 25, "79f9bb9274da80aebccf2fb6f7be2cc403b102ce704d90b1e350865a2fc28e1a", "1:47b:2:7f,1:468:2:7a,1:45f:2:7a,1:45b:2:79,1:459:2:79,1:458:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a,1:457:2:7a" ),
		new( 0, 5, 0, 0, "bbcca602fd3919992bf52dc6f5cfb1de8a783bb93d50bfa2fc1c1c57e4b54833", "1:47b:1:3f,1:468:1:3d,1:45f:1:3d,1:45b:1:3c,1:459:1:3c,1:458:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d,1:457:1:3d" ),
		new( 1, 1, 0, 25, "760160736d0071b02243d8b9547618a77131c6de73385c199fcbe985b3ac8b66", "1:12f:2:d1,1:12f:2:cf,1:12f:2:ce,1:12f:2:cd,1:12f:2:cd,1:12f:2:cc,1:12f:2:cd,1:130:2:ce,1:9d:2:ce,1:53:2:cc,1:2e:2:ce,1:1b:2:ce" ),
		new( 1, 1, 1, 25, "f6a71bcbe775eded60835573871bb3b99cbfb1a2185f23f1c72b9768acf49843", "1:12f:1:68,1:12f:1:67,1:12f:1:67,1:12f:1:66,1:12f:1:66,1:12f:1:66,1:12f:1:66,1:130:1:67,1:9d:1:67,1:53:1:66,1:2e:1:67,1:1b:1:67" ),
		new( 1, 7, 0, 25, "760160736d0071b02243d8b9547618a77131c6de73385c199fcbe985b3ac8b66", "1:12f:2:d1,1:12f:2:cf,1:12f:2:ce,1:12f:2:cd,1:12f:2:cd,1:12f:2:cc,1:12f:2:cd,1:130:2:ce,1:9d:2:ce,1:53:2:cc,1:2e:2:ce,1:1b:2:ce" ),
		new( 1, 5, 0, 0, "f6a71bcbe775eded60835573871bb3b99cbfb1a2185f23f1c72b9768acf49843", "1:12f:1:68,1:12f:1:67,1:12f:1:67,1:12f:1:66,1:12f:1:66,1:12f:1:66,1:12f:1:66,1:130:1:67,1:9d:1:67,1:53:1:66,1:2e:1:67,1:1b:1:67" ),
	];

	/// <summary>The steps of one walk as the walk step writes them, in the golden's text.</summary>
	private static List<string> Steps( int walk, int kind, int generation, int bonus )
	{
		var (x, y, vx, vy, mask, speed, force, tx, ty, count) = ThemeParkIncNavigatorTests.Walks[walk];
		var map = ThemeParkIncNavigatorTests.Map();
		var navigation = new GuestNavigation { X = x, Y = y, VelocityX = vx, VelocityY = vy, Mask = mask, MaxSpeed = speed, MaxForce = force };
		navigation.Navigate( map, tx, ty );
		var steps = new List<string>();
		for ( var step = 0; step < count; step++ )
		{
			var (x0, y0) = (navigation.X, navigation.Y);
			var result = navigation.Step( navigation.MaxSpeed, navigation.MaxForce, map, static ( _, _ ) => false );
			var (x1, y1) = (navigation.X, navigation.Y);
			var heading = result == WalkResult.Stuck ? "-" : $"{ThemeParkIncWalkStep.Heading( x0, y0, x1, y1 ):x}";
			var (template, interval) = ("-", "-");
			if ( result == WalkResult.Walking )
			{
				var run = ThemeParkIncWalkStep.Runs( bonus, kind, generation );
				template = $"{(run ? ThemeParkIncWalkStep.RunTemplate : ThemeParkIncWalkStep.WalkTemplate):x}";
				interval = $"{ThemeParkIncWalkStep.Interval( x0, y0, x1, y1, run ):x}";
			}
			steps.Add( $"{(int)result}:{heading}:{template}:{interval}" );
		}
		return steps;
	}

	[TestMethod]
	public void TheWalkStepFollowsGameExe()
	{
		foreach ( var golden in Expected )
		{
			var steps = Steps( golden.Walk, golden.Kind, golden.Generation, golden.Bonus );
			var what = $"walk {golden.Walk}, kind {golden.Kind}, generation {golden.Generation}";
			Assert.AreEqual( golden.First, string.Join( ',', steps.Take( 12 ) ), what );
			Assert.AreEqual( golden.Digest, Convert.ToHexStringLower( SHA256.HashData( Encoding.ASCII.GetBytes( string.Join( ',', steps ) ) ) ), what );
		}
		// walk 1 arrives: its last steps write the heading (D1)
		Assert.IsTrue( Steps( 1, 1, 0, 25 ).Last().StartsWith( "0:" ) && !Steps( 1, 1, 0, 25 ).Last().StartsWith( "0:-" ) );
	}

	private const int Unwritten = 0x7777;

	/// <summary>
	/// The steps of one walk through the guests' own walk step (<c>GuestSimulation.StepThemeParkInc</c>, a guest with a Theme Park Inc record of the given generation and the bonus word) or the staff's
	/// (<c>ThemeParkIncStaffWorld.Walk</c>, a guard for kind 7, else a handyman), in the golden's text: the heading word, the template and the interval each read back from what the step wrote.
	/// </summary>
	private static List<string> WholeSteps( int walk, int kind, int generation, int bonus )
	{
		var (x, y, vx, vy, mask, speed, force, tx, ty, count) = ThemeParkIncNavigatorTests.Walks[walk];
		var map = ThemeParkIncNavigatorTests.Map();
		var fresh = new GuestNavigation { X = x, Y = y, VelocityX = vx, VelocityY = vy, Mask = mask, MaxSpeed = speed, MaxForce = force };
		var steps = new List<string>();
		static string Show( int value ) => value == Unwritten ? "-" : $"{value:x}";
		if ( kind == ThemeParkIncWalkStep.GuestKind )
		{
			var simulation = new GuestSimulation( new GuestPathGrid( map ), GuestTests.CreateSettings(), 7 );
			var guest = simulation.SpawnInPark( 40, 40 );
			guest.Navigation.LoadRecord( fresh.ToRecord() );
			map.RestoreBlockStamps( 0, null );
			guest.Navigation.Navigate( map, tx, ty );
			guest.ToiletBonus = bonus;
			var tpi = new ThemeParkIncGuestRecord { Generation = generation };
			guest.Tpi = tpi;
			var step = typeof( GuestSimulation ).GetMethod( "StepThemeParkInc", BindingFlags.NonPublic | BindingFlags.Instance )!;
			for ( var index = 0; index < count; index++ )
			{
				tpi.Heading = Unwritten;
				guest.BodySprite.ResetForNewPerson();
				var result = (WalkResult)step.Invoke( simulation, [guest] )!;
				var walking = result == WalkResult.Walking;
				steps.Add( $"{(int)result}:{Show( tpi.Heading )}:{(walking ? $"{(int)guest.BodySprite.PendingTemplate!:x}" : "-")}:{(walking ? $"{guest.BodySprite.PendingInterval:x}" : "-")}" );
			}
			return steps;
		}
		var world = new ThemeParkIncStaffWorld( new GuestPathGrid( map ) );
		var member = new ThemeParkIncStaffMember( 1, kind == ThemeParkIncWalkStep.GuardKind ? ThemeParkIncStaffRole.Guard : ThemeParkIncStaffRole.Handyman, 0, 0, 0, 0, 0 )
		{
			Navigation = fresh,
			SpeedBonus = bonus
		};
		fresh.Navigate( map, tx, ty );
		var walkStep = typeof( ThemeParkIncStaffWorld ).GetMethod( "Walk", BindingFlags.NonPublic | BindingFlags.Instance )!;
		for ( var index = 0; index < count; index++ )
		{
			member.SpriteAngle = Unwritten;
			member.Template = Unwritten;
			member.Sprite.Reset();
			var result = (int)walkStep.Invoke( world, [member] )!;
			var walking = result == 1;
			steps.Add( $"{result}:{Show( member.SpriteAngle )}:{(walking ? Show( member.Template ) : "-")}:{(walking ? $"{member.Sprite.PendingInterval:x}" : "-")}" );
		}
		return steps;
	}

	/// <summary>
	/// The guests' walk step and the staff's, run whole on a guest and on a member, write what Game.exe's walk step <c>0x60B460</c> wrote: the result, the heading word (on arrival too), the template
	/// (only a kid or a guard with a bonus runs) and the interval, for each golden walk and person (the lead's NV3 review).
	/// </summary>
	[TestMethod]
	public void TheGuestsAndTheStaffsWalkStepsFollowGameExe()
	{
		foreach ( var golden in Expected )
		{
			var steps = WholeSteps( golden.Walk, golden.Kind, golden.Generation, golden.Bonus );
			var what = $"walk {golden.Walk}, kind {golden.Kind}, generation {golden.Generation}";
			Assert.AreEqual( golden.First, string.Join( ',', steps.Take( 12 ) ), what );
			Assert.AreEqual( golden.Digest, Convert.ToHexStringLower( SHA256.HashData( Encoding.ASCII.GetBytes( string.Join( ',', steps ) ) ) ), what );
		}
	}

	[TestMethod]
	public void OnlyKidsAndGuardsRun()
	{
		// [BIN:TPI-EXE:0x60B541 run flag] the bonus word above 0 (unsigned) and a kind-1 person of generation 0 or a kind-7 person
		Assert.IsTrue( ThemeParkIncWalkStep.Runs( 25, ThemeParkIncWalkStep.GuestKind, 0 ) );
		Assert.IsFalse( ThemeParkIncWalkStep.Runs( 25, ThemeParkIncWalkStep.GuestKind, 1 ), "a parent walks" );
		Assert.IsFalse( ThemeParkIncWalkStep.Runs( 50, ThemeParkIncWalkStep.GuestKind, 2 ), "a grandparent walks" );
		Assert.IsTrue( ThemeParkIncWalkStep.Runs( 25, ThemeParkIncWalkStep.GuardKind, 2 ), "a guard runs whatever the word +0x2A4 holds" );
		Assert.IsFalse( ThemeParkIncWalkStep.Runs( 25, 5, 0 ), "a handyman never runs" );
		Assert.IsFalse( ThemeParkIncWalkStep.Runs( 0, ThemeParkIncWalkStep.GuestKind, 0 ), "no bonus, no run" );
		Assert.IsTrue( ThemeParkIncWalkStep.Runs( 0x8000, ThemeParkIncWalkStep.GuestKind, 0 ), "the word compare is unsigned" );
	}

	[TestMethod]
	public void AStepThatDidNotMoveFacesTheTopAndQueuesNoInterval()
	{
		Assert.AreEqual( 0, ThemeParkIncWalkStep.Heading( 5 << 16, 7 << 16, 5 << 16, 7 << 16 ), "atan2(0, 0) answers 0" );
		Assert.AreEqual( 0, ThemeParkIncWalkStep.Interval( 0, 0, 0, 0, run: false ), "62 / 0 is no integer: _ftol's indefinite has the low dword 0" );
		Assert.AreEqual( SpritePlayer.LongestInterval, ThemeParkIncWalkStep.Interval( 0, 0, 3, 0, run: false ), "a creeping step clamps to 0xFA" );
		var (x, y) = ThemeParkIncWalkStep.Vector( 0x400 );
		Assert.IsTrue( Math.Abs( x ) < 1e-6 && Math.Abs( y - 1 ) < 1e-6, "0x400 faces +Y" );
	}

	// ---- The stamp counter ------------------------------------------------------------------------------------------------------------------------------------

	[TestMethod]
	public void TheCounterCountsEverySimulationCallOnlyWithThemeParkIncsNavigator()
	{
		var world = new ParkCellMap( 32, 32 );
		Assert.IsFalse( world.CountsSimulationCalls );
		Assert.AreEqual( (1, 1, 0, 0), (world.NextStamp(), world.NextStamp(), world.SetupStamp(), world.StampCounter), "edits only: a request compares the next value and raises nothing" );
		world.SetType( 3, 3, ParkCellType.Path );
		Assert.AreEqual( (1, 1), (world.StampCounter, world.BlockStamp( 0, 0 )) );

		var inc = new ParkCellMap( 32, 32 ) { EdgeRules = WalkEdgeRules.ThemeParkInc };
		Assert.IsTrue( inc.CountsSimulationCalls );
		Assert.AreEqual( (1, 2, 3, 3), (inc.NextStamp(), inc.NextStamp(), inc.SetupStamp(), inc.StampCounter), "every call raises it" );
		inc.SetType( 3, 3, ParkCellType.Path );
		Assert.AreEqual( (4, 4), (inc.StampCounter, inc.BlockStamp( 0, 0 )) );
	}

	/// <summary>
	/// The counter's mode changes no outcome of a Theme Park World run: every comparison the original makes is between values of distinct calls (a block stamp against a walk's stamp in follow_path,
	/// a block stamp against a stranded mark, a mark against a fresh counter value), and the counter only grows. The same guest park with path edits runs under the edit-only counter and under the
	/// counter that counts every call, and every guest walks, waits and decides alike, tick for tick, while only the counter's absolute values differ. A guest is marked stranded now and then as
	/// the wander marks one (the mark is the counter routine's value), so the gates' and the stranded test's comparisons run too.
	/// </summary>
	[TestMethod]
	public void AThemeParkWorldRunIsTheSameUnderBothCountingModes()
	{
		var edits = new GuestSnapshotTests.Rig();
		var counted = new GuestSnapshotTests.Rig();
		counted.Grid.Cells.CountSimulationCalls( true );
		Assert.IsFalse( edits.Grid.Cells.CountsSimulationCalls );
		var sawHeld = false;
		var sawStamp = false;
		for ( long tick = 1; tick <= 9000; tick++ )
		{
			foreach ( var rig in new[] { edits, counted } )
			{
				// a gap in the path that opens and closes stamps its block (the reroutes of follow_path) and can strand a guest (the marks)
				if ( tick % 1200 == 400 )
					rig.Grid.SetPath( 9 + (int)(tick / 1200 % 3), 0, false );
				if ( tick % 1200 == 1000 )
					rig.Grid.SetPath( 9 + (int)(tick / 1200 % 3), 0, true );
				if ( tick % 300 == 150 && rig.Simulation.Guests.FirstOrDefault( guest => guest.State is GuestState.DoingNothing or GuestState.WalkingAround ) is { } marked )
					marked.StrandedMark = rig.Grid.Cells.NextStamp();
				rig.Step();
				GuestSnapshotTests.Drive( rig, tick );
			}
			Assert.AreEqual( Projection( edits ), Projection( counted ), $"the runs diverged at tick {tick}" );
			// a mark that outlives the turn it was set in: a gate found it held
			sawHeld |= tick % 300 == 151 + 60 && edits.Simulation.Guests.Any( guest => guest.StrandedMark != 0 );
			sawStamp |= edits.Grid.Cells.CaptureBlockStamps() != null;
		}
		Assert.IsTrue( sawStamp, "the run stamped a block" );
		Assert.IsTrue( counted.Grid.Cells.StampCounter > edits.Grid.Cells.StampCounter, "the counting mode raised the counter at the requests and set-ups too" );
		Assert.IsTrue( sawHeld, "a stranded mark held" );
	}

	private static string Projection( GuestSnapshotTests.Rig rig )
	{
		var text = new StringBuilder();
		foreach ( var guest in rig.Simulation.Guests )
		{
			var walk = guest.Navigation;
			text.Append( $"{guest.Id}:{(int)guest.State}:{walk.X}:{walk.Y}:{walk.VelocityX}:{walk.VelocityY}:{walk.Index}:{walk.Count}:{walk.PathCount}:{walk.Arrived}:{walk.Stuck}:" );
			text.Append( $"{walk.History & 0x7FFF}:{guest.HasWaypoint}:{guest.StrandedMark != 0};" );
		}
		return text.ToString();
	}

	/// <summary>
	/// The counter is the original's unsigned dword (the lead's NV3 review): past 2^31 it wraps into negative ints, compares unsigned against stamps and marks, and a layout holding such values
	/// saves, validates and restores.
	/// </summary>
	[TestMethod]
	public void TheCounterIsAnUnsignedDwordPastTwoToTheThirtyFirst()
	{
		var inc = new ParkCellMap( 32, 32 ) { EdgeRules = WalkEdgeRules.ThemeParkInc };
		inc.RestoreBlockStamps( int.MaxValue - 1, null );
		Assert.AreEqual( int.MaxValue, inc.NextStamp() );
		Assert.AreEqual( int.MinValue, inc.NextStamp(), "the dword passes 0x7FFFFFFF to 0x80000000" );
		inc.SetType( 3, 3, ParkCellType.Path );
		Assert.AreEqual( int.MinValue + 1, inc.BlockStamp( 0, 0 ) );
		Assert.IsTrue( ParkCellMap.StampBelow( int.MaxValue, int.MinValue ), "0x7FFFFFFF is below 0x80000000 unsigned" );
		Assert.IsFalse( ParkCellMap.StampBelow( int.MinValue, int.MaxValue ) );
		// follow_path's unsigned test: a walk set up now is stamped above the block, so it does not search again
		var walk = new GuestNavigation { X = (3 << 16) + 0x8000, Y = (3 << 16) + 0x8000, MaxSpeed = 13107, MaxForce = 26214 };
		walk.Navigate( inc, (3 << 16) + 0x8000, (3 << 16) + 0x8000 );
		Assert.IsFalse( ParkCellMap.StampBelow( walk.Stamp, inc.BlockStamp( 0, 0 ) ) );

		var data = ParkLayoutSnapshot.CaptureSnapshot( inc, new ParkObjects( null!, new SandboxParkGrid() ) );
		Assert.IsTrue( data.Cells.StampCounter < 0 && data.Cells.BlockStamps!.Any( stamp => stamp < 0 ), "saved as the dword's bits" );
		var restored = ParkLayoutSnapshot.RestoreCellMap( ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( data ) ) );
		Assert.AreEqual( (inc.StampCounter, inc.BlockStamp( 0, 0 )), (restored.StampCounter, restored.BlockStamp( 0, 0 )) );
	}

	[TestMethod]
	public void ACountingMapKeepsItsCounterInTheLayoutAndAThemeParkWorldMapWritesNothingNew()
	{
		var inc = new ParkCellMap( 32, 32 ) { EdgeRules = WalkEdgeRules.ThemeParkInc };
		inc.NextStamp();
		inc.NextStamp();
		Assert.IsNull( inc.CaptureBlockStamps(), "no block is stamped" );
		var hash = new StateHasher();
		inc.AddCanonicalState( hash );
		var zero = new StateHasher();
		new ParkCellMap( 32, 32 ) { EdgeRules = WalkEdgeRules.ThemeParkInc }.AddCanonicalState( zero );
		Assert.AreNotEqual( zero.Value, hash.Value, "the counter joins the digest of a counting map without stamps" );

		var data = ParkLayoutSnapshot.CaptureSnapshot( inc, new ParkObjects( null!, new SandboxParkGrid() ) );
		Assert.AreEqual( (2, true), (data.Cells.StampCounter, data.Cells.SimulationStampCounter) );
		var restored = ParkLayoutSnapshot.RestoreCellMap( ParkResumeJson.Deserialize<ParkLayoutSnapshot.Data>( ParkResumeJson.Serialize( data ) ) );
		Assert.AreEqual( 2, restored.StampCounter );

		var world = new ParkCellMap( 32, 32 );
		world.NextStamp();
		var json = Encoding.UTF8.GetString( ParkResumeJson.Serialize( ParkLayoutSnapshot.CaptureSnapshot( world, new ParkObjects( null!, new SandboxParkGrid() ) ) ) );
		Assert.IsFalse( json.Contains( nameof( ParkLayoutSnapshot.CellMapData.SimulationStampCounter ) ) || json.Contains( nameof( ParkLayoutSnapshot.CellMapData.StampCounter ) ),
			"a Theme Park World map's layout is written as before" );
	}

	// ---- The level's switch ---------------------------------------------------------------------------------------------------------------------------------

	/// <summary>A Theme Park Inc level gives its park's cell map Theme Park Inc's edge test and counter (the default since NV4, read once); a Theme Park World level keeps Theme Park World's.</summary>
	[TestMethod]
	public void TheLevelGivesAThemeParkIncParkTheNavigatorAndThemeParkWorldItsOwnRules()
	{
		var edition = GameEditions.Current;
		try
		{
			GameEditions.Select( GameEdition.ThemeParkInc );
			var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			var cells = new ParkCellMap( 16, 16 );
			level.ApplyParkEdgeRules( cells );
			Assert.AreEqual( WalkEdgeRules.ThemeParkInc, cells.EdgeRules );
			Assert.IsTrue( cells.CountsSimulationCalls );
			GameEditions.Select( GameEdition.ThemeParkWorld );
			var later = new ParkCellMap( 16, 16 );
			level.ApplyParkEdgeRules( later );
			Assert.AreEqual( WalkEdgeRules.ThemeParkInc, later.EdgeRules, "the level read the edition's rules once" );

			var world = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) );
			var worldCells = new ParkCellMap( 16, 16 );
			world.ApplyParkEdgeRules( worldCells );
			Assert.AreEqual( WalkEdgeRules.ThemeParkWorld, worldCells.EdgeRules, "a Theme Park World level walks with Theme Park World's rules" );
			Assert.IsFalse( worldCells.CountsSimulationCalls );
		}
		finally
		{
			GameEditions.Select( edition );
		}
	}
}
