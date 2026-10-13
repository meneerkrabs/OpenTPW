using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Original Theme Park Inc scripts executing the two opcodes no Theme Park World script contains
/// (docs/TPI-COMPARISON.md): WAITABS (45) with 34 instructions in 19 scripts and MULT (48) once in
/// vegetation.RSE. Inconclusive without <c>OPENTPW_TPI_PATH</c> (a Theme Park Inc installation or its
/// Data folder). Static corpus facts plus VM-level execution; the Theme Park Inc slice smoke
/// (docs/RUNNING.md) never reached these opcodes on the science level, so retail play coverage is
/// not claimed beyond these paths.
/// </summary>
[TestClass]
public class RideVMTpiCorpusTests
{
	private static Dictionary<string, byte[]>? cachedCorpus;

	/// <summary>Key: "theme[/folder]/archive.wad/member.RSE" or "levels/vegetation.RSE" relative to the TPI levels folder.</summary>
	internal static Dictionary<string, byte[]>? LoadTpiCorpus()
	{
		if ( cachedCorpus != null )
			return cachedCorpus;
		var path = Environment.GetEnvironmentVariable( "OPENTPW_TPI_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		var data = Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
		var levels = Directory.EnumerateDirectories( data ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "levels", StringComparison.OrdinalIgnoreCase ) );
		if ( levels == null )
			return null;
		var corpus = new Dictionary<string, byte[]>( StringComparer.OrdinalIgnoreCase );
		foreach ( var wadPath in Directory.EnumerateFiles( levels, "*", SearchOption.AllDirectories ).Where( x => x.EndsWith( ".wad", StringComparison.OrdinalIgnoreCase ) ) )
		{
			using var archive = new WadArchive( wadPath );
			var relative = Path.GetRelativePath( levels, wadPath ).Replace( Path.DirectorySeparatorChar, '/' );
			Collect( archive.Root, relative, corpus );
		}
		foreach ( var loose in Directory.EnumerateFiles( levels, "*.rse", SearchOption.AllDirectories ) )
			corpus.Add( Path.GetRelativePath( levels, loose ).Replace( Path.DirectorySeparatorChar, '/' ), File.ReadAllBytes( loose ) );
		return cachedCorpus = corpus;
	}

	private static void Collect( ArchiveDirectory directory, string prefix, Dictionary<string, byte[]> corpus )
	{
		foreach ( var item in directory.Children )
		{
			if ( item is ArchiveDirectory child )
				Collect( child, $"{prefix}/{child.Name}", corpus );
			else if ( item is ArchiveFile file && file.Name!.EndsWith( ".rse", StringComparison.OrdinalIgnoreCase ) )
				corpus.Add( $"{prefix}/{file.Name}", file.GetData() );
		}
	}

	/// <summary>
	/// The RideVMCorpusTests clip-less object host: every animation trigger returns the missing-member clip
	/// (1000 ms), so WAITANIM waits a finite 700 ms and scripts pass their animation gates; every other effect
	/// stays unimplemented. None of the 19 scripts spawns children, so no script resolver is needed.
	/// </summary>
	private sealed class ClipLessObjectEffects : IRideScriptEffects
	{
		public int Perform( RideEffectCall call )
		{
			if ( call.Opcode is Opcode.TRIGANIM or Opcode.WAITANIM or Opcode.LOOPANIM or Opcode.TRIGWAITANIM or Opcode.TRIGANIMSPEED
				or Opcode.TRIGANIM_CH or Opcode.WAITANIM_CH or Opcode.LOOPANIM_CH or Opcode.TRIGWAITANIM_CH )
				return ObjectAnimator.MissingClipMilliseconds;
			return UnimplementedRideScriptEffects.Instance.Perform( call );
		}
	}

	[TestMethod]
	public void TheGapOpcodesMatchTheirHandlersOnEveryThemeParkIncUse()
	{
		var corpus = LoadTpiCorpus();
		if ( corpus == null )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var waitabs = 0;
		var mult = 0;
		var scripts = 0;
		foreach ( var (name, data) in corpus )
		{
			RideScriptFile script;
			try { script = new RideScriptFile( new MemoryStream( data ) ); }
			catch ( Exception exception )
			{
				Assert.Fail( $"{name}: {exception.Message}" );
				continue;
			}
			var uses = script.Instructions.Where( x => x.Opcode is (ushort)Opcode.WAITABS or (ushort)Opcode.MULT ).ToList();
			if ( uses.Count == 0 )
				continue;
			++scripts;
			// Constructing a VM validates every instruction's operand count against the handler table, so this
			// also pins WAITABS at 1 operand and MULT at 3 on every original use.
			new RideVM( script, new RideVMOptions { Seed = 1, SourceName = name } );
			foreach ( var instruction in uses )
			{
				if ( instruction.Opcode == (ushort)Opcode.WAITABS )
				{
					++waitabs;
					Assert.AreEqual( RideScriptOperandKind.Literal, instruction.Operands[0].Kind, $"{name} word {instruction.WordOffset}" );
					var value = instruction.Operands[0].Value;
					Assert.IsTrue( value is > 0 and <= 10000, $"{name} word {instruction.WordOffset}: {value}" );
				}
				else
					++mult;
			}
		}
		Assert.AreEqual( 19, scripts, "the audited script count (docs/TPI-COMPARISON.md)" );
		Assert.AreEqual( 34, waitabs );
		Assert.AreEqual( 1, mult, "vegetation.RSE only" );
	}

	[TestMethod]
	public void VegetationMultipliesItsTimerScaleInsideTheCriticalSection()
	{
		var corpus = LoadTpiCorpus();
		if ( corpus == null )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var executed = new HashSet<Opcode>();
		var vm = new RideVM( new RideScriptFile( new MemoryStream( corpus["vegetation.RSE"] ) ), new RideVMOptions
		{
			Effects = new ClipLessObjectEffects(),
			Seed = 1,
			SourceName = "vegetation.RSE"
		} );
		vm.InstructionExecuting = ( _, instruction ) => executed.Add( instruction.Opcode );
		for ( var tick = 0; tick < 60; tick++ )
			vm.Advance( RideVMTests.Tick );
		Assert.IsTrue( executed.Contains( Opcode.MULT ), "the script passed CRIT_LOCK's MULT after the opening WAITANIM" );
		Assert.IsTrue( executed.Contains( Opcode.SETTIMER ), "the product feeds SETTIMER" );
		Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
	}

	[TestMethod]
	public void CameraObstacleRunsItsWaitAbsoluteAndReturnsToTheIdleLoop()
	{
		var corpus = LoadTpiCorpus();
		if ( corpus == null )
			Assert.Inconclusive( "Set OPENTPW_TPI_PATH to a Theme Park Inc installation or its Data folder." );
		var waitabs = 0;
		var vm = new RideVM( new RideScriptFile( new MemoryStream( corpus["arabian/obstacles/Camera.wad/camera.RSE"] ) ), new RideVMOptions
		{
			Effects = new ClipLessObjectEffects(),
			Seed = 1,
			SourceName = "arabian/obstacles/Camera.wad/camera.RSE"
		} );
		vm.InstructionExecuting = ( _, instruction ) => { if ( instruction.Opcode == Opcode.WAITABS ) ++waitabs; };
		var trigger = vm.GetVariableIndex( "VAR_OBSTACLETRIG" );
		var flag = vm.GetVariableIndex( "VAR_OBSTACLEFLAG" );
		vm.Variables[trigger] = 1;
		// WAITANIM 5 0 waits the missing-member clip (700 ms after the 300 ms floor) and WAITABS 500 follows,
		// so the script is mid-wait well past the animation gate.
		for ( var tick = 0; tick < 50; tick++ )
			vm.Advance( RideVMTests.Tick );
		Assert.AreEqual( 1, waitabs, "the trigger path reached WAITABS 500" );
		Assert.AreEqual( RideVMState.Waiting, vm.State );
		Assert.AreEqual( 1, vm.Variables[flag], "the copies before the wait ran" );
		var slices = vm.SliceCount;
		for ( var tick = 0; tick < 10; tick++ )
			vm.Advance( RideVMTests.Tick );
		Assert.AreEqual( slices, vm.SliceCount, "no slice while the wait is pending" );
		for ( var tick = 0; tick < 25; tick++ )
			vm.Advance( RideVMTests.Tick );
		Assert.IsTrue( vm.SliceCount > slices, "the wait resumed and the idle loop slices again" );
		Assert.AreNotEqual( RideVMState.Faulted, vm.State, vm.FaultMessage );
	}
}
