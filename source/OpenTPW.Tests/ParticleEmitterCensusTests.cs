using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Census of the particle requests the shipped scripts and type records make (EVENT/ADDOBJ types 1 and 2, the per-type effects of the category .sam), so
/// the emitter paths OpenTPW serves are the ones the game data reaches. Skips without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ParticleEmitterCensusTests
{
	[TestMethod]
	public void ShippedParticleRequestsReachOnlyWhatTheDocsSay()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
		try
		{
			var requests = new SortedDictionary<string, int>();
			var scopedLiterals = 0;
			var variableEffects = new SortedSet<string>();
			var fixedWithEffects = new List<string>();
			var type2Negative = new SortedSet<string>();
			var teleportRides = new List<string>();
			foreach ( var theme in ObjectCatalog.Themes )
			{
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
				{
					if ( entry.Settings.GetBool( "UsageInfo.RequiresTeleport" ) )
						teleportRides.Add( $"{theme}/{entry.ArchivePath}" );
					if ( entry.IsFixedItem && (entry.CreateParticleEffect != 0 || entry.DestroyParticleEffect != 0 || entry.RepairParticleEffect != 0 || entry.UpgradeParticleEffect != 0) )
						fixedWithEffects.Add( $"{theme}/{entry.ArchivePath}" );
					var paths = (entry.ScriptPath == null ? Array.Empty<string>() : new[] { entry.ScriptPath }).Concat( entry.Scripts ).Distinct().ToList();
					foreach ( var path in paths )
					{
						RideScriptFile script;
						using ( var stream = entry.FileSystem.OpenRead( path ) )
							script = new RideScriptFile( stream );
						foreach ( var instruction in script.Instructions )
						{
							var opcode = (Opcode)instruction.Opcode;
							if ( opcode is not (Opcode.EVENT or Opcode.EVENT_EXT or Opcode.ADDOBJ or Opcode.ADDOBJ_EXT) || instruction.Operands.Count < 3 )
								continue;
							var type = instruction.Operands[0];
							if ( type.Kind == RideScriptOperandKind.Literal && type.Value is not (1 or 2) )
								continue;
							var key = instruction.Operands[1];
							var effect = instruction.Operands[2];
							var keyClass = key.Kind != RideScriptOperandKind.Literal ? "var" : (short)key.Value < 0 ? "neg" : "key";
							var effectText = effect.Kind != RideScriptOperandKind.Literal ? "var" : $"{(short)effect.Value}";
							var name = $"{theme} {opcode} type {(type.Kind == RideScriptOperandKind.Literal ? type.Value.ToString() : "var")} {keyClass}";
							requests[name] = requests.GetValueOrDefault( name ) + 1;
							if ( effect.Kind != RideScriptOperandKind.Literal )
								variableEffects.Add( $"{theme}{entry.ArchivePath}" );
							else if ( ((short)effect.Value & 0x8000) != 0 )
								scopedLiterals++;
							if ( type.Kind == RideScriptOperandKind.Literal && type.Value == 2 && keyClass == "neg" )
								type2Negative.Add( $"{theme}{entry.ArchivePath}" );
							if ( entry.IsFixedItem && keyClass == "neg" )
								fixedWithEffects.Add( $"{theme}{entry.ArchivePath} script {name}" );
						}
					}
				}
			}
			foreach ( var pair in requests )
				Console.WriteLine( $"{pair.Key}: {pair.Value}" );
			// PTCL-908: what the shipped data reaches (docs/PARTICLES.md): no script names a scoped effect (bit 0x8000) literally, one names its effect through a variable, no fixed item
			// has a type effect or a script that starts an effect at a negative key, and the one type-2 effect with a negative key is Fantasy's bugstv
			Assert.AreEqual( 0, scopedLiterals, "literal effect operands with bit 0x8000" );
			CollectionAssert.AreEqual( new[] { "jungle/levels/jungle/rides/porkpie" }, variableEffects.ToArray(), "scripts whose EVENT/ADDOBJ effect is a variable" );
			Assert.AreEqual( 0, fixedWithEffects.Count, string.Join( "; ", fixedWithEffects ) );
			CollectionAssert.AreEqual( new[] { "fantasy/levels/fantasy/rides/bugstv" }, type2Negative.ToArray(), "ADDOBJ/EVENT type 2 with a negative key" );
			Assert.AreEqual( 20, teleportRides.Count, "rides with UsageInfo.RequiresTeleport" );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void SavedJunglePoolsHoldTheScapeWaterfall()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !System.IO.Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES." );
		foreach ( var path in System.IO.Directory.EnumerateFiles( folder!, "*", System.IO.SearchOption.AllDirectories ).Where( file => System.IO.Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" ).OrderBy( file => file, StringComparer.Ordinal ) )
		{
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var layout = SavePayloadLayout.Parse( payload );
			var particles = SaveParticlesBlock.Parse( payload, layout );
			var effects = particles.ActiveEmitterOrder.Select( slot => particles.Emitters[slot].EffectId ).ToList();
			// scape.omp starts Jungle's waterfall (effect 20) at every level load, so every saved jungle pool holds exactly one such emitter and no other level's pool holds one
			var theme = System.IO.Path.GetFileName( System.IO.Path.GetDirectoryName( path ) );
			Assert.AreEqual( theme == "jungle" ? 1 : 0, effects.Count( effect => effect == 20 ), $"{theme}/{System.IO.Path.GetFileName( path )}" );
		}
	}
}
