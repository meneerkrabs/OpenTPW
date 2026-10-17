using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Scripting;
using static OpenTPW.Tests.ScriptTestSupport;

namespace OpenTPW.Tests;

/// <summary>The escape and denial-of-service corpus of TPW Script: nothing here may crash, hang or change the state (docs/SCRIPT-MODS.md).</summary>
[TestClass]
public class ScriptSandboxTests
{
	private static string Main( string body ) => $"function main()\n{body}\nend";

	/// <summary>Scripts that must run and fail with a given outcome.</summary>
	private static readonly (string Name, string Source, ScriptOutcome Outcome, string Message)[] RunCorpus =
	{
		("infinite while", Main( "while true do end return 1" ), ScriptOutcome.BudgetExhausted, "budget"),
		("infinite while with work", Main( "local i = 0 while true do i = i + 1 end return i" ), ScriptOutcome.BudgetExhausted, "budget"),
		("huge for", Main( "local n = 0 for i = 1, 9223372036854775807 do n = n + 1 end return n" ), ScriptOutcome.BudgetExhausted, "budget"),
		("nested loops", Main( "local n = 0 for i = 1, 100000 do for j = 1, 100000 do n = n + 1 end end return n" ), ScriptOutcome.BudgetExhausted, "budget"),
		("unbounded recursion", "function f(n) return f(n + 1) end\nfunction main() return f(0) end", ScriptOutcome.RuntimeError, "depth"),
		("mutual recursion", "function a(n) return b(n) end\nfunction b(n) return a(n) end\nfunction main() return a(0) end", ScriptOutcome.RuntimeError, "depth"),
		("recursion with big frames", "function f(n) local a = 1 local b = 2 local c = 3 return f(n) + a + b + c end\nfunction main() return f(0) end", ScriptOutcome.RuntimeError, "depth"),
		("recursive loop bomb", "function f() local n = 0 while true do n = f() end return n end\nfunction main() return f() end", ScriptOutcome.RuntimeError, "depth"),
		("doubling string", Main( "local s = 'x' for i = 1, 100 do s = s .. s end return len(s)" ), ScriptOutcome.RuntimeError, "string longer"),
		("string just over the cap", Main( "local s = 'x' for i = 1, 10 do s = s .. s end return len(s .. 'y')" ), ScriptOutcome.RuntimeError, "string longer"),
		("many big concatenations", Main( "local s = 'x' for i = 1, 10 do s = s .. s end local n = 0 for i = 1, 5000 do local t = s .. '' n = n + 1 end return n" ), ScriptOutcome.BudgetExhausted, "budget"),
		("table push flood", Main( "local t = {} for i = 1, 100000 do push(t, i) end return len(t)" ), ScriptOutcome.BudgetExhausted, "budget"),
		("map key flood", Main( "local m = {} for i = 1, 100000 do m['k' .. i] = i end return 1" ), ScriptOutcome.BudgetExhausted, "budget"),
		("add overflow", Main( "return 9223372036854775807 + 1" ), ScriptOutcome.RuntimeError, "overflow"),
		("sub overflow", Main( "return -9223372036854775807 - 2" ), ScriptOutcome.RuntimeError, "overflow"),
		("mul overflow", Main( "local x = 4611686018427387904 return x * 2" ), ScriptOutcome.RuntimeError, "overflow"),
		("neg overflow", Main( "local x = -9223372036854775807 - 1 return -x" ), ScriptOutcome.RuntimeError, "overflow"),
		("pow2 loop overflow", Main( "local x = 1 for i = 1, 100 do x = x * 2 end return x" ), ScriptOutcome.RuntimeError, "overflow"),
		("div by zero", Main( "return 5 // 0" ), ScriptOutcome.RuntimeError, "division by zero"),
		("mod by zero", Main( "return 5 % 0" ), ScriptOutcome.RuntimeError, "division by zero"),
		("min // -1", Main( "local x = -9223372036854775807 - 1 return x // -1" ), ScriptOutcome.RuntimeError, "overflow"),
		("rng zero", Main( "return rng(0)" ), ScriptOutcome.RuntimeError, "rng"),
		("rng negative", Main( "return rng(-5)" ), ScriptOutcome.RuntimeError, "rng"),
		("index a number", Main( "local x = 5 return x.y" ), ScriptOutcome.RuntimeError, "cannot index"),
		("index a string", Main( "local x = 'abc' return x[1]" ), ScriptOutcome.RuntimeError, "cannot index"),
		("call result misuse", "function f() end\nfunction main() return f() + 1 end", ScriptOutcome.RuntimeError, "no value"),
		("key too long", Main( "local m = {} m['" + new string( 'k', 65 ) + "'] = 1 return 1" ), ScriptOutcome.RuntimeError, "key"),
		("empty key", Main( "local m = {} m[''] = 1 return 1" ), ScriptOutcome.RuntimeError, "key"),
	};

	[TestMethod]
	public void EscapeAndDenialOfServiceCorpus()
	{
		foreach ( var (name, source, expected, message) in RunCorpus )
		{
			var host = Host( source );
			var before = ScriptState.Serialize( host.State );
			var result = Call( host, "main" );
			Assert.AreEqual( expected, result.Outcome, $"{name}: {result.Message}" );
			StringAssert.Contains( result.Message!.ToLowerInvariant(), message, name );
			Assert.AreEqual( Previous, result.Value.AsInt, $"{name}: failure returns prev" );
			if ( expected == ScriptOutcome.BudgetExhausted )
				Assert.IsTrue( result.Line >= 1, $"{name}: the budget failure names a line, whichever charge ran out" );
			Assert.AreEqual( before, ScriptState.Serialize( host.State ), $"{name}: state rolled back" );
			Assert.IsTrue( result.Steps <= ScriptLimits.DefaultStepsPerCall + 1, $"{name}: {result.Steps} steps" );
			// The host stays usable after any failure.
			Assert.AreEqual( 5, Call( Host( "function main() return 5 end" ), "main" ).Value.AsInt, name );
		}
	}

	private static string Repeat( string text, int count ) => string.Concat( Enumerable.Repeat( text, count ) );

	/// <summary>Sources that must be refused at compile time, with the position where they go wrong.</summary>
	private static readonly (string Name, string Source, string Message)[] CompileCorpus =
	{
		("deep parentheses", Main( "return " + Repeat( "(", 200 ) + "1" + Repeat( ")", 200 ) ), "nesting"),
		("deep unary", Main( "return " + Repeat( "not ", 200 ) + "true" ), "nesting"),
		("deep negation", Main( "return " + Repeat( "- ", 200 ) + "1" ), "nesting"),
		("deep ifs", Main( Repeat( "if true then ", 200 ) + Repeat( "end ", 200 ) + "return 1" ), "nesting"),
		("deep loops", Main( Repeat( "while true do ", 200 ) + Repeat( "end ", 200 ) ), "nesting"),
		("deep tables", Main( "local t = " + Repeat( "{", 200 ) + Repeat( "}", 200 ) + " return 1" ), "nesting"),
		("deep concat", Main( "return " + Repeat( "'a' .. ", 200 ) + "'b'" ), "nesting"),
		("deep calls", Main( "return " + Repeat( "abs(", 200 ) + "1" + Repeat( ")", 200 ) ), "nesting"),
		("too many tokens", Main( "return 1" + Repeat( "+1", 12000 ) ), "too many tokens"),
		("too many functions", string.Concat( Enumerable.Range( 0, 65 ).Select( i => $"function f{i}() end\n" ) ), "too many functions"),
		("too many parameters", "function f(a,b,c,d,e,f2,g,h,i) end", "too many parameters"),
		("too many locals", Main( string.Concat( Enumerable.Range( 0, 70 ).Select( i => $"local v{i} = {i}\n" ) ) ), "too many local"),
		("string literal too long", Main( "local s = '" + new string( 'x', 2000 ) + "'" ), "string literal too long"),
		("name too long", $"function {new string( 'a', 100 )}() end", "name too long"),
		("integer too large", Main( "return 9223372036854775808" ), "too large"),
		("three decimals", Main( "return fx(1.234)" ), "2 decimals"),
		("bare fixed literal", Main( "return 1.5" ), "fx("),
		("fx of expression", Main( "local a = 1 return fx(a)" ), "number literal"),
		("hex literal", Main( "return 0x10" ), "malformed"),
		("float exponent", Main( "return 1e5" ), "malformed"),
		("slash", Main( "return 4 / 2" ), "'/' is not supported"),
		("length operator", Main( "local t = {1} return #t" ), "len("),
		("method call", Main( "local s = 'a' return s:len()" ), "':'"),
		("varargs", "function f(...) end", "varargs"),
		("nil", Main( "local x = nil" ), "no nil"),
		("goto", Main( "goto done" ), "goto"),
		("repeat", Main( "repeat until true" ), "repeat"),
		("generic for", Main( "for k, v in pairs(state) do end" ), "pairs"),
		("pairs is not a function", Main( "local n = len(pairs(state)) return n" ), "unknown function 'pairs'"),
		("unknown function", Main( "return mystery(1)" ), "unknown function"),
		("unknown global read", Main( "return counter" ), "unknown name"),
		("wrong arity user", "function f(a) return a end\nfunction main() return f(1, 2) end", "takes 1"),
		("wrong arity host", Main( "return min(1)" ), "takes 2"),
		("nested function", Main( "function inner() return 1 end return 1" ), "nested function"),
		("function value", Main( "local f = function() return 1 end return f()" ), "function values"),
		("closure capture attempt", "function make() local n = 0 function inc() n = n + 1 end return 1 end", "nested function"),
		("other functions locals", "function a() local x = 1 return x end\nfunction b() return x end", "unknown name 'x'"),
		("function as value", "function f() return 1 end\nfunction main() local g = f return g end", "is a function, not a value"),
		("host function as value", Main( "local g = min return 1" ), "is a function, not a value"),
		("function as argument", "function f() return 1 end\nfunction main() return abs(f) end", "is a function, not a value"),
		("global assignment", Main( "counter = 1 return counter" ), "not a local variable"),
		("assign to constant", "local limit = 5\nfunction main() limit = 6 return limit end", "constant"),
		("assign to function", "function f() end\nfunction main() f = 1 end", "function"),
		("assign to state", Main( "state = {}" ), "state itself"),
		("shadowing a host function", Main( "local min = 1 return min" ), "reserved"),
		("function named like a host function", "function max() return 1 end", "reserved"),
		("function named state", "function state() return 1 end", "reserved"),
		("duplicate function", "function f() end\nfunction f() end", "already defined"),
		("duplicate constant", "local a = 1\nlocal a = 2", "already defined"),
		("top level assignment", "counter = 0\nfunction main() return counter end", "top level"),
		("top level call", "print(1)\nfunction main() return 1 end", "top level"),
		("top level expression constant", "local a = 1 + 1\nfunction main() return a end", "top level"),
		("top level table constant", "local t = {}\nfunction main() return 1 end", "literal"),
		("top level if", "if true then end", "top level"),
		("top level return", "return 1", "top level"),
		("top level local without value", "local a\nfunction main() return 1 end", "'='"),
		("local without value", Main( "local a return 1" ), "no nil"),
		("return not last", Main( "return 1 local x = 2" ), "last statement"),
		("break outside loop", Main( "break" ), "outside a loop"),
		("missing end", "function main() return 1", "'end'"),
		("unterminated string", Main( "return 'abc" ), "unterminated"),
		("unterminated comment", "--[[ never closed\nfunction main() end", "unterminated"),
		("unknown escape", Main( "return '\\q'" ), "unknown escape"),
		("stray character", Main( "return 1 @ 2" ), "unexpected character"),
		("duplicate map field", Main( "local t = { a = 1, a = 2 } return 1" ), "duplicate field"),
		("call statement indexed", "function f() return {} end\nfunction main() f().x = 1 end", "statement"),
		("expression statement", Main( "1 + 2" ), "unexpected"),
		("bare name statement", Main( "state" ), "assignment or a call"),
		("assignment target not assignable", Main( "len(state) = 1" ), "statement"),
	};

	[TestMethod]
	public void CompileTimeCorpus()
	{
		foreach ( var (name, source, message) in CompileCorpus )
		{
			var error = Assert.ThrowsException<ScriptCompileException>( () => Compile( source ), name );
			StringAssert.Contains( error.Detail, message, $"{name}: {error.Message}" );
			Assert.IsTrue( error.Line >= 1 && error.Column >= 1, name );
		}
	}

	[TestMethod]
	public void StaticLimitsAreExactAtTheBoundary()
	{
		// Source size: exactly the cap passes the size check, one byte more does not.
		var padding = new string( ' ', ScriptLimits.MaxSourceBytes - "function main() return 1 end".Length );
		Compile( "function main() return 1 end" + padding );
		var big = Assert.ThrowsException<ScriptCompileException>( () => Compile( "function main() return 1 end" + padding + " " ) );
		StringAssert.Contains( big.Detail, "source too large" );

		// Tokens: a body with exactly MaxTokens tokens compiles, one more does not.
		// function main ( ) return 1 {+1} end = 6 + 1 + 2n + ... count them by building up to the cap.
		var overhead = 7; // function, main, (, ), return, 1, end
		var pairs = (ScriptLimits.MaxTokens - overhead) / 2;
		Compile( "function main() return 1" + Repeat( "+1", pairs ) + " end" );
		Assert.ThrowsException<ScriptCompileException>( () => Compile( "function main() return 1" + Repeat( "+1", pairs + 1 ) + " end" ) );

		// Nesting: 31 nested ifs (plus the function body and the expression) pass, 40 do not.
		Compile( Main( Repeat( "if true then ", 20 ) + Repeat( "end ", 20 ) ) );
		Assert.ThrowsException<ScriptCompileException>( () => Compile( Main( Repeat( "if true then ", 40 ) + Repeat( "end ", 40 ) ) ) );

		// Functions: exactly MaxFunctions pass.
		Compile( string.Concat( Enumerable.Range( 0, ScriptLimits.MaxFunctions ).Select( i => $"function f{i}() end\n" ) ) );
	}

	[TestMethod]
	public void MemoryCapStopsAllocationEvenWithAHugeStepBudget()
	{
		var options = new ScriptHostOptions { StepsPerCall = 100_000_000, StepsPerTick = 100_000_000 };
		var result = Call( Host( Main( "local s = 'x' for i = 1, 10 do s = s .. s end local n = 0 for i = 1, 5000 do local t = s .. '' n = n + 1 end return n" ), options ), "main" );
		Assert.AreEqual( ScriptOutcome.RuntimeError, result.Outcome );
		StringAssert.Contains( result.Message, "memory" );
		foreach ( var body in new[]
		{
			"local t = {} for i = 1, 4000 do push(t, { 1, 2, 3, 4, 5, 6, 7, 8 }) end return len(t)",
			"local m = {} for i = 1, 3000 do m['k' .. i] = i end local n = 0 for i = 1, 100 do n = n + len(keys(m)) end return n"
		} )
		{
			var flood = Call( Host( Main( body ), options ), "main" );
			Assert.AreEqual( ScriptOutcome.RuntimeError, flood.Outcome, body );
			StringAssert.Contains( flood.Message, "memory", body );
		}
	}

	[TestMethod]
	public void StateLimitsAreEnforcedWhenACallCommits()
	{
		var options = new ScriptHostOptions { StepsPerCall = 100_000_000, StepsPerTick = 100_000_000 };
		var cases = new[]
		{
			( Main( "state.me = state return 1" ), "more than once" ),
			( Main( "local t = state for i = 1, 40 do t.n = {} t = t.n end return 1" ), "deeper" ),
			( Main( "state.a = {} for i = 1, 3000 do push(state.a, i) end state.b = {} for i = 1, 3000 do push(state.b, i) end return 1" ), "values" ),
			( Main( "local s = 'x' for i = 1, 10 do s = s .. s end state.a = {} for i = 1, 70 do push(state.a, s) end return 1" ), "larger" ),
			( Main( "state.a = {} state.a.x = 1 state.b = state.a return 1" ), "more than once" ),
			( Main( "local t = {} state.a = t state.b = { t } return 1" ), "more than once" ),
			( Main( "local s = 'x' state.a = { s, s } state.b = {} state.c = 1 return 1" ), null ),
		};
		foreach ( var (source, message) in cases )
		{
			var host = Host( source, options );
			var result = Call( host, "main" );
			if ( message == null )
			{
				Assert.AreEqual( ScriptOutcome.Ok, result.Outcome, result.Message );
				Assert.AreEqual( "{\"a\":[\"x\",\"x\"],\"b\":{},\"c\":1}", ScriptState.Serialize( host.State ), "equal strings and scalars may repeat; only tables may not" );
				continue;
			}
			Assert.AreEqual( ScriptOutcome.InvalidResult, result.Outcome, source );
			StringAssert.Contains( result.Message, message, source );
			Assert.AreEqual( "{}", ScriptState.Serialize( host.State ) );
			Assert.AreEqual( Previous, result.Value.AsInt );
		}
	}

	[TestMethod]
	public void TableEntryCapStopsFloodsEvenWithAHugeStepBudget()
	{
		var options = new ScriptHostOptions { StepsPerCall = 100_000_000, StepsPerTick = 100_000_000 };
		foreach ( var body in new[] { "local t = {} for i = 1, 100000 do push(t, i) end return len(t)", "local m = {} for i = 1, 100000 do m['k' .. i] = i end return 1" } )
		{
			var result = Call( Host( Main( body ), options ), "main" );
			Assert.AreEqual( ScriptOutcome.RuntimeError, result.Outcome, body );
			StringAssert.Contains( result.Message, "entries", body );
		}
	}

	[TestMethod]
	public void RecursionWithinTheDepthCapWorks()
	{
		var source = "function down(n) if n == 0 then return 0 end return 1 + down(n - 1) end\nfunction main(n) return down(n) end";
		Assert.AreEqual( ScriptLimits.MaxCallDepth - 2, Run( source, ScriptLimits.MaxCallDepth - 2 ).Value.AsInt );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Run( source, ScriptLimits.MaxCallDepth + 5 ).Outcome );
	}

	[TestMethod]
	public void HostViewsCannotBeChangedOrStored()
	{
		var view = ScriptValue.FromTable( ScriptTable.RecordView( new[]
		{
			new KeyValuePair<string, ScriptValue>( "x", 1 ),
			new KeyValuePair<string, ScriptValue>( "list", ScriptTable.ArrayView( new ScriptValue[] { 1, 2 } ) )
		} ) );
		var writes = new[]
		{
			"v.x = 2", "v.y = 2", "v.list[1] = 9", "push(v.list, 3)", "pop(v.list)", "del(v, 'x')", "state.copy = v", "state.copy = v.list",
			"local t = { v }", "local t = { a = v }", "push(state.items, v)", "local t = {} t[1] = v"
		};
		foreach ( var write in writes )
		{
			var host = Host( $"function main(v)\nstate.items = {{}}\n{write}\nreturn 1\nend" );
			var result = Call( host, "main", view );
			Assert.AreEqual( ScriptOutcome.RuntimeError, result.Outcome, $"{write}: {result.Message}" );
			Assert.AreEqual( "{}", ScriptState.Serialize( host.State ), write );
		}
		// Reading and copying scalars is fine.
		var ok = Call( Host( "function main(v) state.x = v.x + v.list[2] return state.x end" ), "main", view );
		Assert.AreEqual( 3, ok.Value.AsInt );
	}

	[TestMethod]
	public void ScriptsHaveNoAmbientCapabilities()
	{
		// Every capability a script might reach for is an unknown name at compile time, so nothing exists to be called.
		foreach ( var name in new[] { "print", "io", "os", "require", "load", "loadstring", "dofile", "debug", "string", "math", "table", "pcall", "error", "assert",
			"setmetatable", "getmetatable", "rawget", "coroutine", "collectgarbage", "_G", "_ENV", "tostring", "tonumber", "type", "select", "next", "ipairs", "pairs" } )
		{
			Assert.ThrowsException<ScriptCompileException>( () => Compile( Main( $"return {name}( 1 )" ) ), name );
			Assert.ThrowsException<ScriptCompileException>( () => Compile( Main( $"local x = {name} return 1" ) ), name );
		}
	}

	[TestMethod]
	public void BudgetResultsAreIdenticalAcrossRuns()
	{
		const string source = """
			function work(n)
				local s = 0
				for i = 1, n do s = s + i * i % 7 end
				return s
			end
			function main(n) state.last = work(n) return state.last end
			function spin() while true do end return 1 end
			""";
		long? steps = null;
		for ( int i = 0; i < 5; i++ )
		{
			var result = Call( Host( source ), "main", 50 );
			Assert.AreEqual( ScriptOutcome.Ok, result.Outcome );
			steps ??= result.Steps;
			Assert.AreEqual( steps, result.Steps );
		}
		var counts = Enumerable.Range( 0, 5 ).Select( _ => Call( Host( source ), "spin" ) ).ToList();
		Assert.IsTrue( counts.All( c => c.Outcome == ScriptOutcome.BudgetExhausted && c.Steps == ScriptLimits.DefaultStepsPerCall + 1 && c.Line == counts[0].Line ) );

		// The budget is a number of steps, and a bigger one lets the same call finish.
		var small = new ScriptHostOptions { StepsPerCall = steps!.Value - 1 };
		Assert.AreEqual( ScriptOutcome.BudgetExhausted, Call( Host( source, small ), "main", 50 ).Outcome );
		var exact = new ScriptHostOptions { StepsPerCall = steps.Value };
		Assert.AreEqual( ScriptOutcome.Ok, Call( Host( source, exact ), "main", 50 ).Outcome );
	}

	[TestMethod]
	public void TickBudgetIsSharedByTheCallsOfATick()
	{
		var options = new ScriptHostOptions { StepsPerCall = 1000, StepsPerTick = 2500 };
		var host = Host( "function main() local n = 0 for i = 1, 100 do n = n + 1 end return n end", options );
		var outcomes = Enumerable.Range( 0, 10 ).Select( _ => Call( host, "main" ) ).ToList();
		var okCount = outcomes.Count( o => o.Outcome == ScriptOutcome.Ok );
		Assert.IsTrue( okCount > 0 && okCount < 10, $"{okCount} calls fit the tick" );
		Assert.IsTrue( outcomes.Skip( okCount ).All( o => o.Outcome == ScriptOutcome.BudgetExhausted ), "once the tick is spent every call is refused" );
		Assert.AreEqual( Previous, outcomes[^1].Value.AsInt );
		host.BeginTick();
		Assert.AreEqual( ScriptOutcome.Ok, Call( host, "main" ).Outcome );
		Assert.AreEqual( 2500 - outcomes[0].Steps, host.TickStepsRemaining );
	}

	private const string NewlineKiloString = "local s = '\\n' for i = 1, 10 do s = s .. s end\n";

	/// <summary>Finding 1: validation must not allocate outside the cap (a shared subtree used to expand to about 12.6M characters).</summary>
	[TestMethod]
	public void CommitValidationCannotBeBlownUpBySharedSubtrees()
	{
		var host = Host( $"function main()\n{NewlineKiloString}local t = s for i = 1, 11 do t = {{ t, t }} end state.x = t return 1\nend" );
		var result = Call( host, "main" );
		Assert.AreEqual( ScriptOutcome.InvalidResult, result.Outcome, result.Message );
		StringAssert.Contains( result.Message, "more than once" );
		Assert.AreEqual( "{}", ScriptState.Serialize( host.State ) );

		// Many distinct big strings: the node and depth checks pass, the serialisation stops at the byte limit without building it.
		var wide = Host( $"function main()\n{NewlineKiloString}state.a = {{}} for i = 1, 4000 do push(state.a, s) end return 1\nend",
			new ScriptHostOptions { StepsPerCall = 100_000_000, StepsPerTick = 100_000_000 } );
		var big = Call( wide, "main" );
		Assert.AreEqual( ScriptOutcome.InvalidResult, big.Outcome, big.Message );
		StringAssert.Contains( big.Message, "larger" );
	}

	[TestMethod]
	public void CommitValidationIsChargedToTheCallBudget()
	{
		const string source = "function main() state.a = {} for i = 1, 200 do push(state.a, i) end return 1 end";
		var steps = Call( Host( source, new ScriptHostOptions { StepsPerCall = 1_000_000 } ), "main" ).Steps;
		var exact = Call( Host( source, new ScriptHostOptions { StepsPerCall = steps } ), "main" );
		Assert.AreEqual( ScriptOutcome.Ok, exact.Outcome, exact.Message );
		var short1 = Host( source, new ScriptHostOptions { StepsPerCall = steps - 1 } );
		var failed = Call( short1, "main" );
		Assert.AreEqual( ScriptOutcome.BudgetExhausted, failed.Outcome );
		Assert.AreEqual( "{}", ScriptState.Serialize( short1.State ), "a call that runs out while committing does not commit" );
		// Without the commit the same loop is cheaper by at least the number of nodes written.
		var uncommitted = Call( Host( "function main() local a = {} for i = 1, 200 do push(a, i) end return 1 end" ), "main" ).Steps;
		Assert.IsTrue( steps >= uncommitted + 200, $"{steps} vs {uncommitted}" );
	}

	/// <summary>Finding 2: a call that does not touch <c>state</c> must not pay for copying it.</summary>
	[TestMethod]
	public void TheStateIsCopiedLazilyAndMetered()
	{
		var json = "{" + string.Join( ",", Enumerable.Range( 0, ScriptLimits.MaxStateNodes - 1 ).Select( i => $"\"k{i}\":{i}" ) ) + "}";
		var big = ScriptState.Parse( json );
		var host = Host( "function none() return 1 end\nfunction read() return state.k5 end\nfunction write() state.k5 = 9 return 1 end", null, big );
		Assert.AreEqual( 2, Call( host, "none" ).Steps, "return 1 is two instructions; nothing is copied" );
		var read = Call( host, "read" );
		Assert.AreEqual( 5, read.Value.AsInt );
		Assert.IsTrue( read.Steps >= ScriptLimits.MaxStateNodes - 1, $"touching state pays for the copy: {read.Steps}" );
		Assert.AreSame( big, host.State, "a read does not replace the state" );
		var tiny = Host( "function read() return state.k5 end", new ScriptHostOptions { StepsPerCall = 100 }, big );
		Assert.AreEqual( ScriptOutcome.BudgetExhausted, Call( tiny, "read" ).Outcome, "the copy is inside the failure boundary" );
		Assert.AreEqual( ScriptOutcome.Ok, Call( Host( "function write() state.k5 = 9 return 1 end", new ScriptHostOptions { StepsPerCall = 100_000 }, big ), "write" ).Outcome );
	}

	/// <summary>Finding 3: key checks cost a bounded amount, however long the string.</summary>
	[TestMethod]
	public void MapKeyChecksAreBounded()
	{
		var host = Host( "function main() local m = { a = 1 } local s = 'e' for i = 1, 10 do s = s .. s end local b = has(m, s) return 1 end" );
		var result = Call( host, "main" );
		Assert.AreEqual( ScriptOutcome.RuntimeError, result.Outcome );
		Assert.IsTrue( result.Steps < 400, $"{result.Steps}" );
		foreach ( var key in new[] { "e\u0301", "\u00e9", "tab\t" } )
			Assert.AreEqual( ScriptOutcome.RuntimeError, Call( Host( $"function main() local m = {{ a = 1 }} return has(m, '{key}') end" ), "main" ).Outcome, key );
	}

	/// <summary>Finding 4: strings are opaque, so nothing can lengthen one (NFC used to double 1,024 x U+0344).</summary>
	[TestMethod]
	public void StringsAreNeverNormalisedOrLengthened()
	{
		var literal = new string( '\u0344', ScriptLimits.MaxStringLength );
		var host = Host( $"function main() state.s = '{literal}' return len(state.s) end" );
		var result = Call( host, "main" );
		Assert.AreEqual( ScriptOutcome.Ok, result.Outcome, result.Message );
		Assert.AreEqual( ScriptLimits.MaxStringLength, result.Value.AsInt );
		Assert.AreEqual( $"{{\"s\":\"{literal}\"}}", ScriptState.Serialize( host.State ), "stored exactly as written" );
		Assert.AreEqual( 2, Run( "function main() return len('e' .. '\u0301') end" ).Value.AsInt, "a decomposed pair stays two units" );
		Assert.AreEqual( 0, Run( "function main() if 'e\u0301' == '\u00e9' then return 1 end return 0 end" ).Value.AsInt, "comparison is ordinal" );
		Assert.ThrowsException<ScriptCompileException>( () => Compile( $"function main() return '{literal}x' end" ) );
		Assert.ThrowsException<ArgumentException>( () => ScriptTable.ArrayView( new ScriptValue[] { new string( 'x', ScriptLimits.MaxStringLength + 1 ) } ) );
		Assert.ThrowsException<ArgumentException>( () => Call( Host( "function main(s) return 1 end" ), "main", new string( 'x', ScriptLimits.MaxStringLength + 1 ) ) );
		Assert.ThrowsException<ArgumentException>( () => ScriptTable.RecordView( new[] { new KeyValuePair<string, ScriptValue>( "\u00e9", 1 ) } ) );
		Assert.ThrowsException<ArgumentException>( () => ScriptTable.RecordView( new[] { new KeyValuePair<string, ScriptValue>( "", 1 ) } ) );
	}

	/// <summary>Finding 5: host views obey the entry cap.</summary>
	[TestMethod]
	public void HostViewsObeyTheEntryCap()
	{
		var max = ScriptLimits.MaxTableEntries;
		var ok = ScriptTable.ArrayView( Enumerable.Range( 0, max ).Select( i => (ScriptValue)i ) );
		Assert.AreEqual( max, ok.Count );
		Assert.ThrowsException<ArgumentException>( () => ScriptTable.ArrayView( Enumerable.Range( 0, max + 1 ).Select( i => (ScriptValue)i ) ) );
		ScriptTable.RecordView( Enumerable.Range( 0, max ).Select( i => new KeyValuePair<string, ScriptValue>( "f" + i, i ) ) );
		Assert.ThrowsException<ArgumentException>( () => ScriptTable.RecordView( Enumerable.Range( 0, max + 1 ).Select( i => new KeyValuePair<string, ScriptValue>( "f" + i, i ) ) ) );
		var options = new ScriptHostOptions { StepsPerCall = 1_000_000 };
		var keys = Call( Host( "function main(v) return len(keys(v)) end", options ), "main", ScriptTable.RecordView( Enumerable.Range( 0, max ).Select( i => new KeyValuePair<string, ScriptValue>( "f" + i, i ) ) ) );
		Assert.AreEqual( max, keys.Value.AsInt, keys.Message );
	}

	/// <summary>Finding 6: constructors reach the documented entry limit (they used to overflow the value stack near 2,000 entries).</summary>
	[TestMethod]
	public void TableConstructorsReachTheEntryLimit()
	{
		var options = new ScriptHostOptions { StepsPerCall = 1_000_000 };
		string Map( int n ) => "function main() local m = { " + string.Join( ", ", Enumerable.Range( 0, n ).Select( i => $"f{i} = {i}" ) ) + " } return len(m) + m.f" + ( n - 1 ) + " end";
		string Array( int n ) => "function main() local a = { " + string.Join( ", ", Enumerable.Range( 0, n ).Select( i => i.ToString() ) ) + " } return len(a) + a[" + n + "] end";
		foreach ( var n in new[] { 2049, ScriptLimits.MaxTableEntries } )
		{
			var map = Call( Host( Map( n ), options ), "main" );
			Assert.AreEqual( ScriptOutcome.Ok, map.Outcome, $"{n} fields: {map.Message}" );
			Assert.AreEqual( 2L * n - 1, map.Value.AsInt );
			var array = Call( Host( Array( n ), options ), "main" );
			Assert.AreEqual( ScriptOutcome.Ok, array.Outcome, $"{n} elements: {array.Message}" );
			Assert.AreEqual( 2L * n - 1, array.Value.AsInt );
		}
		StringAssert.Contains( Assert.ThrowsException<ScriptCompileException>( () => Compile( Map( ScriptLimits.MaxTableEntries + 1 ) ) ).Detail, "too many table entries" );
		StringAssert.Contains( Assert.ThrowsException<ScriptCompileException>( () => Compile( Array( ScriptLimits.MaxTableEntries + 1 ) ) ).Detail, "too many table entries" );
	}

	private static readonly ScriptHostOptions Wide = new() { StepsPerCall = 1_000_000, StepsPerTick = 100_000_000 };

	/// <summary>H1: a commit that fails validation is not free (4,095 strings of 1,024 characters used to cost nothing beyond the script's own steps).</summary>
	[TestMethod]
	public void FailedCommitValidationIsChargedForTheWorkDone()
	{
		var text = new string( 'x', ScriptLimits.MaxStringLength );
		var elements = string.Join( ", ", Enumerable.Repeat( "s", ScriptLimits.MaxStateNodes - 1 ) );
		var exploit = $"local s = \"{text}\"\nfunction f() state.a = {{ {elements} }} return 0 end";
		var baseline = $"local s = \"{text}\"\nfunction f() local a = {{ {elements} }} return 0 end";
		var host = Host( exploit, Wide );
		var result = Call( host, "f" );
		var script = Call( Host( baseline, Wide ), "f" ).Steps;
		Assert.AreEqual( ScriptOutcome.InvalidResult, result.Outcome, result.Message );
		StringAssert.Contains( result.Message, "larger" );
		// The walk stops after about 64 strings (64K characters), each costing 1 + 1024/16 steps; it is charged, and bounded.
		Assert.IsTrue( result.Steps >= script + 3000, $"commit work charged: {result.Steps} vs {script}" );
		Assert.IsTrue( result.Steps <= script + 6000, $"commit work bounded: {result.Steps} vs {script}" );
		Assert.AreEqual( Wide.StepsPerTick - result.Steps, host.TickStepsRemaining, "and charged to the tick as well" );
		Assert.AreEqual( "{}", ScriptState.Serialize( host.State ) );
	}

	/// <summary>H1: the charge at a failed or successful commit is the same for a state rebuilt by a save round trip (different dictionary insertion order).</summary>
	[TestMethod]
	public void CommitChargesSurviveASaveRoundTrip()
	{
		const string source = """
			function big()
				local s = 'x'
				for i = 1, 10 do s = s .. s end
				return s
			end
			function init()
				local s = big()
				for i = 60, 1, -1 do state['k' .. i] = s end
				return 1
			end
			function grow()
				local s = big()
				state.z3 = s
				state.z2 = s
				state.z1 = s
				state.a0 = s
				return 1
			end
			function touch()
				state.k7 = 'y'
				return 1
			end
			""";
		var original = Host( source, Wide );
		Assert.AreEqual( ScriptOutcome.Ok, Call( original, "init" ).Outcome );
		var resumed = Host( source, Wide, ScriptState.Parse( ScriptState.Serialize( original.State ) ) );
		foreach ( var hook in new[] { "grow", "touch" } )
		{
			var a = Call( original, hook );
			var b = Call( resumed, hook );
			Assert.AreEqual( a.Outcome, b.Outcome, hook );
			Assert.AreEqual( a.Steps, b.Steps, $"{hook}: the same steps with and without a save round trip" );
			Assert.AreEqual( a.Message, b.Message, hook );
		}
		Assert.AreEqual( ScriptOutcome.Ok, Call( Host( source, Wide, resumed.State ), "grow" ).Outcome, "after touch shrank k7 the same growth fits" );
	}

	/// <summary>L1: the budget is checked while the commit runs, also on success.</summary>
	[TestMethod]
	public void TheBudgetIsCheckedWhileCommitting()
	{
		const string source = "function main() state.a = {} for i = 1, 2000 do push(state.a, i) end return 1 end";
		var steps = Call( Host( source, Wide ), "main" ).Steps;
		var limit = steps - 500; // runs out during the walk or the write, not in the script
		var host = Host( source, new ScriptHostOptions { StepsPerCall = limit } );
		var result = Call( host, "main" );
		Assert.AreEqual( ScriptOutcome.BudgetExhausted, result.Outcome );
		Assert.AreEqual( limit + 1, result.Steps, "work stops at the budget, it does not run to the end first" );
		Assert.AreEqual( "{}", ScriptState.Serialize( host.State ) );
	}

	/// <summary>L2: the byte limit is checked after every token, so 4,000 large integers cannot build more than about 64K characters.</summary>
	[TestMethod]
	public void TheWriterStopsAtTheByteLimitAfterEveryToken()
	{
		const string source = "function main() local big = 1000000000000000000 state.a = {} local a = state.a for i = 1, 4000 do push(a, big + i) end return 1 end";
		var steps = Call( Host( "function main() local big = 1000000000000000000 local st = {} st.a = {} local a = st.a for i = 1, 4000 do push(a, big + i) end return 1 end", Wide ), "main" ).Steps;
		var result = Call( Host( source, Wide ), "main" );
		Assert.AreEqual( ScriptOutcome.InvalidResult, result.Outcome, result.Message );
		StringAssert.Contains( result.Message, "larger" );
		// Walk: one step per node (4,002). Write: one step per 16 characters up to the limit (4,096). Writing 4,000 integers in full would cost about 5,400.
		Assert.IsTrue( result.Steps - steps <= 4002 + 4096 + 64, $"{result.Steps - steps}" );
	}

	/// <summary>L3: hook results are an int, a bool or a string; a table result cannot be asked for, and a diagnostic string is bounded.</summary>
	[TestMethod]
	public void TablesAreNotHookResults()
	{
		var host = Host( "function main() return state end" );
		Assert.ThrowsException<ArgumentException>( () => host.Call( "main", ReadOnlySpan<ScriptValue>.Empty, 0, new ScriptResultSpec( ScriptKind.Table ), Identity ) );
		Assert.ThrowsException<ArgumentException>( () => host.Call( "main", ReadOnlySpan<ScriptValue>.Empty, 0, new ScriptResultSpec( ScriptKind.Unit ), Identity ) );
		var wrong = Call( host, "main" );
		Assert.AreEqual( ScriptOutcome.InvalidResult, wrong.Outcome );
		StringAssert.Contains( wrong.Message, "must return int, not table" );
		Assert.AreEqual( "table[0]", ScriptValue.FromTable( ScriptState.Empty() ).ToString() );
		// A shared subtree returned as a result is not expanded by anything.
		var dag = Call( Host( "function main() local t = 'x' for i = 1, 30 do t = { t, t } end return t end", Wide ), "main" );
		Assert.AreEqual( ScriptOutcome.InvalidResult, dag.Outcome );
		Assert.IsTrue( dag.Message!.Length < 200 );
	}

	/// <summary>L4: an invalid key is an error for every kind of table, empty or not.</summary>
	[TestMethod]
	public void HasTreatsInvalidKeysTheSameForEveryTable()
	{
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return has({}, '') and 1 or 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local b = has({}, '') return 1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local b = has({ a = 1 }, '') return 1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local b = has({}, '\u00e9') return 1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local b = has({}, true) return 1" ) );
		Assert.AreEqual( 1, Eval( "if not has({}, 'ok') and not has({}, 3) and has({ ok = 1 }, 'ok') then return 1 end return 0" ) );
	}

	/// <summary>L6: running out of steps in a charge (here a concatenation) still names the line.</summary>
	[TestMethod]
	public void BudgetExhaustionInAChargeNamesTheLine()
	{
		var result = Run( "function main()\n  local s = 'x'\n  for i = 1, 10 do s = s .. s end\n  for i = 1, 5000 do\n    local t = s .. ''\n  end\n  return 1\nend" );
		Assert.AreEqual( ScriptOutcome.BudgetExhausted, result.Outcome );
		Assert.AreEqual( 5, result.Line );
	}
}
