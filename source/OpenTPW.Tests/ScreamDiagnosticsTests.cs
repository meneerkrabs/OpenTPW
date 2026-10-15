using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using N = System.Numerics;

namespace OpenTPW.Tests;

/// <summary>
/// The scream diagnostics (docs/AUDIO.md "Scream diagnostics"): a second STARTSCREAM before a STOPSCREAM is the original's refusal (tp.exe 0x551130, log 0x765a4c) that
/// leaks the old chain, and the counts follow the script's own view of its handle, so a headless run without audio counts the same.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ScreamDiagnosticsTests
{
	private string root = null!;
	private List<Entity> previousEntities = null!;
	private ObjectCatalogEntry entry = null!;

	[TestInitialize]
	public void Setup()
	{
		Log ??= new();
		previousEntities = Entity.All;
		Entity.All = new();
		root = Directory.CreateTempSubdirectory( "opentpw-scream-diagnostics-" ).FullName;
		var model = Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x42 ), 1 );
		BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( 0x44 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x268 ), 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( model.AsSpan( 0x78 ), 0x264 );
		foreach ( var offset in new[] { 0x38, 0x3a, 0x3c, 0x3e, 0x4a } )
			BinaryPrimitives.WriteUInt16LittleEndian( model.AsSpan( offset ), 0 );
		File.WriteAllBytes( Path.Combine( root, "model.MD2" ), model );
		File.WriteAllBytes( Path.Combine( root, "script.RSE" ), new RideVMTests.Asm().I( Opcode.ENDSLICE ).Build( Enum.GetNames<RideVariables>() ) );
		entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Ride, "/synthetic", new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 1100\nInfo.Name Screamer\n", "synthetic.sam" ) } ),
			new ObjectShape( new[] { "*" } ), "/model.MD2", null, "/script.RSE", Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), new BaseFileSystem( root ), null );
		GameAudio.Screams.Reset();
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var entity in Entity.All.ToArray() )
			entity.Delete();
		Entity.All = previousEntities;
		Directory.Delete( root, true );
		GameAudio.Screams.Reset();
	}

	private static void Run( OriginalObjectRuntime owner, Opcode opcode, params object[] operands )
	{
		using var data = new MemoryStream( new RideVMTests.Asm().I( opcode, operands ).I( Opcode.ENDSLICE ).Build( Array.Empty<string>() ) );
		var vm = new RideVM( data, new RideVMOptions { Effects = owner.Script!.Effects } );
		vm.Advance( FixedStepClock.TickDuration );
		Assert.AreNotEqual( RideVMState.Faulted, vm.State );
		vm.Stop();
	}

	[TestMethod]
	public void ASecondStartWithoutAStopIsCountedAsALeakWithoutAudio()
	{
		var owner = new OriginalObject( entry, new ObjectPlacement( 3, 4, 0, new N.Vector2( -7, 11 ), 9 ) ).Runtime;
		Run( owner, Opcode.STARTSCREAM, 3, 20 );
		Assert.AreEqual( (1, 0, 0), (GameAudio.Screams.Starts, GameAudio.Screams.Refusals, GameAudio.Screams.Stops) );
		Run( owner, Opcode.STARTSCREAM, 3, 20 );
		Assert.AreEqual( (1, 1, 0), (GameAudio.Screams.Starts, GameAudio.Screams.Refusals, GameAudio.Screams.Stops), "the refusal leaks the held chain" );
		Run( owner, Opcode.STARTSCREAM, 3, 20 );
		Assert.AreEqual( (2, 1, 0), (GameAudio.Screams.Starts, GameAudio.Screams.Refusals, GameAudio.Screams.Stops), "the refusal overwrote the handle with 0: the next start is accepted" );
		Run( owner, Opcode.STOPSCREAM );
		Assert.AreEqual( 1, GameAudio.Screams.Stops );
		Run( owner, Opcode.STOPSCREAM );
		Assert.AreEqual( 1, GameAudio.Screams.Stops, "nothing is held: nothing stops" );
		Run( owner, Opcode.STARTSCREAM, 3, 20 );
		Assert.AreEqual( (3, 1, 1), (GameAudio.Screams.Starts, GameAudio.Screams.Refusals, GameAudio.Screams.Stops) );
	}

	[TestMethod]
	public void BandZeroHoldsNoChainAndEveryBandIsCounted()
	{
		var owner = new OriginalObject( entry, new ObjectPlacement( 3, 4, 0, new N.Vector2( -7, 11 ), 9 ) ).Runtime;
		Run( owner, Opcode.STARTSCREAM, 0, 20 );
		Run( owner, Opcode.STARTSCREAM, 0, 20 );
		Assert.AreEqual( 0, GameAudio.Screams.Refusals, "band 0 plays nothing and keeps no handle (0x551130 returns 0)" );
		Run( owner, Opcode.STARTSCREAM, 5, 20 );
		Run( owner, Opcode.STOPSCREAM );
		CollectionAssert.AreEqual( new[] { (0, 2), (5, 1) }, GameAudio.Screams.Bands.OrderBy( pair => pair.Key ).Select( pair => (pair.Key, pair.Value) ).ToArray() );
		var line = GameAudio.Screams.Summary();
		StringAssert.Contains( line, "Screamer: 3 starts, 0 refused, 1 stops" );
	}
}
