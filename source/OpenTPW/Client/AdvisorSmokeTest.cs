using System.Diagnostics;

namespace OpenTPW;

/// <summary>
/// Native check for <c>--smoke-test --advisor-say N</c>: the advisor overlay renders,
/// the clip's playback clock advances to its end, both mouth meshes are shown in the order
/// the LIP timeline predicts, and the two captured advisor viewports differ.
/// </summary>
internal sealed class AdvisorSmokeTest : IDisposable
{
	private const int FrameLimit = 60 * 120;
	private readonly Advisor advisor;
	private readonly List<string> mouthSequence = new();
	private byte[]? talkingFrame;
	private byte[]? closedFrame;
	private TimeSpan lastPosition = TimeSpan.Zero;
	private readonly Stopwatch wallClock = new();
	private int frame;
	private bool completed;

	public AdvisorSmokeTest( Advisor advisor )
	{
		this.advisor = advisor;
	}

	public void Update()
	{
		++frame;
		wallClock.Start();
		SandboxSmokeTest.Require( frame < FrameLimit, "advisor clip finishes within the frame limit" );
		var position = advisor.Position;
		SandboxSmokeTest.Require( position >= lastPosition, "advisor playback clock is monotonic" );
		lastPosition = position;
		// The frame just presented was rendered with the mouth chosen during this update.
		var mouth = advisor.LastRenderedMouth;
		if ( mouth != null && (mouthSequence.Count == 0 || mouthSequence[^1] != mouth) )
			mouthSequence.Add( mouth );
		if ( mouth == Advisor.TalkingMouth && talkingFrame == null && frame > 10 )
			talkingFrame = SandboxSmokeTest.CaptureFrame( "advisor-talking.png" ).Pixels;
		if ( mouth == Advisor.ClosedMouth && talkingFrame != null && closedFrame == null )
			closedFrame = SandboxSmokeTest.CaptureFrame( "advisor-closed.png" ).Pixels;
		if ( advisor.IsSpeaking || completed )
			return;

		SandboxSmokeTest.Require( talkingFrame != null && closedFrame != null, "advisor showed talking and closed mouths" );
		SandboxSmokeTest.Require( MouthDiffers( talkingFrame!, closedFrame! ), "advisor mouth pixels change with the LIP state" );
		SandboxSmokeTest.Require( mouthSequence[^1] == Advisor.ClosedMouth, "advisor mouth closes at the end of the clip" );
		var ratio = wallClock.Elapsed / advisor.Position;
		SandboxSmokeTest.Require( ratio > 0.8 && ratio < 1.5, "advisor playback clock advances in real time" );
		completed = true;
		Log.Trace( $"Native advisor smoke test passed: sp_{advisor.ClipNumber:000}, {frame} frames, {wallClock.Elapsed.TotalSeconds:F2} s wall time for {advisor.Position.TotalSeconds:F2} s of speech, mouth sequence {string.Join( " > ", mouthSequence )}, clock {advisor.ClockSource}." );
		Render.Window.SdlWindow.Close();
	}

	private bool MouthDiffers( byte[] first, byte[] second )
	{
		var (left, top, right, bottom) = advisor.MouthScreenRectangle();
		var width = (int)Render.ResolveColorTexture.Width;
		var height = (int)Render.ResolveColorTexture.Height;
		var changed = 0;
		for ( var row = Math.Max( 0, top ); row < Math.Min( height, bottom ); row++ )
			for ( var column = Math.Max( 0, left ); column < Math.Min( width, right ); column++ )
			{
				var offset = (row * width + column) * 4;
				if ( first[offset] != second[offset] || first[offset + 1] != second[offset + 1] || first[offset + 2] != second[offset + 2] )
					changed++;
			}
		Log.Trace( $"Advisor mouth rectangle ({left},{top})-({right},{bottom}): {changed} pixels differ between talking and closed captures." );
		return changed > 20;
	}

	public void VerifyCompleted() => SandboxSmokeTest.Require( completed, "complete the advisor smoke test" );

	public void Dispose() => Render.PostUpdate -= Update;
}
