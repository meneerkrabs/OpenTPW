using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static OpenTPW.Tests.RideVMTests;

namespace OpenTPW.Tests;

/// <summary>
/// The RSSE block of an original save gives the imported scripts their saved ids, list order, next id and pass counter (tp.exe loader 0x5597A0, DET-013): the header is read raw over
/// 0x8791A0 and every record is linked at the list's head, so one slice in eight passes falls on the passes the saved park ran it on.
/// </summary>
[TestClass]
public class SavedScriptImportTests
{
	private static RideVM Counter( RideScriptWorld world )
	{
		var asm = new Asm();
		asm.Label( "loop" ).I( Opcode.ADD, V( 0 ), 1 ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "loop" );
		return new RideVM( new RideScriptFile( new MemoryStream( asm.Build( new[] { "V0", "V1" } ) ) ), new RideVMOptions { World = world, Seed = 1 } );
	}

	[TestMethod]
	public void SavedIdsTheReversedListOrderTheNextIdAndThePassCounterAreTaken()
	{
		var world = new RideScriptWorld( 7 );
		var scripts = Enumerable.Range( 0, 3 ).Select( _ => Counter( world ) ).ToArray();
		// the save lists its scripts head first: 9, 8, 7 (the newest first), as the jungle autosave does
		world.ImportSaved( new[] { (scripts[0], 7), (scripts[1], 8), (scripts[2], 9) }, new[] { 9, 8, 7 }, 10, 97036 );
		CollectionAssert.AreEqual( new[] { 7, 8, 9 }, scripts.Select( script => script.ScriptId ).ToArray() );
		Assert.AreSame( scripts[1], world.Find( 8 ) );
		Assert.IsNull( world.Find( 1 ), "the creation-order ids are gone" );
		// the loader links each record at the head: the file's last record heads the list, so the walk runs 7, 8, 9
		CollectionAssert.AreEqual( scripts, world.ListOrder.ToArray() );
		Assert.AreEqual( 97036, world.PassCounter );
		var created = Counter( world );
		Assert.AreEqual( 10, created.ScriptId, "the next id is the saved header's" );
		Assert.AreSame( created, world.ListOrder[0], "a script created after the load is linked at the head" );
	}

	[TestMethod]
	public void TheSliceCadenceFollowsTheSavedIdsAndPassCounter()
	{
		var world = new RideScriptWorld( 7 );
		var scripts = Enumerable.Range( 0, 5 ).Select( _ => Counter( world ) ).ToArray();
		var ids = new[] { 1, 2, 7, 8, 9 };
		const long saved = 124154;
		world.ImportSaved( scripts.Select( ( script, index ) => (script, ids[index]) ).ToArray(), new[] { 9, 8, 7, 2, 1 }, 10, saved );
		for ( var pass = 1; pass <= 16; pass++ )
		{
			var before = scripts.Select( script => script.Variables[0] ).ToArray();
			world.RunPass( SimulationSubstep.Milliseconds );
			Assert.AreEqual( saved + pass, world.PassCounter );
			for ( var index = 0; index < scripts.Length; index++ )
				Assert.AreEqual( ((ids[index] ^ (saved + pass)) & 7) == 0, scripts[index].Variables[0] != before[index], $"script {ids[index]} on pass {saved + pass}" );
		}
	}

	[TestMethod]
	public void ALiveScriptTheSaveDoesNotHoldGetsAFreshIdAtTheHead()
	{
		var world = new RideScriptWorld( 7 );
		var scripts = Enumerable.Range( 0, 4 ).Select( _ => Counter( world ) ).ToArray();
		world.ImportSaved( new[] { (scripts[0], 2), (scripts[1], 1), (scripts[2], 5) }, new[] { 5, 2, 1 }, 6, 0 );
		Assert.AreEqual( 6, scripts[3].ScriptId, "a script the save lacks takes the next id, as one created after the load" );
		CollectionAssert.AreEqual( new[] { scripts[3], scripts[1], scripts[0], scripts[2] }, world.ListOrder.ToArray(), "it heads the list; the loaded ones follow in reverse of the saved order" );
		Assert.AreEqual( 7, Counter( world ).ScriptId );
	}

	[TestMethod]
	public void ANextIdBelowASavedIdIsRaisedPastIt()
	{
		var world = new RideScriptWorld( 7 );
		var script = Counter( world );
		world.ImportSaved( new[] { (script, 12) }, new[] { 12 }, 3, 5 );
		Assert.AreEqual( 13, Counter( world ).ScriptId, "ids are never reused" );
	}

	[TestMethod]
	public void UnusableInputIsRefusedBeforeAnythingChanges()
	{
		var world = new RideScriptWorld( 7 );
		var first = Counter( world );
		var second = Counter( world );
		var foreign = Counter( new RideScriptWorld( 7 ) );
		Assert.ThrowsException<ArgumentException>( () => world.ImportSaved( new[] { (first, 4), (second, 4) }, new[] { 4 }, 5, 0 ) );
		Assert.ThrowsException<ArgumentException>( () => world.ImportSaved( new[] { (first, 0) }, new[] { 0 }, 5, 0 ) );
		Assert.ThrowsException<ArgumentException>( () => world.ImportSaved( new[] { (foreign, 4) }, new[] { 4 }, 5, 0 ) );
		Assert.ThrowsException<ArgumentException>( () => world.ImportSaved( new[] { (first, 4), (first, 5) }, new[] { 4, 5 }, 6, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => world.ImportSaved( new[] { (first, 4) }, new[] { 4 }, 0, 0 ) );
		Assert.AreEqual( (1, 2), (first.ScriptId, second.ScriptId) );
		Assert.AreEqual( 0, world.PassCounter );
	}

	[TestMethod]
	public void ALoadedWorldStillSnapshotsAndRestoresItsScripts()
	{
		var world = new RideScriptWorld( 7 );
		var scripts = Enumerable.Range( 0, 3 ).Select( _ => Counter( world ) ).ToArray();
		world.ImportSaved( scripts.Select( ( script, index ) => (script, new[] { 1, 2, 7 }[index]) ).ToArray(), new[] { 7, 2, 1 }, 8, 40 );
		var snapshot = world.CaptureSnapshot();
		CollectionAssert.AreEqual( new[] { 1, 2, 7 }, snapshot.Scripts.Select( script => script.ScriptId ).ToArray() );
		Assert.AreEqual( 8, snapshot.NextScriptId );
		Assert.AreEqual( 40, snapshot.PassCounter );
	}
}
