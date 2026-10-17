using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Scripting;
using static OpenTPW.Tests.ScriptTestSupport;

namespace OpenTPW.Tests;

/// <summary>TPW Script grammar and semantics (docs/SCRIPT-MODS.md).</summary>
[TestClass]
public class ScriptLanguageTests
{
	[TestMethod]
	public void IntegerArithmeticTruncatesTowardZero()
	{
		Assert.AreEqual( 7, Eval( "return 1 + 2 * 3" ) );
		Assert.AreEqual( 9, Eval( "return (1 + 2) * 3" ) );
		Assert.AreEqual( -3, Eval( "return -7 // 2" ) );
		Assert.AreEqual( -3, Eval( "return 7 // -2" ) );
		Assert.AreEqual( 3, Eval( "return -7 // -2" ) );
		Assert.AreEqual( -1, Eval( "return -7 % 2" ) );
		Assert.AreEqual( 1, Eval( "return 7 % -2" ) );
		Assert.AreEqual( 0, Eval( "return 7 % 1" ) );
		Assert.AreEqual( long.MaxValue, Eval( "return 9223372036854775807" ) );
		Assert.AreEqual( long.MinValue, Eval( "return -9223372036854775807 - 1" ) );
		Assert.AreEqual( 5, Eval( "return - -5" ) );
	}

	[TestMethod]
	public void FixedPointLiteralsAreHundredths()
	{
		Assert.AreEqual( 150, Eval( "return fx(1.5)" ) );
		Assert.AreEqual( 105, Eval( "return fx(1.05)" ) );
		Assert.AreEqual( 200, Eval( "return fx(2)" ) );
		Assert.AreEqual( -5, Eval( "return fx(-0.05)" ) );
		Assert.AreEqual( 300, Eval( "return fxmul(fx(1.5), fx(2))" ) );
		Assert.AreEqual( 150, Eval( "return fxdiv(fx(3), fx(2))" ) );
		Assert.AreEqual( -150, Eval( "return fxdiv(fx(-3), fx(2))" ) );
		Assert.AreEqual( 33, Eval( "return fxdiv(fx(1), fx(3))" ) );
		Assert.AreEqual( 125, Run( "local rate = fx(1.25)\nfunction main() return rate end" ).Value.AsInt );
	}

	[TestMethod]
	public void ControlFlow()
	{
		Assert.AreEqual( 55, Eval( "local s = 0 for i = 1, 10 do s = s + i end return s" ) );
		Assert.AreEqual( 30, Eval( "local s = 0 for i = 10, 1, -2 do s = s + i end return s" ) );
		Assert.AreEqual( 0, Eval( "local s = 0 for i = 5, 1 do s = s + 1 end return s" ) );
		Assert.AreEqual( 6, Eval( "local s = 0 for i = 1, 100 do if i > 3 then break end s = s + i end return s" ) );
		Assert.AreEqual( 10, Eval( "local i = 0 while i < 10 do i = i + 1 end return i" ) );
		Assert.AreEqual( 2, Eval( "local x = 5 if x < 3 then return 1 elseif x < 8 then return 2 else return 3 end" ) );
		Assert.AreEqual( 3, Eval( "local x = 50 if x < 3 then return 1 elseif x < 8 then return 2 else return 3 end" ) );
		Assert.AreEqual( 9, Eval( "local n = 0 for i = 1, 3 do for j = 1, 3 do n = n + 1 end end return n" ) );
		Assert.AreEqual( 4, Eval( "local n = 0 for i = 1, 3 do for j = 1, 3 do if j == 2 then break end n = n + 1 end end return n + 1" ) );
		Assert.AreEqual( 3, Eval( "local n = 1 do local n2 = 2 n = n + n2 end return n" ) );
	}

	[TestMethod]
	public void ForLoopVariableIsACopyAndEndsAtTheLongLimit()
	{
		Assert.AreEqual( 10, Eval( "local n = 0 for i = 1, 5 do i = i + 100 n = n + 2 end return n" ) );
		Assert.AreEqual( 3, Eval( "local n = 0 for i = 9223372036854775805, 9223372036854775807 do n = n + 1 end return n" ) );
		Assert.AreEqual( 2, Eval( "local n = 0 for i = -9223372036854775807 - 1, -9223372036854775807, 1 do n = n + 1 end return n" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "for i = 1, 5, 0 do end return 1" ) );
	}

	[TestMethod]
	public void LogicIsStrictlyBoolean()
	{
		Assert.AreEqual( 1, Eval( "if true and not false then return 1 end return 0" ) );
		Assert.AreEqual( 1, Eval( "if false or 3 > 2 then return 1 end return 0" ) );
		Assert.AreEqual( 1, Eval( "if 1 == 1 and 2 ~= 3 and 3 >= 3 and 3 <= 3 and 4 > 3 and 3 < 4 then return 1 end return 0" ) );
		// Short circuit: the right side must not run (it would divide by zero).
		Assert.AreEqual( 1, Eval( "if false and 1 // 0 == 0 then return 0 end if true or 1 // 0 == 0 then return 1 end return 0" ) );
		Assert.AreEqual( 1, Eval( "if 'abc' < 'abd' and 'abc' == 'abc' and 'b' > 'a' then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "if 1 then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "if 1 and true then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "if true and 1 then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "if not 1 then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "if 1 == 'a' then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "if 'a' < 1 then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "if {} == {} then return 1 end return 0" ) );
	}

	[TestMethod]
	public void StringsAndConcatenation()
	{
		Assert.AreEqual( 5, Eval( "return len('he' .. 'llo')" ) );
		Assert.AreEqual( 4, Eval( "return len('a' .. 12 .. 'b')" ) );
		Assert.AreEqual( 1, Eval( "if 'x' .. 1 + 2 == 'x3' then return 1 end return 0" ) );
		Assert.AreEqual( 1, Run( """
			function main() if "a\n\t\\\"" == 'a\n\t\\"' then return 1 end return 0 end
			""" ).Value.AsInt );
		Assert.AreEqual( 1, Eval( "if 'a' .. 'b' .. 'c' == 'abc' then return 1 end return 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return 'a' .. true" ) );
		Assert.AreEqual( 2, Eval( "return len('é')" + " + 1" ) ); // one NFC character
	}

	[TestMethod]
	public void FunctionsConstantsAndRecursion()
	{
		const string source = """
			local base = 10
			local label = "x"
			local on = true
			local neg = -4

			-- a comment
			function fib(n)
				if n < 2 then return n end
				return fib(n - 1) + fib(n - 2)
			end

			--[[ block
			comment ]]
			function later(n) return helper(n) * 2 end
			function helper(n) return n + base + neg end

			function main(a, b)
				if on and len(label) == 1 then
					return fib(10) + later(1) + a * b
				end
				return 0
			end
			""";
		// fib(10) = 55, later(1) = (1 + 10 - 4) * 2 = 14, a * b = 6
		Assert.AreEqual( 75, Run( source, 2, 3 ).Value.AsInt );
	}

	[TestMethod]
	public void HostFunctions()
	{
		Assert.AreEqual( 3, Eval( "return min(3, 5)" ) );
		Assert.AreEqual( 5, Eval( "return max(3, 5)" ) );
		Assert.AreEqual( 4, Eval( "return abs(-4)" ) );
		Assert.AreEqual( 7, Eval( "return clamp(9, 1, 7)" ) );
		Assert.AreEqual( 1, Eval( "return clamp(-9, 1, 7)" ) );
		Assert.AreEqual( 4, Eval( "return clamp(4, 1, 7)" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return clamp(4, 7, 1)" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return abs(-9223372036854775807 - 1)" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return min(1, true)" ) );
	}

	[TestMethod]
	public void TablesOnStateAndLocals()
	{
		var host = Host( """
			function main()
				state.count = 0
				state.names = {}
				state.pos = { x = 1, y = 2 }
				push(state.names, "a")
				push(state.names, "b")
				state.names[2] = "c"
				state.pos.x = state.pos.x + 5
				local t = { 10, 20, 30 }
				local m = { k = 7 }
				state.count = t[1] + t[3] + m.k + len(t) + len(state.names)
				if has(state.pos, "x") and not has(state.pos, "z") and has(t, 3) and not has(t, 4) then state.count = state.count + 1 end
				return state.count
			end
			""" );
		var result = Call( host, "main" );
		Assert.AreEqual( ScriptOutcome.Ok, result.Outcome, result.Message );
		Assert.AreEqual( 10 + 30 + 7 + 3 + 2 + 1, result.Value.AsInt );
		Assert.AreEqual( "{\"count\":53,\"names\":[\"a\",\"c\"],\"pos\":{\"x\":6,\"y\":2}}", ScriptState.Serialize( host.State ) );
	}

	[TestMethod]
	public void PopDelAndKeys()
	{
		var host = Host( """
			function main()
				state.items = { 1, 2, 3 }
				local last = pop(state.items)
				state.m = { b = 1, a = 2, c = 3 }
				del(state.m, "b")
				local ks = keys(state.m)
				return last * 100 + len(ks) * 10 + len(keys({}))
			end
			""" );
		var result = Call( host, "main" );
		Assert.AreEqual( 320, result.Value.AsInt, result.Message );
		Assert.AreEqual( "{\"items\":[1,2],\"m\":{\"a\":2,\"c\":3}}", ScriptState.Serialize( host.State ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Call( Host( "function main() local m = {a = 1} del(m, 'zz') return 1 end" ), "main" ).Outcome );
		// keys() are sorted ordinally whatever the insertion order.
		Assert.AreEqual( 1, Run( "function main() local k = keys({ b = 1, a = 2, c = 3 }) if k[1] == 'a' and k[2] == 'b' and k[3] == 'c' then return 1 end return 0 end" ).Value.AsInt );
	}

	[TestMethod]
	public void NilSemanticsAreDefined()
	{
		// There is no nil: reading an absent key or index is an error, has() asks, a function without a result cannot be used as a value.
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return state.missing" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local t = { 1 } return t[2]" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local t = { 1 } return t[0]" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local t = { a = 1 } return t.b" ) );
		Assert.AreEqual( 0, Eval( "return len({})" ) );
		var noResult = Run( "function nothing() end\nfunction main() local x = nothing() return 1 end" );
		Assert.AreEqual( ScriptOutcome.RuntimeError, noResult.Outcome );
		Assert.AreEqual( 1, Run( "function nothing() end\nfunction main() nothing() return 1 end" ).Value.AsInt );
		Assert.AreEqual( ScriptOutcome.InvalidResult, Run( "function main() end" ).Outcome );
	}

	[TestMethod]
	public void ArraysGrowOnlyByOneAndNeverMixWithMaps()
	{
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local t = {} t[2] = 1 return 1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local t = { 1 } t[3] = 1 return 1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local t = { 1 } t.a = 1 return 1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "local t = { a = 1 } t[1] = 1 return 1" ) );
		Assert.AreEqual( 2, Eval( "local t = {} t[1] = 5 t[2] = 6 return len(t)" ) );
		Assert.AreEqual( 1, Eval( "local t = {} t.a = 5 return len(t)" ) );
	}

	[TestMethod]
	public void DivisionByZeroOverflowAndUnaryMinus()
	{
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return 1 // 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return 1 % 0" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return fxdiv(1, 0)" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return 9223372036854775807 + 1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return -9223372036854775807 - 2" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return 9223372036854775807 * 2" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return -(-9223372036854775807 - 1)" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return (-9223372036854775807 - 1) // -1" ) );
		Assert.AreEqual( 0, Eval( "return (-9223372036854775807 - 1) % -1" ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Outcome( "return fxmul(9223372036854775807, 9223372036854775807)" ) );
		var failed = Run( "function main()\n  local x = 9223372036854775807\n  return x + 1\nend" );
		Assert.AreEqual( 3, failed.Line );
		StringAssert.Contains( failed.Message, "overflow" );
	}

	[TestMethod]
	public void RuntimeErrorsCarryTheLine()
	{
		var result = Run( "function main()\n  local a = 1\n  local b = a // 0\n  return b\nend" );
		Assert.AreEqual( ScriptOutcome.RuntimeError, result.Outcome );
		Assert.AreEqual( 3, result.Line );
		Assert.AreEqual( Previous, result.Value.AsInt, "the previous result comes back on failure" );
		StringAssert.Contains( result.Message, "division by zero" );
	}

	[TestMethod]
	public void CompileErrorsCarryLineAndColumn()
	{
		var error = Assert.ThrowsException<ScriptCompileException>( () => Compile( "function main()\n  return 1 / 2\nend" ) );
		Assert.AreEqual( 2, error.Line );
		Assert.AreEqual( 12, error.Column );
		StringAssert.Contains( error.Detail, "//" );

		error = Assert.ThrowsException<ScriptCompileException>( () => Compile( "function main()\n  local x = \nend" ) );
		Assert.AreEqual( 3, error.Line );

		error = Assert.ThrowsException<ScriptCompileException>( () => Compile( "function main()\n  return @\nend" ) );
		Assert.AreEqual( (2, 10), (error.Line, error.Column) );
	}

	[TestMethod]
	public void SourceIsHashedAndDecodedFromTheSameBytes()
	{
		var bytes = Encoding.UTF8.GetBytes( "function main() return 1 end" );
		var program = ScriptProgram.Compile( bytes );
		Assert.AreEqual( Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes ) ).ToLowerInvariant(), program.Sha256 );
		Assert.IsTrue( program.HasFunction( "main" ) );
		Assert.AreEqual( 0, program.ParameterCount( "main" ) );
		Assert.IsFalse( ScriptProgram.TryCompile( new byte[] { 0xff, 0xfe, 0x00 }, out _, out var invalid ) );
		StringAssert.Contains( invalid!.Detail, "UTF-8" );
		Assert.IsFalse( ScriptProgram.TryCompile( new byte[] { 0xEF, 0xBB, 0xBF, (byte)'f' }, out _, out _ ), "a byte order mark is not script text" );
	}

	[TestMethod]
	public void HookArgumentsAndViews()
	{
		var view = ScriptTable.RecordView( new[]
		{
			new KeyValuePair<string, ScriptValue>( "id", 7 ),
			new KeyValuePair<string, ScriptValue>( "name", "Zoom" ),
			new KeyValuePair<string, ScriptValue>( "tags", ScriptTable.ArrayView( new ScriptValue[] { 1, 2, 3 } ) )
		} );
		var result = Run( "function main(v, n) return v.id + len(v.tags) + v.tags[3] + len(v.name) + n end", ScriptValue.FromTable( view ), 10 );
		Assert.AreEqual( 7 + 3 + 3 + 4 + 10, result.Value.AsInt );
		var host = Host( "function main(a) return a end" );
		Assert.ThrowsException<ArgumentException>( () => Call( host, "main" ) );
		Assert.ThrowsException<ArgumentException>( () => Call( host, "main", ScriptValue.FromTable( ScriptState.Empty() ) ) );
		Assert.AreEqual( ScriptOutcome.RuntimeError, Call( host, "nope", 1 ).Outcome );
		Assert.ThrowsException<ArgumentException>( () => ScriptTable.ArrayView( new[] { ScriptValue.FromTable( ScriptState.Empty() ) } ) );
	}

	[TestMethod]
	public void HookResultsAreTypedAndRanged()
	{
		var host = Host( "function score(n) return n end\nfunction flag(n) return n > 0 end\nfunction label(n) return 'x' end" );
		var prev = ScriptValue.FromInt( 5 );
		var ok = host.Call( "score", new ScriptValue[] { 50 }, prev, ScriptResultSpec.Int( 0, 100 ), Identity );
		Assert.AreEqual( ScriptOutcome.Ok, ok.Outcome );
		Assert.AreEqual( 50, ok.Value.AsInt );
		var high = host.Call( "score", new ScriptValue[] { 500 }, prev, ScriptResultSpec.Int( 0, 100 ), Identity );
		Assert.AreEqual( ScriptOutcome.InvalidResult, high.Outcome );
		Assert.AreEqual( 5, high.Value.AsInt );
		Assert.AreEqual( ScriptOutcome.InvalidResult, host.Call( "flag", new ScriptValue[] { 1 }, prev, ScriptResultSpec.Int( 0, 100 ), Identity ).Outcome );
		Assert.IsTrue( host.Call( "flag", new ScriptValue[] { 1 }, false, ScriptResultSpec.Bool, Identity ).Value.AsBool );
		Assert.AreEqual( "x", host.Call( "label", new ScriptValue[] { 1 }, "p", ScriptResultSpec.String, Identity ).Value.AsString );
	}
}
