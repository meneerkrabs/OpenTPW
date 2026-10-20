using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

// [EXT:periodic-autosave] the schedule, the refusals, the atomic write, the recovery choice and the option
[TestClass, DoNotParallelize]
public class PeriodicAutosaveTests
{
	private string root = null!;
	private BaseFileSystem previousFiles = null!;
	private GameEdition previousEdition;
	private Logger previousLog = null!;
	private string slot = null!;

	[TestInitialize]
	public void Initialize()
	{
		previousLog = Log; Log = new(); previousFiles = SaveFileSystem; previousEdition = GameEditions.Current; GameEditions.Select( GameEdition.ThemeParkWorld );
		root = Directory.CreateTempSubdirectory( "opentpw-autosave-" ).FullName; SaveFileSystem = new( root );
		slot = AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle", AutomaticSlotKind.Autosave );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Log = previousLog; SaveFileSystem = previousFiles; GameEditions.Select( previousEdition );
		if ( !OperatingSystem.IsWindows() ) File.SetUnixFileMode( root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
		Directory.Delete( root, true );
	}

	private static ParkResumeSnapshot Snapshot( string name = "jungle", DateTimeOffset? saved = null ) => ParkResumeSaveFileTests.Envelope() with
	{
		DisplayName = name, SavedUtc = saved ?? DateTimeOffset.UnixEpoch,
		Gate = new ParkGate.ParkGateSnapshot( 0 ), Staff = new StaffWorldSimulation.StaffWorldStateSnapshot( Array.Empty<StaffWorldSimulation.StaffActorSnapshot>() )
	};

	// the frames of play of a 60 Hz game, as the 0.25 s clamp allows: 4 frames of 0.25 s make a second
	private static AutosaveResult Play( PeriodicAutosave autosave, double seconds, Func<ParkResumeSnapshot?> capture, string path, bool playing = true )
	{
		var result = AutosaveResult.NotDue;
		for ( var elapsed = 0.0; elapsed < seconds - 1e-9; elapsed += 0.25 )
		{
			result = autosave.Tick( 0.25, playing, capture, path, Console.WriteLine );
			if ( result is AutosaveResult.Saved or AutosaveResult.Refused ) return result;
		}
		return result;
	}

	// a generous slice budget: one frame serializes everything, whatever the JIT is doing (the slicing itself has its own tests)
	private static PeriodicAutosave Synchronous( List<(string Path, byte[] Bytes)>? writes = null ) =>
		new( ( path, bytes ) => { writes?.Add( (path, bytes) ); ParkResumeSaveFile.SaveBytes( path, bytes ); }, work => { work(); return Task.CompletedTask; } ) { SliceBudget = TimeSpan.FromMinutes( 1 ) };

	[TestMethod]
	public void SavesAfterFiveMinutesOfPlayAndNotBefore()
	{
		var autosave = Synchronous(); var captures = 0;
		ParkResumeSnapshot? Capture() { captures++; return Snapshot(); }
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 299, Capture, slot ) ); Assert.AreEqual( 0, captures ); Assert.IsFalse( File.Exists( slot ) );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 2, Capture, slot ) ); Assert.AreEqual( 1, captures ); Assert.IsTrue( File.Exists( slot ) );
		Assert.AreEqual( 0, autosave.Elapsed, 1e-9, "the count starts again after a save" );
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 299, Capture, slot ) ); Assert.AreEqual( 1, captures );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 2, Capture, slot ) ); Assert.AreEqual( 2, captures );
	}

	[TestMethod]
	public void OnlyPlayTimeCountsAndAFrameSpikeAddsAQuarterSecond()
	{
		var autosave = Synchronous(); var captures = 0;
		ParkResumeSnapshot? Capture() { captures++; return Snapshot(); }
		// ten minutes paused: nothing counted, so resuming play does not save at once
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 600, Capture, slot, playing: false ) ); Assert.AreEqual( 0, autosave.Elapsed );
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 10, Capture, slot ) ); Assert.AreEqual( 0, captures );
		// a frame of an hour (a suspended laptop) counts a quarter of a second
		autosave.Tick( 3600, true, Capture, slot ); Assert.AreEqual( 10.25, autosave.Elapsed, 1e-9 );
		autosave.Tick( double.NaN, true, Capture, slot ); autosave.Tick( -5, true, Capture, slot ); Assert.AreEqual( 10.25, autosave.Elapsed, 1e-9 );
		// a pause in the middle of the interval only delays the save by its own length
		Play( autosave, 280, Capture, slot ); Assert.AreEqual( 0, captures ); Play( autosave, 600, Capture, slot, playing: false ); Assert.AreEqual( 0, captures );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 20, Capture, slot ) );
	}

	[TestMethod]
	public void SerializationIsSpreadOverFramesAndProducesTheManualSavesBytes()
	{
		var snapshot = Snapshot( "sliced" ); var expected = ParkResumeSaveFile.Serialize( snapshot );
		var sliced = ParkResumeSaveFile.BeginSlicedSerialize( snapshot ); Assert.ThrowsException<InvalidOperationException>( () => sliced.Bytes );
		while ( !sliced.Run( TimeSpan.Zero ) ) { }
		CollectionAssert.AreEqual( expected, sliced.Bytes ); Assert.IsTrue( sliced.Slices > 20, $"a zero budget does one step per slice ({sliced.Slices})" );
		// a generous budget does it in one slice
		var whole = ParkResumeSaveFile.BeginSlicedSerialize( snapshot ); Assert.IsTrue( whole.Run( TimeSpan.FromMinutes( 1 ) ) ); Assert.AreEqual( 1, whole.Slices ); CollectionAssert.AreEqual( expected, whole.Bytes );
		var again = ParkResumeSaveFile.Deserialize( sliced.Bytes ); Assert.AreEqual( "sliced", again.DisplayName );
	}

	[TestMethod]
	public void SlicedWalkerCoversEveryShapeOfTheCodec()
	{
		// the walker takes over the serializer's contract for objects and lists, so no type of the snapshot may rely on what it does not mirror
		var options = (System.Text.Json.JsonSerializerOptions)typeof( ParkResumeJson ).GetField( "options", BindingFlags.NonPublic | BindingFlags.Static )!.GetValue( null )!;
		var seen = new HashSet<Type>(); var work = new Queue<Type>(); work.Enqueue( typeof( ParkResumeSnapshot ) ); var problems = new List<string>();
		while ( work.Count > 0 )
		{
			var type = work.Dequeue(); if ( !seen.Add( type ) ) continue;
			var info = options.GetTypeInfo( type );
			if ( info.PolymorphismOptions != null ) problems.Add( $"{type} is polymorphic" );
			if ( info.Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Enumerable && info.ElementType != null ) work.Enqueue( info.ElementType );
			if ( info.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object ) continue;
			foreach ( var property in info.Properties )
			{
				if ( property.CustomConverter != null ) problems.Add( $"{type}.{property.Name} has its own converter" );
				if ( property.IsExtensionData ) problems.Add( $"{type}.{property.Name} is extension data" );
				if ( property.PropertyType == typeof( object ) || options.GetTypeInfo( property.PropertyType ).Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object && ( property.PropertyType.IsInterface || property.PropertyType.IsAbstract ) ) problems.Add( $"{type}.{property.Name} is declared as {property.PropertyType}" );
				work.Enqueue( property.PropertyType );
			}
		}
		Assert.AreEqual( 0, problems.Count, string.Join( "; ", problems ) );
	}

	[TestMethod]
	public void SerializingTakesSeveralFramesAndStopsWithItsPark()
	{
		var autosave = Synchronous(); autosave.SliceBudget = TimeSpan.Zero; var captures = 0;
		ParkResumeSnapshot? Capture() { captures++; return Snapshot( "frames" ); }
		Assert.AreEqual( AutosaveResult.Serializing, Play( autosave, 301, Capture, slot ), "the capture frame only starts the serialization" ); Assert.IsTrue( autosave.SerializationInProgress ); Assert.IsFalse( File.Exists( slot ) );
		// the park is paused meanwhile: the serialization goes on, no second capture happens
		AutosaveResult result; var frames = 1; do { result = autosave.Tick( 0.25, false, Capture, slot ); frames++; } while ( result == AutosaveResult.Serializing );
		Assert.AreEqual( AutosaveResult.Saved, result ); Assert.IsTrue( frames > 20 ); Assert.AreEqual( 1, captures ); Assert.AreEqual( "frames", ParkResumeSaveFile.Load( slot ).DisplayName );
		// a new park drops a serialization in progress: its snapshot is never written
		File.Delete( slot ); Assert.AreEqual( AutosaveResult.Serializing, Play( autosave, 301, Capture, slot ) );
		autosave.Reset(); Assert.IsFalse( autosave.SerializationInProgress ); Assert.AreEqual( AutosaveResult.NotDue, autosave.Tick( 0.25, true, Capture, slot ) ); Assert.IsFalse( File.Exists( slot ) );
	}

	[TestMethod]
	public void SnapshotTheValidationRefusesIsNeverWritten()
	{
		var autosave = Synchronous(); var warnings = new List<string>(); var checks = 0;
		AutosaveResult Frame() => autosave.Tick( 0.25, true, () => Snapshot( "refused" ), slot, warnings.Add, _ => { checks++; throw new InvalidDataException( "the asset manifest differs" ); } );
		Play( autosave, 299, () => Snapshot(), slot );
		var result = AutosaveResult.NotDue; for ( var i = 0; i < 20 && result != AutosaveResult.Refused; i++ ) result = Frame();
		Assert.AreEqual( AutosaveResult.Refused, result ); Assert.AreEqual( 1, checks ); Assert.IsFalse( File.Exists( slot ) ); StringAssert.Contains( warnings.Single(), "asset manifest" ); Assert.AreEqual( PeriodicAutosave.RetrySeconds, autosave.Wait );
		Assert.IsFalse( autosave.SerializationInProgress );
	}

	[TestMethod]
	public void RefusedCaptureIsRetriedSoonNotAfterAWholeInterval()
	{
		var autosave = Synchronous(); var warnings = new List<string>(); var speaking = true; var captures = 0;
		ParkResumeSnapshot? Capture() { captures++; if ( speaking ) throw new InvalidOperationException( "Cannot capture the automatic advisor while a response is being spoken" ); return Snapshot(); }
		Play( autosave, 299, Capture, slot ); Assert.AreEqual( 0, captures );
		var result = AutosaveResult.NotDue; for ( var i = 0; i < 8 && result != AutosaveResult.Refused; i++ ) result = autosave.Tick( 0.25, true, Capture, slot, warnings.Add );
		Assert.AreEqual( AutosaveResult.Refused, result ); Assert.AreEqual( 1, captures ); Assert.IsFalse( File.Exists( slot ) ); Assert.AreEqual( PeriodicAutosave.RetrySeconds, autosave.Wait );
		// still speaking after the retry wait: refused again, quietly
		Assert.AreEqual( AutosaveResult.Refused, Play( autosave, 20, Capture, slot ) ); Assert.AreEqual( 2, captures );
		speaking = false;
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 14, Capture, slot ) ); Assert.AreEqual( 2, captures, "nothing is tried before the retry wait is over" );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 2, Capture, slot ) ); Assert.IsTrue( File.Exists( slot ) );
		Assert.AreEqual( PeriodicAutosave.IntervalSeconds, autosave.Wait ); Assert.AreEqual( 1, warnings.Count, "one warning for the run of refusals" );
		// a capture that declines with null (a staff tool in hand) is a refusal too
		Play( autosave, 301, () => null, slot ); Assert.AreEqual( PeriodicAutosave.RetrySeconds, autosave.Wait );
	}

	[TestMethod]
	public void ParkThatCanNeverBeCapturedBacksOffToTheWholeInterval()
	{
		var autosave = Synchronous(); var captures = 0;
		ParkResumeSnapshot? Capture() { captures++; throw new InvalidDataException( "not resumable" ); }
		Play( autosave, 300, Capture, slot );
		for ( var i = 1; i < PeriodicAutosave.RefusalsBeforeBackoff; i++ ) Play( autosave, 16, Capture, slot );
		Assert.AreEqual( PeriodicAutosave.RefusalsBeforeBackoff, captures ); Assert.AreEqual( PeriodicAutosave.IntervalSeconds, autosave.Wait );
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 299, Capture, slot ) ); Assert.AreEqual( PeriodicAutosave.RefusalsBeforeBackoff, captures );
	}

	[TestMethod]
	public void BugsInCaptureAreNotSwallowed()
	{
		var autosave = Synchronous(); Play( autosave, 299, Snapshot0, slot );
		Assert.ThrowsException<ArgumentNullException>( () => Play( autosave, 5, () => throw new ArgumentNullException( "bug" ), slot ) );
		static ParkResumeSnapshot? Snapshot0() => null;
	}

	[TestMethod]
	public void StaysDueWhileTheLastWriteRunsAndRetriesAfterAFailedWrite()
	{
		var release = new ManualResetEventSlim(); var writes = 0;
		var autosave = new PeriodicAutosave( ( path, bytes ) => { writes++; release.Wait(); ParkResumeSaveFile.SaveBytes( path, bytes ); }, work => Task.Run( work ) ) { SliceBudget = TimeSpan.FromMinutes( 1 ) };
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 301, () => Snapshot(), slot ) ); Assert.IsTrue( autosave.WriteInProgress ); Assert.IsTrue( SpinWait.SpinUntil( () => Volatile.Read( ref writes ) == 1, 10000 ) );
		// the next interval ends while the write is stuck: busy, not a second write, and the player is not blocked
		Assert.AreEqual( AutosaveResult.Busy, Play( autosave, 301, () => Snapshot( "second" ), slot ) ); Assert.AreEqual( 1, writes );
		release.Set(); autosave.Flush();
		Assert.AreEqual( AutosaveResult.Saved, autosave.Tick( 0.25, true, () => Snapshot( "second" ), slot ) ); autosave.Flush();
		Assert.AreEqual( "second", ParkResumeSaveFile.Load( slot ).DisplayName );

		var failing = new PeriodicAutosave( ( _, _ ) => throw new IOException( "disk full" ), work => { try { work(); return Task.CompletedTask; } catch ( Exception error ) { return Task.FromException( error ); } } ) { SliceBudget = TimeSpan.FromMinutes( 1 ) };
		var warnings = new List<string>();
		Assert.AreEqual( AutosaveResult.Saved, Play( failing, 301, () => Snapshot(), slot ) );
		Assert.AreEqual( AutosaveResult.Refused, failing.Tick( 0.25, true, () => Snapshot(), slot, warnings.Add ) );
		StringAssert.Contains( warnings.Single(), "disk full" ); Assert.AreEqual( PeriodicAutosave.RetrySeconds, failing.Wait );
	}

	/// <summary>
	/// A write that is started and then held: the started Action, its completion task and its destination are captured, so a test
	/// controls exactly when a write runs, succeeds or fails around a park switch, without assuming any delta size.
	/// </summary>
	private sealed class HeldWrites
	{
		private readonly List<Action> work = new();
		private readonly List<TaskCompletionSource> gates = new();
		/// <summary>(destination, bytes) of every persist that ran, in run order; a controlled failure runs first, then throws.</summary>
		public readonly List<(string Path, byte[] Bytes)> Ran = new();

		public int StartedWrites => work.Count;

		internal PeriodicAutosave Create() => new( ( path, bytes ) => Ran.Add( (path, bytes) ), Work ) { SliceBudget = TimeSpan.FromMinutes( 1 ) };

		private Task Work( Action started )
		{
			work.Add( started );
			var gate = new TaskCompletionSource();
			gates.Add( gate );
			return gate.Task;
		}

		/// <summary>Runs the <paramref name="index"/>'th started write to its end; <paramref name="fail"/> throws after the persist ran (its task faults).</summary>
		public void Finish( int index, bool fail = false )
		{
			try
			{
				work[index]();
				if ( fail ) throw new IOException( "disk full" );
				gates[index].SetResult();
			}
			catch ( Exception error ) { gates[index].SetException( error ); }
		}
	}

	[TestMethod]
	public void SwitchingParksDoesNotRetimeTheNewParkForTheOldParksFailedWrite()
	{
		var held = new HeldWrites(); var autosave = held.Create();
		var slotA = Path.Combine( root, "first.otpw.json" ); var slotB = Path.Combine( root, "second.otpw.json" );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 301, () => Snapshot( "first" ), slotA ), "park A's write is started" );
		// the switch (GameFlow.UpdateAutosave): the count starts again and the old write keeps running on its own
		autosave.Reset(); Assert.IsTrue( autosave.WriteInProgress, "the old park's write stays tracked, so Busy and the single atomic writer are kept" );
		// park B accrues play time, and the old write then fails under it
		for ( var frame = 0; frame < 4; frame++ ) Assert.AreEqual( AutosaveResult.NotDue, autosave.Tick( 0.25, true, () => Snapshot( "second" ), slotB ) );
		Assert.AreEqual( 1.0, autosave.Elapsed, 1e-9 );
		held.Finish( 0, fail: true );
		// the new park's frame observes the fault exactly once: one qualified warning, and its schedule is what this frame alone makes of it
		var warnings = new List<string>();
		Assert.AreEqual( AutosaveResult.NotDue, autosave.Tick( 0.25, true, () => Snapshot( "second" ), slotB, warnings.Add ) );
		Assert.AreEqual( 1, warnings.Count, "the old park's failed write warns exactly once" );
		StringAssert.Contains( warnings[0], "previous park" ); StringAssert.Contains( warnings[0], "disk full" ); StringAssert.Contains( warnings[0], "the current park autosave schedule is unchanged" );
		Assert.AreEqual( 1.25, autosave.Elapsed, 1e-9, "the new park's count is not reset by the old park's failure" );
		Assert.AreEqual( PeriodicAutosave.IntervalSeconds, autosave.Wait ); Assert.AreEqual( 0, autosave.Refusals );
		// the documented schedule stands: park B autosaves 300 s of play after it started, into its own slot
		var captures = 0;
		Assert.AreEqual( AutosaveResult.NotDue, autosave.Tick( 0.25, true, () => { captures++; return Snapshot( "second" ); }, slotB, warnings.Add ) );
		Assert.AreEqual( 1, warnings.Count, "the old failure is observed only once" );
		Assert.AreEqual( 1.5, autosave.Elapsed, 1e-9 );
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 298.25, () => { captures++; return Snapshot( "second" ); }, slotB ) );
		Assert.AreEqual( 0, captures ); Assert.AreEqual( 299.75, autosave.Elapsed, 1e-9 );
		Assert.AreEqual( AutosaveResult.Saved, autosave.Tick( 0.25, true, () => { captures++; return Snapshot( "second" ); }, slotB ) );
		Assert.AreEqual( 1, captures, "the old failure added no early or extra capture" );
		held.Finish( 1 );
		Assert.AreEqual( slotB, held.Ran[1].Path ); Assert.AreEqual( "second", ParkResumeSaveFile.Deserialize( held.Ran[1].Bytes ).DisplayName );
		// the captured A bytes stayed bound to A's slot through the switch, even though their write failed
		Assert.AreEqual( slotA, held.Ran[0].Path ); Assert.AreEqual( "first", ParkResumeSaveFile.Deserialize( held.Ran[0].Bytes ).DisplayName );
		autosave.Flush();
	}

	[TestMethod]
	public void OldParksCompletedButUnconsumedFailedWriteWarnsOnceAtTheNewParksFirstFrameWithoutRetiming()
	{
		var held = new HeldWrites(); var autosave = held.Create();
		var slotA = Path.Combine( root, "first.otpw.json" ); var slotB = Path.Combine( root, "second.otpw.json" );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 301, () => Snapshot( "first" ), slotA ) );
		Assert.AreEqual( 1, held.StartedWrites );
		// the old write already failed (completed but unconsumed) before the park switch
		held.Finish( 0, fail: true );
		Assert.IsFalse( autosave.WriteInProgress, "a finished write no longer holds the single writer" );
		Assert.IsFalse( autosave.SerializationInProgress );
		autosave.Reset();
		var warnings = new List<string>();
		// the new park's first frame observes the fault once: it warns and keeps its own schedule
		Assert.AreEqual( AutosaveResult.NotDue, autosave.Tick( 0.25, true, () => Snapshot( "second" ), slotB, warnings.Add ) );
		Assert.AreEqual( 1, warnings.Count );
		StringAssert.Contains( warnings[0], "previous park" ); StringAssert.Contains( warnings[0], "disk full" ); StringAssert.Contains( warnings[0], "schedule is unchanged" );
		Assert.AreEqual( 0.25, autosave.Elapsed, 1e-9 ); Assert.AreEqual( PeriodicAutosave.IntervalSeconds, autosave.Wait );
		// a reset after the old failure was reported repeats nothing
		autosave.Reset();
		Assert.AreEqual( AutosaveResult.NotDue, autosave.Tick( 0.25, true, () => Snapshot( "second" ), slotB, warnings.Add ) );
		Assert.AreEqual( 1, warnings.Count, "no second warning" ); Assert.AreEqual( 0.25, autosave.Elapsed, 1e-9 );
		// the new park proceeds on its own schedule, into its own slot
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 300, () => Snapshot( "second" ), slotB ) );
		held.Finish( 1 );
		Assert.AreEqual( slotB, held.Ran[1].Path );
		Assert.AreEqual( slotA, held.Ran[0].Path ); Assert.AreEqual( "first", ParkResumeSaveFile.Deserialize( held.Ran[0].Bytes ).DisplayName );
		autosave.Flush();
	}

	[TestMethod]
	public void OldParksSuccessfulWriteKeepsBusyUntilItFinishesThenTheNewParkProceedsAndBothWritesStayBound()
	{
		var held = new HeldWrites(); var autosave = held.Create();
		var slotA = Path.Combine( root, "first.otpw.json" ); var slotB = Path.Combine( root, "second.otpw.json" );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 301, () => Snapshot( "first" ), slotA ) );
		autosave.Reset();
		// park B comes due while the old park's write still runs: busy, no capture of B, the old bytes still bound to their attempt
		var captures = 0;
		Assert.AreEqual( AutosaveResult.Busy, Play( autosave, 300, () => { captures++; return Snapshot( "second" ); }, slotB ) );
		Assert.AreEqual( 0, captures, "the new park is not captured while the old park's write runs" );
		// the old write finishes into its own slot; B's due frame proceeds
		held.Finish( 0 );
		Assert.AreEqual( AutosaveResult.Saved, autosave.Tick( 0.25, true, () => { captures++; return Snapshot( "second" ); }, slotB ) );
		Assert.AreEqual( 1, captures ); Assert.IsTrue( autosave.WriteInProgress ); Assert.AreEqual( 2, held.StartedWrites );
		held.Finish( 1 );
		// the captured A and B bytes stayed bound to their own slots through the switch
		Assert.AreEqual( slotA, held.Ran[0].Path ); Assert.AreEqual( "first", ParkResumeSaveFile.Deserialize( held.Ran[0].Bytes ).DisplayName );
		Assert.AreEqual( slotB, held.Ran[1].Path ); Assert.AreEqual( "second", ParkResumeSaveFile.Deserialize( held.Ran[1].Bytes ).DisplayName );
		autosave.Flush();
	}

	[TestMethod]
	public void TheCurrentParksFailedWriteStillRetriesAfterFifteenSecondsOfPlay()
	{
		var attempts = 0; var bytes = new List<byte[]>();
		var autosave = new PeriodicAutosave( ( _, data ) =>
			{
				if ( attempts++ == 0 ) throw new IOException( "disk full" );
				bytes.Add( data );
			}, work =>
			{
				try { work(); return Task.CompletedTask; }
				catch ( Exception error ) { return Task.FromException( error ); }
			} ) { SliceBudget = TimeSpan.FromMinutes( 1 ) };
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 301, () => Snapshot( "retry" ), slot ) );
		var warnings = new List<string>();
		Assert.AreEqual( AutosaveResult.Refused, autosave.Tick( 0.25, true, () => Snapshot( "retry" ), slot, warnings.Add ) );
		StringAssert.Contains( warnings.Single(), "disk full" ); Assert.AreEqual( PeriodicAutosave.RetrySeconds, autosave.Wait );
		// the documented retry: a fresh capture is saved 15 s of play after the failure, and the final bytes are the second capture's
		Assert.AreEqual( AutosaveResult.NotDue, Play( autosave, 14, () => Snapshot( "retry" ), slot ) );
		Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 2, () => Snapshot( "retry" ), slot ) );
		Assert.AreEqual( 1, bytes.Count );
		Assert.AreEqual( "retry", ParkResumeSaveFile.Deserialize( bytes.Single() ).DisplayName );
		autosave.Flush();
	}

	[TestMethod]
	public void WriteIsAtomicAndNeverTouchesTheManualOrCheckpointSlot()
	{
		var manual = SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" ); var checkpoint = AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" );
		ParkResumeSaveFile.Save( manual, Snapshot( "manual" ) ); ParkResumeSaveFile.Save( checkpoint, Snapshot( "checkpoint" ) );
		var manualBytes = File.ReadAllBytes( manual ); var checkpointBytes = File.ReadAllBytes( checkpoint );
		var writes = new List<(string Path, byte[] Bytes)>(); var autosave = Synchronous( writes );
		Play( autosave, 301, () => Snapshot( "auto 1" ), slot ); var first = File.ReadAllBytes( slot );
		Assert.AreEqual( slot, writes.Single().Path ); CollectionAssert.AreEqual( manualBytes, File.ReadAllBytes( manual ) ); CollectionAssert.AreEqual( checkpointBytes, File.ReadAllBytes( checkpoint ) );
		Assert.AreEqual( "auto 1", ParkResumeSaveFile.Load( slot ).DisplayName ); Assert.AreEqual( 0, Directory.GetFiles( root, "*.tmp" ).Length );

		// a write that cannot create its temporary file (read-only directory) leaves the previous autosave byte for byte (skipped where the folder stays writable: Windows, a root user)
		if ( !OperatingSystem.IsWindows() )
		{
			File.SetUnixFileMode( root, UnixFileMode.UserRead | UnixFileMode.UserExecute );
			try
			{
				var probe = Path.Combine( root, "probe" ); var stillWritable = false;
				try { File.WriteAllText( probe, "x" ); File.Delete( probe ); stillWritable = true; } catch ( UnauthorizedAccessException ) { }
				if ( !stillWritable )
				{
					Assert.ThrowsException<UnauthorizedAccessException>( () => ParkResumeSaveFile.SaveBytes( slot, ParkResumeSaveFile.Serialize( Snapshot( "auto 2" ) ) ) );
					CollectionAssert.AreEqual( first, File.ReadAllBytes( slot ) );
				}
			}
			finally { File.SetUnixFileMode( root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute ); }
		}
		// a process that died inside a write left a temporary file: the next write removes it and replaces the slot
		var stale = slot + "." + Guid.NewGuid().ToString( "N" ) + ".tmp"; File.WriteAllText( stale, "{ torn" );
		PeriodicAutosave.PersistWithCleanup( slot, ParkResumeSaveFile.Serialize( Snapshot( "auto 3" ) ) );
		Assert.IsFalse( File.Exists( stale ) ); Assert.AreEqual( "auto 3", ParkResumeSaveFile.Load( slot ).DisplayName );
		// bytes that are not a world save are never written as one
		Assert.ThrowsException<InvalidDataException>( () => ParkResumeSaveFile.SaveBytes( Path.Combine( root, "x.tpws" ), new byte[] { 1 } ) );
	}

	[TestMethod]
	public void AutosaveSlotIsItsOwnFileAndRoundTripsThroughTheContinuationProbe()
	{
		Assert.AreNotEqual( AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" ), slot ); StringAssert.EndsWith( slot, "opentpw-jungle-autosave-world.json" );
		Assert.IsNull( AutomaticThemeContinuation.Probe( SaveFileSystem, "jungle", AutomaticSlotKind.Autosave ) );
		Play( Synchronous(), 301, () => Snapshot( "saved by autosave", new DateTimeOffset( 2026, 10, 18, 12, 0, 0, TimeSpan.Zero ) ), slot );
		var entry = AutomaticThemeContinuation.Probe( SaveFileSystem, "jungle", AutomaticSlotKind.Autosave )!;
		Assert.AreEqual( AutomaticSlotKind.Autosave, entry.Kind ); Assert.AreEqual( "saved by autosave", entry.DisplayName ); Assert.IsNull( AutomaticThemeContinuation.Probe( SaveFileSystem, "jungle" ) );
		Assert.AreEqual( "saved by autosave", AutomaticThemeContinuation.Read( SaveFileSystem, entry ).DisplayName );
		// an entry of one slot is refused for the other
		Assert.ThrowsException<InvalidDataException>( () => AutomaticThemeContinuation.Read( SaveFileSystem, entry with { Kind = AutomaticSlotKind.Checkpoint } ) );
	}

	private static readonly DateTimeOffset Noon = new( 2026, 10, 18, 12, 0, 0, TimeSpan.Zero );

	[TestMethod]
	public void RecoveryOffersTheAutosaveOnlyWhenItIsNewerThanTheManualSaveAndTheCheckpoint()
	{
		var manual = SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" ); var checkpoint = AutomaticThemeContinuation.Slot( SaveFileSystem, "jungle" );
		Assert.IsNull( AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" ) );
		// an autosave alone is offered (a crash before any manual save or exit)
		ParkResumeSaveFile.Save( slot, Snapshot( "auto", Noon ) );
		Assert.AreEqual( AutomaticSlotKind.Autosave, AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" )!.Kind );
		// older than the manual save: the manual save is the player's latest word, no autosave offer
		ParkResumeSaveFile.Save( manual, Snapshot( "manual", Noon.AddMinutes( 1 ) ) ); Assert.IsNull( AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" ) );
		// newer than the manual save: offered
		ParkResumeSaveFile.Save( slot, Snapshot( "auto", Noon.AddMinutes( 6 ) ) ); Assert.AreEqual( AutomaticSlotKind.Autosave, AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" )!.Kind );
		// the exit checkpoint is newer than the autosave: the checkpoint is offered as before
		ParkResumeSaveFile.Save( checkpoint, Snapshot( "checkpoint", Noon.AddMinutes( 7 ) ) ); var newest = AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" )!;
		Assert.AreEqual( (AutomaticSlotKind.Checkpoint, "checkpoint"), (newest.Kind, newest.DisplayName) );
		// an autosave after the checkpoint (a second park played after leaving the first) wins again
		ParkResumeSaveFile.Save( slot, Snapshot( "auto", Noon.AddMinutes( 12 ) ) ); Assert.AreEqual( AutomaticSlotKind.Autosave, AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" )!.Kind );
		// a damaged autosave is not offered; the checkpoint still is
		File.WriteAllText( slot, "{ not a save" ); Assert.AreEqual( AutomaticSlotKind.Checkpoint, AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" )!.Kind );
	}

	private static void Optional( object target, string name, object value ) => target.GetType().GetProperty( name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )?.SetValue( target, value );
	private static LobbyIslandInfo Island() => new( 0, "jungle", "", "", "", "jungle", 0, 0, (0, 0, 0), Array.Empty<string>(), false, 0 );

	[TestMethod]
	public void ThemeMenuLabelsTheAutosaveEntryAndActivatesItsExactEntry()
	{
		ParkResumeSaveFile.Save( slot, Snapshot( "auto", Noon ) );
		var entry = AutomaticThemeContinuation.Newest( SaveFileSystem, "jungle" )!; var activated = new List<AutomaticParkContinueEntry>();
		var actions = new FrontEndActions(); Optional( actions, "AutomaticEntry", (Func<string, AutomaticParkContinueEntry?>)(_ => entry) ); Optional( actions, "ContinueAutomatic", (Action<AutomaticParkContinueEntry>)activated.Add );
		var strings = OriginalUiTests.FakeStrings(); var menu = new FrontEndMenu( strings, new[] { Island() }, actions );
		var context = new UiContext( strings, null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
		menu.Stack.Update( context, UiInput.Click( menu.Stack.Top!.Find( "enterPark" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreEqual( "themeStart", menu.Stack.Top!.Name );
		var label = ((UiButton)menu.Stack.Top.Find( "choice0" )!).Text!();
		StringAssert.StartsWith( label, strings.Extra( OpenTpwText.ContinueAutosavePark ) ); Assert.AreNotEqual( strings.Extra( OpenTpwText.ContinueAutomaticPark ), strings.Extra( OpenTpwText.ContinueAutosavePark ) );
		menu.Stack.Update( context, UiInput.Click( menu.Stack.Top.Find( "choice0" )!.ScreenRect( context.Canvas ).Center ) );
		Assert.AreSame( entry, activated.Single() );
		// the checkpoint entry keeps its own label
		var checkpointEntry = entry with { Kind = AutomaticSlotKind.Checkpoint }; Optional( actions, "AutomaticEntry", (Func<string, AutomaticParkContinueEntry?>)(_ => checkpointEntry) );
		var other = new FrontEndMenu( strings, new[] { Island() }, actions );
		other.Stack.Update( context, UiInput.Click( other.Stack.Top!.Find( "enterPark" )!.ScreenRect( context.Canvas ).Center ) );
		StringAssert.StartsWith( ((UiButton)other.Stack.Top!.Find( "choice0" )!).Text!(), strings.Extra( OpenTpwText.ContinueAutomaticPark ) );
	}

	private static void Set( object target, string name, object? value ) => target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( target, value );

	[TestMethod]
	public void GameFlowTakesTheAutosaveIntoItsOwnSlotAndCountsPlayTimeOnly()
	{
		var flow = (GameFlow)RuntimeHelpers.GetUninitializedObject( typeof( GameFlow ) ); Set( flow, "autosave", Synchronous() );
		var level = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); Set( level, "<LevelName>k__BackingField", "jungle" ); var captures = 0;
		flow.AutosaveCaptureOverride = _ => { captures++; return Snapshot( "flow" ); };
		Assert.AreEqual( AutosaveResult.NotDue, flow.RunAutosave( level, 299, false ), "paused time never counts" );
		Assert.AreEqual( AutosaveResult.NotDue, flow.RunAutosave( level, 299, true ) );
		AutosaveResult result = AutosaveResult.NotDue; for ( var i = 0; i < 1300 && result != AutosaveResult.Saved; i++ ) result = flow.RunAutosave( level, 0.25, true );
		Assert.AreEqual( AutosaveResult.Saved, result ); Assert.AreEqual( 1, captures );
		Assert.AreEqual( "flow", ParkResumeSaveFile.Load( slot ).DisplayName ); Assert.IsFalse( File.Exists( SaveFileSystem.GetAbsolutePath( "opentpw-jungle-world.json" ) ) );
	}

	[TestMethod]
	public void OnlyOwnedWritableThemeParksAreEligible()
	{
		var eligible = typeof( GameFlow ).GetMethod( "AutomaticEligible", BindingFlags.Static | BindingFlags.NonPublic )!;
		var visit = (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ); Set( visit, "<Visit>k__BackingField", RuntimeHelpers.GetUninitializedObject( typeof( Level ).GetProperty( "Visit" )!.PropertyType ) );
		Assert.IsFalse( (bool)eligible.Invoke( null, new object[] { visit } )!, "a read-only visit never autosaves" );
		Assert.IsFalse( (bool)eligible.Invoke( null, new object[] { (Level)RuntimeHelpers.GetUninitializedObject( typeof( Level ) ) } )!, "a level without a park, as the front end's" );
	}

	[TestMethod]
	public void CapturingForAnAutosaveDoesNotChangeTheSimulation()
	{
		using var renderer = new AutomaticCheckpointCpuRendererScope();
		var fixture = new UpgradeCatalogueDefaultsTests(); fixture.Setup();
		try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests );
			var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
			var assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
			typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			var before = world.Level.ComputeStateHash(); var random = world.Level.CaptureRandomState();
			var autosave = Synchronous();
			for ( var i = 0; i < 3; i++ )
				Assert.AreEqual( AutosaveResult.Saved, Play( autosave, 301, () => world.Level.CaptureResumeSnapshot( null, "jungle", assets, DateTimeOffset.UtcNow ), slot ) );
			// the capture of the autosave defers its validation to the serialization's first step; both orders give the manual save's bytes
			Action? validate = null; var unchecked_ = world.Level.CaptureResumeSnapshotUnchecked( null, "jungle", out validate, assets, Noon ); validate();
			CollectionAssert.AreEqual( ParkResumeSaveFile.Serialize( world.Level.CaptureResumeSnapshot( null, "jungle", assets, Noon ) ), ParkResumeSaveFile.Serialize( unchecked_ ) );
			Assert.AreEqual( before, world.Level.ComputeStateHash(), "the state hash, which covers every random stream, did not move" );
			// the snapshot is a detached copy: the park may run on while it is serialized in slices
			var detached = world.Level.CaptureResumeSnapshot( null, "jungle", assets, Noon ); var detachedBytes = ParkResumeSaveFile.Serialize( detached );
			for ( var tick = 0; tick < 600; tick++ ) world.Level.AdvanceSimulation( FixedStepClock.TickDuration ); Assert.AreNotEqual( before, world.Level.ComputeStateHash(), "the park really ran on" );
			CollectionAssert.AreEqual( detachedBytes, ParkResumeSaveFile.Serialize( detached ), "the captured snapshot did not change while the park ran" );
			before = world.Level.ComputeStateHash();
			Assert.AreEqual( random, world.Level.CaptureRandomState() );
			var saved = AutomaticThemeContinuation.Read( SaveFileSystem, AutomaticThemeContinuation.Probe( SaveFileSystem, "jungle", AutomaticSlotKind.Autosave )! );
			Assert.AreEqual( 2, saved.Layout.Objects.Count );
		}
		finally { fixture.Cleanup(); }
	}

	/// <summary>Every object a snapshot or the live park reaches by its fields, by identity (strings, types, delegates and enums are not followed).</summary>
	private static Dictionary<object, string> Reach( object root )
	{
		var seen = new Dictionary<object, string>( ReferenceEqualityComparer.Instance ); var stack = new Stack<(object, string)>(); stack.Push( (root, root.GetType().Name) );
		while ( stack.Count > 0 )
		{
			var (current, path) = stack.Pop(); var type = current.GetType();
			if ( type.IsPrimitive || type.IsEnum || current is string || current is Type || current is Delegate || current is System.Reflection.MemberInfo || current is Thread || current is Task || type.IsPointer ) continue;
			if ( !type.IsValueType && !seen.TryAdd( current, path ) ) continue;
			if ( current is Array array )
			{
				if ( !array.GetType().GetElementType()!.IsPrimitive ) foreach ( var item in array ) if ( item != null ) stack.Push( (item, path + "[]") );
				continue;
			}
			for ( var t = type; t != null && t != typeof( object ); t = t.BaseType )
				foreach ( var field in t.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly ) )
				{
					if ( field.FieldType.IsPointer || field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof( string ) ) continue;
					object? value; try { value = field.GetValue( current ); } catch { continue; }
					if ( value != null ) stack.Push( (value, path + "." + field.Name) );
				}
		}
		return seen;
	}

	[TestMethod]
	public void CapturedSnapshotSharesNoMutableStateWithTheRunningParkAndStaysStableWhileSliced()
	{
		using var renderer = new AutomaticCheckpointCpuRendererScope();
		var fixture = new UpgradeCatalogueDefaultsTests(); fixture.Setup();
		try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests );
			var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
			var assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
			typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			var guests = world.Level.Guests!; var grid = guests.Grid;
			for ( var y = 0; y < 30; y++ ) for ( var x = 0; x < 30; x++ ) grid.SetPath( x, y, true );
			try { for ( var i = 0; i < 200; i++ ) guests.SpawnInPark( i % 30, i / 30 % 30 ); } catch ( InvalidOperationException ) { }
			for ( var tick = 0; tick < 300; tick++ ) world.Level.AdvanceSimulation( FixedStepClock.TickDuration );
			var snapshot = world.Level.CaptureResumeSnapshotUnchecked( null, "jungle", out var check, assets, Noon ); check();
			var reference = ParkResumeSaveFile.Serialize( snapshot );
			var captured = Reach( snapshot ); var live = Reach( world.Level );
			var shared = captured.Keys.Where( live.ContainsKey ).Where( item => item is not Array { Length: 0 } ).Where( item => item is Array || item is System.Collections.IList || item is System.Collections.IDictionary || ( !item.GetType().IsValueType && item.GetType().GetMethod( "<Clone>$" ) == null && item.GetType().GetProperties().Any( p => p.SetMethod is { IsPublic: true } && !p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any() ) ) ).ToList();
			Assert.AreEqual( 0, shared.Count, "the snapshot aliases live mutable state: " + string.Join( " | ", shared.Take( 20 ).Select( item => captured[item] + " == " + live[item] ) ) );
			// the park runs on between the slices of the serialization
			var sliced = ParkResumeSaveFile.BeginSlicedSerialize( snapshot, check ); var frames = 0;
			while ( !sliced.Run( TimeSpan.Zero ) ) { frames++; for ( var tick = 0; tick < 3; tick++ ) world.Level.AdvanceSimulation( FixedStepClock.TickDuration ); }
			Assert.IsTrue( frames > 10, "serialization took several slices" );
			CollectionAssert.AreEqual( reference, sliced.Bytes, "a park that kept running between the slices did not tear the save" );
		}
		finally { fixture.Cleanup(); }
	}

	// Measurement, not a check: OPENTPW_AUTOSAVE_BENCH=<guests> prints the capture, serialization and write time of a park with that many guests (docs/PARK-SAVES.md)
	[TestMethod]
	public void MeasureCaptureSerializeAndWriteOfALargePark()
	{
		if ( !int.TryParse( Environment.GetEnvironmentVariable( "OPENTPW_AUTOSAVE_BENCH" ), out var count ) ) return;
		using var renderer = new AutomaticCheckpointCpuRendererScope();
		var fixture = new UpgradeCatalogueDefaultsTests(); fixture.Setup();
		try
		{
			var type = typeof( UpgradeCatalogueDefaultsTests );
			var world = ((Level Level, ParkEconomy Economy, OriginalObject One, OriginalObject Two, ParkObjectState State))type.GetMethod( "World", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( fixture, new object[] { "", 0, false } )!;
			var assets = (Dictionary<string, string>)type.GetMethod( "PrepareCapture", BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, new object[] { world } )!;
			typeof( Level ).GetMethod( "SetupStaff", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			typeof( Level ).GetMethod( "SetupParkGate", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( world.Level, null );
			var guests = world.Level.Guests!; var grid = guests.Grid; var spawned = 0;
			for ( var y = 0; y < 30; y++ ) for ( var x = 0; x < 30; x++ ) grid.SetPath( x, y, true );
			try { for ( var i = 0; i < count; i++ ) { guests.SpawnInPark( i % 30, i / 30 % 30 ); spawned++; } } catch ( InvalidOperationException ) { }
			var validateMs = new List<double>(); var slicedTotal = new List<double>(); var slicedMax = new List<double>(); var slices = 0; var capture = new List<double>(); var serialize = new List<double>(); var write = new List<double>(); var bytes = 0;
			for ( var run = 0; run < 25; run++ )
			{
				var t0 = System.Diagnostics.Stopwatch.GetTimestamp(); var snapshot = world.Level.CaptureResumeSnapshotUnchecked( null, "jungle", out var check, assets, DateTimeOffset.UtcNow );
				var t1 = System.Diagnostics.Stopwatch.GetTimestamp(); var data = ParkResumeSaveFile.Serialize( snapshot );
				var t2 = System.Diagnostics.Stopwatch.GetTimestamp(); PeriodicAutosave.PersistWithCleanup( slot, data ); var t3 = System.Diagnostics.Stopwatch.GetTimestamp();
				var v0 = System.Diagnostics.Stopwatch.GetTimestamp(); check(); var v1 = System.Diagnostics.Stopwatch.GetTimestamp();
				var sliced = ParkResumeSaveFile.BeginSlicedSerialize( snapshot ); var maxSlice = 0.0; var total = 0.0;
				while ( true ) { var q0 = System.Diagnostics.Stopwatch.GetTimestamp(); var done = sliced.Run( TimeSpan.FromMilliseconds( 2 ) ); var ms = System.Diagnostics.Stopwatch.GetElapsedTime( q0 ).TotalMilliseconds; total += ms; maxSlice = Math.Max( maxSlice, ms ); if ( done ) break; }
				CollectionAssert.AreEqual( data, sliced.Bytes ); if ( run > 0 ) { validateMs.Add( System.Diagnostics.Stopwatch.GetElapsedTime( v0, v1 ).TotalMilliseconds ); slicedTotal.Add( total ); slicedMax.Add( maxSlice ); slices = sliced.Slices; }
				if ( run == 0 ) continue; // warm-up
				bytes = data.Length; capture.Add( System.Diagnostics.Stopwatch.GetElapsedTime( t0, t1 ).TotalMilliseconds ); serialize.Add( System.Diagnostics.Stopwatch.GetElapsedTime( t1, t2 ).TotalMilliseconds ); write.Add( System.Diagnostics.Stopwatch.GetElapsedTime( t2, t3 ).TotalMilliseconds );
			}
			string Stat( List<double> values ) => $"median {values.OrderBy( v => v ).ElementAt( values.Count / 2 ):0.0} ms, max {values.Max():0.0} ms";
			Console.WriteLine( $"AUTOSAVE-BENCH guests={spawned} bytes={bytes / 1024} KiB capture[{Stat( capture )}] serialize[{Stat( serialize )}] write[{Stat( write )}] validate[{Stat( validateMs )}] sliced-total[{Stat( slicedTotal )}] slice-max[{Stat( slicedMax )}] slices={slices}" );
		}
		finally { fixture.Cleanup(); }
	}

	[TestMethod]
	public void OptionIsOnByDefaultSurvivesOldFilesAndRoundTrips()
	{
		Assert.IsTrue( new GameOptions().Autosave );
		var path = Path.Combine( root, GameOptions.FileName );
		File.WriteAllText( path, "{ \"Advisor\": false }" ); Assert.IsTrue( GameOptions.Load( path ).Autosave, "an options file from before the option has autosave on" );
		var off = new GameOptions { Autosave = false }; off.Save( path ); Assert.IsFalse( GameOptions.Load( path ).Autosave );
		var copy = new GameOptions(); copy.CopyFrom( off ); Assert.IsFalse( copy.Autosave ); Assert.IsFalse( off.Clone().Autosave );
	}

	[TestMethod]
	public void OptionsPageTogglesAutosaveAndCancelRestoresIt()
	{
		var stack = new UiScreenStack(); var options = new GameOptions(); var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720 } );
		var main = OptionsScreen.Create( stack, OriginalUiTests.FakeStrings(), OriginalUiTests.OptionsServicesFor( options, display, languages: new List<string>() ), () => { } ); stack.Push( main );
		main.Find( "openTpw" )!.Activate(); var page = stack.Top!;
		Assert.AreEqual( "Autosave:ui334", ((UiLabel)page.Find( "autosaveLabel" )!).Text() );
		((UiButton)page.Find( "autosave" )!).Activate();
		Assert.IsFalse( options.Autosave ); Assert.AreEqual( "Autosave:ui333", ((UiLabel)page.Find( "autosaveLabel" )!).Text() );
		stack.Update( new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) ) { Canvas = new UiCanvas( 1024, 768 ) }, UiInput.Key( UiKeys.Back ) );
		main.Find( "cancel" )!.Activate(); Assert.IsTrue( options.Autosave, "Cancel restores the value from before the screen opened" );
	}

	[TestMethod]
	public void EveryLanguageHasTheNewTexts()
	{
		foreach ( var language in SupplementaryStrings.SupportedLanguages )
			foreach ( var key in new[] { OpenTpwText.ContinueAutosavePark, OpenTpwText.AutosaveOption, OpenTpwText.AutosaveHelp } )
			{
				var text = SupplementaryStrings.Get( key, language ); Assert.IsFalse( string.IsNullOrWhiteSpace( text ), $"{key} in {language}" );
				if ( language != "English" ) Assert.AreNotEqual( SupplementaryStrings.Get( key, "English" ), text, $"{key} is translated for {language}" );
			}
	}
}
