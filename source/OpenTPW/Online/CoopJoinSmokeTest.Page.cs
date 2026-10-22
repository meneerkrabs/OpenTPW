namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] A page's runner of the join phase (docs/COOP.md, "The browser's join"): the browser calls the game once an animation frame, so the page attaches the
/// smoke's handlers around the game's update in the native order, times each whole frame call for the smoke, hands it any throw out of the frame, and takes the handlers off once the
/// smoke has ended. The test itself is the native one; its runner's handlers record the outcome instead of closing a window or exiting.
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

	/// <summary>The smoke's handlers around the game's update, in the native order: its frame start, its join update, the game's update, its frame end.</summary>
	internal void Attach( Renderer renderer )
	{
		renderer.OnUpdate -= flow.Update;
		renderer.OnUpdate += FrameStarts;
		renderer.OnUpdate += JoinUpdate;
		renderer.OnUpdate += flow.Update;
		renderer.OnUpdate += FrameEnds;
	}

	/// <summary>Takes the smoke's handlers off, leaving the game's update; after a frame, never inside one (a frame's handlers run on as they were when it began).</summary>
	internal void Detach( Renderer renderer )
	{
		renderer.OnUpdate -= FrameStarts;
		renderer.OnUpdate -= JoinUpdate;
		renderer.OnUpdate -= FrameEnds;
		LoadProfile.Enable( false );
	}

	/// <summary>Whether the run has ended, passed or failed.</summary>
	internal bool Ended => done;

	/// <summary>The evidence written again once the frame that ended the run has returned, with that frame's whole call time (written first inside the frame).</summary>
	internal void PageEnded()
	{
		try
		{
			WriteJson( "join-evidence.json", JoinEvidence( completed, failure ) );
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Log.Warning( $"Co-op join smoke: writing the evidence after the run's last frame failed: {error.Message}" );
		}
	}

	/// <summary>The frames the join phase has run.</summary>
	internal int Frames => frame;

	/// <summary>Where the join phase stands.</summary>
	internal string Position => Where();

	/// <summary>The whole frame call that just ended, its update and its drawing, in milliseconds: the last update frame's when it is this one's.</summary>
	internal void PageFrameEnded( double milliseconds )
	{
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
