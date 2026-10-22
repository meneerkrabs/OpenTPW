using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Census of the shipped rides' sound EVENT keys (types 3-9, literal key >= 0) against their own models with selector 0x200
/// ([BIN:TPW-EXE:0x556b90]): how many resolve, and how many would play nothing. Skips without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class EventKeyCensusTests
{
	[TestMethod]
	public void EverySoundEventKeyOfTheShippedRidesResolvesToANodeWithSelector0x200()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
		try
		{
			var counts = new Dictionary<ParticleKeyStatus, int>();
			var variableKeys = 0;
			var negativeKeys = 0;
			var scripts = 0;
			var objects = 0;
			var lost = new List<string>();
			foreach ( var theme in ObjectCatalog.Themes )
			{
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
				{
					var paths = (entry.ScriptPath == null ? Array.Empty<string>() : new[] { entry.ScriptPath }).Concat( entry.Scripts ).Distinct().ToList();
					if ( paths.Count == 0 )
						continue;
					objects++;
					var model = ObjectAssets.LoadModel( entry.FileSystem, entry.ModelPath );
					var frames = ModelAnimationPlayer.ComputeRestTransforms( model );
					var resolver = new ParticleKeyResolver( model );
					var placement = new ObjectPlacement( 0, 0, 0, System.Numerics.Vector2.Zero, 0 );
					foreach ( var path in paths )
					{
						RideScriptFile script;
						using ( var stream = entry.FileSystem.OpenRead( path ) )
							script = new RideScriptFile( stream );
						scripts++;
						foreach ( var instruction in script.Instructions.Where( item => item.Opcode == (ushort)Opcode.EVENT && item.Operands.Count >= 3 ) )
						{
							var type = instruction.Operands[0];
							if ( type.Kind != RideScriptOperandKind.Literal || type.Value is < 3 or > 9 )
								continue;
							var key = instruction.Operands[1];
							if ( key.Kind != RideScriptOperandKind.Literal )
							{
								variableKeys++;
								continue;
							}
							if ( (short)key.Value < 0 )
							{
								negativeKeys++;
								continue;
							}
							var status = resolver.Resolve( (short)key.Value, ParticleKeyResolver.SoundSelector, frames, placement ).Status;
							counts[status] = counts.GetValueOrDefault( status ) + 1;
							if ( status != ParticleKeyStatus.Resolved )
								lost.Add( $"{theme}/{entry.ArchivePath} {path} EVENT {type.Value} key {(short)key.Value} effect {instruction.Operands[2].Value}: {status}" );
						}
					}
				}
			}
			Console.WriteLine( $"EVENT census: {objects} objects, {scripts} scripts; keys >= 0: " + string.Join( ", ", counts.Select( pair => $"{pair.Key} {pair.Value}" ) )
				+ $"; negative keys {negativeKeys}; variable keys {variableKeys}" );
			foreach ( var line in lost )
				Console.WriteLine( line );
			Assert.AreEqual( 0, lost.Count, "keys that would play nothing: " + string.Join( "; ", lost.Take( 5 ) ) );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	/// <summary>
	/// Census of the EVENT instruction's type operand over every shipped script: type 10 (tp.exe 0x5573d0 case 10: the object type's own sound list, type record +0x4F0, found by
	/// 0x4145e0; ObjectTypeSoundsTests) has no shipped user. Skips without <c>OPENTPW_GAME_PATH</c>.
	/// </summary>
	[TestMethod]
	public void NoShippedScriptSubmitsAnObjectSoundOfType10()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
		try
		{
			var types = new SortedDictionary<int, int>();
			var variableTypes = 0;
			var scripts = 0;
			var otherTypeTen = new List<string>();
			foreach ( var theme in ObjectCatalog.Themes )
			{
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
				{
					foreach ( var path in (entry.ScriptPath == null ? Array.Empty<string>() : new[] { entry.ScriptPath }).Concat( entry.Scripts ).Distinct() )
					{
						RideScriptFile script;
						using ( var stream = entry.FileSystem.OpenRead( path ) )
							script = new RideScriptFile( stream );
						scripts++;
						foreach ( var instruction in script.Instructions.Where( item => item.Opcode == (ushort)Opcode.EVENT && item.Operands.Count >= 3 ) )
						{
							var type = instruction.Operands[0];
							if ( type.Kind != RideScriptOperandKind.Literal )
							{
								variableTypes++;
								continue;
							}
							types[type.Value] = types.GetValueOrDefault( type.Value ) + 1;
						}
						// the other forms that reach the dispatcher 0x5573d0: EVENT_EXT, ADDOBJ and ADDOBJ_EXT
						foreach ( var instruction in script.Instructions.Where( item => item.Opcode is (ushort)Opcode.EVENT_EXT or (ushort)Opcode.ADDOBJ or (ushort)Opcode.ADDOBJ_EXT && item.Operands.Count >= 3 ) )
						{
							var type = instruction.Operands[0];
							if ( type.Kind != RideScriptOperandKind.Literal || type.Value == 10 )
								otherTypeTen.Add( $"{entry.ArchivePath} {path} {(Opcode)instruction.Opcode} {type.Kind} {type.Value}" );
						}
					}
				}
			}
			Console.WriteLine( $"EVENT type census: {scripts} scripts; " + string.Join( ", ", types.Select( pair => $"type {pair.Key}: {pair.Value}" ) ) + $"; variable types {variableTypes}" );
			Assert.AreEqual( 0, variableTypes, "the type operand is a literal in every shipped script" );
			Assert.IsFalse( types.ContainsKey( 10 ), "a shipped script submits an EVENT of type 10 (the object type's own sound list)" );
			Assert.AreEqual( 0, otherTypeTen.Count, "an EVENT_EXT, ADDOBJ or ADDOBJ_EXT of type 10 or a variable type: " + string.Join( "; ", otherTypeTen.Take( 5 ) ) );
		}
		finally
		{
			FileSystem = previous;
		}
	}
}
