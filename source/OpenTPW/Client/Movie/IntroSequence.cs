using Veldrid;

namespace OpenTPW;

/// <summary>
/// Plays the start-up movies (<see cref="IntroPlaylist"/>) in the game window before the front end. Skippable
/// with Esc, Space or a mouse button; a missing or unreadable movie is skipped silently, like the original.
/// </summary>
internal sealed class IntroSequence : IDisposable
{
	private readonly string dataDirectory;
	private readonly Queue<string> remaining;
	private readonly bool withAudio;
	private readonly float gain;
	private readonly IntroSkipGate gate = new();
	private MovieScreen? current;
	private bool completed;

	public IntroSequence( string dataDirectory, IEnumerable<string> movies, bool withAudio, float gain )
	{
		this.dataDirectory = dataDirectory;
		remaining = new Queue<string>( movies );
		this.withAudio = withAudio;
		this.gain = gain;
		// Movies are not 3D world content: they always render at output size.
		Render.WorldScalingAllowed = false;
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
		var skip = gate.Poll( SkipInputDown() );
		while ( current == null )
		{
			if ( !remaining.TryDequeue( out var name ) )
			{
				Complete();
				return;
			}
			if ( skip )
			{
				Log.Trace( $"Intro movie {name} skipped." );
				continue;
			}
			current = TryOpen( name );
		}
		if ( skip )
			current.Playback.Skip();
		current.Update();
		if ( current.Playback.IsFinished )
		{
			current.Dispose();
			current = null;
		}
	}

	public void Draw() => current?.Render();

	private MovieScreen? TryOpen( string name )
	{
		try
		{
			var screen = new MovieScreen( MovieCommands.Open( dataDirectory, name ), withAudio, gain, handleSkipInput: false );
			Log.Trace( $"Intro movie {name}: {screen.Playback.Width}x{screen.Playback.Height}, {screen.Playback.Duration:F1} s{(screen.Playback.HasAudio ? "" : ", no audio")}." );
			return screen;
		}
		catch ( Exception exception ) when ( exception is IOException or NotSupportedException or InvalidDataException or ArgumentException )
		{
			Log.Warning( $"Intro movie {name} not played: {exception.Message}" );
			return null;
		}
	}

	private void Complete()
	{
		completed = true;
		Render.WorldScalingAllowed = true;
		Completed?.Invoke();
	}

	public void Dispose()
	{
		current?.Dispose();
		current = null;
	}
}
