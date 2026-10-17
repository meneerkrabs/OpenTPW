using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// Outside the opcode test map's classes on purpose: the map names the real opcodes, and this test runs a number the table does not have.
/// </summary>
[TestClass]
public class RseUnnamedOpcodeTests
{
	/// <summary>Every named opcode has a handler (the ride lights, EVENT_EXT and GETCUSTPTCLCODE since tp.exe 0x555C7A..0x555D9A, 0x552707, 0x5563D5); a number outside the table still faults.</summary>
	[TestMethod]
	public void ANumberOutsideTheOpcodeTableFaultsTheScript()
	{
		var unknown = new RideVM( new RideScriptFile( new MemoryStream( new Asm().I( (Opcode)200, 1 ).Build( new[] { "V0" } ) ) ), new RideVMOptions { Seed = 1 } );
		unknown.Advance( 1d / 60 );
		Assert.AreEqual( RideVMState.Faulted, unknown.State );
		StringAssert.Contains( unknown.FaultMessage, "OP_200" );
	}
}
