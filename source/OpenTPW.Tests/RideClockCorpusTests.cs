using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The shipped scripts read the scheduler clock only through differences (inconclusive without OPENTPW_GAME_PATH), which is why the port's
/// per-world clock origin (it starts at 0) and the original's app-lifetime clock (0x785970, never reset for a new park) behave alike (RIDES-044 retired).
/// </summary>
[TestClass]
public class RideClockCorpusTests
{
	/// <summary>
	/// Every variable a GETTIME stores into is used only by GETTIME, ADD and SUB (a deadline is the clock plus an interval, and a wait is the clock minus a
	/// stored value), except the scratch variable of the difference form (<c>GETTIME t; SUB t stored t</c>), whose very next instruction is the SUB. So no script
	/// can observe the absolute value.
	/// </summary>
	[TestMethod]
	public void EveryGetTimeIsUsedOnlyThroughDifferences()
	{
		var uses = 0;
		foreach ( var (name, data) in RseScriptTests.LoadCorpusBytes() )
		{
			var script = new RideScriptFile( new MemoryStream( data ) );
			var list = script.Instructions;
			var stored = new HashSet<int>();
			var scratch = new HashSet<int>();
			for ( var i = 0; i < list.Count; i++ )
			{
				if ( list[i].Opcode != (ushort)Opcode.GETTIME )
					continue;
				uses++;
				var target = list[i].Operands[0].Value;
				Assert.AreEqual( RideScriptOperandKind.Variable, list[i].Operands[0].Kind, $"{name}: GETTIME stores into a variable" );
				var next = i + 1 < list.Count ? list[i + 1] : null;
				if ( next is { } && next.Opcode == (ushort)Opcode.SUB && IsVariable( next.Operands, 0, target ) && IsVariable( next.Operands, 2, target ) && IsAnyVariable( next.Operands, 1 ) )
					scratch.Add( target );
				else
					stored.Add( target );
			}
			// variables that only ever receive the clock: a stored variable is only ever advanced by ADD (its own destination) or subtracted from a fresh clock read (the SUB follows a GETTIME of the SUB's destination)
			foreach ( var variable in stored )
			{
				for ( var j = 0; j < list.Count; j++ )
				{
					var instruction = list[j];
					var opcode = (Opcode)instruction.Opcode;
					if ( opcode == Opcode.GETTIME || !instruction.Operands.Any( x => x.Kind == RideScriptOperandKind.Variable && x.Value == variable ) )
						continue;
					var fine = opcode == Opcode.ADD && IsVariable( instruction.Operands, 0, variable ) && instruction.Operands.Skip( 1 ).All( x => x.Kind != RideScriptOperandKind.Variable || x.Value != variable );
					if ( opcode == Opcode.SUB && j > 0 && list[j - 1].Opcode == (ushort)Opcode.GETTIME && IsVariable( instruction.Operands, 1, variable )
						&& IsVariable( instruction.Operands, 2, list[j - 1].Operands[0].Value ) && IsVariable( instruction.Operands, 0, list[j - 1].Operands[0].Value ) )
						fine = true;
					Assert.IsTrue( fine, $"{name}: {script.VariableNames[variable]} (a GETTIME value) is used by {opcode} at word {instruction.WordOffset}" );
				}
			}
		}
		Assert.IsTrue( uses > 100, "the corpus uses GETTIME" );
	}

	private static bool IsVariable( IReadOnlyList<RideScriptOperand> operands, int index, int variable ) =>
		index < operands.Count && operands[index].Kind == RideScriptOperandKind.Variable && operands[index].Value == variable;

	private static bool IsAnyVariable( IReadOnlyList<RideScriptOperand> operands, int index ) =>
		index < operands.Count && operands[index].Kind == RideScriptOperandKind.Variable;
}
