namespace OpenTPW;

/// <summary>
/// Plays one movie in the game window: playback clock and frame selection from <see cref="MoviePlayback"/>,
/// GPU presentation through <see cref="MoviePresenter"/>, skip on any new key press or mouse click.
/// </summary>
internal sealed class MovieScreen : IDisposable
{
	private readonly MoviePlayback playback;
	private bool lastKeyDown = true;
	private bool lastMouseDown = true;

	public MovieScreen( TgqMovieFile movie, bool withAudio )
	{
		IMovieAudioOutput? output = null;
		if ( withAudio )
		{
			output = SdlMovieAudioOutput.TryOpen( movie.AudioHeader.SampleRate, out var failure );
			if ( output == null )
				Log.Warning( $"Movie audio unavailable ({failure}); playing video on the fixed-step clock." );
		}
		playback = new MoviePlayback( movie, output );
		Presenter = new MoviePresenter( movie.Width, movie.Height, global::Global.Render.MultisampledFramebuffer.OutputDescription );
	}

	public MoviePlayback Playback => playback;
	public MoviePresenter Presenter { get; }
	public event Action? Finished;

	public void Update()
	{
		// Keys or buttons already held when the movie starts do not skip it; a fresh press does.
		var keyDown = Input.Keyboard.KeysDown.Count > 0;
		var mouseDown = Input.Mouse.Left || Input.Mouse.Right;
		if ( (keyDown && !lastKeyDown) || (mouseDown && !lastMouseDown) )
			playback.Skip();
		lastKeyDown = keyDown;
		lastMouseDown = mouseDown;

		var wasFinished = playback.IsFinished;
		if ( playback.Update( Time.Delta ) )
			Presenter.Upload( playback.CurrentFrame! );
		if ( playback.IsFinished && !wasFinished )
		{
			Log.Trace( $"Movie finished{(playback.WasSkipped ? " (skipped)" : "")}: clock {playback.Clock:F2} s, {playback.FramesDecoded} frames shown, {playback.FramesDropped} dropped, {(playback.HasAudio ? "audio" : "fixed-step")} clock." );
			Finished?.Invoke();
		}
	}

	public void Render()
	{
		var framebuffer = global::Global.Render.MultisampledFramebuffer;
		Presenter.Draw( global::Global.Render.CommandList, framebuffer.Width, framebuffer.Height );
	}

	public void Dispose()
	{
		playback.Dispose();
		Presenter.Dispose();
	}
}
