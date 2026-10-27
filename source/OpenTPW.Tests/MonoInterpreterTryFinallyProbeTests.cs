using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Controls for a Mono interpreter fault the browser pins met (CI "pins (interpreter)", October 2026) in the projection helper of the guest tests: in a method called about 70 times (the interpreter's
/// tier-up) whose <c>try</c> assigns locals that are read after the <c>finally</c>, a <c>finally</c> body that runs a <c>for</c> or <c>foreach</c> loop over an array of value tuples (its index and the element's int copied
/// into temporaries) overwrote the low half of those locals: a returned <c>(ulong, ulong)</c> came back as <c>0x111122220000000B, 0x555566660000000C</c> (the loop's last indices) from call 69 to 75 on, whatever the
/// return form (a tuple, a class, locals after the finally) and with or without Jiterpreter traces; Mono AOT and the desktop runtime were right. Probed 2026-10-27: three finally bodies of the browser build did loop (World/Level.Simulation.cs SimulateSweptTurn, Client/GameFlow.CoopJoin.cs's slice, the pin runner of OpenTPW.Web); none misbehaved in the pins, but each read locals after the finally, so
/// each loop moved into a helper method, and tools/test_no_loop_in_finally.py fails on any loop or LINQ call in a finally body of those projects. The other finally blocks (the guest decisions, ScriptHost and the rest) only call or subtract a field, which the controls below repeat hot and which hold. The helper that met it
/// (GuestRefusedTargetTests.VerifyRetainedTargetHash) has no try/finally any more. A new simulation method with a loop in a finally and a try-assigned local read after it needs this class's pattern checked first.
/// </summary>
[TestClass]
public class MonoInterpreterTryFinallyProbeTests
{
	private sealed class Holder { public int Value; }

	private const ulong First = 0x1111222233334444UL, Second = 0x5555666677778888UL;

	private static Func<ulong> Counter()
	{
		var calls = 0;
		return () => ++calls == 1 ? First : Second;
	}

	private static (Holder, int)[] ManyPairs( int count ) => Enumerable.Range( 0, count ).Select( index => (new Holder(), index) ).ToArray();

	private static (ulong A, ulong B) TupleWithoutTry( Func<ulong> compute, (Holder Holder, int Target)[] pairs )
	{
		var a = compute();
		var b = compute();
		foreach ( var pair in pairs ) pair.Holder.Value = pair.Target;
		return (a, b);
	}

	private static (ulong A, ulong B) TupleWithPlainFinally( Func<ulong> compute, Holder holder )
	{
		try
		{
			var a = compute();
			var b = compute();
			return (a, b);
		}
		finally { holder.Value = 7; }
	}

	private static string FirstWrong( Func<(ulong A, ulong B)> call, int calls )
	{
		for ( var index = 0; index < calls; index++ )
		{
			var value = call();
			if ( value != (First, Second) )
				return $"call {index}: {value.A:X16},{value.B:X16}";
		}
		return "none";
	}

	[TestMethod] public void HotNoTryForeach() { var pairs = ManyPairs( 12 ); Assert.AreEqual( "none", FirstWrong( () => TupleWithoutTry( Counter(), pairs ), 20000 ) ); }
	[TestMethod] public void HotPlainFinally() { var holder = new Holder(); Assert.AreEqual( "none", FirstWrong( () => TupleWithPlainFinally( Counter(), holder ), 20000 ) ); }

	private sealed class Decision
	{
		public int Ends;
		public void End() => Ends++;
		public readonly List<(int Id, uint Score)> Candidates = Enumerable.Range( 1, 6 ).Select( index => (index, (uint)(index * 10)) ).ToList();
	}

	// the shape of GuestSimulation.ChooseAttraction: locals assigned in the try, read after the finally, whose body only calls
	private static int ChooseLike( Decision decision, bool counted )
	{
		uint best = 0;
		var chosen = 0;
		try
		{
			foreach ( var candidate in decision.Candidates )
			{
				if ( candidate.Score <= best )
					continue;
				(best, chosen) = (candidate.Score, candidate.Id);
			}
		}
		finally
		{
			if ( counted )
				decision.End();
		}
		return chosen == 0 ? -1 : chosen * 100 + (int)best;
	}

	private static (long Best, bool Keeps) KeepsLike( Decision decision, bool counted )
	{
		var best = -1L;
		var keeps = false;
		try
		{
			foreach ( var candidate in decision.Candidates )
				if ( best < 0 || candidate.Score > best )
					best = candidate.Score;
			keeps = best > 50;
		}
		finally
		{
			decision.End();
			if ( counted )
				decision.End();
		}
		return (best, keeps);
	}

	[TestMethod]
	public void HotChooseLike()
	{
		var decision = new Decision();
		for ( var index = 0; index < 20000; index++ )
			Assert.AreEqual( 660, ChooseLike( decision, index % 2 == 0 ), $"call {index}" );
	}

	[TestMethod]
	public void HotKeepsLike()
	{
		var decision = new Decision();
		for ( var index = 0; index < 20000; index++ )
			Assert.AreEqual( (60L, true), KeepsLike( decision, index % 2 == 0 ), $"call {index}" );
	}

	private readonly record struct Value( int Kind, ulong Payload, object? Reference );

	private sealed class Interp
	{
		public long Steps;
		public Value Execute( int index ) { Steps += 3 + index % 5; return new Value( 2, First + (ulong)index, null ); }
	}

	private sealed class HostLike
	{
		public long TickRemaining = 1_000_000_000;
		public readonly Interp Interpreter = new();

		// the shape of ScriptHost.Call: a struct local assigned in a try with catch clauses, read after a finally that subtracts a field of the interpreter
		public Value Call( int index )
		{
			Value value;
			try
			{
				value = Interpreter.Execute( index );
			}
			catch ( InvalidOperationException )
			{
				return default;
			}
			finally
			{
				TickRemaining -= Interpreter.Steps;
			}
			return value;
		}
	}

	[TestMethod]
	public void HotScriptHostLike()
	{
		var host = new HostLike();
		for ( var index = 0; index < 20000; index++ )
		{
			var value = host.Call( index );
			Assert.AreEqual( (2, First + (ulong)index), (value.Kind, value.Payload), $"call {index}" );
		}
	}
}
