using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The level's placed ambient sounds: the <c>scape.omp</c> reader, the looping one-shot, and the distance law a placed
/// voice is turned down by. Synthetic tests need no game data; the corpus tests skip without <c>OPENTPW_GAME_PATH</c>.
/// </summary>
[TestClass]
public class PlacedAmbientTests
{
	private const uint Tag = 0x5F4A424F; // "OBJ_"

	[TestInitialize]
	public void Setup() => Log ??= new();

	/// <summary>A file of records <paramref name="size"/> bytes each, the given words written from the start of each record.</summary>
	private static byte[] Build( int[][] records, int size = 60, int tail = 4 )
	{
		var data = new byte[12 + records.Length * size + tail];
		BinaryPrimitives.WriteUInt32LittleEndian( data, Tag );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 4 ), records.Length );
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 8 ), size );
		for ( var record = 0; record < records.Length; record++ )
		{
			for ( var word = 0; word < records[record].Length && word * 4 < size; word++ )
				BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 12 + record * size + word * 4 ), records[record][word] );
		}
		return data;
	}

	[TestMethod]
	public void ShortRecordsDoNotReadTheirNeighboursOrTheIncludeTail()
	{
		var bytes = Build( new[] { new[] { 1, 20 }, new[] { 2, 30 } }, size: 8 );
		BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( bytes.Length - 4 ), 0x4C434E49 );
		var file = new PlacedObjectsFile( bytes );
		Assert.AreEqual( 2, file.Records.Count );
		CollectionAssert.AreEqual( new[] { 1, 20 }.Concat( new int[13] ).ToArray(), file.Records[0].Words );
		CollectionAssert.AreEqual( new[] { 2, 30 }.Concat( new int[13] ).ToArray(), file.Records[1].Words );
		Assert.AreEqual( 1, new PlacedObjectsFile( bytes[..^(4 + 1)] ).Records.Count, "a partial final record is not read" );
	}

	[TestMethod]
	public void RecordSizesMustContainWholeWordsAndExtendedRecordsMustBeComplete()
	{
		var malformed = Build( new[] { new[] { 1 } } );
		BinaryPrimitives.WriteInt32LittleEndian( malformed.AsSpan( 8 ), 7 );
		Assert.ThrowsException<InvalidDataException>( () => new PlacedObjectsFile( malformed ) );
		var extended = Build( new[] { new[] { 1 }, new[] { 2 } }, size: 64, tail: 0 );
		Assert.AreEqual( 1, new PlacedObjectsFile( extended[..^1] ).Records.Count, "the full stride is needed, including the skipped words" );
	}

	[TestMethod]
	public void ARangedSoundRecordIsReadWithItsCategoryEffectPlaceAndRange()
	{
		// word 1 the category (0 global), word 2 the effect, words 3-5 the place, word 6 the range (x2/1024 in park units)
		var file = new PlacedObjectsFile( Build( new[] { new[] { 1, 0, 8, 543500, 0, 525500, 50000 } } ) );

		Assert.AreEqual( 1, file.Records.Count );
		var record = file.Records[0];
		Assert.AreEqual( PlacedObjectsFile.RangedSoundType, record.Type );
		Assert.AreEqual( 0, record.Category, "the global cat_ambient" );
		Assert.AreEqual( 8, record.Sound );
		Assert.AreEqual( (530, 0, 513), record.SoundPlace, "word / 1024, toward nought" );
		Assert.AreEqual( 97, record.Range, "50000 x 2 / 1024" );
	}

	[TestMethod]
	public void ARectangleRecordKeepsItsRectangleAndIsNotAPlacedSound()
	{
		var file = new PlacedObjectsFile( Build( new[] { new[] { 3, 0, 0, 1024 * 10, 0, 1024 * 20, 0, 1024 * 4, 1024 * 6 } } ) );

		Assert.AreEqual( PlacedObjectsFile.RectangleSoundType, file.Records[0].Type );
		Assert.AreEqual( ((10, 20), (14, 26)), file.Records[0].Rectangle );
	}

	[TestMethod]
	public void ARangeOfNoughtLeavesTheSoundsOwnRangeToTheCatalogue()
	{
		var file = new PlacedObjectsFile( Build( new[] { new[] { 1, 1, 143, 1024, 0, 1024, 0 } } ) );

		Assert.AreEqual( 0, file.Records[0].Range );
	}

	[TestMethod]
	public void ALoopedVoiceLivesOnUntilItIsStoppedAndAPlainOneShotEnds()
	{
		var mixer = new AudioMixer( null );
		var audio = new Mp2Audio( AudioMixer.OutputRate, 1, new short[] { 1000, 1000, 1000, 1000 }, 1, 0 );

		var plain = mixer.Play( audio, AudioChannel.Effects, 1f );
		var looped = mixer.Play( audio, AudioChannel.Effects, 1f );
		looped.Loops = true;

		mixer.Mix( 2000 );

		Assert.IsTrue( plain.IsFinished, "a one-shot ends when its last frame is mixed" );
		Assert.IsFalse( looped.IsFinished, "a looped voice keeps wrapping its sample" );
		looped.Stop();
		Assert.IsTrue( looped.IsFinished );
	}

	[TestMethod]
	public void APlacedVoiceIsSilentPastItsRangeAndFullNearTheListener()
	{
		// The distance law (the fork's QMixer law with the game's 2.0 minimum and the voice's own range); near is full gain.
		Assert.AreEqual( 1f, AudioListener.RangeGain( 97, 1 ), "inside the minimum distance" );
		Assert.AreEqual( 0f, AudioListener.RangeGain( 97, 97 ), "at the range" );
		var middle = AudioListener.RangeGain( 97, 50 );
		Assert.IsTrue( middle > 0 && middle < 1, $"between: {middle}" );
		Assert.AreEqual( MathF.Pow( (97f - 50) / (97f - 2), 0.8f ), middle, 1e-5f );
		Assert.AreEqual( 1f, AudioListener.RangeGain( 0, 500 ), "no range: always full" );
	}

	[TestMethod]
	public void ASourceToTheListenersRightPansRightAndOutOfRangeIsSilent()
	{
		AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		try
		{
			// Forward is +z; the listener's right is -x in this frame's convention (the same right the placement uses).
			var (_, panLeft) = AudioListener.Placement( new Vector3( -20, 0, 0 ), 100 );
			var (_, panRight) = AudioListener.Placement( new Vector3( 20, 0, 0 ), 100 );
			Assert.IsTrue( Math.Sign( panLeft ) != Math.Sign( panRight ), $"opposite sides: {panLeft} {panRight}" );
			var (gain, _) = AudioListener.Placement( new Vector3( 500, 0, 0 ), 100 );
			Assert.AreEqual( 0f, gain, "past the range" );
		}
		finally
		{
			AudioListener.Update( Vector3.Zero, new Vector3( 0, 0, 1 ) );
		}
	}

	[TestMethod]
	public void AnEventFlaggedZeroEightLoopsAndAPlainOneShotDoesNot()
	{
		// flags 0x8 with no 0x4 is a one-shot (0x6 and 0x606 are sentences); the loop is the 0x8 bit alone.
		Assert.AreEqual( SoundEventPlayer.OneShot, new SoundEvent( 8, 4000, 0x8, 0, Array.Empty<SoundEntry>() ).Player );
		Assert.AreEqual( SoundEventPlayer.OneShot, new SoundEvent( 0x20, 4300, 0x0, 0, Array.Empty<SoundEntry>() ).Player );
		Assert.AreEqual( SoundEventPlayer.BranchingSentence, new SoundEvent( 0x21, 4300, 0x606, 8, Array.Empty<SoundEntry>() ).Player );
	}

	// ---- original data (OPENTPW_GAME_PATH) ----

	private static string? DataDirectory()
	{
		var path = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( path ) || !Directory.Exists( path ) )
			return null;
		return Directory.EnumerateDirectories( path ).FirstOrDefault( directory => string.Equals( Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? path;
	}

	[TestMethod]
	public void EveryShippedScapeFileParsesToItsClaimedRecordsAndEndsInItsIncludeChunk()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		// The record counts and the type mix of the four shipped files (read by hand from each file).
		var expected = new (string Level, int Records, int Ranged, int Rectangle, int Particle)[]
		{
			("jungle", 7, 4, 2, 1),
			("fantasy", 4, 3, 1, 0),
			("hallow", 4, 3, 1, 0),
			("space", 4, 3, 1, 0),
		};
		foreach ( var (level, records, ranged, rectangle, particle) in expected )
		{
			var bytes = fileSystem.ReadAllBytes( $"/levels/{level}/scape.omp" );
			var file = new PlacedObjectsFile( bytes );
			Assert.AreEqual( records, file.Records.Count, level );
			Assert.AreEqual( ranged, file.Records.Count( record => record.Type == PlacedObjectsFile.RangedSoundType ), $"{level} type 1" );
			Assert.AreEqual( rectangle, file.Records.Count( record => record.Type == PlacedObjectsFile.RectangleSoundType ), $"{level} type 3" );
			Assert.AreEqual( particle, file.Records.Count( record => record.Type == PlacedObjectsFile.ParticleType ), $"{level} type 2" );
			// The file ends in the INCL chunk after the last record, which the loader never reads.
			Assert.AreEqual( "INCL", System.Text.Encoding.ASCII.GetString( bytes, 12 + records * 60, 4 ), $"{level} tail" );
		}
	}

	[TestMethod]
	public void EveryPlacedSoundOfTheShippedParksResolvesInItsCategory()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		var global = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_ambient" );
		Assert.IsNotNull( global );
		foreach ( var level in new[] { "jungle", "fantasy", "hallow", "space" } )
		{
			var levelAmbient = SoundCategory.TryLoad( fileSystem, $"/levels/{level}/Sound", "cat_ambient" );
			Assert.IsNotNull( levelAmbient, level );
			var file = new PlacedObjectsFile( fileSystem.ReadAllBytes( $"/levels/{level}/scape.omp" ) );
			foreach ( var record in file.Records.Where( record => record.Type == PlacedObjectsFile.RangedSoundType ) )
			{
				var category = record.Category == 0 ? global : levelAmbient;
				Assert.IsNotNull( category!.Catalog.Find( (uint)record.Sound ), $"{level}: effect {record.Sound} of category {record.Category}" );
			}
		}
	}

	[TestMethod]
	public void TheJunglesWaterfallLoopsAndIsOneOfItsPlacedAmbients()
	{
		var data = DataDirectory();
		if ( data == null )
			Assert.Inconclusive( "OPENTPW_GAME_PATH is not set." );
		var fileSystem = new BaseFileSystem( data );
		var global = SoundCategory.TryLoad( fileSystem, "/global/sound", "cat_ambient" );
		Assert.IsNotNull( global );
		var file = new PlacedObjectsFile( fileSystem.ReadAllBytes( "/levels/jungle/scape.omp" ) );
		var sounds = file.Records.Where( record => record.Type == PlacedObjectsFile.RangedSoundType && record.Category == 0 ).ToArray();
		Assert.AreEqual( 2, sounds.Length, "the waterfall and the river" );
		foreach ( var record in sounds )
		{
			var evt = global!.Catalog.Find( (uint)record.Sound );
			Assert.IsNotNull( evt );
			Assert.AreEqual( SoundEventPlayer.OneShot, evt.Player, $"effect {record.Sound}" );
			Assert.AreNotEqual( 0, evt.Flags & 0x8, $"effect {record.Sound} loops" );
		}
	}
}
