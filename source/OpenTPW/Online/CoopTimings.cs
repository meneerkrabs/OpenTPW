using System.Diagnostics;
using System.Globalization;

namespace OpenTPW;

/// <summary>[EXT:online-coop-snapshot-join] How a part of a join's work was timed (docs/COOP.md, "Measurements").</summary>
[System.Text.Json.Serialization.JsonConverter( typeof( System.Text.Json.Serialization.JsonStringEnumConverter<CoopTimingStatus> ) )]
public enum CoopTimingStatus
{
	/// <summary>The part did not run (a build that ended before it, a game without its advisor).</summary>
	Skipped,
	/// <summary>The part ran and was timed.</summary>
	Measured,
	/// <summary>The part's result was kept from an earlier build (the asset manifest), so it ran no work of its own.</summary>
	Cached
}

/// <summary>
/// [EXT:online-coop-snapshot-join] One part of a join's work, timed on the wall clock (<see cref="Stopwatch"/>, never a session's clock): the steps it ran (units of work between two
/// checks of a frame's budget), the frames it ran in, its milliseconds and its longest step. A measurement only: the logs and the native join smoke's evidence read it, nothing else.
/// </summary>
public sealed class CoopWorkTiming
{
	public string Name { get; set; } = "";
	public CoopTimingStatus Status { get; set; }
	public int Steps { get; set; }
	public int Frames { get; set; }
	public double Milliseconds { get; set; }
	public double LongestMilliseconds { get; set; }

	/// <summary>The frame (a build's slice, a capture's frame) this part last ran in, so each frame counts once.</summary>
	private int lastFrame;

	internal static CoopWorkTiming Of( string name ) => new() { Name = name };

	/// <summary>A step of <paramref name="milliseconds"/>, in the frame numbered <paramref name="frame"/> (1 first).</summary>
	internal void Add( double milliseconds, int frame )
	{
		Steps++;
		Milliseconds += milliseconds;
		LongestMilliseconds = Math.Max( LongestMilliseconds, milliseconds );
		if ( Status == CoopTimingStatus.Skipped )
			Status = CoopTimingStatus.Measured;
		if ( frame != lastFrame )
		{
			Frames++;
			lastFrame = frame;
		}
	}

	public override string ToString() => Status == CoopTimingStatus.Skipped ? $"{Name} skipped"
		: string.Create( CultureInfo.InvariantCulture, $"{Name} {Milliseconds:0.0} ms in {Steps} steps over {Frames} frames (longest {LongestMilliseconds:0.0}){(Status == CoopTimingStatus.Cached ? ", cached" : "")}" );
}

/// <summary>[EXT:online-coop-snapshot-join] The milliseconds since <paramref name="start"/>, a <see cref="Stopwatch.GetTimestamp"/>.</summary>
internal static class CoopClock
{
	public static double Since( long start ) => Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
}

/// <summary>
/// [EXT:online-coop-snapshot-join] What one join snapshot cost its host (docs/COOP.md, "Measurements"). The cut is one step at a tick boundary: the frames before it published, the session
/// digest, the session state, the save's snapshot (the asset manifest with it when no earlier save kept it) and the serializer's setup. Then over frames: the save's serialization in
/// slices, its copy, the payload writer's setup (the whole uncompressed body), the compression in slices, whose last ends zlib's stream and copies the payload out, and the upload.
/// </summary>
public sealed class CoopCaptureTimings
{
	public uint Generation { get; set; }
	public long Ticks { get; set; }
	public double PublishMilliseconds { get; set; }
	public double DigestMilliseconds { get; set; }
	public double StateMilliseconds { get; set; }
	public double SnapshotMilliseconds { get; set; }
	public double SerializerSetupMilliseconds { get; set; }
	public CoopWorkTiming Save { get; set; } = CoopWorkTiming.Of( "save" );
	public double SaveCopyMilliseconds { get; set; }
	public double WriterSetupMilliseconds { get; set; }
	public CoopWorkTiming Compression { get; set; } = CoopWorkTiming.Of( "compression" );
	/// <summary>Of the compression's last slice: zlib's end and the payload's copy.</summary>
	public double FinishMilliseconds { get; set; }
	public long SaveBytes { get; set; }
	public long BodyBytes { get; set; }
	public long PayloadBytes { get; set; }
	/// <summary>The frames from the cut to the upload, the cut's frame among them.</summary>
	public int Frames { get; set; }
	/// <summary>The most a single frame spent on the capture (the cut's frame, or a slice's).</summary>
	public double LongestFrameMilliseconds { get; set; }
	/// <summary>The upload's wall time, from its start to its answer; null while none came.</summary>
	public double? UploadSeconds { get; set; }

	public double CutMilliseconds => PublishMilliseconds + DigestMilliseconds + StateMilliseconds + SnapshotMilliseconds + SerializerSetupMilliseconds;

	public override string ToString() => string.Create( CultureInfo.InvariantCulture,
		$"capture {Generation} at tick {Ticks}: cut {CutMilliseconds:0.0} ms (publish {PublishMilliseconds:0.0}, digest {DigestMilliseconds:0.0}, state {StateMilliseconds:0.0}, "
		+ $"snapshot {SnapshotMilliseconds:0.0}, setup {SerializerSetupMilliseconds:0.0}); {Save}; copy {SaveCopyMilliseconds:0.0} ms; writer {WriterSetupMilliseconds:0.0} ms; {Compression}, "
		+ $"finish {FinishMilliseconds:0.0} ms; {SaveBytes / 1024.0:0} KiB save, {BodyBytes / 1024.0:0} KiB body, {PayloadBytes / 1024.0:0} KiB payload; {Frames} frames, longest "
		+ $"{LongestFrameMilliseconds:0.0} ms; upload {(UploadSeconds is { } upload ? $"{upload:0.000} s" : "unanswered")}" );
}

/// <summary>
/// [EXT:online-coop-snapshot-join] A part of a build's step that alone took a frame's budget or more: its stage, its number among the stage's parts (1 the first), its milliseconds and, in
/// the level's stage, the level's construction progress after it (from 0 to 1, as the level reports it at its yields), which tells the part of the construction it ran.
/// </summary>
public sealed record CoopLongStep( string Stage, int Step, double Milliseconds, float? Progress )
{
	public override string ToString() => Progress is { } progress
		? string.Create( CultureInfo.InvariantCulture, $"{Stage} {Step} (to {progress:0.###}) {Milliseconds:0.0} ms" )
		: string.Create( CultureInfo.InvariantCulture, $"{Stage} {Step} {Milliseconds:0.0} ms" );
}

/// <summary>
/// [EXT:online-coop-snapshot-join] A build's own timings (<see cref="ICoopJoinBuild.Timings"/>): its stages in their order (each listed, a stage it never reached as skipped), the parts of
/// its steps that took a frame's budget alone, the frames it ran in, the most one of them spent on it, which build of the process it is (the first is the process's cold one), and its
/// work by <see cref="LoadProfile"/> section, taken over its own slices only while the profile records (a section still open when a build ends early, abandoned or failed, is not in it).
/// </summary>
public sealed class CoopBuildTimings
{
	public List<CoopWorkTiming> Stages { get; } = new();
	public List<CoopLongStep> LongSteps { get; } = new();
	public int Frames { get; set; }
	public double LongestFrameMilliseconds { get; set; }
	public int ProcessBuild { get; set; }
	public Dictionary<string, double> Sections { get; } = new();

	/// <summary>A build's timings with its stages listed in their order, none run yet.</summary>
	internal static CoopBuildTimings ForStages( params string[] names )
	{
		var timings = new CoopBuildTimings();
		foreach ( var name in names )
			timings.Stages.Add( CoopWorkTiming.Of( name ) );
		return timings;
	}

	/// <summary>The stage named <paramref name="name"/>, added after the others the first time.</summary>
	internal CoopWorkTiming Stage( string name )
	{
		foreach ( var stage in Stages )
			if ( stage.Name == name )
				return stage;
		var added = CoopWorkTiming.Of( name );
		Stages.Add( added );
		return added;
	}
}

/// <summary>
/// [EXT:online-coop-snapshot-join] What one capture's build cost a guest (docs/COOP.md, "Measurements"), whatever became of it: the download, the decode, the build's stages over frames
/// (with the level's <see cref="LoadProfile"/> sections while the profile records), the check at the cut, the swap, and the catch-up to the host's frames after it.
/// </summary>
public sealed class CoopJoinTimings
{
	public uint Generation { get; set; }
	/// <summary>
	/// "joined", "resynced", "failed its check", "timed out" (the build's deadline), "abandoned" (a newer capture), "refused", "failed" (a download, a build or an install) or "ended" (the
	/// session ended first); empty while it runs.
	/// </summary>
	public string Outcome { get; set; } = "";
	public int DownloadAttempts { get; set; }
	/// <summary>The download's wall time, from its first request to the last answer, a server's waits between the attempts included.</summary>
	public double DownloadSeconds { get; set; }
	/// <summary>The last attempt's wall time, from its request to its answer.</summary>
	public double LastAttemptSeconds { get; set; }
	public long PayloadBytes { get; set; }
	public double DecodeMilliseconds { get; set; }
	/// <summary>The build's stages in their order (<see cref="ICoopJoinBuild.Timings"/>).</summary>
	public List<CoopWorkTiming> Build { get; set; } = new();
	/// <summary>The parts of the build's steps that alone took a frame's budget or more, in their order.</summary>
	public List<CoopLongStep> LongSteps { get; set; } = new();
	/// <summary>The frames the build ran in, the most one of them spent on it, and which build of the process it was (1 the process's first, its cold one; 0 none).</summary>
	public int BuildFrames { get; set; }
	public double LongestBuildFrameMilliseconds { get; set; }
	public int ProcessBuild { get; set; }
	/// <summary>
	/// The build's work by <see cref="LoadProfile"/> section, milliseconds, over its own slices (empty while the profile is off; a section still open when the build ended early is not in
	/// it).
	/// </summary>
	public Dictionary<string, double> Sections { get; set; } = new();
	public double CheckMilliseconds { get; set; }
	/// <summary>The whole swap: the old stream's end, the install, and the new stream with the frames buffered for it.</summary>
	public double SwapMilliseconds { get; set; }
	/// <summary>
	/// The live updates, ticks and time from the swap until the guest's stream has taken a frame of the host's from after the cut and follows within its window with no frame waiting to
	/// apply (the frames it had when it caught up; a frame still on its way is not known). Null while it has not (a host that publishes nothing after the cut, a paused and idle one,
	/// leaves it null), or no park was swapped in.
	/// </summary>
	public long? CatchUpTicks { get; set; }
	public int? CatchUpFrames { get; set; }
	public double? CatchUpSeconds { get; set; }

	public override string ToString() => string.Create( CultureInfo.InvariantCulture,
		$"join {Generation} {(Outcome.Length == 0 ? "running" : Outcome)}: download {DownloadSeconds:0.000} s ({PayloadBytes / 1024.0:0} KiB, {DownloadAttempts} attempt(s)), decode "
		+ $"{DecodeMilliseconds:0.0} ms; build {BuildFrames} frames (the process's build {ProcessBuild}), longest {LongestBuildFrameMilliseconds:0.0} ms: {string.Join( "; ", Build )}; "
		+ $"check {CheckMilliseconds:0.0} ms, swap {SwapMilliseconds:0.0} ms"
		+ $"{(LongSteps.Count > 0 ? "; long steps " + string.Join( ", ", LongSteps ) : "")}"
		+ $"{(Sections.Count > 0 ? "; sections " + string.Join( ", ", Sections.OrderByDescending( item => item.Value ).Select( item => FormattableString.Invariant( $"{item.Key} {item.Value:0.0}" ) ) ) : "")}" );
}
