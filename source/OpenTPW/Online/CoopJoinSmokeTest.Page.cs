namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] A page's runner of the join phase (docs/COOP.md, "The browser's join") and of the host phase (C2c-2b-4, "The browser's host"): the browser calls the
/// game once an animation frame, so the page attaches the smoke's handlers in the native order (around the game's update for the join phase, in its place for the host phase), times
/// each whole frame call for the join phase, hands the smoke any throw out of the frame, and takes the handlers off once the smoke has ended. The test itself is the native one; its
/// runner's handlers record the outcome instead of closing a window or exiting.
/// </summary>
internal sealed partial class CoopJoinSmokeTest
{
	/// <summary>
	/// Starts the join phase in a running game that shows its front end, reading the host's evidence from <paramref name="evidenceDirectory"/>. A start that fails (missing or
	/// malformed evidence) leaves nothing behind: the profile off, the player's saves back.
	/// </summary>
	internal static CoopJoinSmokeTest StartPageJoin( GameFlow flow, string evidenceDirectory, bool measuring, Action<string> completedBy, Action<string> failedBy )
	{
		ArgumentNullException.ThrowIfNull( flow );
		var test = new CoopJoinSmokeTest( flow, evidenceDirectory, joining: true, measuring, "Browser", completedBy, failedBy );
		try
		{
			// the level's construction by section, in the builds' timings (docs/COOP.md, "Measurements")
			LoadProfile.Enable( true );
			test.StartJoin();
			return test;
		}
		catch
		{
			LoadProfile.Enable( false );
			test.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Starts the host phase in a running game that shows its front end, writing the host's evidence to <paramref name="evidenceDirectory"/> (C2c-2b-4: a desktop guest replays
	/// it). Its level start reads the level's files at once, so the page has copied them (<see cref="HostLevel"/>). A start that fails leaves the player's saves back.
	/// </summary>
	internal static CoopJoinSmokeTest StartPageHost( GameFlow flow, string evidenceDirectory, Action<string> completedBy, Action<string> failedBy )
	{
		ArgumentNullException.ThrowIfNull( flow );
		Directory.CreateDirectory( evidenceDirectory );
		var test = new CoopJoinSmokeTest( flow, evidenceDirectory, joining: false, measuring: false, "Browser", completedBy, failedBy );
		try
		{
			test.StartHost();
			// the page's long host frames with their named work, unless another recording of it runs (a page's performance harness)
			test.hostLongFrames = new List<CoopSmokeHostFrame>();
			if ( !FrameMarks.Enabled )
			{
				FrameMarks.Enable( true );
				test.hostMarks = true;
			}
			return test;
		}
		catch
		{
			test.Dispose();
			throw;
		}
	}

	/// <summary>
	/// The smoke's handlers in the native order: for the join phase around the game's update (its frame start, its join update, the game's update, its frame end); for the host
	/// phase in its place, as the native runner has it (the host's frame advances the park itself, through the session's stream).
	/// </summary>
	internal void Attach( Renderer renderer )
	{
		renderer.OnUpdate -= flow.Update;
		if ( !joining )
		{
			renderer.OnUpdate += HostUpdate;
			return;
		}
		// the page's loop starts each frame's marks before the frame (Program.Frame)
		runnerClearsMarks = true;
		renderer.OnUpdate += FrameStarts;
		renderer.OnUpdate += JoinUpdate;
		renderer.OnUpdate += flow.Update;
		renderer.OnUpdate += FrameEnds;
	}

	/// <summary>Takes the smoke's handlers off, leaving the game's update; after a frame, never inside one (a frame's handlers run on as they were when it began).</summary>
	internal void Detach( Renderer renderer )
	{
		if ( !joining )
		{
			renderer.OnUpdate -= HostUpdate;
			renderer.OnUpdate += flow.Update;
			StopHostMarks();
			return;
		}
		renderer.OnUpdate -= FrameStarts;
		renderer.OnUpdate -= JoinUpdate;
		renderer.OnUpdate -= FrameEnds;
		LoadProfile.Enable( false );
	}

	/// <summary>Whether the run has ended, passed or failed.</summary>
	internal bool Ended => done;

	/// <summary>
	/// The evidence written again once the frame that ended the run has returned (written first inside the frame): the join phase's with that frame's whole call time, the host
	/// phase's with its long frames; null when it was written, else why not (a run whose evidence could not be written has not passed).
	/// </summary>
	internal string? PageEnded()
	{
		try
		{
			// the host phase's evidence again with its long frames, the last one's among them
			if ( !joining )
				WriteJson( "host-evidence.json", (hostWritten ?? HostEvidence( completed, failure )) with { LongFrames = hostLongFrames?.ToArray() } );
			else
				WriteJson( "join-evidence.json", JoinEvidence( completed, failure ) );
			return null;
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Log.Warning( $"Co-op join smoke: writing the evidence after the run's last frame failed: {error.Message}" );
			return error.Message;
		}
	}

	/// <summary>The frames the join phase has run.</summary>
	internal int Frames => frame;

	/// <summary>Where the join phase stands.</summary>
	internal string Position => Where();

	/// <summary>The whole frame call that just ended, its update and its drawing, in milliseconds: the last update frame's when it is this one's.</summary>
	internal void PageFrameEnded( double milliseconds )
	{
		if ( !joining )
		{
			if ( hostLongFrames != null && milliseconds >= LongBuildFrameMilliseconds )
				hostLongFrames.Add( new CoopSmokeHostFrame( frame, hostStage.ToString(), milliseconds, FrameMarks.Snapshot() ) );
			return;
		}
		if ( updateFrames.Count > 0 && updateFrames[^1].Frame == frame && updateFrames[^1].CallMilliseconds == null )
			updateFrames[^1] = updateFrames[^1] with { CallMilliseconds = milliseconds };
	}

	/// <summary>
	/// A throw out of the page's frame, the game's update or its drawing as much as the smoke's own: the run's failure (once), also when the smoke completed earlier in the same frame
	/// (its evidence is written again as a failure; the page publishes a pass only after the whole frame).
	/// </summary>
	internal void PageFailed( Exception error )
	{
		if ( completed )
			(completed, done) = (false, false);
		Fail( error );
	}
}
