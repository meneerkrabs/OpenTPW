namespace OpenTPW;

/// <summary>How one <see cref="PeriodicAutosave.Tick"/> ended.</summary>
internal enum AutosaveResult
{
	/// <summary>Nothing was due.</summary>
	NotDue,
	/// <summary>A snapshot was captured; its serialization runs in time-boxed slices over the next frames (this frame included).</summary>
	Serializing,
	/// <summary>The serialization finished and the write started (or finished, when it ran inline).</summary>
	Saved,
	/// <summary>The park could not be captured right now (the advisor is speaking, a staff tool is in hand, the catalogue is not resumable); the next attempt comes soon.</summary>
	Refused,
	/// <summary>The previous background write is still running; the autosave stays due and is tried again next frame.</summary>
	Busy
}

/// <summary>
/// [EXT:periodic-autosave] The schedule and writer of the periodic autosave (docs/PARK-SAVES.md, "Periodic autosave"): every
/// <see cref="IntervalSeconds"/> of play time in an owned park the game captures the park between two simulation ticks and writes it atomically into the
/// theme's own autosave slot, never the manual save. Only play counts: paused frames (pause menu, map window, texture-pack download, a park being built)
/// add nothing, so a long pause delays the save instead of triggering one the moment play resumes, and a hitching or suspended frame adds at most
/// <see cref="LongestFrameSeconds"/>. A park that cannot be captured at the due time (a response is being spoken) is retried after <see cref="RetrySeconds"/>
/// rather than skipped for a whole interval.
/// </summary>
/// <remarks>
/// The capture runs on the game thread in one frame, between two simulation ticks, because the snapshot is built from live simulation state and must be consistent; it does not
/// draw from a random stream or change the simulation. The snapshot is a detached copy, so its JSON serialization is spread over the next frames in slices of
/// <see cref="SliceBudget"/> (<see cref="SlicedJsonSerializer"/>; the same bytes as the manual save's), which keeps a large park from dropping frames on either platform. The file write
/// (temporary file, flush, move) then runs on a worker thread on the desktop; the browser has no second thread and writes inline into its in-memory file system, which the
/// page mirrors to IndexedDB (docs/WEB.md).
/// </remarks>
internal sealed class PeriodicAutosave
{
	/// <summary>Five minutes of play time between two autosaves.</summary>
	internal const double IntervalSeconds = 300;
	/// <summary>The wait before trying again after a refused capture or a failed write.</summary>
	internal const double RetrySeconds = 15;
	/// <summary>After this many refusals in a row the autosave waits a whole interval, so a park that can never be captured does not retry (and log) all the time.</summary>
	internal const int RefusalsBeforeBackoff = 8;
	/// <summary>The most one frame adds to the play time.</summary>
	internal const double LongestFrameSeconds = 0.25;

	private readonly Action<string, byte[]> persist;
	private readonly Func<Action, Task> background;
	private double elapsed;
	private double wait = IntervalSeconds;
	private int refusals;
	private Task? writing;
	/// <summary>Whether the write <see cref="writing"/> tracks belongs to the park now playing; <see cref="Reset"/> clears it while the task keeps running.</summary>
	private bool writingForCurrentPark;
	private Exception? writeError;
	private SlicedJsonSerializer? slicing;
	private string? slicingPath;
	/// <summary>Makes the next play-time count find the autosave due (the browser harness measures its capture in a real frame).</summary>
	internal void MakeDue() => wait = 0;
	/// <summary>The time one frame spends serializing.</summary>
	internal TimeSpan SliceBudget { get; set; } = TimeSpan.FromMilliseconds( 2 );
	/// <summary>Frames the last serialization took.</summary>
	internal int LastSlices { get; private set; }
	/// <summary>The longest single slice of the last serialization, in milliseconds.</summary>
	internal double LastLongestSliceMilliseconds { get; private set; }
	/// <summary>Milliseconds the last capture took on the game thread.</summary>
	internal double LastCaptureMilliseconds { get; private set; }
	/// <summary>Milliseconds the last JSON serialization took on the game thread, over all its slices.</summary>
	internal double LastSerializeMilliseconds { get; private set; }
	/// <summary>Milliseconds the last file write took (on the worker thread, when there is one).</summary>
	internal double LastWriteMilliseconds { get; private set; }
	/// <summary>Bytes of the last autosave.</summary>
	internal int LastBytes { get; private set; }

	internal PeriodicAutosave( Action<string, byte[]>? persist = null, Func<Action, Task>? background = null )
	{
		this.persist = persist ?? PersistWithCleanup;
		// the browser's only thread must not wait for a task another thread would have to run
		this.background = background ?? ( OperatingSystem.IsBrowser() ? Inline : work => Task.Run( work ) );
	}

	private static Task Inline( Action work )
	{
		try { work(); return Task.CompletedTask; }
		catch ( Exception error ) { return Task.FromException( error ); }
	}

	/// <summary>Seconds of play counted since the last autosave attempt.</summary>
	internal double Elapsed => elapsed;
	/// <summary>Seconds of play after which the next attempt is made.</summary>
	internal double Wait => wait;
	internal int Refusals => refusals;
	internal bool IsDue => elapsed >= wait;
	internal bool WriteInProgress => writing is { IsCompleted: false };
	internal bool SerializationInProgress => slicing != null;

	/// <summary>Adds the frame's play time: nothing while the game is not <paramref name="playing"/>, and at most <see cref="LongestFrameSeconds"/>.</summary>
	internal void Advance( double deltaSeconds, bool playing )
	{
		if ( !playing || !( deltaSeconds > 0 ) ) return;
		elapsed += Math.Min( deltaSeconds, LongestFrameSeconds );
	}

	/// <summary>
	/// A new park (or a resumed one): the count starts again, a serialization of the old park is dropped and a write of it is left to finish on its
	/// own — the task keeps running alone, so <see cref="WriteInProgress"/> holds and writes stay single and atomic, but its outcome belongs to the
	/// old park: a failure it reports warns and leaves the new park's count, wait and refusals exactly to this frame alone.
	/// </summary>
	internal void Reset()
	{
		elapsed = 0; wait = IntervalSeconds; refusals = 0; slicing = null; slicingPath = null; writingForCurrentPark = false;
	}

	/// <summary>
	/// Counts the frame, continues a running serialization and, once due, captures. <paramref name="capture"/> returns a shape-checked snapshot, or null or throws
	/// <see cref="InvalidOperationException"/>, <see cref="NotSupportedException"/> or <see cref="InvalidDataException"/> when the park cannot be captured now;
	/// anything else is a bug and propagates. <paramref name="path"/> is the autosave slot. <paramref name="validate"/>, when given, checks the captured snapshot as the first step of the
	/// serialization (a later frame than the capture); a snapshot it refuses is never written.
	/// </summary>
	internal AutosaveResult Tick( double deltaSeconds, bool playing, Func<ParkResumeSnapshot?> capture, string path, Action<string>? warn = null, Action<ParkResumeSnapshot>? validate = null )
	{
		Advance( deltaSeconds, playing );
		if ( writing is { IsCompleted: true } finished )
		{
			var thisPark = writingForCurrentPark;
			writing = null; writingForCurrentPark = false;
			if ( finished.IsFaulted )
			{
				var error = finished.Exception!.GetBaseException();
				// a write this park started is this park's failed autosave, retried soon with a fresh capture; a write the park before this one
				// started only reports itself — the current park's count, wait and refusals are what this frame alone makes of them
				if ( thisPark ) writeError = error;
				else warn?.Invoke( $"A periodic autosave from a previous park could not be written ({error.Message}); the current park autosave schedule is unchanged." );
			}
		}
		if ( slicing != null ) return Continue( warn );
		if ( writeError != null ) return Failed( writeError, warn );
		if ( !IsDue || !playing ) return AutosaveResult.NotDue;
		if ( WriteInProgress ) return AutosaveResult.Busy;
		try
		{
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			var snapshot = capture();
			if ( snapshot == null ) return Refuse( null, warn );
			LastCaptureMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			slicing = ParkResumeSaveFile.BeginSlicedSerialize( snapshot, validate == null ? null : () => validate( snapshot ) ); slicingPath = path;
			LastSerializeMilliseconds = 0; LastLongestSliceMilliseconds = 0; LastSlices = 0;
		}
		catch ( Exception error ) when ( error is InvalidOperationException or NotSupportedException or InvalidDataException )
		{
			return Refuse( error, warn );
		}
		refusals = 0; elapsed = 0; wait = IntervalSeconds;
		return Continue( warn );
	}

	/// <summary>One slice of the running serialization; the write starts with the last one.</summary>
	private AutosaveResult Continue( Action<string>? warn )
	{
		var serializer = slicing!; byte[] bytes;
		try
		{
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			var done = serializer.Run( SliceBudget );
			var slice = System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			LastSerializeMilliseconds += slice; LastLongestSliceMilliseconds = Math.Max( LastLongestSliceMilliseconds, slice ); LastSlices = serializer.Slices;
			if ( !done ) return AutosaveResult.Serializing;
			bytes = ParkResumeSaveFile.CheckSize( serializer.Bytes );
		}
		catch ( Exception error ) when ( error is InvalidOperationException or NotSupportedException or InvalidDataException or System.Text.Json.JsonException )
		{
			slicing = null; return Failed( error, warn );
		}
		var path = slicingPath!; slicing = null; slicingPath = null; LastBytes = bytes.Length;
		writingForCurrentPark = true;
		writing = background( () =>
		{
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			persist( path, bytes );
			LastWriteMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
		} );
		return AutosaveResult.Saved;
	}

	/// <summary>A serialization or write that failed: retried soon with a fresh capture; the previous autosave is untouched.</summary>
	private AutosaveResult Failed( Exception error, Action<string>? warn )
	{
		warn?.Invoke( $"The periodic autosave could not be written ({error.Message}); trying again in {RetrySeconds:0} seconds." );
		writeError = null; elapsed = 0; wait = RetrySeconds;
		return AutosaveResult.Refused;
	}

	private AutosaveResult Refuse( Exception? error, Action<string>? warn )
	{
		// the first refusal of a run is reported once; a speaking advisor is routine, so the retries stay quiet
		if ( refusals == 0 && error != null ) warn?.Invoke( $"The periodic autosave waits: the park cannot be captured now ({error.Message})" );
		refusals++; elapsed = 0;
		wait = refusals >= RefusalsBeforeBackoff ? IntervalSeconds : RetrySeconds;
		return AutosaveResult.Refused;
	}

	/// <summary>Waits for a running write (tests, and the shutdown of a park).</summary>
	internal void Flush()
	{
		try { writing?.Wait(); } catch ( AggregateException ) { }
		writing = null;
	}

	/// <summary>The atomic write, after removing temporary files an earlier write left behind when the process died inside it.</summary>
	internal static void PersistWithCleanup( string path, byte[] bytes )
	{
		try
		{
			var directory = Path.GetDirectoryName( Path.GetFullPath( path ) );
			if ( directory != null && Directory.Exists( directory ) )
				foreach ( var stale in Directory.EnumerateFiles( directory, Path.GetFileName( path ) + ".*.tmp" ) )
					File.Delete( stale );
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException ) { }
		ParkResumeSaveFile.SaveBytes( path, bytes );
	}
}
