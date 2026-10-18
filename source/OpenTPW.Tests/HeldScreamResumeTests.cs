using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// A ride script's held scream is state the script holds (the handle, its STARTSCREAM and SCREAMLEVEL operands and the handles a refused
/// STARTSCREAM lost), the same with or without audio, so a park save and its hashes do not depend on the sound service. A restore brings that
/// state back exactly and starts no sound; the chain plays again once the resumed park's audio runs (<see cref="Level.ResumeHeldScreams"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public class HeldScreamResumeTests
{
	private static readonly string[] Variables = Enum.GetNames<RideVariables>();
	private static readonly FieldInfo EventsField = typeof( GameAudio ).GetField( "events", BindingFlags.Static | BindingFlags.NonPublic )!;
	private static readonly FieldInfo KidsField = typeof( GameAudio ).GetField( "<Kids>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic )!;
	private static readonly FieldInfo ChainField = typeof( OriginalObjectEffects ).GetField( "screamChain", BindingFlags.Instance | BindingFlags.NonPublic )!;
	private string directory = null!;
	private ObjectCatalogEntry entry = null!;
	private object? previousEvents;
	private object? previousKids;
	private AudioMixer? mixer;

	[TestInitialize]
	public void Initialize()
	{
		Log ??= new();
		directory = Directory.CreateTempSubdirectory( "opentpw-held-scream-" ).FullName;
		File.WriteAllBytes( Path.Combine( directory, "model.md2" ), Md2ModelFileTests.CreateGeometry() );
		File.WriteAllBytes( Path.Combine( directory, "root.rse" ), new Asm().I( Opcode.ENDSLICE ).Build( Variables ) );
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1000\nInfo.Name Screamer\n" ) } );
		entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/synthetic.wad", settings, new ObjectShape( new[] { "*" } ),
			"/model.md2", null, "/root.rse", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(),
			Array.Empty<string>(), new[] { "/model.md2" }, new BaseFileSystem( directory ), null );
		previousEvents = EventsField.GetValue( null );
		previousKids = KidsField.GetValue( null );
		EventsField.SetValue( null, null );
		GameAudio.Screams.Reset();
	}

	[TestCleanup]
	public void Cleanup()
	{
		EventsField.SetValue( null, previousEvents );
		KidsField.SetValue( null, previousKids );
		mixer?.Dispose();
		GameAudio.Screams.Reset();
		Directory.Delete( directory, recursive: true );
	}

	/// <summary>A sound service without a device whose <c>cat_kids</c> holds the held-scream events 71 to 74.</summary>
	private SoundEventSystem StartAudio()
	{
		mixer ??= new AudioMixer( null );
		var system = new SoundEventSystem( mixer, 7 );
		var category = (SoundCategory)RuntimeHelpers.GetUninitializedObject( typeof( SoundCategory ) );
		var catalog = (SoundCatalog)RuntimeHelpers.GetUninitializedObject( typeof( SoundCatalog ) );
		var sound = new SoundEntry( 100, 100, 0, 0, 0, 0, 1, 100, new[] { new SoundSample( 1, 65535, 0, 1, 0 ) }, Array.Empty<SoundLink>(), Array.Empty<byte>() );
		typeof( SoundCatalog ).GetField( "<Events>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!
			.SetValue( catalog, new uint[] { 71, 72, 73, 74 }.Select( id => new SoundEvent( id, 2700, 0x404, 0, new[] { sound } ) ).ToArray() );
		typeof( SoundCategory ).GetField( "<Catalog>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( category, catalog );
		EventsField.SetValue( null, system );
		KidsField.SetValue( null, category );
		return system;
	}

	private static void StopAudio() => EventsField.SetValue( null, null );

	private static void Run( OriginalObjectRuntime ride, Opcode opcode, params int[] values ) => ride.Script!.Effect( opcode,
		values.Select( value => new Operand( ride.Script!, new RideScriptOperand( RideScriptOperandKind.Literal, (ushort)value ), -1 ) ).ToArray() );

	private static SoundChain? Chain( OriginalObjectRuntime ride ) => (SoundChain?)ChainField.GetValue( ride.Effects );

	private static (bool Held, bool? ScriptHolds, int Band, int Level, int Speed, int Leaked) ScreamState( OriginalObjectRuntimeSnapshot snapshot ) =>
		(snapshot.HeldScream, snapshot.ScriptHoldsScream, snapshot.HeldScreamBand, snapshot.HeldScreamLevel, snapshot.HeldScreamSpeed, snapshot.OwnedScreamCount);

	/// <summary>A start, a level change, a refused start (a lost handle), a stop and a new start with a new level.</summary>
	private static void Scream( OriginalObjectRuntime ride )
	{
		Run( ride, Opcode.STARTSCREAM, 3, 40 );
		Run( ride, Opcode.SCREAMLEVEL, 70 );
		Run( ride, Opcode.STARTSCREAM, 3, 40 );
		Run( ride, Opcode.STARTSCREAM, 2, 30 );
		Run( ride, Opcode.SCREAMLEVEL, 90 );
	}

	[TestMethod]
	public void TheHeldScreamIsSavedTheSameWithAndWithoutAudio()
	{
		var silent = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 );
		Scream( silent );
		Assert.IsNull( Chain( silent ), "no sound service, no chain" );
		var withoutAudio = silent.CaptureSnapshot();

		StartAudio();
		var audible = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 );
		Scream( audible );
		Assert.IsTrue( audible.Effects.PlaysHeldScream, "with the sound service the held scream plays" );
		var withAudio = audible.CaptureSnapshot();

		var speed = audible.Script!.SpeedBias;
		Assert.AreEqual( (true, (bool?)true, 2, 90, speed, 1), ScreamState( withAudio ), "the handle, its operands and the lost handle of the refused start" );
		Assert.AreEqual( ScreamState( withAudio ), ScreamState( withoutAudio ), "the saved scream does not depend on the sound service" );
		Assert.AreEqual( (4, 2, 0), (GameAudio.Screams.Starts, GameAudio.Screams.Refusals, GameAudio.Screams.Stops), "both runs counted the same starts and refusals" );
		// the original resolves SCREAMLEVEL's operand and leaves a script without a handle untouched
		Run( audible, Opcode.STOPSCREAM );
		Run( audible, Opcode.SCREAMLEVEL, 10 );
		Assert.AreEqual( (false, (bool?)false, 0, 0, 0, 1), ScreamState( audible.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void ARestoreBringsTheScreamBackExactlyAndStartsNoChainUntilTheParkAudioRuns()
	{
		var system = StartAudio();
		var ride = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 );
		Scream( ride );
		var saved = ride.CaptureSnapshot();
		var candidate = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), seed: 1, open: true, attractionId: saved.AttractionId );
		Assert.AreEqual( 0, candidate.RestoreSnapshot( saved ).Count );
		Assert.IsNull( Chain( candidate ), "a candidate park makes no sound, even while the live park's audio runs" );
		Assert.AreEqual( ScreamState( saved ), ScreamState( candidate.CaptureSnapshot() ), "the restore reproduces the saved scream" );

		candidate.ResumeHeldScream();
		var chain = Chain( candidate );
		Assert.IsNotNull( chain, "the held scream plays again once the park's audio runs" );
		Assert.AreEqual( 72u, chain!.Event.Id, "band 2 plays cat_kids 72" );
		Assert.AreEqual( GameAudio.ScreamParameter( 90, saved.HeldScreamSpeed ), chain.Parameter, "from the last SCREAMLEVEL's level" );
		candidate.ResumeHeldScream();
		Assert.AreSame( chain, Chain( candidate ), "a playing chain is kept" );
		Assert.AreEqual( ScreamState( saved ), ScreamState( candidate.CaptureSnapshot() ), "the chain changes nothing that is saved" );

		// leaving a park stops every chain: the next park audio plays the held scream again
		system.StopChains();
		Assert.IsFalse( candidate.Effects.PlaysHeldScream );
		candidate.ResumeHeldScream();
		Assert.IsTrue( candidate.Effects.PlaysHeldScream );
		Assert.AreNotSame( chain, Chain( candidate ) );

		// the script's next start is refused as in the uninterrupted park, and its stop ends the chain
		Run( candidate, Opcode.STARTSCREAM, 3, 40 );
		Assert.AreEqual( (false, (bool?)false, 0, 0, 0, 2), ScreamState( candidate.CaptureSnapshot() ), "the restored handle refuses: one more lost handle" );
		Run( ride, Opcode.STARTSCREAM, 3, 40 );
		Assert.AreEqual( ScreamState( ride.CaptureSnapshot() ), ScreamState( candidate.CaptureSnapshot() ), "the resumed ride goes on as the uninterrupted one" );
	}

	[TestMethod]
	public void ARestoreWithoutAudioKeepsTheHandleAndAStoppedObjectHoldsNone()
	{
		StartAudio();
		var ride = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 );
		Run( ride, Opcode.STARTSCREAM, 5, 60 );
		var saved = ride.CaptureSnapshot();
		StopAudio();
		var candidate = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), seed: 1, open: true, attractionId: saved.AttractionId );
		candidate.RestoreSnapshot( saved );
		candidate.ResumeHeldScream();
		Assert.IsNull( Chain( candidate ), "no sound service, no chain" );
		Assert.AreEqual( ScreamState( saved ), ScreamState( candidate.CaptureSnapshot() ), "a run without audio restores the same scream" );
		Run( candidate, Opcode.STOPSCREAM );
		Assert.AreEqual( 1, GameAudio.Screams.Stops, "the restored handle is stopped as the script holds it" );

		var stoppedRide = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), seed: 1, open: true, attractionId: saved.AttractionId );
		stoppedRide.RestoreSnapshot( saved );
		StartAudio();
		stoppedRide.Stop();
		stoppedRide.ResumeHeldScream();
		Assert.IsNull( Chain( stoppedRide ), "a stopped object screams no more" );
		Assert.AreEqual( (false, (bool?)false, 0, 0, 0, 0), ScreamState( stoppedRide.CaptureSnapshot() ), "its script holds no scream" );

		// a stopped snapshot restores no scream, whatever an older or edited save says
		var stoppedSave = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), seed: 1, open: true, attractionId: saved.AttractionId );
		stoppedSave.RestoreSnapshot( saved with { Stopped = true, OwnedScreamCount = 3 } );
		stoppedSave.ResumeHeldScream();
		Assert.IsNull( Chain( stoppedSave ) );
		Assert.AreEqual( (false, (bool?)false, 0, 0, 0, 0), ScreamState( stoppedSave.CaptureSnapshot() ) );
	}

	[TestMethod]
	public void AnOlderSaveTakesTheScriptsViewOfTheHandleAndFallsBackToTheChainFlag()
	{
		var ride = new OriginalObjectRuntime( entry, new RideScriptWorld(), seed: 1 );
		var fresh = ride.CaptureSnapshot();
		OriginalObjectRuntime Restored( OriginalObjectRuntimeSnapshot snapshot )
		{
			var candidate = OriginalObjectRuntime.CreateForRestore( entry, new RideScriptWorld(), seed: 1, open: true, attractionId: fresh.AttractionId );
			candidate.RestoreSnapshot( snapshot );
			return candidate;
		}
		// before the script's view was saved, the chain flag stood for the handle
		Assert.IsTrue( Restored( fresh with { HeldScream = true, ScriptHoldsScream = null, HeldScreamBand = 3, HeldScreamLevel = 40, HeldScreamSpeed = 50 } ).Effects.ScriptHoldsScream );
		// a save made without audio before the operands followed the script's handle kept the handle but no operands: the handle comes back
		// (the script's next start is refused, as in the run that made the save), and band 0 plays nothing until the script stops it
		var headless = Restored( fresh with { HeldScream = false, ScriptHoldsScream = true } );
		Assert.IsTrue( headless.Effects.ScriptHoldsScream );
		StartAudio();
		headless.ResumeHeldScream();
		Assert.IsNull( Chain( headless ) );
		Assert.AreEqual( (true, (bool?)true, 0, 0, 0, 0), ScreamState( headless.CaptureSnapshot() ), "saved again with both flags set" );
	}
}
