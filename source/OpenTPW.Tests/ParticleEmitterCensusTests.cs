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
	public void ShippedParticleRequestsAreInventoried()
	{
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game." );
		try
		{
			var requests = new SortedDictionary<string, int>();
			var effects = new SortedDictionary<string, SortedSet<int>>();
			var typeEffects = new List<string>();
			foreach ( var theme in ObjectCatalog.Themes )
			{
				foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
				{
					if ( entry.Settings.GetBool( "UsageInfo.RequiresTeleport" ) )
						typeEffects.Add( $"TELEPORT {theme}/{entry.ArchivePath}" );
					if ( entry.IsFixedItem || entry.CreateParticleEffect != 0 || entry.DestroyParticleEffect != 0 )
						typeEffects.Add( $"{theme}/{entry.ArchivePath} fixed={entry.IsFixedItem} cat={entry.Category} create={entry.CreateParticleEffect} destroy={entry.DestroyParticleEffect} repair={entry.RepairParticleEffect} upgrade={entry.UpgradeParticleEffect}" );
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
							var id = $"{theme} {entry.ArchivePath}";
							if ( !effects.TryGetValue( id + " " + name, out var set ) )
								effects[id + " " + name] = set = new SortedSet<int>();
							if ( effect.Kind == RideScriptOperandKind.Literal )
								set.Add( (short)effect.Value );
						}
					}
				}
			}
			foreach ( var pair in requests )
				Console.WriteLine( $"{pair.Key}: {pair.Value}" );
			foreach ( var pair in effects )
				Console.WriteLine( $"  {pair.Key}: effects {string.Join( ",", pair.Value )}" );
			foreach ( var line in typeEffects )
				Console.WriteLine( line );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void SavedPoolsAreInventoried()
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
			var list = particles.ActiveEmitterOrder.Select( slot => particles.Emitters[slot] ).Select( emitter => $"{emitter.EffectId}" ).ToList();
			Console.WriteLine( $"{System.IO.Path.GetFileName( System.IO.Path.GetDirectoryName( path ) )}/{System.IO.Path.GetFileName( path )}: {list.Count} active emitters: {string.Join( ",", list )}" );
		}
	}
}
