using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>How the PC player presents movies: the volume curve and the 16-bit display packing (tp.exe, docs/TGQ-MOVIES.md).</summary>
[TestClass]
public class MoviePresentationTests
{
	[DataTestMethod]
	[DataRow( 100, 1023, 0 )]
	[DataRow( 75, 767, -2503 )]
	[DataRow( 50, 511, -5005 )]
	[DataRow( 1, 10, -9903 )]
	[DataRow( 0, 0, -10000 )]
	public void VolumePercentageBecomesDeviceVolumeThenDirectSoundAttenuation( int percent, int device, int hundredthsDb )
	{
		Assert.AreEqual( device, MovieVolumeCurve.DeviceVolume( percent, true ) );
		Assert.AreEqual( hundredthsDb, MovieVolumeCurve.HundredthsDb( device ) );
	}

	[TestMethod]
	public void SwitchOffPlaysTheDeviceMinimumAndSilence()
	{
		Assert.AreEqual( 0, MovieVolumeCurve.DeviceVolume( 100, false ) );
		Assert.AreEqual( 0f, MovieVolumeCurve.Gain( 100, false ) );
		Assert.AreEqual( 0f, MovieVolumeCurve.Gain( 0, true ), "−10000 is DirectSound's silence" );
	}

	[TestMethod]
	public void FullVolumeIsUnattenuatedAndTheCurveFollowsDecibels()
	{
		Assert.AreEqual( 1f, MovieVolumeCurve.Gain( 100, true ) );
		Assert.AreEqual( MathF.Pow( 10f, -2503 / 2000f ), MovieVolumeCurve.Gain( 75, true ), 1e-6f );
		Assert.AreEqual( 0.0562f, MovieVolumeCurve.Gain( 75, true ), 1e-3f, "75 % is about −25 dB" );
		var previous = -1f;
		for ( var percent = 0; percent <= 100; percent++ )
		{
			var gain = MovieVolumeCurve.Gain( percent, true );
			Assert.IsTrue( gain >= previous, "the curve never falls as the slider rises" );
			previous = gain;
		}
	}

	[TestMethod]
	public void TheOptionsMovieGainUsesTheCurve()
	{
		var options = new GameOptions { MovieVolume = 75, MovieOn = true };
		Assert.AreEqual( MovieVolumeCurve.Gain( 75, true ), options.MovieGain );
		options.MovieOn = false;
		Assert.AreEqual( 0f, options.MovieGain );
	}

	[DataTestMethod]
	[DataRow( 640, 480, 0, 64, 640, 352 )]
	[DataRow( 160, 120, 0, 16, 160, 88 )]
	[DataRow( 400, 300, 0, 40, 400, 220 )]
	[DataRow( 1024, 768, 0, 102, 1024, 563 )]
	[DataRow( 1280, 1024, 0, 136, 1280, 750 )]
	[DataRow( 1600, 1200, 0, 160, 1600, 880 )]
	public void OriginalBoxIsTheScreenWidthBy352Over480AtTheTopSixtyFourOver480( int screenWidth, int screenHeight, int x, int y, int width, int height )
	{
		Assert.AreEqual( (x, y, width, height), MoviePresenter.OriginalBox( screenWidth, screenHeight ) );
	}

	[DataTestMethod]
	[DataRow( 160, 120 )]
	[DataRow( 320, 240 )]
	[DataRow( 400, 300 )]
	[DataRow( 512, 384 )]
	[DataRow( 800, 600 )]
	[DataRow( 1024, 768 )]
	[DataRow( 1600, 1200 )]
	public void LetterboxedPictureEqualsTheOriginalBoxOnEveryFourByThreeMode( int screenWidth, int screenHeight )
	{
		// The original only ran fixed 4:3 modes (and 1280 x 1024), where stretching to the screen width keeps the 640:352 aspect.
		var original = MoviePresenter.OriginalBox( screenWidth, screenHeight );
		var fitted = MoviePresenter.Fit( 320, 352, screenWidth, screenHeight, MoviePresenter.OriginalDisplayAspect );
		Assert.AreEqual( original.X, fitted.X );
		Assert.IsTrue( Math.Abs( original.Y - fitted.Y ) <= 1, $"y {original.Y} vs {fitted.Y}" );
		Assert.AreEqual( original.Width, fitted.Width );
		Assert.IsTrue( Math.Abs( original.Height - fitted.Height ) <= 1, $"height {original.Height} vs {fitted.Height}" );
	}

	[TestMethod]
	public void PackingRoundsEvenColumnsAndTruncatesOddColumns()
	{
		// 5 bits: even columns (v + 4) >> 3 clamped to 31, odd columns v >> 3, expanded by bit replication.
		Assert.AreEqual( 0, TqiColour.Pack( 3, 5, 0 ) );
		Assert.AreEqual( 8, TqiColour.Pack( 4, 5, 0 ), "4 rounds up to field value 1" );
		Assert.AreEqual( 0, TqiColour.Pack( 4, 5, 1 ), "the odd column truncates 4 to 0" );
		Assert.AreEqual( 255, TqiColour.Pack( 255, 5, 0 ), "the rounded 32 clamps to the field maximum 31" );
		Assert.AreEqual( 255, TqiColour.Pack( 255, 5, 1 ) );
		// 6 bits: even (v + 2) >> 2, odd v >> 2.
		Assert.AreEqual( 4, TqiColour.Pack( 2, 6, 0 ) );
		Assert.AreEqual( 0, TqiColour.Pack( 2, 6, 1 ) );
		Assert.AreEqual( 255, TqiColour.Pack( 254, 6, 0 ) );
		Assert.AreEqual( 247, TqiColour.Pack( 241, 5, 1 ), "the brightest neutral red 241 packs to field value 30 of 31" );
		Assert.AreEqual( 247, TqiColour.Pack( 241, 5, 0 ) );
		Assert.AreEqual( 8, TqiColour.Pack( 4, 5, 0 ) - TqiColour.Pack( 4, 5, 1 ), "the two columns differ only where the half-step matters" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => TqiColour.Pack( 0, 0, 0 ) );
	}

	[TestMethod]
	public void TheOriginalPacksOnlyOnAnOption0Display()
	{
		Assert.AreEqual( TqiDisplayFormat.Rgb565, MoviePresenter.OriginalDisplayFormat( 0 ), "the original's presets run a 16-bit display" );
		Assert.IsNull( MoviePresenter.OriginalDisplayFormat( 1 ), "a 32-bit display keeps the colour tables' 8-bit values" );
	}

	[TestMethod]
	public void PackedChannelsStayWithinOneFieldStepOfTheTableValue()
	{
		for ( var value = 0; value < 256; value++ )
		{
			foreach ( var (bits, column) in new[] { (5, 0), (5, 1), (6, 0), (6, 1) } )
			{
				var packed = TqiColour.Pack( (byte)value, bits, column );
				Assert.IsTrue( Math.Abs( packed - value ) <= 2 << (8 - bits), $"{value} at {bits} bits column {column} gave {packed}" );
			}
		}
	}

	[TestMethod]
	public void DisplayFrameIsThePackedColourTableOutput()
	{
		var frame = TqiDecoder.Decode( MoviePlaybackTests.SyntheticFrame() );
		var plain = new byte[16 * 16 * 4];
		var shown = new byte[16 * 16 * 4];
		frame.WriteRgba32( plain );
		frame.WriteDisplayRgba32( shown, TqiDisplayFormat.Rgb565 );
		for ( var pixel = 0; pixel < 256; pixel++ )
		{
			var column = pixel % 16;
			Assert.AreEqual( TqiColour.Pack( plain[pixel * 4], 5, column ), shown[pixel * 4] );
			Assert.AreEqual( TqiColour.Pack( plain[pixel * 4 + 1], 6, column ), shown[pixel * 4 + 1] );
			Assert.AreEqual( TqiColour.Pack( plain[pixel * 4 + 2], 5, column ), shown[pixel * 4 + 2] );
			Assert.AreEqual( 255, shown[pixel * 4 + 3] );
		}
		Assert.ThrowsException<ArgumentException>( () => frame.WriteDisplayRgba32( new byte[10], TqiDisplayFormat.Rgb565 ) );
	}

	[TestMethod]
	public void PresenterShowsFullColourByDefaultAndTheOriginalPackingOnRequest()
	{
		// A silent capture of bf.tgq frame 127 as presented: full 8-bit colour by default ([EXT:movie-full-color]); the original's
		// 16-bit packing of the same frame stays reproducible and pinned.
		var movie = new TgqMovieFile( System.IO.File.OpenRead( System.IO.Path.Combine( OriginalMovieDirectory(), "bf.tgq" ) ) );
		var frame = movie.DecodeVideoFrame( 127 );
		var plain = new byte[frame.Width * frame.Height * 4];
		frame.WriteRgba32( plain );
		var shown = new byte[plain.Length];
		MoviePresenter.ToRgba( frame, null, shown );
		CollectionAssert.AreEqual( plain, shown );
		Assert.AreEqual( "51E4375FF971DB435DA81D19A96C18D8702BE12F6EE8B1C3D557416F1B8D7209", System.Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( shown ) ) );

		var packed = new byte[plain.Length];
		MoviePresenter.ToRgba( frame, TqiDisplayFormat.Rgb565, packed );
		CollectionAssert.AreNotEqual( plain, packed );
		for ( var pixel = 0; pixel < packed.Length / 4; pixel++ )
		{
			// Every packed channel is a replicated 5- or 6-bit field.
			Assert.AreEqual( packed[pixel * 4] & 7, packed[pixel * 4] >> 5 );
			Assert.AreEqual( packed[pixel * 4 + 1] & 3, packed[pixel * 4 + 1] >> 6 );
		}
		Assert.AreEqual( "01EF358E76F64111233E933D9394BE6D178D6C75E5DF6A98DEA084E9530BDE91", System.Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( packed ) ) );
	}

	private static string OriginalMovieDirectory()
	{
		var gamePath = Environment.GetEnvironmentVariable( "OPENTPW_GAME_PATH" );
		if ( string.IsNullOrWhiteSpace( gamePath ) || !System.IO.Directory.Exists( gamePath ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH for the selected original TGQ movie corpus." );
		var dataPath = System.IO.Directory.EnumerateDirectories( gamePath! ).FirstOrDefault( directory => string.Equals( System.IO.Path.GetFileName( directory ), "data", StringComparison.OrdinalIgnoreCase ) ) ?? gamePath!;
		var directory = System.IO.Directory.EnumerateDirectories( dataPath ).FirstOrDefault( entry => string.Equals( System.IO.Path.GetFileName( entry ), "movies", StringComparison.OrdinalIgnoreCase ) );
		if ( directory == null )
			Assert.Inconclusive( "The selected original TGQ movie corpus is missing." );
		return directory!;
	}
}
