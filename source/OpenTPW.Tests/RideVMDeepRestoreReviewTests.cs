using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public sealed class RideVMDeepRestoreReviewTests
{
	[TestMethod]
	public void NestedChildGraphRestoresAndContinuesFromAGrandchildWait()
	{
		var grandchild = new RideVMTests.Asm()
			.I( Opcode.WAIT, 1000 ).I( Opcode.COPY, RideVMTests.V( 0 ), 9 )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "G" } );
		var child = new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 0 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "C" }, "grand.rse" );
		var parent = new RideVMTests.Asm()
			.I( Opcode.SPAWNCHILD, RideVMTests.S( 0 ) )
			.Label( "idle" ).I( Opcode.ENDSLICE ).I( Opcode.BRANCH, "idle" )
			.Build( new[] { "P" }, "child.rse" );
		RideScriptFile? Resolve( string name ) => name switch
		{
			"child.rse" => new RideScriptFile( new MemoryStream( child ) ),
			"grand.rse" => new RideScriptFile( new MemoryStream( grandchild ) ),
			_ => null
		};
		RideVM Root( RideScriptWorld world ) => new( new RideScriptFile( new MemoryStream( parent ) ),
			new RideVMOptions { Seed = 42, World = world, SourceName = "parent.rse", ResolveScript = Resolve } );
		var world = new RideScriptWorld();
		var original = Root( world );
		for ( var tick = 0; tick < 3; tick++ ) original.Advance( RideVMSnapshotTests.Tick );
		Assert.IsNotNull( original.Child?.Child, "this is a real two-level spawned child chain" );
		Assert.AreEqual( RideVMState.Waiting, original.Child.Child.State );
		var snapshot = world.CaptureSnapshot();
		snapshot.ValidateSnapshot();
		var restoredWorld = new RideScriptWorld();
		var restored = Root( restoredWorld );
		restoredWorld.RestoreSnapshot( snapshot, new Dictionary<int, RideVM> { [original.ScriptId] = restored } );
		Assert.AreEqual( original.Child.Child.ScriptId, restored.Child!.Child!.ScriptId );
		for ( var tick = 0; tick < 90; tick++ )
		{
			original.Advance( RideVMSnapshotTests.Tick );
			restored.Advance( RideVMSnapshotTests.Tick );
			var expected = world.CaptureSnapshot();
			var actual = restoredWorld.CaptureSnapshot();
			Assert.AreEqual( expected.Scripts.Count, actual.Scripts.Count );
			for ( var index = 0; index < expected.Scripts.Count; index++ )
			{
				var a = new StateHasher();
				var b = new StateHasher();
				world.Find( expected.Scripts[index].ScriptId )!.AddCanonicalState( a );
				restoredWorld.Find( actual.Scripts[index].ScriptId )!.AddCanonicalState( b );
				Assert.AreEqual( a.Value, b.Value, $"script {index} diverged at tick {tick}" );
			}
		}
		Assert.AreEqual( 9, restored.Child.Child.Variables[0] );
	}
}
