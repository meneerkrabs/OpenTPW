using System.Security.Cryptography;

namespace OpenTPW;

/// <summary>Command-line movie playback: <c>--play-movie &lt;name&gt; [--mute] [--headless | --smoke-test]</c>.</summary>
internal static class MovieCommands
{
	/// <summary>Headless step: a 60 Hz game loop.</summary>
	public const double HeadlessStepSeconds = 1d / 60;

	public static string? GetMovieName( string[] args )
	{
		var index = Array.IndexOf( args, "--play-movie" );
		if ( index < 0 )
			return null;
		if ( index + 1 >= args.Length || args[index + 1].StartsWith( "--" ) )
			throw new ArgumentException( "--play-movie requires a movie name from Data/Movies (for example bf)." );
		return args[index + 1];
	}

	public static TgqMovieFile Open( string dataDirectory, string name )
	{
		using var stream = File.OpenRead( MovieLibrary.Resolve( dataDirectory, name ) );
		return new TgqMovieFile( stream );
	}

	/// <summary>Decode-and-clock simulation without a window, GPU or audio device; prints the playback statistics.</summary>
	public static void RunHeadless( string dataDirectory, string name, bool withAudio )
	{
		var movie = Open( dataDirectory, name );
		var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		var result = MovieSimulation.Run( movie, HeadlessStepSeconds, withAudio, onFrame: playback =>
		{
			var frame = playback.CurrentFrame!;
			hash.AppendData( frame.Y );
			hash.AppendData( frame.Cb );
			hash.AppendData( frame.Cr );
		} );
		Console.WriteLine( $"Movie {name}: {movie.VideoFrameCount} frames {movie.Width}x{movie.Height} at {movie.AudioHeader.FrameRate} fps; audio {movie.AudioHeader.SampleCount} frames at {movie.AudioHeader.SampleRate} Hz." );
		Console.WriteLine( $"Headless {(withAudio ? "audio-clocked" : "fixed-step")} playback at 60 Hz: {result.Updates} updates, {result.FramesDecoded} frames decoded, {result.FramesDropped} dropped, {result.Holds} holds, ended at {result.Clock:F3} s; peak audio queue {result.MaximumQueuedAudioFrames} frames." );
		Console.WriteLine( $"Presented planes SHA-256: {Convert.ToHexString( hash.GetHashAndReset() )}" );
	}

	/// <summary>Plays the movie in the game window until it ends, is skipped or the window closes.</summary>
	public static void Play( string dataDirectory, string name, bool withAudio, bool smokeTest )
	{
		// Movies are not 3D world content: they always render at output size.
		Render.WorldScalingAllowed = false;
		var movie = Open( dataDirectory, name );
		using var screen = new MovieScreen( movie, withAudio && !smokeTest );
		Render.OnUpdate += screen.Update;
		Render.OnRender += screen.Render;
		screen.Finished += () => Render.Window.SdlWindow.Close();
		MovieSmokeTest? smoke = null;
		if ( smokeTest )
		{
			smoke = new MovieSmokeTest( screen, Path.GetFileNameWithoutExtension( name ) );
			Render.PostUpdate += smoke.Update;
		}
		try
		{
			Render.Run();
		}
		finally
		{
			Render.OnUpdate -= screen.Update;
			Render.OnRender -= screen.Render;
			if ( smoke != null )
				Render.PostUpdate -= smoke.Update;
		}
		smoke?.VerifyCompleted();
	}
}
