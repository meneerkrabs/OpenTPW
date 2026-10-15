using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Buffers.Binary;
using System.Text;

namespace OpenTPW.Tests;

/// <summary>
/// Review round 17 probe: post-open decode failures from synthetic TGQ bytes (no original files) reach
/// <see cref="IntroSequence"/> as recoverable exception types through the real <see cref="MoviePlayback"/> path.
/// </summary>
[TestClass]
public class Round17IntroDecodeProbeTests
{
	private static byte[] Chunk( string fourCC, byte[] payload )
	{
		var chunk = new byte[8 + payload.Length];
		Encoding.ASCII.GetBytes( fourCC ).CopyTo( chunk, 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk.AsSpan( 4 ), (uint)chunk.Length );
		payload.CopyTo( chunk, 8 );
		return chunk;
	}

	/// <summary>A header-valid 16x16 pIQT whose bitstream words are <paramref name="words"/>.</summary>
	private static byte[] Frame( params uint[] words )
	{
		var payload = new byte[8 + 4 * words.Length];
		BinaryPrimitives.WriteUInt16LittleEndian( payload, 16 );
		BinaryPrimitives.WriteUInt16LittleEndian( payload.AsSpan( 2 ), 16 );
		payload[4] = 99;
		for ( var i = 0; i < words.Length; i++ )
			BinaryPrimitives.WriteUInt32LittleEndian( payload.AsSpan( 8 + 4 * i ), words[i] );
		return payload;
	}

	private static TgqMovieFile Movie( byte[] frame )
	{
		var header = new byte[] { (byte)'P', (byte)'T', 0, 0, 0x1b, 1, 30, 0x85, 4, 0, 0, 0, 0, 0x82, 1, 2, 0x83, 1, 7, 0xff };
		var parts = new List<byte[]> { Chunk( "SCHl", header ), Chunk( "SCCl", new byte[4] ), Chunk( "pIQT", frame ), Chunk( "SCEl", Array.Empty<byte>() ) };
		return new TgqMovieFile( new MemoryStream( parts.SelectMany( part => part ).ToArray() ) );
	}

	private sealed class PlaybackMovie : IIntroMovie
	{
		private readonly MoviePlayback playback;
		public int Disposals;
		public PlaybackMovie( TgqMovieFile movie ) => playback = new MoviePlayback( movie, null );
		public bool IsFinished => playback.IsFinished;
		public void Skip() => playback.Skip();
		public void Update() => playback.Update( 1d / 30 );
		public void Render() { }
		public void Dispose() { Disposals++; playback.Dispose(); }
	}

	public static IEnumerable<object[]> CorruptFrames => new[]
	{
		new object[] { "empty bitstream", Frame() },
		new object[] { "all-ones bitstream", Frame( 0xFFFFFFFF ) },
		new object[] { "all-ones long bitstream", Frame( Enumerable.Repeat( 0xFFFFFFFFu, 64 ).ToArray() ) },
		new object[] { "zero bitstream", Frame( 0 ) },
	};

	[TestMethod]
	[DynamicData( nameof( CorruptFrames ) )]
	public void CorruptFrameOpensThenFailsOnlyWithARecoverableType( string label, byte[] frame )
	{
		var movie = Movie( frame );
		Exception? thrown = null;
		try { movie.DecodeVideoFrame( 0 ); }
		catch ( Exception exception ) { thrown = exception; }
		Console.WriteLine( $"{label}: {thrown?.GetType().Name ?? "decoded"} {thrown?.Message}" );
		if ( thrown != null )
			Assert.IsTrue( thrown is InvalidDataException or IOException or NotSupportedException or ArgumentException, $"{label}: {thrown}" );

		var broken = new PlaybackMovie( Movie( frame ) );
		var following = new PlaybackMovie( Round17GoodMovie() );
		var queue = new Queue<IIntroMovie>( new IIntroMovie[] { broken, following } );
		using var intro = new IntroSequence( new[] { "broken", "following" }, _ => queue.Dequeue(), () => false, _ => { } );
		for ( var i = 0; i < 200 && !intro.IsCompleted; i++ )
			intro.Update();
		Assert.IsTrue( intro.IsCompleted, label );
		Assert.AreEqual( 1, broken.Disposals, label );
		Assert.AreEqual( 1, following.Disposals, label );
	}

	private static TgqMovieFile Round17GoodMovie() => MoviePlaybackTests.Movie( 2, 0 );
}
