namespace OpenTPW;

/// <summary>Finds original movies by name in the data directory's Movies folder (case-insensitive).</summary>
public static class MovieLibrary
{
	public const string Extension = ".tgq";

	/// <summary>
	/// Resolves <paramref name="name"/> ("bf" or "bf.tgq") to a file in <c>&lt;data&gt;/Movies</c>.
	/// Names containing path separators are rejected so the lookup stays inside the movie folder.
	/// </summary>
	public static string Resolve( string dataDirectory, string name )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		if ( name.IndexOfAny( new[] { '/', '\\' } ) >= 0 || name.Contains( ".." ) )
			throw new ArgumentException( "Movie names must not contain path separators.", nameof( name ) );
		var fileName = name.EndsWith( Extension, StringComparison.OrdinalIgnoreCase ) ? name : name + Extension;
		var directory = Directory.EnumerateDirectories( dataDirectory )
			.FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), "movies", StringComparison.OrdinalIgnoreCase ) )
			?? throw new DirectoryNotFoundException( $"No Movies folder in '{dataDirectory}'." );
		return Directory.EnumerateFiles( directory )
			.FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), fileName, StringComparison.OrdinalIgnoreCase ) )
			?? throw new FileNotFoundException( $"Movie '{fileName}' not found. Available: {string.Join( ", ", List( dataDirectory ) )}." );
	}

	public static IEnumerable<string> List( string dataDirectory )
	{
		var directory = Directory.EnumerateDirectories( dataDirectory )
			.FirstOrDefault( entry => string.Equals( Path.GetFileName( entry ), "movies", StringComparison.OrdinalIgnoreCase ) );
		return directory == null
			? Enumerable.Empty<string>()
			: Directory.EnumerateFiles( directory, "*", SearchOption.TopDirectoryOnly )
				.Where( entry => entry.EndsWith( Extension, StringComparison.OrdinalIgnoreCase ) )
				.Select( entry => Path.GetFileNameWithoutExtension( entry ) )
				.Order( StringComparer.OrdinalIgnoreCase );
	}
}

/// <summary>Result of a GPU-free playback run.</summary>
public sealed record MovieSimulationResult(
	int FramesDecoded, int FramesDropped, int Holds, int Updates, double Clock, int LastFrameIndex, long MaximumQueuedAudioFrames, long UnderrunFrames );

public static class MovieSimulation
{
	/// <summary>
	/// Plays a movie headless with fixed update steps: decodes the frames the clock selects and, when
	/// <paramref name="withAudio"/> is set, streams audio into a simulated device that plays in real time.
	/// </summary>
	public static MovieSimulationResult Run( TgqMovieFile movie, double stepSeconds, bool withAudio, int maximumUpdates = 1_000_000, Action<MoviePlayback>? onFrame = null )
	{
		if ( !double.IsFinite( stepSeconds ) || stepSeconds <= 0 )
			throw new ArgumentOutOfRangeException( nameof( stepSeconds ) );
		var output = withAudio ? new SimulatedMovieAudioOutput( movie.AudioHeader.SampleRate ) : null;
		using var playback = new MoviePlayback( movie, output );
		var updates = 0;
		var maximumQueued = 0L;
		var underrun = 0L;
		while ( !playback.IsFinished && updates < maximumUpdates )
		{
			// The device plays between game updates; the playback then reads its position.
			if ( updates > 0 )
				output?.Advance( stepSeconds );
			if ( playback.Update( updates == 0 ? 0 : stepSeconds ) )
				onFrame?.Invoke( playback );
			++updates;
			if ( output != null )
			{
				maximumQueued = output.MaximumQueuedFrames;
				underrun = output.UnderrunFrames;
			}
		}
		return new MovieSimulationResult( playback.FramesDecoded, playback.FramesDropped, playback.Holds, updates, playback.Clock,
			playback.CurrentFrameIndex, maximumQueued, underrun );
	}
}
