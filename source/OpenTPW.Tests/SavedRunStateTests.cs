using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The saved run state of an RSSE record (tp.exe loader 0x5597A0) on the saved game clock: the program counter, the stack and heap, the variables, the limbo table, the result register and
/// every absolute deadline (WAIT 0x553783, WAITABS 0x553828, SETTIMER 0x55641B) resume where the saved park stopped.
/// </summary>
[TestClass]
public class SavedRunStateTests
{
	private const uint Clock = 185_660_000;

	/// <summary>A 0xF4-byte struct and buffers in the loader's layout for a script of <paramref name="vm"/>'s sizes.</summary>
	private static SaveRsseRecord Record( RideVM vm, int programCounter, Action<byte[]>? edit = null, int[]? variables = null, int[]? stack = null, int[]? limbo = null )
	{
		var raw = new byte[SaveRsseRecord.Bytes];
		void Set( int offset, uint value ) => BitConverter.GetBytes( value ).CopyTo( raw, offset );
		Set( 0x08, 7 );
		Set( 0x3C, (uint)programCounter );
		Set( 0x40, unchecked( (uint)(vm.StackCapacity - 1) ) );
		Set( 0x50, (uint)vm.Script.CodeWordCount );
		Set( 0x54, (uint)vm.StackCapacity );
		Set( 0x58, (uint)vm.LimboCapacity );
		Set( 0x8C, (uint)vm.Variables.Length );
		Set( 0x94, 50 );
		Set( 0x98, 0xFFFFFFFF );
		Set( 0xA8, 0xFFFF );
		Set( 0xC0, 50 );
		raw[0xE6] = 0xFF;
		raw[0xE7] = 0xFF;
		edit?.Invoke( raw );
		static byte[] Words( int[] words ) => words.SelectMany( BitConverter.GetBytes ).ToArray();
		return new SaveRsseRecord
		{
			Raw = raw, Code = new byte[vm.Script.CodeWordCount * 4], Stack = Words( stack ?? new int[vm.StackCapacity] ), Variables = Words( variables ?? new int[vm.Variables.Length] ),
			Strings = Array.Empty<byte>(), Limbo = Words( limbo ?? new int[vm.LimboCapacity * 2] ), Bounce = Array.Empty<byte>(), Walk = Array.Empty<byte>(), Nodes = Array.Empty<byte>(),
			Folder = "data\\levels\\jungle\\Features\\test\\", Objects = Array.Empty<SaveRsseObject>(),
		};
	}

	private static RideVM Waiter( RideScriptWorld world ) => new( new RideScriptFile( new System.IO.MemoryStream( new Asm()
		.I( Opcode.WAIT, 500 ).I( Opcode.ADD, V( 0 ), 1 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0", "V1", "V2", "V3" } ) ) ), new RideVMOptions { World = world, Seed = 1 } );

	private static void Run( RideVM vm, int ticks )
	{
		for ( var tick = 0; tick < ticks; tick++ )
			vm.Advance( Tick );
	}

	[TestMethod]
	public void AScriptSavedInsideAWaitResumesWhenTheSavedDeadlineComes()
	{
		var world = new RideScriptWorld( 7 );
		var vm = Waiter( world );
		Assert.IsTrue( world.ImportClock( Clock ) );
		// the saved program counter is the WAIT (word 0) with the deadline 500 ms ahead of the saved clock: the original rewinds onto the instruction and re-runs it each pass
		Assert.IsNull( vm.ImportSavedRunState( Record( vm, 0, raw => BitConverter.GetBytes( Clock + 500 ).CopyTo( raw, 0xA0 ) ), Clock ) );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( 500d, vm.WakeTimeMilliseconds, 1e-9 );
		Run( vm, 29 );
		Assert.AreEqual( 0, vm.Variables[0], "500 ms = 30 ticks have not passed" );
		Run( vm, 2 );
		Assert.AreEqual( 1, vm.Variables[0], "the wait ended and the script went on past the WAIT, not into a new one" );
		Assert.AreEqual( Clock + 31 * 1000d / 60, vm.SchedulerMilliseconds, 1e-6, "the script clock continues from the saved game clock" );
	}

	[TestMethod]
	public void ADeadlineAlreadyPassedContinuesAtOnceAndAnUnarmedWaitStartsAgain()
	{
		var world = new RideScriptWorld( 7 );
		var passed = Waiter( world );
		var unarmed = Waiter( world );
		world.ImportClock( Clock );
		Assert.IsNull( passed.ImportSavedRunState( Record( passed, 0, raw => BitConverter.GetBytes( Clock - 10 ).CopyTo( raw, 0xA0 ) ), Clock ) );
		Assert.IsNull( unarmed.ImportSavedRunState( Record( unarmed, 0 ), Clock ) );
		Run( passed, 1 );
		Run( unarmed, 1 );
		Assert.AreEqual( 1, passed.Variables[0], "a deadline not ahead of the saved clock is over" );
		Assert.AreEqual( RideVMState.Waiting, unarmed.State, "a WAIT with no deadline arms its own when it runs" );
		Assert.AreEqual( 0, unarmed.Variables[0] );
	}

	/// <summary>
	/// A saved WAIT4ANIM deadline behind the saved clock (a finished animation wait, as in every original save) imports as no deadline: the
	/// script's snapshot stays valid (a -1 stand-in failed "WAIT4ANIM deadline -1 is not finite and non-negative", so the restart snapshot
	/// and the autosave of a park started from an original save were never written) and WAIT4ANIM continues either way.
	/// </summary>
	[TestMethod]
	public void APastWait4AnimDeadlineImportsAsNoneAndTheScriptSnapshotStaysValid()
	{
		var world = new RideScriptWorld( 7 );
		var vm = Waiter( world );
		world.ImportClock( Clock );
		Assert.IsNull( vm.ImportSavedRunState( Record( vm, 0, raw => BitConverter.GetBytes( Clock - 5000 ).CopyTo( raw, 0xA4 ) ), Clock ) );
		Assert.AreEqual( 0d, vm.Wait4AnimDeadline );
		var snapshot = vm.CaptureSnapshot();
		snapshot.ValidateSnapshot();
		Assert.AreEqual( 0d, snapshot.Wait4AnimDeadline );
		Assert.IsNull( vm.ImportSavedRunState( Record( vm, 0, raw => BitConverter.GetBytes( Clock ).CopyTo( raw, 0xA4 ) ), Clock ), "a deadline equal to the saved clock is over too" );
		Assert.AreEqual( 0d, vm.Wait4AnimDeadline );
	}

	[TestMethod]
	public void StackHeapVariablesLimboFlagsAndTimersAreTheSavedWords()
	{
		var world = new RideScriptWorld( 7 );
		var vm = new RideVM( new RideScriptFile( new System.IO.MemoryStream( new Asm().WithHeader( 4, 2 ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0", "V1", "V2", "V3" } ) ) ),
			new RideVMOptions { World = world, Seed = 1 } );
		world.ImportClock( Clock );
		var record = Record( vm, 0, raw =>
		{
			BitConverter.GetBytes( 2u ).CopyTo( raw, 0x40 );
			BitConverter.GetBytes( 1u ).CopyTo( raw, 0x44 );
			BitConverter.GetBytes( 0xFFFFFFFEu ).CopyTo( raw, 0x48 );
			BitConverter.GetBytes( 1u ).CopyTo( raw, 0x60 );
			BitConverter.GetBytes( Clock + 4000 ).CopyTo( raw, 0xC4 );
			BitConverter.GetBytes( Clock + 900 ).CopyTo( raw, 0xA4 );
			raw[0xB8] = 3;
			BitConverter.GetBytes( (short)70 ).CopyTo( raw, 0xC0 );
		}, variables: new[] { 5, -6, 7, 8 }, stack: new[] { 42, 0, 0x20000008, 0 }, limbo: new[] { 31, (int)Clock + 2000, 0, 0 } );
		Assert.IsNull( vm.ImportSavedRunState( record, Clock ) );
		CollectionAssert.AreEqual( new[] { 5, -6, 7, 8 }, vm.Variables );
		Assert.AreEqual( 1, vm.HeapCount );
		Assert.AreEqual( 42, vm.GetStackSlot( 0 ) );
		Assert.AreEqual( 1, vm.CallDepth, "the pointer 2 of a 4-word stack leaves one return address on the call side" );
		Assert.AreEqual( 1, vm.LimboCount );
		Assert.AreEqual( (31, (int)Clock + 2000), vm.GetLimboSlot( 0 ) );
		Assert.AreEqual( RideVM.VMFlags.Sign, vm.Flags );
		Assert.AreEqual( 3, vm.TurboByte );
		Assert.AreEqual( 70, vm.SpeedBias );
		Assert.AreEqual( 4000, vm.TimerRemaining );
		Assert.AreEqual( 900d, vm.Wait4AnimDeadline );
		Assert.AreEqual( (int)Clock, vm.Effects is null ? 0 : (int)vm.ClockMilliseconds32 );
		Assert.AreEqual( 1, world.Scripts.Count );
	}

	[TestMethod]
	public void ARecordThatDoesNotFitTheScriptChangesNothing()
	{
		var world = new RideScriptWorld( 7 );
		var vm = Waiter( world );
		world.ImportClock( Clock );
		var before = vm.Variables.ToArray();
		Assert.IsNotNull( vm.ImportSavedRunState( Record( vm, 1 ), Clock ), "word 1 is the WAIT's operand, not an instruction" );
		Assert.IsNotNull( vm.ImportSavedRunState( Record( vm, 0, variables: new[] { 1, 2 } ), Clock ) );
		Assert.IsNotNull( vm.ImportSavedRunState( Record( vm, 0, raw => BitConverter.GetBytes( 9u ).CopyTo( raw, 0x0C ) ), Clock ), "a script with a child keeps its fresh state" );
		Assert.IsNotNull( vm.ImportSavedRunState( Record( vm, 0, raw => BitConverter.GetBytes( 99u ).CopyTo( raw, 0x40 ) ), Clock ), "a stack pointer outside the stack" );
		CollectionAssert.AreEqual( before, vm.Variables );
		Assert.AreEqual( 0, vm.ProgramCounter );
		Assert.AreEqual( RideVMState.Running, vm.State );
	}

	[TestMethod]
	public void TheClockIsTakenOnlyBeforeAnyScriptHasRun()
	{
		var world = new RideScriptWorld( 7 );
		var vm = Waiter( world );
		Run( vm, 1 );
		Assert.IsFalse( world.ImportClock( Clock ) );
		Assert.AreEqual( 0, vm.ClockOriginMilliseconds );
	}

	[TestMethod]
	public void AnImportedScriptReadsTheSavedClockAndItsTimerCountsDown()
	{
		var world = new RideScriptWorld( 7 );
		var vm = new RideVM( new RideScriptFile( new System.IO.MemoryStream( new Asm()
			.I( Opcode.GETTIME, V( 1 ) ).I( Opcode.GETTIMER, V( 2 ) ).Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" ).Build( new[] { "V0", "V1", "V2", "V3" } ) ) ),
			new RideVMOptions { World = world, Seed = 1 } );
		world.ImportClock( Clock );
		Assert.IsNull( vm.ImportSavedRunState( Record( vm, 0, raw => BitConverter.GetBytes( Clock + 1000 ).CopyTo( raw, 0xC4 ) ), Clock ) );
		Run( vm, 1 );
		Assert.AreEqual( Clock + 16, (uint)vm.Variables[1], "GETTIME is the saved clock plus the time since the import (one 60 Hz tick, 16.7 ms)" );
		Assert.AreEqual( 984, vm.Variables[2], "GETTIMER counts the saved timer (1000 ms ahead of the saved clock) down by the same tick" );
	}
}
