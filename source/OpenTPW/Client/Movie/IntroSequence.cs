using Veldrid;

namespace OpenTPW;

/// <summary>The owned movie operations needed by the intro queue; allows lifecycle checks without a GPU.</summary>
internal interface IIntroMovie : IDisposable
{
	bool IsFinished { get; }
	void Skip();
	void Update();
	void Render();
}

/// <summary>
/// Plays the start-up movies (<see cref="IntroPlaylist"/>) in the game window before the front end. Skippable
/// with Esc, Space or a mouse button; a missing or unreadable movie is skipped silently, like the original.
/// </summary>
internal sealed class IntroSequence : IDisposable
{
	private readonly Queue<string> remaining;
	private readonly Func<string, IIntroMovie?> openMovie;
	private readonly Func<bool> skipInput;
	private readonly Action<bool> setWorldScaling;
	private readonly IntroSkipGate gate = new();
	private IIntroMovie? current;
	private string? currentName;
	private bool completed;

	public IntroSequence( string dataDirectory, IEnumerable<string> movies, bool withAudio, float gain )
		: this( movies, name => Open( dataDirectory, name, withAudio, gain ), SkipInputDown, allowed => Render.WorldScalingAllowed = allowed ) { }

	internal IntroSequence( IEnumerable<string> movies, Func<string, IIntroMovie?> openMovie, Func<bool> skipInput, Action<bool> setWorldScaling )
	{
		remaining = new Queue<string>( movies );
		this.openMovie = openMovie;
		this.skipInput = skipInput;
		this.setWorldScaling = setWorldScaling;
		// Movies are not 3D world content: they always render at output size.
		setWorldScaling( false );
	}

	public bool IsCompleted => completed;
	/// <summary>Raised once, from <see cref="Update"/>, when no movie is left.</summary>
	public event Action? Completed;

	public static bool SkipInputDown() =>
		Input.Keyboard.KeysDown.Contains( Key.Escape ) || Input.Keyboard.KeysDown.Contains( Key.Space ) || Input.Mouse.Left || Input.Mouse.Right;

	public void Update()
	{
		if ( completed )
			return;
		var skip = gate.Poll( skipInput() );
		while ( current == null )
		{
			if ( !remaining.TryDequeue( out var name ) )
			{
				Complete();
				return;
			}
			if ( skip )
			{
				Log?.Trace( $"Intro movie {name} skipped." );
				continue;
			}
			try
			{
				current = openMovie( name );
				currentName = name;
			}
			catch ( Exception exception ) when ( RecoverableMovieError( exception ) )
			{
				Log?.Warning( $"Intro movie {name} not played: {exception.Message}" );
			}
		}
		try
		{
			if ( skip )
				current.Skip();
			current.Update();
			if ( current.IsFinished )
				ReleaseCurrent();
		}
		catch ( Exception exception ) when ( RecoverableMovieError( exception ) )
		{
			Log?.Warning( $"Intro movie {currentName} stopped: {exception.Message}" );
			ReleaseCurrent();
		}
	}

	public void Draw() => current?.Render();

	private static bool RecoverableMovieError( Exception exception ) =>
		exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException or ArgumentException;

	private static IIntroMovie Open( string dataDirectory, string name, bool withAudio, float gain )
	{
		var screen = new MovieScreen( MovieCommands.Open( dataDirectory, name ), withAudio, gain, handleSkipInput: false );
		Log?.Trace( $"Intro movie {name}: {screen.Playback.Width}x{screen.Playback.Height}, {screen.Playback.Duration:F1} s{(screen.Playback.HasAudio ? "" : ", no audio")}." );
		return screen;
	}

	private void Complete()
	{
		completed = true;
		setWorldScaling( true );
		Completed?.Invoke();
	}

	public void Dispose()
	{
		completed = true;
		ReleaseCurrent();
		setWorldScaling( true );
	}

	private void ReleaseCurrent()
	{
		var movie = current;
		current = null;
		currentName = null;
		movie?.Dispose();
	}
}
