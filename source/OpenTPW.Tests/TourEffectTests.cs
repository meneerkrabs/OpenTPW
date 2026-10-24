using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The tour cars' cosmetic effects (docs/TOURS.md): the engine voice (tp.exe 0x0055A944 start, the per-state parameters of 0x0055ABF0, the move at 0x0055C1B5,
/// the stop at 0x0055CDA0), the clip triggers (0x0055AA80), the condition effects of TOUR 9 (0x0055DA70: FlySmoke, FlyFire, the repair) and the emitter's draw-time
/// follow (0x0055CE60). Synthetic controllers; the car meshes' smoke node with OPENTPW_GAME_PATH.
/// </summary>
[TestClass]
public class TourEffectTests
{
	private BaseFileSystem? previousFileSystem;

	[TestInitialize]
	public void Initialize() => previousFileSystem = FileSystem;

	[TestCleanup]
	public void Cleanup() => FileSystem = previousFileSystem!;

	private static Func<uint> Random( uint seed )
	{
		var state = seed;
		return () => state = unchecked( state * 1664525u + 1013904223u );
	}

	private static (TourController Controller, List<TourEffect> Effects) Rig( uint seed = 1 )
	{
		var controller = new TourController( TourStation.Default, Random( seed ), new TourWorld() );
		var effects = new List<TourEffect>();
		controller.Effect += effects.Add;
		return (controller, effects);
	}

	private static void Run( TourController controller, int ticks )
	{
		for ( var tick = 0; tick < ticks; tick++ )
			controller.Tick();
	}

	[TestMethod]
	public void ADockedCarsVoiceTakesEightyAndMovesWithItEveryTick()
	{
		var (controller, effects) = Rig();
		Run( controller, 5 );
		var parameters = effects.Where( effect => effect.Kind == TourEffectKind.EngineParameter ).ToList();
		Assert.AreEqual( 5, parameters.Count );
		Assert.IsTrue( parameters.All( effect => effect.Value == TourEffects.ParameterDocked && effect.Slot == 0 ), "state 1 sets 80 every tick (0x55B09F)" );
		var moves = effects.Where( effect => effect.Kind == TourEffectKind.EngineMove ).ToList();
		Assert.AreEqual( 5, moves.Count, "one move per live car per tick (0x55C20A)" );
		var car = controller.Cars[0];
		Assert.AreEqual( (car.X, car.Y, car.Z), (moves[^1].X, moves[^1].Y, moves[^1].Z), "the move carries the car's place after its motion" );
	}

	[TestMethod]
	public void TheStatesSetTheTracedParametersAndTriggerTheirClipsOnlyOnAChange()
	{
		var (controller, effects) = Rig( 3 );
		controller.Command( TourController.CommandOpen, 1 );
		controller.Command( TourController.CommandDuration, 30 );
		controller.Board( 7 );
		Assert.AreEqual( 1, controller.Command( TourController.CommandDepart, 0 ) );
		var car = controller.Cars[0];
		var seen = new Dictionary<int, HashSet<int>>();
		for ( var tick = 0; tick < 6000 && !(seen.ContainsKey( TourCar.StateLanding ) && car.State == TourCar.StateDocked); tick++ )
		{
			effects.Clear();
			var state = car.State;
			controller.Tick();
			foreach ( var effect in effects.Where( effect => effect.Kind == TourEffectKind.EngineParameter && effect.Slot == 0 ) )
			{
				seen.TryAdd( state, new HashSet<int>() );
				seen[state].Add( effect.Value );
			}
		}
		Assert.IsTrue( seen[TourCar.StateTakeoff].SetEquals( new[] { TourEffects.ParameterTakeoff } ), "taking off: 15 (0x55B65B)" );
		Assert.IsTrue( seen[TourCar.StateTour].SetEquals( new[] { TourEffects.ParameterFlying } ), "flying: 30" );
		Assert.IsTrue( seen[TourCar.StateLanding].IsSubsetOf( new[] { TourEffects.ParameterFlying, TourEffects.ParameterDocked } ), "the descent 30, the touchdown 80 (0x55BBF5)" );
		// the clip triggers: one per change, the serial counts them
		var (again, clips) = Rig( 3 );
		var first = again.Cars[0];
		var serial = first.AnimationSerial;
		again.Command( TourController.CommandOpen, 1 );
		again.Command( TourController.CommandDuration, 30 );
		again.Board( 7 );
		again.Command( TourController.CommandDepart, 0 );
		Run( again, 200 );
		var triggers = clips.Where( effect => effect.Kind == TourEffectKind.Animation ).ToList();
		Assert.IsTrue( triggers.Count >= 2, "take-off 4, then flying 5" );
		Assert.AreEqual( (4, 1), (triggers[0].Id, triggers[0].Value), "take-off loops (0x55B632)" );
		for ( var index = 1; index < triggers.Count; index++ )
			Assert.AreNotEqual( (triggers[index - 1].Id, triggers[index - 1].Value), (triggers[index].Id, triggers[index].Value), "the same animation is not triggered twice in a row" );
		Assert.AreEqual( serial + triggers.Count, first.AnimationSerial );
	}

	[TestMethod]
	public void ANewCarStartsItsEngineAndItsClipZero()
	{
		var (controller, effects) = Rig( 9 );
		controller.Command( TourController.CommandOpen, 1 );
		controller.Command( TourController.CommandCars, 2 );
		controller.Tick();
		var start = effects.Single( effect => effect.Kind == TourEffectKind.EngineStart );
		Assert.AreEqual( 1, start.Slot );
		Assert.IsTrue( effects.Any( effect => effect.Kind == TourEffectKind.Animation && effect.Slot == 1 && effect.Id == 0 && effect.Value == 0 ),
			"the creation resets the last trigger to −1, so animation 0 always triggers (0x55A8D9)" );
		var slotOne = effects.Where( effect => effect.Kind == TourEffectKind.Animation && effect.Slot == 1 ).ToList();
		Assert.AreEqual( (0, 0), (slotOne[0].Id, slotOne[0].Value), "the creation's trigger comes first, then the state's own (a new car returns: 5)" );
		Assert.AreEqual( slotOne[^1].Id, controller.Cars[1].Animation );
	}

	[TestMethod]
	public void TourNineMakesSmokeThenFireAndTheRepairPlaysItsSound()
	{
		var (controller, effects) = Rig( 4 );
		var car = controller.Cars[0];
		controller.Command( TourController.CommandCondition, 2 );
		var smoke = effects.Single( effect => effect.Kind == TourEffectKind.EmitterStart );
		Assert.AreEqual( TourEffects.ParticleFlySmoke, smoke.Id );
		Assert.AreEqual( (car.X, car.Y, car.Z), (smoke.X, smoke.Y, smoke.Z), "worn: the raw module coordinates go in as particle units (0x55DB95)" );
		Assert.AreEqual( TourEffects.ParticleFlySmoke, car.Emitter );
		effects.Clear();
		controller.Command( TourController.CommandCondition, 3 );
		Assert.AreEqual( TourEffectKind.EmitterStop, effects[0].Kind, "the old emitter goes first" );
		var fire = effects.Single( effect => effect.Kind == TourEffectKind.EmitterStart );
		Assert.AreEqual( TourEffects.ParticleFlyFire, fire.Id );
		Assert.AreEqual( ((car.X << 10) / 300, (car.Y << 10) / 300, (car.Z << 10) / 300), (fire.X, fire.Y, fire.Z), "broken: (x << 10) / 300 (0x55DBD8)" );
		Assert.IsTrue( car.Broken );
		effects.Clear();
		controller.Command( TourController.CommandCondition, 1 );
		Assert.AreEqual( TourEffectKind.Animation, effects[0].Kind );
		Assert.AreEqual( (10, 0), (effects[0].Id, effects[0].Value), "the repair clip 10, once" );
		Assert.AreEqual( TourEffectKind.RepairSound, effects[1].Kind );
		Assert.AreEqual( TourEffects.SoundRepair, effects[1].Id );
		Assert.AreEqual( TourEffectKind.EmitterStop, effects[^1].Kind, "the mend destroys the emitter last (0x55DB5C)" );
		Assert.AreEqual( 0, car.Emitter );
		effects.Clear();
		controller.Command( TourController.CommandCondition, 2 );
		controller.Command( TourController.CommandCondition, 1 );
		Assert.IsFalse( effects.Any( effect => effect.Kind == TourEffectKind.RepairSound ), "no repair sound for a car that was not broken" );
	}

	[TestMethod]
	public void TheEmitterFollowsTheDrawnNodeOfALiveCarOnly()
	{
		var (controller, effects) = Rig();
		controller.DrawEmitter( 0, new System.Numerics.Vector3( 1, 2, 3 ) );
		Assert.AreEqual( 0, effects.Count, "no emitter, nothing to move" );
		controller.Command( TourController.CommandCondition, 3 );
		controller.DrawEmitter( 0, new System.Numerics.Vector3( 1, 2, 3 ) );
		Assert.AreEqual( new System.Numerics.Vector3( 1, 2, 3 ), effects.Single( effect => effect.Kind == TourEffectKind.EmitterMove ).Node );
		controller.DrawEmitter( 5, new System.Numerics.Vector3( 1, 2, 3 ) );
		Assert.AreEqual( 1, effects.Count( effect => effect.Kind == TourEffectKind.EmitterMove ), "a free slot has none" );
	}

	[TestMethod]
	public void AnEmitterSurvivesASaveAndAnOldSaveHasNone()
	{
		var (controller, _) = Rig();
		controller.Command( TourController.CommandCondition, 2 );
		var saved = TourSave.Capture( controller );
		var json = System.Text.Json.JsonSerializer.Serialize( saved );
		var loaded = System.Text.Json.JsonSerializer.Deserialize<TourControllerSaveData>( json )!;
		TourSave.Validate( loaded, guest => true, new HashSet<int>() );
		var copy = new TourController( loaded, Random( 1 ), new TourWorld() );
		Assert.AreEqual( TourEffects.ParticleFlySmoke, copy.Cars[0].Emitter );
		var hashA = new StateHasher();
		var hashB = new StateHasher();
		controller.AddCanonicalState( hashA );
		copy.AddCanonicalState( hashB );
		Assert.AreEqual( hashA.Value, hashB.Value );
		// a save from before the field: the car holds none
		var old = json.Replace( ",\"Emitter\":39", "" );
		Assert.AreNotEqual( json, old );
		var legacy = System.Text.Json.JsonSerializer.Deserialize<TourControllerSaveData>( old )!;
		TourSave.Validate( legacy, guest => true, new HashSet<int>() );
		Assert.AreEqual( 0, new TourController( legacy, Random( 1 ), new TourWorld() ).Cars[0].Emitter );
		// an unknown particle is refused
		var bad = System.Text.Json.JsonSerializer.Deserialize<TourControllerSaveData>( json.Replace( "\"Emitter\":39", "\"Emitter\":7" ) )!;
		Assert.ThrowsException<InvalidDataException>( () => TourSave.Validate( bad, guest => true, new HashSet<int>() ) );
	}

	[TestMethod]
	public void TheEffectsDrawNoWorldRandomness()
	{
		int Draws( bool listen )
		{
			var draws = 0;
			var state = 1u;
			var controller = new TourController( TourStation.Default, () => { draws++; return state = unchecked( state * 1664525u + 1013904223u ); }, new TourWorld() );
			if ( listen )
				controller.Effect += _ => { };
			controller.Command( TourController.CommandCondition, 2 );
			controller.Command( TourController.CommandCondition, 3 );
			controller.Command( TourController.CommandCondition, 1 );
			Run( controller, 50 );
			return draws;
		}
		Assert.AreEqual( Draws( false ), Draws( true ), "the effects are presentation: no world draw is added" );
	}

	/// <summary>Every tour ride: its car mesh carries the smoke node (class 0x100, number 1), its EventMap names the engine event (variable 0) and the parameter code (variable 10).</summary>
	[TestMethod]
	public void TheShippedTourRidesCarryTheSmokeNodeAndTheirEngineVariables()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original ride archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		foreach ( var (theme, infoId) in new[] { ("jungle", 1170), ("hallow", 2170), ("space", 3170), ("fantasy", 4170) } )
		{
			var entry = ObjectCatalog.Load( theme ).Get( infoId );
			var name = entry.Settings["SupplementalMeshes[0].FileName"]!.Trim( '"', ' ' );
			var path = entry.FileSystem.GetFiles( entry.ArchivePath ).First( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) );
			Assert.IsTrue( TrackRenderer.SparksNode( ObjectAssets.LoadModel( entry.FileSystem, path ), 1 ) >= 0, $"{theme}: {name} has the node class 0x100 number 1" );
			var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld( 3 ), seed: 5, open: true );
			for ( var tick = 0; tick < 60; tick++ )
				runtime.Simulate( 1f / FixedStepClock.TicksPerSecond );
			var engine = runtime.EventMapVariable( TourEffects.EngineEventVariable );
			Assert.IsTrue( engine > 0, $"{theme}: the engine event" );
			var rides = SoundCategory.TryLoad( FileSystem, $"/levels/{theme}/Sound", "cat_rides" );
			Assert.IsNotNull( rides?.Catalog.Find( (uint)engine!.Value ), $"{theme}: engine event {engine} in the level cat_rides" );
			Assert.IsNotNull( runtime.EventMapVariable( TourEffects.EngineParameterVariable ), theme );
			Assert.IsNotNull( SoundCategory.TryLoad( FileSystem, "/global/sound", "cat_rides" )?.Catalog.Find( TourEffects.SoundRepair ), "the repair sound 0xBB is a global cat_rides event" );
			runtime.Stop();
		}
	}

	/// <summary>
	/// The car meshes' own clip members cover every animation the controller triggers (0, 3, 4, 5, 6, 9, 10 by the letters C L S M E B R) and an animator over the car mesh plays
	/// them: the birds flap by vertex tracks, the balloon's and craft's clips move nodes or nothing.
	/// </summary>
	[TestMethod]
	public void TheCarMeshesOwnClipsCoverEveryTriggeredAnimationAndPlay()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the original ride archives." );
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		if ( !File.Exists( Path.Combine( dataPath, "levels", "Standard.sam" ) ) )
			Assert.Inconclusive( "OPENTPW_GAME_PATH lacks levels/Standard.sam." );
		FileSystem = new BaseFileSystem( dataPath );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		foreach ( var (theme, infoId) in new[] { ("jungle", 1170), ("hallow", 2170), ("space", 3170), ("fantasy", 4170) } )
		{
			var entry = ObjectCatalog.Load( theme ).Get( infoId );
			var name = entry.Settings["SupplementalMeshes[0].FileName"]!.Trim( '"', ' ' );
			var files = entry.FileSystem.GetFiles( entry.ArchivePath ).ToList();
			var path = files.First( file => string.Equals( Path.GetFileName( file ), name, StringComparison.OrdinalIgnoreCase ) );
			var model = ObjectAssets.LoadModel( entry.FileSystem, path );
			var animations = ObjectAnimations.Find( Path.GetFileNameWithoutExtension( path ), files.Where( file => file != path ) );
			var animator = new ObjectAnimator( model );
			var moved = false;
			foreach ( var id in new[] { 0, 3, 4, 5, 6, 9, 10 } )
			{
				var file = ObjectAnimations.Resolve( animations, id, 0 );
				Assert.IsNotNull( file, $"{theme}: {name} has a clip for animation {id}" );
				var clip = ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip;
				Assert.IsNotNull( clip, file.Path );
				animator.Trigger( 0, clip, Path.GetFileName( file.Path ), id, 0, loop: id is 4 or 5 or 9 );
				Assert.AreEqual( id, animator.GetAnimationId( 0 ) );
				animator.Advance( 0.5 );
				moved |= Enumerable.Range( 0, model.Meshes.Count ).Any( mesh => animator.GetVertexPositions( mesh ) != null );
			}
			if ( theme is "jungle" or "fantasy" )
				Assert.IsTrue( moved, $"{theme}: the bird's clips move its vertices" );
			Assert.IsTrue( ObjectRenderParts.Build( entry, model, animations ).Count > 0 );
		}
	}
}
