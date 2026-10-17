using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Scripting;
using static OpenTPW.Tests.ScriptTestSupport;

namespace OpenTPW.Tests;

/// <summary>TPW Script state: transactions, canonical serialisation, the schema, the RNG and the cross-platform determinism corpus (docs/SCRIPT-MODS.md).</summary>
[TestClass]
public class ScriptStateTests
{
	private const string Counter = """
		function bump(by)
			if not has(state, "n") then state.n = 0 end
			state.n = state.n + by
			return state.n
		end
		function bumpThenFail(by)
			state.n = state.n + by
			state.list = { 1, 2, 3 }
			return 1 // 0
		end
		function bumpThenSpin(by)
			state.n = state.n + by
			while true do end
			return 1
		end
		function bumpThenWrongResult(by)
			state.n = state.n + by
			return 1000
		end
		function readN() return state.n end
		""";

	[TestMethod]
	public void OnlyASuccessfulCallCommitsTheState()
	{
		var host = Host( Counter );
		Assert.AreEqual( 5, Call( host, "bump", 5 ).Value.AsInt );
		Assert.AreEqual( "{\"n\":5}", ScriptState.Serialize( host.State ) );

		var failed = Call( host, "bumpThenFail", 100 );
		Assert.AreEqual( ScriptOutcome.RuntimeError, failed.Outcome );
		Assert.AreEqual( Previous, failed.Value.AsInt );
		Assert.AreEqual( "{\"n\":5}", ScriptState.Serialize( host.State ) );

		Assert.AreEqual( ScriptOutcome.BudgetExhausted, Call( host, "bumpThenSpin", 100 ).Outcome );
		Assert.AreEqual( "{\"n\":5}", ScriptState.Serialize( host.State ) );

		var invalid = host.Call( "bumpThenWrongResult", new ScriptValue[] { 100 }, 9, ScriptResultSpec.Int( 0, 10 ), Identity );
		Assert.AreEqual( ScriptOutcome.InvalidResult, invalid.Outcome );
		Assert.AreEqual( 9, invalid.Value.AsInt );
		Assert.AreEqual( "{\"n\":5}", ScriptState.Serialize( host.State ) );

		Assert.AreEqual( 8, Call( host, "bump", 3 ).Value.AsInt );
		Assert.AreEqual( "{\"n\":8}", ScriptState.Serialize( host.State ) );
	}

	[TestMethod]
	public void CommittedStateIsNeverChangedByLaterCalls()
	{
		var host = Host( """
			function init()
				state.deep = { a = { b = { 1, 2, 3 } }, tags = { "x" } }
				return 1
			end
			function edit(fail)
				state.deep.a.b[2] = 99
				push(state.deep.tags, "y")
				if fail then return 1 // 0 end
				return 1
			end
			""" );
		Call( host, "init" );
		var committed = host.State;
		var before = ScriptState.Serialize( committed );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Call( host, "edit", true ).Outcome );
		Assert.AreEqual( before, ScriptState.Serialize( committed ), "a failed call leaves the old state object untouched" );
		Assert.AreSame( committed, host.State );
		Assert.AreEqual( ScriptOutcome.Ok, Call( host, "edit", false ).Outcome );
		Assert.AreEqual( before, ScriptState.Serialize( committed ), "a successful call builds a new state; the old one is a snapshot" );
		Assert.AreEqual( "{\"deep\":{\"a\":{\"b\":[1,99,3]},\"tags\":[\"x\",\"y\"]}}", ScriptState.Serialize( host.State ) );
	}

	[TestMethod]
	public void UnchangedStateKeepsTheSameObject()
	{
		var host = Host( Counter );
		Call( host, "bump", 1 );
		var state = host.State;
		Assert.AreEqual( 1, Call( host, "readN" ).Value.AsInt );
		Assert.AreSame( state, host.State );
	}

	[TestMethod]
	public void ContinuingAndResumingFromASerialisedStateAgree()
	{
		const string source = """
			function step(i)
				if not has(state, "log") then state.log = {} state.by = {} state.empty = {} state.seen = {} end
				push(state.log, i * i)
				if len(state.log) > 3 then pop(state.log) end
				state.by["k" .. i % 3] = i
				if i % 4 == 0 then del(state.by, "k0") end
				if i == 5 then state.seen = { 1 } end
				if i == 6 then pop(state.seen) end
				return len(state.log) * 100 + len(keys(state.by)) * 10 + len(state.seen)
			end
			""";
		var continuing = Host( source );
		var resumed = Host( source );
		for ( int i = 1; i <= 12; i++ )
		{
			var a = Call( continuing, "step", i );
			var b = Call( resumed, "step", i );
			Assert.AreEqual( a.Value.AsInt, b.Value.AsInt, $"step {i}" );
			Assert.AreEqual( a.Steps, b.Steps, $"step {i}" );
			resumed.SetState( ScriptState.Parse( ScriptState.Serialize( resumed.State ) ) );
			Assert.AreEqual( ScriptState.Serialize( continuing.State ), ScriptState.Serialize( resumed.State ), $"step {i}" );
		}
	}

	[TestMethod]
	public void StateRoundTripsCanonically()
	{
		const string text = "{\"b\":[1,-2,{\"z\":true,\"a\":false}],\"a\":\"he said \\\"hi\\\"\\n\",\"c\":{},\"d\":[],\"e\":9223372036854775807,\"f\":-9223372036854775808}";
		var parsed = ScriptState.Parse( text );
		var canonical = ScriptState.Serialize( parsed );
		Assert.AreEqual( "{\"a\":\"he said \\\"hi\\\"\\n\"".Replace( "\\n", "\\u000a" ), canonical.Substring( 0, canonical.IndexOf( ",\"b\"", StringComparison.Ordinal ) ) );
		Assert.AreEqual( "{\"a\":\"he said \\\"hi\\\"\\u000a\",\"b\":[1,-2,{\"a\":false,\"z\":true}],\"c\":{},\"d\":[],\"e\":9223372036854775807,\"f\":-9223372036854775808}", canonical );
		Assert.AreEqual( canonical, ScriptState.Serialize( ScriptState.Parse( canonical ) ), "serialise(parse(x)) is a fixed point" );
		Assert.AreEqual( "{}", ScriptState.Serialize( ScriptState.Empty() ) );
		Assert.AreEqual( canonical, ScriptState.Serialize( ScriptState.Parse( " { \"f\" : -9223372036854775808 , \"e\" : 9223372036854775807 , \"d\" : [ ] , \"c\" : { } , \"b\" : [ 1 , -2 , { \"z\" : true , \"a\" : false } ] , \"a\" : \"he said \\\"hi\\\"\\n\" } " ) ), "key order and whitespace do not matter" );
	}

	[TestMethod]
	public void StateSchemaRejectsEverythingElse()
	{
		var bad = new[]
		{
			"[]", "[1]", "1", "\"x\"", "null", "{\"a\":null}", "{\"a\":1.5}", "{\"a\":1e3}", "{\"a\":01}", "{\"a\":-0}", "{\"a\":+1}", "{\"a\":9223372036854775808}",
			"{\"a\":1,\"a\":2}", "{\"a\":1,}", "{\"a\":[1,]}", "{a:1}", "{\"a\":1} x", "{\"a\":\"\\q\"}", "{\"a\":\"\\ud800\"}", "{\"a\":\"abc", "{\"a\":nul}", "{\"\":1}",
			"{\"" + new string( 'k', 65 ) + "\":1}", "{\"a\":\"" + new string( 'x', 1025 ) + "\"}", "{\"a\":\"e\u0301\"}", "{\"e\u0301\":1}", "",
			string.Concat( Enumerable.Repeat( "{\"a\":", 20 ) ) + "1" + string.Concat( Enumerable.Repeat( "}", 20 ) ),
			"{\"a\":[" + string.Join( ",", Enumerable.Range( 0, 5000 ) ) + "]}"
		};
		foreach ( var text in bad )
			Assert.ThrowsException<ScriptStateException>( () => ScriptState.Parse( text ), text.Length > 80 ? text[..80] : text );

		// The depth limit is exact: 16 levels of tables pass, 17 do not.
		string Nested( int levels ) => string.Concat( Enumerable.Repeat( "{\"a\":", levels - 1 ) ) + "{}" + string.Concat( Enumerable.Repeat( "}", levels - 1 ) );
		ScriptState.Parse( Nested( ScriptLimits.MaxStateDepth ) );
		Assert.ThrowsException<ScriptStateException>( () => ScriptState.Parse( Nested( ScriptLimits.MaxStateDepth + 1 ) ) );

		// Size limit: a state of many 1000-character strings is refused at the byte limit.
		var big = "{\"a\":[" + string.Join( ",", Enumerable.Repeat( "\"" + new string( 'x', 1000 ) + "\"", 70 ) ) + "]}";
		Assert.ThrowsException<ScriptStateException>( () => ScriptState.Parse( big ) );

		// A host view cannot be state, and arrays cannot be the root.
		Assert.ThrowsException<ScriptStateException>( () => ScriptState.Validate( ScriptTable.RecordView( Array.Empty<KeyValuePair<string, ScriptValue>>() ) ) );
		Assert.ThrowsException<ScriptStateException>( () => new ScriptHost( Compile( "function main() end" ), "community.x", null, ScriptTable.ArrayView( new ScriptValue[] { 1 } ) ) );
	}

	[TestMethod]
	public void StateStringsAreNfcNormalisedOnCommit()
	{
		// "e" followed by a combining acute accent, built at run time so that it is not already normalised.
		var host = Host( "function main() local s = 'e' .. '\u0301' state.s = s state.k = {} state.k[s] = 1 return len(s) end" );
		var result = Call( host, "main" );
		Assert.AreEqual( 2, result.Value.AsInt, "inside the call the string is what it was built as" );
		Assert.AreEqual( "{\"k\":{\"\u00e9\":1},\"s\":\"\u00e9\"}", ScriptState.Serialize( host.State ).Replace( "\\u00e9", "\u00e9" ) );
		Assert.IsTrue( ScriptState.Serialize( host.State ).Contains( "\u00e9" ) );
		Assert.AreEqual( 1, Call( Host( "function main() return len(state.s) end", null, host.State ), "main" ).Value.AsInt );
	}

	[TestMethod]
	public void ValueKindsInStateSurviveATrip()
	{
		var host = Host( "function main() state.i = -5 state.b = true state.s = 'x' state.a = { 1, 2 } state.m = { k = false } return 1 end" );
		Call( host, "main" );
		var copy = Host( "function main() return 1 end", null, ScriptState.Parse( ScriptState.Serialize( host.State ) ) );
		var reader = Host( "function main() if state.i == -5 and state.b and state.s == 'x' and state.a[2] == 2 and not state.m.k then return 1 end return 0 end", null, copy.State );
		Assert.AreEqual( 1, Call( reader, "main" ).Value.AsInt );
	}

	[TestMethod]
	public void RngIsAStatelessHashOfTheCallIdentity()
	{
		var a = ScriptRng.Draw( 1, "community.m", "hook", 7, 100, 0, 1_000_000 );
		Assert.AreEqual( a, ScriptRng.Draw( 1, "community.m", "hook", 7, 100, 0, 1_000_000 ), "same identity, same value, any number of times" );
		var different = new[]
		{
			ScriptRng.Draw( 2, "community.m", "hook", 7, 100, 0, 1_000_000 ), ScriptRng.Draw( 1, "community.n", "hook", 7, 100, 0, 1_000_000 ),
			ScriptRng.Draw( 1, "community.m", "hook2", 7, 100, 0, 1_000_000 ), ScriptRng.Draw( 1, "community.m", "hook", 8, 100, 0, 1_000_000 ),
			ScriptRng.Draw( 1, "community.m", "hook", 7, 101, 0, 1_000_000 ), ScriptRng.Draw( 1, "community.m", "hook", 7, 100, 1, 1_000_000 )
		};
		foreach ( var value in different )
			Assert.AreNotEqual( a, value );
		for ( long ordinal = 0; ordinal < 200; ordinal++ )
			Assert.IsTrue( ScriptRng.Draw( 9, "m", "h", 1, 1, ordinal, 6 ) is >= 0 and < 6 );
		Assert.AreEqual( 0, ScriptRng.Draw( 9, "m", "h", 1, 1, 3, 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => ScriptRng.Draw( 1, "m", "h", 1, 1, 0, 0 ) );

		// A script's draws are the ordinals 0, 1, 2... of its call identity, and a fresh call restarts at 0.
		var host = Host( "function main() return rng(1000) * 1000000 + rng(1000) * 1000 + rng(1000) end", null, null, "community.m" );
		var result = Call( host, "main" );
		var identity = Identity;
		long Draw( long ordinal ) => ScriptRng.Draw( identity.ParkSeed, "community.m", "main", identity.GuestBirthId, identity.ParkTurn, ordinal, 1000 );
		Assert.AreEqual( Draw( 0 ) * 1_000_000 + Draw( 1 ) * 1000 + Draw( 2 ), result.Value.AsInt );
		Assert.AreEqual( result.Value.AsInt, Call( host, "main" ).Value.AsInt );
		var other = host.Call( "main", ReadOnlySpan<ScriptValue>.Empty, 0, AnyInt, identity with { GuestBirthId = 43 } );
		Assert.AreNotEqual( result.Value.AsInt, other.Value.AsInt );
	}

	[TestMethod]
	public void RngValuesArePinned()
	{
		// Pinned so that any platform or later change that moves the hash is noticed: saves depend on these values.
		var values = Enumerable.Range( 0, 6 ).Select( i => ScriptRng.Draw( 123456789, "community.patient-guests", "scoreAttraction", 4242, 9001, i, 1000 ) ).ToArray();
		Assert.AreEqual( "791,448,796,580,853,550", string.Join( ",", values ) );
	}

	private static readonly (string Name, string Source, string Hook, long[] Arguments)[] DeterminismCorpus =
	{
		("arith", "function main(a, b) return (a * b + a // b - a % b) * fxmul(fx(1.25), a) end", "main", new long[] { 37, -5 }),
		("fib", "function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end\nfunction main(n) return fib(n) end", "main", new long[] { 11 }),
		("loops", "function main(n) local s = 0 for i = 1, n do for j = i, n, 3 do s = s + i * j % 11 end end return s end", "main", new long[] { 40 }),
		("strings", "function main(n) local s = '' for i = 1, n do s = s .. i .. ',' end return len(s) * 1000 + n end", "main", new long[] { 60 }),
		("state tables", "function main(n) state.a = {} state.m = {} for i = 1, n do push(state.a, i * i) state.m['k' .. i % 7] = i end return len(keys(state.m)) * 100 + len(state.a) end", "main", new long[] { 30 }),
		("rng", "function main(n) local s = 0 for i = 1, n do s = s + rng(97) end return s end", "main", new long[] { 25 }),
		("clamp chain", "function main(a) return clamp(a * 3, -10, 10) + min(a, 4) + max(a, -4) + abs(a) end", "main", new long[] { -7 }),
		("overflow", "function main(a) return a * a * a * a * a end", "main", new long[] { 100000 }),
		("budget", "function main(a) local n = a while true do n = n + 1 end return n end", "main", new long[] { 1 }),
		("div zero", "function main(a) return 10 // (a - a) end", "main", new long[] { 3 }),
		("depth", "function f(n) return f(n + 1) end\nfunction main(a) return f(a) end", "main", new long[] { 0 }),
		("nfc", "function main(a) state.s = 'e' .. '\u0301' .. 'a\u030a' return len(state.s) end", "main", new long[] { 0 }),
		("sorted keys", "function main(a) state.m = { zeta = 1, alpha = 2, mid = 3 } state.m.beta = 4 local k = keys(state.m) return len(k) end", "main", new long[] { 0 }),
		("bad result", "function main(a) return 'text' end", "main", new long[] { 0 }),
	};

	[TestMethod]
	public void DeterminismCorpusHasAGoldenHashOnEveryPlatform()
	{
		var transcript = new StringBuilder();
		foreach ( var (name, source, hook, arguments) in DeterminismCorpus )
		{
			var host = Host( source, null, null, "community.golden" );
			var result = host.Call( hook, arguments.Select( a => (ScriptValue)a ).ToArray(), -1, ScriptResultSpec.Int( long.MinValue, long.MaxValue ), new ScriptCallIdentity( 99, 7, 12345 ) );
			transcript.Append( name ).Append( '|' ).Append( result.Outcome ).Append( '|' ).Append( result.Value ).Append( '|' ).Append( result.Steps ).Append( '|' )
				.Append( result.Line ).Append( '|' ).Append( ScriptState.Serialize( host.State ) ).Append( '\n' );
		}
		var hash = Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( transcript.ToString() ) ) ).ToLowerInvariant();
		Assert.AreEqual( "1998e037943dc0cfea8786de9a50540b3ed4da7cc05b81d883981c4f9cc9521b", hash, transcript.ToString() );
	}
}
