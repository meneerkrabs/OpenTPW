using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The nodes that the shipped WALKON operands name (tp.exe 0x00556F40 looks u1 and u2 up when the walker boards, 0x005571A0 looks u3 and u4 up when it leaves) are dummies of the ride's model,
/// so every walker's walk-on and walk-off takes a measured time (inconclusive without OPENTPW_GAME_PATH).
/// </summary>
[TestClass]
[DoNotParallelize]
public class WalkNodeCorpusTests
{
	private BaseFileSystem? originalFileSystem;
	private bool initialized;

	[TestInitialize]
	public void Initialize()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the walk node corpus." );
		originalFileSystem = previous;
		initialized = true;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( initialized )
			FileSystem = originalFileSystem!;
		ObjectCatalog.BonusDataRoot = null;
	}

	[TestMethod]
	public void EveryShippedWalkOnNamesNodesTheModelHas()
	{
		var checkedWalkers = 0;
		var missing = new List<string>();
		foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( x => x.ScriptPath != null && x.ModelPath != null ).OrderBy( x => x.InfoId ) )
			{
				var script = new RideScriptFile( entry.FileSystem.OpenRead( entry.ScriptPath! ) );
				foreach ( var walkOn in script.Instructions.Where( x => x.Opcode == (ushort)Opcode.WALKON ) )
				{
					Assert.IsTrue( script.WalkSize > 0, $"{entry.DisplayName}: a script that walks riders declares a walk table" );
					Assert.AreEqual( 7, walkOn.Operands.Count );
					Assert.AreEqual( RideScriptOperandKind.Literal, walkOn.Operands[5].Kind, $"{entry.DisplayName}: the walk action is a literal" );
					Assert.AreEqual( (RideScriptOperandKind.Literal, 1), (walkOn.Operands[6].Kind, (int)walkOn.Operands[6].Value), $"{entry.DisplayName}: the last operand is the literal 1 (bit 0: the walking body takes its height from the nodes)" );
					int? Number( int index ) => walkOn.Operands[index].Kind == RideScriptOperandKind.Literal ? (int)walkOn.Operands[index].Value : null;
					var action = Number( 5 )!.Value;
					var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), 1 );
					// the head-action rides count their head id up in a variable (VAR_ONRIDE, VAR_HEADCOUNT, VAR_TEMP): only the literal nodes are checked here
					if ( Number( 1 ) is { } u1 && Number( 2 ) is { } u2 )
					{
						var on = runtime.WalkNodeDistance( 0x800, u1, action == 4 ? 0x80 : 0x800, u2 );
						if ( on == null )
							missing.Add( $"{theme}/{entry.DisplayName}: walk-on nodes {u1} and {u2}" );
					}
					if ( Number( 3 ) is { } u3 && Number( 4 ) is { } u4 )
					{
						var off = runtime.WalkNodeDistance( action, u3, u4 );
						if ( off == null )
							missing.Add( $"{theme}/{entry.DisplayName}: walk-off nodes {u3} and {u4}" );
					}
					checkedWalkers++;
					runtime.Stop();
				}
			}
		
		Assert.IsTrue( checkedWalkers > 30, $"the corpus has more than 30 WALKON instructions, got {checkedWalkers}" );
		Assert.AreEqual( 0, missing.Count, string.Join( "; ", missing ) );
	}

	/// <summary>
	/// BOUNCE (tp.exe 0x00555674) stores its deadline as the clock plus its second operand x 1000 ms with no default. The four shipped scripts that BOUNCE pass VAR_DURATION, which ride init sets from
	/// the settings' InitDuration, so a placed ride's duration is above 0 and the operand never needs a default.
	/// </summary>
	[TestMethod]
	public void EveryShippedBounceRideStartsWithADurationAboveZero()
	{
		var bouncers = 0;
		foreach ( var theme in new[] { "jungle", "hallow", "space", "fantasy" } )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries.Where( x => x.ScriptPath != null && x.ModelPath != null ).OrderBy( x => x.InfoId ) )
			{
				var script = new RideScriptFile( entry.FileSystem.OpenRead( entry.ScriptPath! ) );
				if ( !script.Instructions.Any( x => x.Opcode == (ushort)Opcode.BOUNCE ) )
					continue;
				bouncers++;
				var bounce = script.Instructions.First( x => x.Opcode == (ushort)Opcode.BOUNCE );
				Assert.AreEqual( RideScriptOperandKind.Variable, bounce.Operands[1].Kind, $"{entry.DisplayName}: the duration operand is a variable" );
				Assert.AreEqual( "VAR_DURATION", script.VariableNames[(int)bounce.Operands[1].Value], entry.DisplayName );
				var runtime = new OriginalObjectRuntime( entry, new RideScriptWorld(), 1 );
				Assert.IsTrue( runtime.Script!.Variables[(int)RideVariables.VAR_DURATION] > 0, $"{entry.DisplayName}: VAR_DURATION {runtime.Script.Variables[(int)RideVariables.VAR_DURATION]}" );
				runtime.Stop();
			}
		Assert.AreEqual( 4, bouncers, "Bouncy (jungle, space), Brain Buster and Jelly" );
	}
}
