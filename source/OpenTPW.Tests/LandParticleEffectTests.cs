using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// PTCL-908: the land's own particle effects, the type 2 records of a level's <c>scape.omp</c> (tp.exe 0x550e00 calling 0x521E60 with the record's effect and place). Jungle's one record is
/// the waterfall (effect 20) at (545100, 5000, 538700); a pool started with it keeps the emitter for good and draws particles.
/// </summary>
[TestClass]
public class LandParticleEffectTests
{
	private static string? DataDirectory()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		return Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
	}

	private static ParticleTemplateFile? ShippedTemplates()
	{
		if ( DataDirectory() is not { } data )
			return null;
		var particle = Directory.EnumerateDirectories( data ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "particle", StringComparison.OrdinalIgnoreCase ) );
		var plb = particle == null ? null : Directory.EnumerateFiles( particle ).FirstOrDefault( path => string.Equals( Path.GetFileName( path ), "Tp2.plb", StringComparison.OrdinalIgnoreCase ) );
		if ( plb == null )
			return null;
		using var stream = File.OpenRead( plb );
		return new ParticleTemplateFile( stream );
	}

	private static PlacedObjectsFile.Record Record( params int[] words ) => new( words.Concat( new int[PlacedObjectsFile.WordsKept] ).Take( PlacedObjectsFile.WordsKept ).ToArray() );

	[TestMethod]
	public void OnlyTheType2RecordsStartAndTheirWordsAreTheEffectAndThePlace()
	{
		var record = Record( 2, 20, 0, 545100, 5000, 538700 );
		Assert.AreEqual( 20, record.Effect );
		Assert.AreEqual( (545100, 5000, 538700), record.Place, "words 3, 4 and 5 go to the pool as they are (0x550e00 passes local_3c[3], [4], [5])" );
	}

	[TestMethod]
	public void ARefusedEffectOrASoundRecordStartsNothingAndTheLoaderGoesOn()
	{
		var file = ShippedTemplates();
		if ( file == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Tp2.plb." );
		var system = new ParticleSystem( file!.Templates, 1024, effectors: file.EffectorTemplates );
		var started = ParticleRendererModule.StartPlacedIn( system, new[]
		{
			Record( 1, 1, 181, 21700, 0, 40700, 10000 ),
			Record( 3, 0, 2, 362100, 0, 32100, 80000, 100000, -100000 ),
			Record( 2, 9999, 0, 1, 2, 3 ),
			Record( 2, -1, 0, 1, 2, 3 ),
			Record( 2, 20, 0, 545100, 5000, 538700 ),
		} );
		Assert.AreEqual( 1, started, "the sounds and the two out-of-range effects start nothing (the pool refuses ids outside 0..0x68), the waterfall starts" );
		Assert.AreEqual( 1, system.LiveEmitters );
	}

	[TestMethod]
	public void JunglesScapeRecordStartsTheWaterfallThatKeepsRunningAndDrawsParticles()
	{
		var file = ShippedTemplates();
		var data = DataDirectory();
		if ( file == null || data == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the shipped Tp2.plb and scape.omp." );
		var scape = new PlacedObjectsFile( new BaseFileSystem( data! ).ReadAllBytes( "/levels/jungle/scape.omp" ) );
		var land = scape.Records.Where( record => record.Type == PlacedObjectsFile.ParticleType ).ToArray();
		Assert.AreEqual( 1, land.Length );
		Assert.AreEqual( (20, (545100, 5000, 538700)), (land[0].Effect, land[0].Place), "effect 20, P_EFFECT_WaterFall (Data/Particle/par_lib.h)" );
		var system = new ParticleSystem( file!.Templates, 1024, effectors: file.EffectorTemplates );
		Assert.AreEqual( 1, ParticleRendererModule.StartPlacedIn( system, land ) );
		var sprites = new List<ParticleSprite>();
		var drawn = 0;
		for ( var tick = 0; tick < 400; tick++ )
		{
			system.Tick();
			sprites.Clear();
			system.Snapshot( sprites );
			drawn = Math.Max( drawn, sprites.Count );
		}
		Assert.AreEqual( 1, system.LiveEmitters, "the waterfall emitter is immortal: it is still there after about 12 seconds, as every saved jungle pool holds it" );
		Assert.IsTrue( drawn > 0, "and it draws particles" );
	}

	[TestMethod]
	public void TheOtherLevelsHaveNoLandEffects()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH." );
		var fileSystem = new BaseFileSystem( data! );
		foreach ( var level in new[] { "fantasy", "hallow", "space" } )
			Assert.AreEqual( 0, new PlacedObjectsFile( fileSystem.ReadAllBytes( $"/levels/{level}/scape.omp" ) ).Records.Count( record => record.Type == PlacedObjectsFile.ParticleType ), level );
	}
}
