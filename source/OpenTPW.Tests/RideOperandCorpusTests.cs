using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The operand meanings read from the original's handlers (docs/RSE-VM.md, "Hooked opcodes: the operands as the handlers read them") constrain the values the 308 shipped scripts
/// can pass: a type operand that selects a dispatcher case lies in the table, a destination the handler writes only when it is a variable is one (or the literal 0 where only the flags
/// matter), a duration is positive. Inconclusive without OPENTPW_GAME_PATH; no game bytes are kept here.
/// </summary>
[TestClass]
public class RideOperandCorpusTests
{
	private static IEnumerable<(string Name, RideScriptFile Script, RideScriptInstruction Instruction)> Instructions( params Opcode[] opcodes )
	{
		var wanted = opcodes.Select( x => (ushort)x ).ToHashSet();
		foreach ( var (name, data) in RseScriptTests.LoadCorpusBytes() )
		{
			var script = new RideScriptFile( new MemoryStream( data ) );
			foreach ( var instruction in script.Instructions.Where( x => wanted.Contains( x.Opcode ) ) )
				yield return (name, script, instruction);
		}
	}

	private static bool IsLiteral( RideScriptOperand operand ) => operand.Kind == RideScriptOperandKind.Literal;
	private static bool IsVariable( RideScriptOperand operand ) => operand.Kind == RideScriptOperandKind.Variable;
	private static int Literal( RideScriptOperand operand ) => (short)operand.Value;
	private static string Variable( RideScriptFile script, RideScriptOperand operand ) => script.VariableNames[operand.Value];

	/// <summary>The object opcodes: the dispatcher 0x5573D0 takes types 1 to 10 (a type below 1 or above 10 logs and creates nothing, 0x557931).</summary>
	[TestMethod]
	public void ObjectTypesLieInTheDispatchersTable()
	{
		var counts = new SortedDictionary<int, int>();
		foreach ( var (name, _, instruction) in Instructions( Opcode.ADDOBJ, Opcode.ADDOBJ_EXT, Opcode.EVENT ) )
		{
			Assert.IsTrue( IsLiteral( instruction.Operands[0] ), $"{name}: the type is a literal" );
			var type = Literal( instruction.Operands[0] );
			Assert.IsTrue( type is >= 1 and <= 10, $"{name}: type {type}" );
			counts[type] = counts.GetValueOrDefault( type ) + 1;
		}
		Assert.IsTrue( counts.Keys.All( type => type != 10 ), "no shipped script uses the object type's own list (type 10)" );
		Assert.IsTrue( counts.Count >= 8, "types 1 to 9 but 6 occur" );
	}

	/// <summary>A key is a literal (a model node number or -1, the footprint middle) except in 24 EVENTs and two ADDOBJs that read it from a variable; an effect or event id is a positive literal in every ADDOBJ and EVENT but one (Worn.RSE's particle EVENT, whose key and id are variables).</summary>
	[TestMethod]
	public void ObjectKeysAndIdsAreNodeNumbersOrMinusOneAndLiterals()
	{
		var variableKeys = 0;
		var variableIds = new List<string>();
		foreach ( var (name, _, instruction) in Instructions( Opcode.ADDOBJ, Opcode.EVENT ) )
		{
			var key = instruction.Operands[1];
			if ( IsVariable( key ) )
				variableKeys++;
			else
				Assert.IsTrue( Literal( key ) is -1 or >= 1 and <= 20, $"{name}: key {Literal( key )}" );
			if ( IsVariable( instruction.Operands[2] ) )
				variableIds.Add( name );
			else
				Assert.IsTrue( Literal( instruction.Operands[2] ) > 0, $"{name}: the id is positive" );
		}
		Assert.AreEqual( 26, variableKeys );
		CollectionAssert.AreEqual( new[] { "jungle/rides/porkpie.wad/Worn.RSE" }, variableIds );
	}

	/// <summary>KILLOBJ, FADEOBJ and SETOBJPARAM name a slot with a literal; SETOBJPARAM is only ever slot 1, parameter 20, value 0 or 75.</summary>
	[TestMethod]
	public void SlotOperandsAreLiteralsAndSetObjParamIsPinned()
	{
		foreach ( var (name, _, instruction) in Instructions( Opcode.ADDOBJ, Opcode.KILLOBJ, Opcode.FADEOBJ ) )
		{
			var slot = instruction.Operands[instruction.Opcode == (ushort)Opcode.ADDOBJ ? 3 : 0];
			Assert.IsTrue( IsLiteral( slot ) && Literal( slot ) >= 0, $"{name}: the slot is a literal" );
		}
		var settings = Instructions( Opcode.SETOBJPARAM ).Select( x => $"{Literal( x.Instruction.Operands[0] )}/{Literal( x.Instruction.Operands[1] )}/{Literal( x.Instruction.Operands[2] )}" ).Distinct().OrderBy( x => x ).ToArray();
		CollectionAssert.AreEqual( new[] { "1/20/0", "1/20/75" }, settings );
	}

	/// <summary>The animation opcodes' out operand is the one the handler writes only when it is a variable; the shipped scripts use a variable or the literal 0 (no result wanted).</summary>
	[TestMethod]
	public void TriggerOutOperandsAreVariablesOrZero()
	{
		foreach ( var (name, _, instruction) in Instructions( Opcode.TRIGANIM, Opcode.TRIGWAITANIM, Opcode.TRIGANIMSPEED, Opcode.TRIGANIM_CH, Opcode.TRIGWAITANIM_CH ) )
		{
			var output = instruction.Operands[2];
			Assert.IsTrue( IsVariable( output ) || IsLiteral( output ) && Literal( output ) == 0, $"{name}: {(Opcode)instruction.Opcode} out operand" );
		}
		foreach ( var (name, _, instruction) in Instructions( Opcode.TRIGANIMSPEED ) )
			Assert.IsTrue( IsLiteral( instruction.Operands[3] ) && Literal( instruction.Operands[3] ) > 0, $"{name}: a positive speed (the guard EXT:trig-anim-speed-guard never fires on shipped scripts)" );
	}

	/// <summary>The destinations of the queries the handlers write only for a variable: a variable, or the literal 0 where the script wants the flags only.</summary>
	[TestMethod]
	public void QueryDestinationsAreVariablesOrZero()
	{
		foreach ( var (name, _, instruction) in Instructions( Opcode.UNBOUNCE, Opcode.FORCEUNBOUNCE, Opcode.BOUNCING, Opcode.WALKGET, Opcode.UNLIMBO, Opcode.FORCEUNLIMBO ) )
			Assert.IsTrue( IsVariable( instruction.Operands[0] ), $"{name}: {(Opcode)instruction.Opcode} writes a variable" );
		foreach ( var (name, _, instruction) in Instructions( Opcode.GETANIM_CH, Opcode.WALKFLOATSTAT, Opcode.INLIMBO, Opcode.LIMBOSPACE, Opcode.MIN, Opcode.SEC ) )
			Assert.IsTrue( IsVariable( instruction.Operands[0] ) || IsLiteral( instruction.Operands[0] ) && Literal( instruction.Operands[0] ) == 0, $"{name}: {(Opcode)instruction.Opcode} destination" );
	}

	/// <summary>GETTIMER (tp.exe 0x55644D) writes a variable destination; its literal destinations (0 in 17 uses, 10000 in 4) only set the flags.</summary>
	[TestMethod]
	public void GetTimerDestinationsAreVariablesOrFlagOnlyLiterals()
	{
		var literals = Instructions( Opcode.GETTIMER ).Where( x => IsLiteral( x.Instruction.Operands[0] ) ).Select( x => Literal( x.Instruction.Operands[0] ) ).Distinct().OrderBy( x => x ).ToArray();
		CollectionAssert.AreEqual( new[] { 0, 10000 }, literals );
	}

	/// <summary>
	/// The bounce opcodes (tp.exe 0x555674): the rider and the duration are variables, the duration is VAR_DURATION, and a script that bounces declares a bounce table.
	/// </summary>
	[TestMethod]
	public void BounceOperandsAreRiderAndDurationVariablesOfATableScript()
	{
		var scripts = new HashSet<string>();
		foreach ( var (name, script, instruction) in Instructions( Opcode.BOUNCE ) )
		{
			scripts.Add( name );
			Assert.IsTrue( IsVariable( instruction.Operands[0] ) && IsVariable( instruction.Operands[1] ) );
			Assert.AreEqual( "VAR_LETMEON", Variable( script, instruction.Operands[0] ) );
			Assert.AreEqual( "VAR_DURATION", Variable( script, instruction.Operands[1] ) );
			Assert.IsTrue( script.BounceSize > 0, $"{name}: a bounce table" );
		}
		Assert.AreEqual( 4, scripts.Count );
	}

	/// <summary>
	/// WALKON (tp.exe 0x555963): the rider is a variable, the walk action is a literal the walk update knows (1, 2, 4, 5 or 6; only 2 and 4 do something), the last operand is the literal 1 (bit 0: the walking
	/// body's height comes from the nodes) and the script declares a walk table.
	/// </summary>
	[TestMethod]
	public void WalkOnOperandsAreARiderVariableAndKnownActions()
	{
		foreach ( var (name, script, instruction) in Instructions( Opcode.WALKON ) )
		{
			Assert.IsTrue( IsVariable( instruction.Operands[0] ), $"{name}: the rider" );
			Assert.IsTrue( IsLiteral( instruction.Operands[5] ) && Literal( instruction.Operands[5] ) is 1 or 2 or 4 or 5 or 6, $"{name}: the walk action" );
			Assert.IsTrue( IsLiteral( instruction.Operands[6] ) && Literal( instruction.Operands[6] ) == 1, $"{name}: the last operand" );
			Assert.IsTrue( script.WalkSize > 0, $"{name}: a walk table" );
		}
	}

	/// <summary>Durations are positive: WAIT (milliseconds, divided by the script speed), SETTIMER (milliseconds), LIMBO (seconds); a literal is above 0 and below a minute.</summary>
	[TestMethod]
	public void TimeOperandsArePositiveAndBelowAMinuteWhenLiteral()
	{
		foreach ( var (name, _, instruction) in Instructions( Opcode.WAIT, Opcode.SETTIMER ) )
			Assert.IsTrue( IsVariable( instruction.Operands[0] ) || Literal( instruction.Operands[0] ) is > 0 and <= 30000, $"{name}: {(Opcode)instruction.Opcode} {Literal( instruction.Operands[0] )} ms" );
		foreach ( var (name, _, instruction) in Instructions( Opcode.LIMBO ) )
			Assert.IsTrue( IsVariable( instruction.Operands[1] ) || Literal( instruction.Operands[1] ) is > 0 and <= 60, $"{name}: LIMBO {Literal( instruction.Operands[1] )} s" );
	}

	/// <summary>
	/// The sound opcodes: REPAIREFFECT's value is 0 or 1; SETREVERB's level lies in 0 to 10; DIPMUSIC is only ever 1; SCREAMLEVEL lies in 0 to 100; the scream band is a variable, VAR_ONRIDE (the
	/// riders on board) in 81 of the 86 STARTSCREAM and SINGLESCREAM (VAR_TEMP in four, VAR_PEEPS in one) and always a variable, and the level is -1 (a negative level picks the band alone, 0x555F69) or 0 to 100.
	/// </summary>
	[TestMethod]
	public void SoundOperandsKeepToTheirRanges()
	{
		foreach ( var (name, _, instruction) in Instructions( Opcode.REPAIREFFECT ) )
			Assert.IsTrue( IsLiteral( instruction.Operands[0] ) && Literal( instruction.Operands[0] ) is 0 or 1, $"{name}: REPAIREFFECT" );
		foreach ( var (name, _, instruction) in Instructions( Opcode.SETREVERB ) )
			Assert.IsTrue( IsLiteral( instruction.Operands[0] ) && Literal( instruction.Operands[0] ) is >= 0 and <= 10, $"{name}: SETREVERB" );
		foreach ( var (name, _, instruction) in Instructions( Opcode.DIPMUSIC ) )
			Assert.AreEqual( 1, Literal( instruction.Operands[0] ), $"{name}: DIPMUSIC" );
		foreach ( var (name, _, instruction) in Instructions( Opcode.SCREAMLEVEL ) )
			Assert.IsTrue( IsLiteral( instruction.Operands[0] ) && Literal( instruction.Operands[0] ) is >= 0 and <= 100, $"{name}: SCREAMLEVEL" );
		var onRide = 0;
		foreach ( var (name, script, instruction) in Instructions( Opcode.STARTSCREAM, Opcode.SINGLESCREAM ) )
		{
			Assert.IsTrue( IsVariable( instruction.Operands[0] ), $"{name}: the band is a variable" );
			if ( Variable( script, instruction.Operands[0] ) == "VAR_ONRIDE" )
				onRide++;
			var level = Literal( instruction.Operands[1] );
			Assert.IsTrue( IsLiteral( instruction.Operands[1] ) && (level is >= 0 and <= 100 || instruction.Opcode == (ushort)Opcode.SINGLESCREAM && level == -1), $"{name}: {(Opcode)instruction.Opcode} level {level}" );
		}
		Assert.AreEqual( 81, onRide );
	}

	/// <summary>SPARK (tp.exe 0x5564ED) looks two key nodes up for a log line and nothing else: its first operand is a variable, the second the node 1, the third 500 and the fourth 0 in its one use.</summary>
	[TestMethod]
	public void SparkIsUsedOnceWithItsOperandsDroppedByTheOriginal()
	{
		var spark = Instructions( Opcode.SPARK ).Single();
		Assert.AreEqual( (RideScriptOperandKind.Variable, RideScriptOperandKind.Literal, 1, 500, 0), (spark.Instruction.Operands[0].Kind, spark.Instruction.Operands[1].Kind, Literal( spark.Instruction.Operands[1] ), Literal( spark.Instruction.Operands[2] ), Literal( spark.Instruction.Operands[3] )) );
	}

	/// <summary>
	/// CRIT_LOCK (tp.exe 0x551D44) only stops instructions counting against the slice budget until the next slice start or CRIT_UNLOCK (the flag's only reader is the slice loop, 0x551725; the whole image
	/// references it six times): every shipped script that locks also unlocks, and waits do occur inside a lock (a wait ends the slice, and the next slice clears the flag, 0x551701).
	/// </summary>
	[TestMethod]
	public void EveryScriptThatLocksAlsoUnlocksAndALockedWaitIsAnOrdinaryWait()
	{
		var locking = 0;
		var waitsInside = 0;
		foreach ( var (name, data) in RseScriptTests.LoadCorpusBytes() )
		{
			var script = new RideScriptFile( new MemoryStream( data ) );
			var locks = script.Instructions.Count( x => x.Opcode == (ushort)Opcode.CRIT_LOCK );
			var unlocks = script.Instructions.Count( x => x.Opcode == (ushort)Opcode.CRIT_UNLOCK );
			if ( locks == 0 )
				continue;
			locking++;
			Assert.IsTrue( unlocks >= 1, $"{name}: {locks} locks, {unlocks} unlocks" );
			var inside = false;
			foreach ( var instruction in script.Instructions )
			{
				if ( instruction.Opcode == (ushort)Opcode.CRIT_LOCK )
					inside = true;
				else if ( instruction.Opcode == (ushort)Opcode.CRIT_UNLOCK )
					inside = false;
				else if ( inside && instruction.Opcode is (ushort)Opcode.WAIT or (ushort)Opcode.WAITANIM or (ushort)Opcode.WAIT4ANIM or (ushort)Opcode.TRIGWAITANIM )
					waitsInside++;
			}
		}
		Assert.IsTrue( locking > 50, $"{locking} scripts lock" );
		Assert.IsTrue( waitsInside > 0, "at least one shipped script waits inside a lock (bumper.RSE)" );
	}
}
