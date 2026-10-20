using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// <c>OPENTPW_TPWS_SAVES</c> (see <see cref="OriginalSaveCorpusTests"/>): the Scripts and Particles blocks of every original save are consumed exactly by their typed readers, and
/// with <c>OPENTPW_GAME_PATH</c> the saved template tables equal <c>Data/Particle/Tp2.plb</c> and the pool state restores into the runtime particle system.
/// </summary>
[TestClass]
public class OriginalSaveEngineBlocksCorpusTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };

	[TestMethod]
	public void ScriptsAndParticlesAreConsumedExactlyInEverySave()
	{
		var folder = Environment.GetEnvironmentVariable( "OPENTPW_TPWS_SAVES" );
		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
			Assert.Inconclusive( "Set OPENTPW_TPWS_SAVES to a folder with original .TPWS/.INTS saves in per-theme subfolders." );
		var files = Directory.EnumerateFiles( folder!, "*", SearchOption.AllDirectories )
			.Where( file => Path.GetExtension( file ).ToUpperInvariant() is ".TPWS" or ".INTS" && Themes.Contains( Path.GetFileName( Path.GetDirectoryName( file ) )!.ToLowerInvariant() ) )
			.OrderBy( file => file, StringComparer.Ordinal ).ToList();
		if ( files.Count == 0 )
			Assert.Inconclusive( "The OPENTPW_TPWS_SAVES folder has no .TPWS/.INTS files in a theme subfolder." );
		// the documented corpus is 11 saves (a .TPWS or autosave and a restart.INTS in each of the four themes)
		Assert.IsTrue( files.Count >= 11, $"The corpus holds {files.Count} saves, not the 11 of docs/TPWS-PAYLOAD.md." );
		var shipped = ShippedFile();
		var scripts = 0;
		var checkedAgainstShippedFile = 0;
		var emitters = 0;
		foreach ( var path in files )
		{
			var name = $"{Path.GetFileName( Path.GetDirectoryName( path ) )}/{Path.GetFileName( path )}";
			using var reader = new SaveReader( path );
			var payload = reader.ReadFile();
			var layout = SavePayloadLayout.Parse( payload );

			// Scripts: loader 0x475730 consumes the block to its delimiter
			var block = SaveScriptsBlock.Parse( payload, layout );
			Assert.AreEqual( layout[SaveBlock.Scripts].Length, block.Length, name );
			Assert.AreEqual( 100, block.SlotCount, $"{name}: the script table is 100 slots (0x4752B3)" );
			Assert.AreEqual( block.SlotWords.Skip( 1 ).Count( word => word != 0 ), block.Records.Count, name );
			foreach ( var record in block.Records )
			{
				Assert.AreEqual( (uint)record.Slot, record.StoredSlot, $"{name}: slot {record.Slot} stores its own number" );
				Assert.AreEqual( SaveScriptRecord.ProgramTable, record.Program, name );
				Assert.AreEqual( 0x14u, record.StackCapacity, name );
				Assert.IsTrue( record.EntryPoint < 0x2000 && record.ProgramCounter < 0x2000 && record.LastCounter < 0x2000, $"{name}: slot {record.Slot} counters are program indexes" );
				Assert.IsTrue( record.State is >= 1 and <= 4, $"{name}: state {record.State}" );
				Assert.IsTrue( record.Stack.All( word => word == 0 ) && record.Depth == 0, $"{name}: slot {record.Slot} runs no nested block between ticks" );
				Assert.IsTrue( float.IsFinite( record.Position.X ) && float.IsFinite( record.Position.Z ), name );
				scripts++;
			}

			// Particles: loader 0x51F7A0
			var particles = SaveParticlesBlock.Parse( payload, layout );
			Assert.AreEqual( layout[SaveBlock.Particles].Length, particles.Length, name );
			Assert.AreEqual( 76252, particles.Length, $"{name}: the game's block is 8 + 4 + 0x9E68 + 4 + 0x8B60 + 4 bytes" );
			Assert.IsTrue( particles.On, name );
			Assert.AreEqual( 0x800u, particles.Globals.PoolCapacity, name );
			Assert.AreEqual( 0, particles.ParticlePool.Length, $"{name}: the save call passes a pool length of 0 (0x41661B)" );
			Assert.AreEqual( 120, particles.ActiveEmitterOrder.Count + particles.FreeEmitterOrder.Count, name );
			Assert.AreEqual( 20, particles.ActiveEffectorOrder.Count + particles.FreeEffectorOrder.Count, name );
			Assert.IsTrue( particles.Globals.Density is 500 or 1000 or 1500, $"{name}: density {particles.Globals.Density} is a detail file's PARTICLEDENSITY" );
			foreach ( var slot in particles.ActiveEmitterOrder )
			{
				var emitter = particles.Emitters[slot];
				Assert.IsTrue( emitter.EffectId < SaveParticlesBlock.EmitterTemplates, $"{name}: emitter {slot}" );
				Assert.AreEqual( TemplateName( particles.EmitterTemplateBytes[emitter.EffectId] ), emitter.Name, $"{name}: emitter {slot} is a copy of template {emitter.EffectId}" );
				emitters++;
			}

			Assert.AreEqual( 0, particles.ListProblems.Count, $"{name}: {string.Join( "; ", particles.ListProblems )}" );
			if ( shipped == null )
				continue;
			checkedAgainstShippedFile++;
			// the in-memory template table is the file's, but for the scratch templates 101 to 104 the game rewrites at run time (jungle changes 101 and 102)
			for ( var id = 0; id <= 100; id++ )
				CollectionAssert.AreEqual( shipped.Templates[id].Raw.ToArray(), particles.EmitterTemplateBytes[id], $"{name}: emitter template {id}" );
			for ( var id = 0; id < SaveParticlesBlock.EffectorTemplates; id++ )
				CollectionAssert.AreEqual( shipped.EffectorTemplates[id].Raw.ToArray(), particles.EffectorTemplateBytes[id], $"{name}: effector template {id}" );
			// the documented losses of the conversion (SaveParticleState) are harmless here: the fields Restore refills from the runtime's templates equal the saved ones, and no emitter uses a scratch template
			foreach ( var slot in particles.ActiveEmitterOrder )
			{
				var emitter = particles.Emitters[slot];
				var template = shipped.Templates[emitter.EffectId];
				Assert.IsTrue( emitter.EffectId <= 100, $"{name}: emitter {slot} uses scratch template {emitter.EffectId}" );
				Assert.AreEqual( (template.Hold, template.Group, template.ScreenSpace, template.RingMode, template.Immortal), (emitter.Hold, emitter.Group, emitter.ScreenSpace, emitter.RingMode, emitter.Immortal), $"{name}: emitter {slot} template-derived fields" );
			}
			var state = particles.ToSystemState( shipped.Templates )!;
			Assert.IsNull( ParticleSystem.ValidateState( state ), name );
			var system = new ParticleSystem( shipped.Templates, effectors: shipped.EffectorTemplates );
			system.Restore( state );
			var captured = system.Capture();
			Assert.IsNotNull( captured, $"{name}: an initialized empty pool still saves its configuration and generator" );
			if ( captured != null )
			{
				// every carried field comes back; the only change is the post-load pass (0x5200B0): screen-space emitters and killed-on-load effectors get life -2
				var expected = new ParticleSystemState
				{
					Configuration = state.Configuration,
					Random = state.Random, TickCounter = state.TickCounter, GenerationCounter = state.GenerationCounter, EffectorGenerationCounter = state.EffectorGenerationCounter,
					Emitters = state.Emitters.Select( emitter => shipped.Templates[emitter.EffectId].ScreenSpace ? emitter with { Life = -2 } : emitter ).ToList(),
					FreeEmitters = state.FreeEmitters,
					Effectors = state.Effectors.Select( effector => shipped.EffectorTemplates[effector.EffectorId].KilledOnLoad ? effector with { Life = -2 } : effector ).ToList(),
					FreeEffectors = state.FreeEffectors
				};
				Assert.AreEqual( System.Text.Json.JsonSerializer.Serialize( expected ), System.Text.Json.JsonSerializer.Serialize( captured ), $"{name}: the captured pool equals the saved one" );
			}
		}
		Assert.IsTrue( scripts > 0 && emitters > 0, "the corpus holds sprite scripts and live emitters" );
		if ( checkedAgainstShippedFile == 0 )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to a folder with Data/Particle/Tp2.plb: the template and restore checks did not run." );
	}

	private static string TemplateName( byte[] template )
	{
		var span = template.AsSpan( 0x118, 16 );
		var end = span.IndexOf( (byte)0 );
		return System.Text.Encoding.ASCII.GetString( end < 0 ? span : span[..end] );
	}

	private static ParticleTemplateFile? ShippedFile()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !Directory.Exists( gamePath ) )
			return null;
		var dataPath = Directory.EnumerateDirectories( gamePath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath;
		var particle = Directory.EnumerateDirectories( dataPath ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "particle", StringComparison.OrdinalIgnoreCase ) );
		var plb = particle == null ? null : Directory.EnumerateFiles( particle ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Tp2.plb", StringComparison.OrdinalIgnoreCase ) );
		if ( plb == null )
			return null;
		using var stream = File.OpenRead( plb );
		return new ParticleTemplateFile( stream );
	}
}
