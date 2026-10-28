using System.Text.Json;
using OpenTPW.Online.Coop;

namespace OpenTPW;

internal sealed partial class CoopJoinSmokeTest
{
	/// <summary>The host frames the replay delivers per join frame: the recorded pace, twice over (the guest catches up within its own budget).</summary>
	private const int Pace = 2;
	/// <summary>The most join frames the guest may take to reach the host's last tick once every frame is out.</summary>
	private const int CatchUpFrames = 3600;
	/// <summary>
	/// The save's parts a session's peers may hold apart, by their full paths: presentation, which no simulation reads and no digest covers (docs/COOP.md, "Checks"). The economy part carries
	/// the world streams with their own copy of the sound seed.
	/// </summary>
	private static readonly HashSet<string> PresentationParts = new( StringComparer.Ordinal ) { "$.SavedUtc", "$.Particles", "$.SnowCover", "$.MessageTube", "$.SoundSeed", "$.Economy.World.SoundSeed" };

	/// <summary>The first capture's generations: the wrong digest's, the one abandoned part way through its build, and the one swapped in.</summary>
	private const uint WrongGeneration = 1, AbandonedGeneration = 2, JoinedGeneration = 3;
	/// <summary>The second capture's resync.</summary>
	private const uint ResyncGeneration = 4;

	private enum JoinStep { FrontEnd, Joining, Following, Approaching, Resyncing, Resynced, CatchingUp, Done }

	private JoinStep joinStep;
	private CoopSmokeHostEvidence hostEvidence = null!;
	private CoopSmokeReplayTransport replay = null!;
	private CoopSession? joinSession;
	private long paceFrame;
	private int stepFrame;
	private List<Entity>? frontEndScene;
	/// <summary>The front end's entities when the join began: no build may add to them or take from them.</summary>
	private HashSet<Entity>? frontEndEntities;
	/// <summary>The first (wrong) capture's build: once over, its level steps are all the steps of the capture's level, which every build of the capture takes alike.</summary>
	private ICoopJoinBuild? firstBuild;
	private bool abandonedMidBuild;
	private Level? firstPark;
	private int requestsAnswered;
	private string? scriptFailure;
	private readonly List<(long HostFrame, CoopStreamMessage Message)> pendingAnswers = new();
	/// <summary>The current generation's assignment and its answer once sent: what a guest that heard nothing is told again, as the host repeats them.</summary>
	private CoopStreamMessage? lastAssigned, lastReady;
	/// <summary>The checkpoints the host published in each recorded frame (the guest stream must compare every one in its range).</summary>
	private Dictionary<ulong, int> checkpointsIn = new();
	private readonly List<string> joinSteps = new();
	private (ulong Digest, ulong World) joinFinal;
	private string[] saveDifferences = Array.Empty<string>();

	private void StartJoin()
	{
		hostEvidence = ReadJson<CoopSmokeHostEvidence>( "host-evidence.json" );
		Require( hostEvidence.Completed && hostEvidence.Failure == null && hostEvidence.Cuts.Length == 2, "read the completed host phase's evidence as the oracle" );
		// the captures are read from the evidence directory by the names the evidence gives
		Require( hostEvidence.Cuts.All( cut => IsEvidenceFileName( cut.File ) ), "the host evidence names its captures by flat file names" );
		var frames = ReadFrames();
		var joinedAt = hostEvidence.Cuts[0].Assigned;
		// the server relays a member the frames after its join
		var after = frames.Where( item => item.Seq > joinedAt ).ToList();
		Require( after.Count > 0 && after[^1].Seq == hostEvidence.LastFrame, "the recorded frames run from the member's join to the host's last frame" );
		replay = new CoopSmokeReplayTransport( after, joinedAt ) { Heard = Heard };
		checkpointsIn = after.ToDictionary( item => item.Seq, item => CoopStreamFrame.Decode( item.Payload ).Checkpoints.Count );
		paceFrame = hostEvidence.Cuts[0].AssignedHostFrame;
		// measuring, the named work of the frames that swap a park in, unless another recording of it runs (a page's performance harness, a hitch report)
		swapMarks = measuring && !FrameMarks.Enabled ? new List<CoopSmokeFrameMarks>() : null;
		buildMarks = swapMarks != null ? new List<CoopSmokeBuildFrame>() : null;
		// a page's start shows the front end already; the native run starts without one
		if ( flow.Menu == null )
			flow.ShowFrontEnd();
		frontEndScene = Entity.All;
		joinStep = JoinStep.FrontEnd;
	}

	/// <summary>The smoke's part of a join frame, before the game's: the replayed host's answers and downloads that are due, the frames at their pace, then the checks of where the join stands.</summary>
	private void JoinUpdate()
	{
		++frame;
		if ( done )
			return;
		try
		{
			stepFrame++;
			// a build that ran past the session's deadline is a slow runner (a slow page, a slow copy of the level's files), not a wrong digest: said as such, before the unexpected
			// request for a capture that follows it
			Require( joinSession?.JoinTimings.All( item => item.Outcome != "timed out" ) ?? true,
				$"every build finished within the session's {CoopSession.BuildDeadlineSeconds} s deadline (a slower run, not a wrong digest)" );
			if ( scriptFailure != null )
				throw new InvalidOperationException( $"Co-op join smoke test failed: {scriptFailure}." );
			if ( joinStep == JoinStep.FrontEnd )
			{
				if ( frame < WarmUpFrames )
					return;
				Require( flow.Level == null && flow.Menu != null, "the game stands in the front end" );
				frontEndEntities = Entity.All.ToHashSet();
				joinSession = flow.JoinSharedPark( replay, () => true );
				Require( flow.HeldJoin == joinSession, "the game holds the join it started" );
				Advance( JoinStep.Joining );
				return;
			}
			paceFrame += Pace;
			foreach ( var due in pendingAnswers.Where( answer => answer.HostFrame <= paceFrame ).ToList() )
			{
				pendingAnswers.Remove( due );
				Answer( due.Message );
			}
			replay.Pump();
			// the frames up to the second cut wait there until the followed park stands at it (its check below), then the rest go out
			replay.DeliverUpTo( paceFrame, joinStep < JoinStep.Resyncing ? hostEvidence.Cuts[1].Frame : ulong.MaxValue );
			var session = joinSession!;
			Require( session.EndedWith == null && replay.LeftWith == null, $"the join's session goes on ({session.EndedWith ?? replay.LeftWith})" );
			Require( session.GuestStream?.Desync == null, $"no desync at a checkpoint ({session.GuestStream?.Desync})" );
			switch ( joinStep )
			{
				case JoinStep.Joining:
					if ( flow.Level == null )
					{
						// every build runs set aside: the front end stays the live scene, whole, and no entity of a build (one abandoned included) is left in it
						Require( ReferenceEquals( Entity.All, frontEndScene ) && Level.Current == null && flow.Menu != null, "the build runs beside the live front end" );
						Require( Entity.All.Count == frontEndEntities!.Count && Entity.All.All( frontEndEntities.Contains ), "no entity of a build is in the front end's scene" );
						if ( session.Building is { } first && requestsAnswered == 1 )
							firstBuild = first;
						// part way through the level's construction, at the first frame with saved objects placed in its scene: while it restores them one a step when the restore
						// spans frames, as it does in a process's first build, else just after (a warm one may restore them in one frame's slice); the check above must find none
						// of them left behind
						else if ( session.Building is { PlacedObjects: > 0 } second && requestsAnswered == 2 && !abandonedMidBuild && !measuring )
							ReassignMidBuild( session, second );
					}
					if ( session.Phase == CoopGuestPhase.Following && flow.Level != null )
					{
						// the first answer's wrong digest failed its check at the cut; the second's build was abandoned part way for a newer assignment; the third built and swapped in. Measuring,
						// the untouched first capture built and swapped in at once
						if ( measuring )
							Require( requestsAnswered == 1 && session.FailedJoins == 0 && !abandonedMidBuild, $"the untouched first capture joined at once ({session.FailedJoins} failed joins)" );
						else
							Require( requestsAnswered == 2 && session.FailedJoins == 1 && abandonedMidBuild,
								$"one failed check and one abandoned build before the swap ({session.FailedJoins} failed joins, {requestsAnswered} answers, abandoned {abandonedMidBuild})" );
						Require( ReferenceEquals( flow.Level, session.Level ) && flow.HeldJoin == null && flow.Menu == null && flow.Hud != null, "the join's park is the game's, with its HUD" );
						Require( frontEndScene != null && frontEndScene.Count == 0 && !ReferenceEquals( Entity.All, frontEndScene ), "the swap retired the front end's scene" );
						Require( flow.Level!.IsCoopGuestPark && flow.Level.CoopSession == session, "a guest's park in the session" );
						firstPark = flow.Level;
						joinSteps.Add( measuring ? $"joined at tick {flow.Level.TickCount} from the untouched first capture" : $"joined at tick {flow.Level.TickCount} after one failed check" );
						Advance( JoinStep.Following );
					}
					else if ( session.FailedJoins == 1 && joinSteps.Count == 0 )
					{
						// the failed build was abandoned beside the live front end
						Require( flow.Level == null && flow.Menu != null && ReferenceEquals( Entity.All, frontEndScene ) && frontEndScene!.Count > 0,
							"the failed check left the front end live and whole" );
						joinSteps.Add( "the wrong digest failed its check at the cut; the front end stayed" );
					}
					return;
				case JoinStep.Following:
					if ( paceFrame >= hostEvidence.Cuts[1].AssignedHostFrame && replay.Delivered >= hostEvidence.Cuts[1].Frame )
						Advance( JoinStep.Approaching );
					return;
				case JoinStep.Approaching:
				{
					// the followed park reaches the second cut (paused, with edits since its last tick, which only the records bring here): the host's digest and world hash there, and
					// every checkpoint of its stream compared
					var resync = hostEvidence.Cuts[1];
					var followed = flow.Level!;
					var stream = session.GuestStream!;
					if ( followed.TickCount < resync.Ticks || stream.LastRecord < resync.Records || followed.Commands.ScheduledCount > 0 )
					{
						Require( stepFrame < CatchUpFrames, $"the followed park reached the second cut (tick {followed.TickCount} of {resync.Ticks}, record {stream.LastRecord} of {resync.Records})" );
						return;
					}
					var digest = CoopDigest.Compute( followed, out var world );
					Require( followed.TickCount == resync.Ticks && stream.LastRecord == resync.Records && (digest, world) == (resync.Digest, resync.WorldHash),
						$"the followed park stands at the second cut as the host did ({digest:X16}/{resync.Digest:X16}, {world:X16}/{resync.WorldHash:X16})" );
					var expected = checkpointsIn.Where( pair => pair.Key > hostEvidence.Cuts[0].Frame && pair.Key <= resync.Frame ).Sum( pair => pair.Value );
					Require( stream.Checks == expected && expected > 0, $"the followed stream compared every checkpoint of its frames ({stream.Checks} of {expected})" );
					joinSteps.Add( $"followed to the second cut, {stream.Checks} checkpoints compared" );
					// the host assigns the member the second capture (it asked for one), then answers it once uploaded; measuring, the capture as the host wrote it
					var generation = measuring ? resync.Generation : ResyncGeneration;
					Answer( new CoopSnapshotAssigned( resync.Assigned ) { Generation = generation } );
					var recorded = File.ReadAllBytes( Path.Combine( evidenceDirectory, resync.File ) );
					var payload = measuring ? recorded : Rewrite( recorded, ResyncGeneration );
					var sha = replay.Serve( payload );
					pendingAnswers.Add( (resync.ReadyHostFrame, new CoopSnapshotReady( resync.Records, resync.Ticks, resync.Frame, resync.Digest, resync.WorldHash, sha, payload.Length )
						{ Generation = generation }) );
					Advance( JoinStep.Resyncing );
					return;
				}
				case JoinStep.Resyncing:
					if ( session.Phase == CoopGuestPhase.Following && session.GuestStream?.Generation == (measuring ? hostEvidence.Cuts[1].Generation : ResyncGeneration) )
					{
						Require( !ReferenceEquals( flow.Level, firstPark ) && ReferenceEquals( flow.Level, session.Level ) && flow.Hud != null, "the resync swapped the host's park in over the old one" );
						Require( session.FailedJoins == (measuring ? 0 : 1), "the resync was no failed join" );
						joinSteps.Add( $"resynced at tick {flow.Level!.TickCount}" );
						Advance( JoinStep.Resynced );
					}
					return;
				case JoinStep.Resynced:
					if ( replay.AllDelivered )
						Advance( JoinStep.CatchingUp );
					return;
				case JoinStep.CatchingUp:
					var level = flow.Level!;
					if ( level.TickCount < hostEvidence.LastTick || session.GuestStream!.LastRecord < hostEvidence.LastRecord || level.Commands.ScheduledCount > 0 )
					{
						Require( stepFrame < CatchUpFrames, $"the guest reached the host's last tick {hostEvidence.LastTick} (at {level.TickCount}, record {session.GuestStream!.LastRecord} of {hostEvidence.LastRecord})" );
						return;
					}
					Require( level.TickCount == hostEvidence.LastTick, "the guest stands exactly at the host's last tick" );
					var last = checkpointsIn.Where( pair => pair.Key > hostEvidence.Cuts[1].Frame ).Sum( pair => pair.Value );
					Require( session.GuestStream.Checks == last && last > 0, $"the resynced stream compared every checkpoint of its frames ({session.GuestStream.Checks} of {last})" );
					joinSteps.Add( $"followed to the end, {last} checkpoints compared" );
					CompareWithHost( level );
					return;
			}
		}
		catch ( Exception error )
		{
			Fail( error );
		}
	}

	/// <summary>
	/// The host assigns the member a newer capture of the same cut while the second's build constructs its level, with entities staged in its scene and steps of its level left: the
	/// session abandons that build at once (the replayed host's messages go out within this frame), and the third is built and swapped in.
	/// </summary>
	private void ReassignMidBuild( CoopSession session, ICoopJoinBuild build )
	{
		var (steps, total, staged, placed, restoring) = (build.LevelSteps, firstBuild?.LevelSteps ?? 0, build.StagedEntities, build.PlacedObjects, build.RestoringObjects);
		Require( staged > 0 && placed > 0 && steps < total, $"the next build is part way through its level ({steps} of {total} steps, {placed} objects placed, {staged} entities staged)" );
		abandonedMidBuild = true;
		var cut = hostEvidence.Cuts[0];
		var payload = Rewrite( File.ReadAllBytes( Path.Combine( evidenceDirectory, cut.File ) ), JoinedGeneration );
		var sha = replay.Serve( payload );
		Answer( new CoopSnapshotAssigned( cut.Assigned ) { Generation = JoinedGeneration } );
		Answer( new CoopSnapshotReady( cut.Records, cut.Ticks, cut.Frame, cut.Digest, cut.WorldHash, sha, payload.Length ) { Generation = JoinedGeneration } );
		replay.Pump();
		Require( !session.IsBuilding && session.Assigned == JoinedGeneration && flow.Level == null, "the newer assignment abandoned the build part way, beside the live front end" );
		joinSteps.Add( $"a newer assignment abandoned the next build {steps} of its level's {total} steps in, {(restoring ? "while it restored" : "just after it restored")} the saved objects "
			+ $"({placed} placed), with {staged} entities staged" );
	}

	/// <summary>
	/// Every build's timings are kept with its outcome, in the order of their generations, and are whole: finite and not negative, each stage that ran timed, the joined and the resynced
	/// park checked, swapped and caught up. The adversarial join has four (a failed check, an abandoned build, a join and a resync), the measuring mode two (a join and a resync).
	/// </summary>
	private void RequireJoinTimings( CoopSession session )
	{
		var timings = session.JoinTimings;
		var expected = measuring ? new[] { "joined", "resynced" } : new[] { "failed its check", "abandoned", "joined", "resynced" };
		Require( timings.Select( item => item.Outcome ).SequenceEqual( expected ), $"every build's timings are kept with its outcome ({string.Join( ", ", timings.Select( item => item.Outcome ) )})" );
		// the process's builds in order, the first its cold one
		Require( timings.Select( item => item.ProcessBuild ).SequenceEqual( Enumerable.Range( 1, timings.Count ) ), "the builds are the process's first ones, in order" );
		foreach ( var timing in timings )
		{
			var values = new[] { timing.DownloadSeconds, timing.LastAttemptSeconds, timing.DecodeMilliseconds, timing.LongestBuildFrameMilliseconds, timing.CheckMilliseconds,
				timing.SwapMilliseconds, timing.CatchUpSeconds ?? 0 }.Concat( timing.Build.SelectMany( stage => new[] { stage.Milliseconds, stage.LongestMilliseconds } ) ).Concat( timing.Sections.Values );
			Require( values.All( value => double.IsFinite( value ) && value >= 0 ), $"build {timing.Generation}'s timings are finite and not negative ({timing})" );
			Require( timing.DownloadAttempts == 1 && timing.PayloadBytes > 0 && timing.BuildFrames > 0
				&& timing.Build.Select( stage => stage.Name ).SequenceEqual( new[] { "codec", "save", "files", "catalogue", "manifest", "validation", "economy", "level", "hud", "advisor",
					"prepare", "digest" } )
				&& timing.Build.Take( 2 ).All( stage => stage.Status == CoopTimingStatus.Measured ),
				$"build {timing.Generation} was downloaded once and built over frames, every stage listed ({timing})" );
			Require( timing.Build.All( stage => stage.Status != CoopTimingStatus.Measured || stage.Steps > 0 && stage.Frames > 0 && stage.Frames <= timing.BuildFrames ),
				$"every stage that ran was timed within the build's frames ({timing})" );
			// a stage of one step ends before its yield: the next step's start, which only begins the next stage, is no part of it; the advisor's warms its message tables a step
			// each first (AdvisorTables.WarmSteps)
			Require( timing.Build.Where( stage => stage.Name is "economy" or "hud" or "advisor" )
				.All( stage => stage.Steps == (stage.Status == CoopTimingStatus.Skipped ? 0 : stage.Name == "advisor" ? AdvisorTables.WarmStepCount + 1 : 1) ),
				$"the economy and the HUD ran one step each, the advisor its tables' warm steps and one ({timing})" );
			// the save's parse a slice of its bytes a step, then its checks in theirs (ParkResumeSaveFile.DeserializeSteps)
			Require( timing.Build.Single( stage => stage.Name == "save" ).Steps >= 3, $"the save's parse ran a step a slice of its bytes and its checks their own ({timing})" );
			// the wait for the level's files asks once a slice and leaves the rest of it to the live scene
			Require( timing.Build.Single( stage => stage.Name == "files" ) is { } files && files.Steps == files.Frames,
				$"the wait for the level's files asked once a frame ({timing})" );
			// a part that alone took the budget: of a stage that ran, once, within its parts and its longest, the level's with its progress; each stage's longest part among them
			Require( timing.LongSteps.All( step => timing.Build.SingleOrDefault( stage => stage.Name == step.Stage ) is { } stage && step.Step >= 1 && step.Step <= stage.Steps
					&& double.IsFinite( step.Milliseconds ) && step.Milliseconds >= GameFlow.ParkBuildBudgetMilliseconds && step.Milliseconds <= stage.LongestMilliseconds
					&& (step.Stage == "level" ? step.Progress is >= 0 and <= 1 : step.Progress == null) )
				&& timing.LongSteps.DistinctBy( step => (step.Stage, step.Step) ).Count() == timing.LongSteps.Count
				&& timing.Build.All( stage => stage.LongestMilliseconds < GameFlow.ParkBuildBudgetMilliseconds
					|| timing.LongSteps.Any( step => step.Stage == stage.Name && step.Milliseconds == stage.LongestMilliseconds ) ),
				$"the parts that alone took the budget are listed once with their stages, each stage's longest among them ({timing})" );
			if ( timing.Outcome is "joined" or "resynced" )
			{
				Require( timing.Build.Single( stage => stage.Name == "level" ).Steps > 0 && timing.Build.Single( stage => stage.Name == "hud" ).Status == CoopTimingStatus.Measured
					&& timing.CatchUpTicks != null && timing.CatchUpFrames > 0 && timing.CatchUpSeconds != null && timing.Sections.ContainsKey( "resume" ) && timing.Sections.ContainsKey( "join-state" ),
					$"build {timing.Generation} ran every stage, was swapped in and caught up ({timing})" );
			}
		}
		var worst = updateFrames.Where( item => item.Step is nameof( JoinStep.Joining ) or nameof( JoinStep.Resyncing ) ).MaxBy( item => item.Milliseconds );
		if ( worst != null )
			joinSteps.Add( $"longest update frame while building {worst.Milliseconds:0.0} ms (frame {worst.Frame}, {worst.Step})" );
	}

	/// <summary>Sends a host message, kept as the generation's last assignment or answer.</summary>
	private void Answer( CoopStreamMessage message )
	{
		if ( message is CoopSnapshotAssigned )
			lastAssigned = message;
		else if ( message is CoopSnapshotReady )
			lastReady = message;
		replay.Answer( message );
	}

	private void Advance( JoinStep step )
	{
		joinStep = step;
		stepFrame = 0;
	}

	/// <summary>
	/// The replayed host's answers to what the game sends: the join request gets the first capture with a wrong digest at its cut, the failed check's request the same cut, right, under a
	/// new generation; a desync, a stopped stream or another request fails the smoke (the script is checked at the next frame, outside the session's own call).
	/// </summary>
	private void Heard( CoopStreamMessage message )
	{
		var cut = hostEvidence.Cuts[0];
		switch ( message )
		{
			case CoopSnapshotRequest { Reason: CoopSnapshotReason.Join } when requestsAnswered == 0 && measuring:
			{
				// the capture as the host wrote it, under its own generation
				requestsAnswered = 1;
				var payload = File.ReadAllBytes( Path.Combine( evidenceDirectory, cut.File ) );
				var sha = replay.Serve( payload );
				Answer( new CoopSnapshotAssigned( cut.Assigned ) { Generation = cut.Generation } );
				pendingAnswers.Add( (cut.ReadyHostFrame, new CoopSnapshotReady( cut.Records, cut.Ticks, cut.Frame, cut.Digest, cut.WorldHash, sha, payload.Length ) { Generation = cut.Generation }) );
				break;
			}
			case CoopSnapshotRequest { Reason: CoopSnapshotReason.Join } when requestsAnswered == 0:
			{
				requestsAnswered = 1;
				var digest = cut.Digest ^ 0x5A5A_5A5A_5A5A_5A5AUL;
				var tampered = Rewrite( File.ReadAllBytes( Path.Combine( evidenceDirectory, cut.File ) ), WrongGeneration, digest );
				var sha = replay.Serve( tampered );
				Answer( new CoopSnapshotAssigned( cut.Assigned ) { Generation = WrongGeneration } );
				pendingAnswers.Add( (cut.ReadyHostFrame, new CoopSnapshotReady( cut.Records, cut.Ticks, cut.Frame, digest, cut.WorldHash, sha, tampered.Length ) { Generation = WrongGeneration }) );
				break;
			}
			case CoopSnapshotRequest { Reason: CoopSnapshotReason.FailedCheck, Known: WrongGeneration } when requestsAnswered == 1:
			{
				requestsAnswered = 2;
				var payload = Rewrite( File.ReadAllBytes( Path.Combine( evidenceDirectory, cut.File ) ), AbandonedGeneration );
				var sha = replay.Serve( payload );
				Answer( new CoopSnapshotAssigned( cut.Assigned ) { Generation = AbandonedGeneration } );
				Answer( new CoopSnapshotReady( cut.Records, cut.Ticks, cut.Frame, cut.Digest, cut.WorldHash, sha, payload.Length ) { Generation = AbandonedGeneration } );
				break;
			}
			case CoopSnapshotRequest { Reason: CoopSnapshotReason.NoAnswer }:
				// a slow machine waited longer than its answer wait: the host repeats what it sent
				if ( lastAssigned != null )
					replay.Answer( lastAssigned );
				if ( lastReady != null && lastReady.Generation == lastAssigned?.Generation )
					replay.Answer( lastReady );
				break;
			case CoopSnapshotRequest request:
				scriptFailure ??= $"the game asked for a capture the script does not expect: {request} after {requestsAnswered} answers";
				break;
			case CoopDesyncReport report:
				scriptFailure ??= $"the guest reported a desync: {report}";
				break;
			case CoopProgress { State: CoopGuestState.Desynced or CoopGuestState.TooFarBack } progress:
				scriptFailure ??= $"the guest's stream stopped: {progress}";
				break;
		}
	}

	/// <summary>The end: the guest's park at the host's last tick has the host's digest, world hash, session state and save, outside what is presentation.</summary>
	private void CompareWithHost( Level level )
	{
		var digest = CoopDigest.Compute( level, out var world );
		joinFinal = (digest, world);
		Require( (digest, world) == (hostEvidence.FinalDigest, hostEvidence.FinalWorldHash),
			$"the guest's park ends with the host's digest and world hash ({digest:X16}/{hostEvidence.FinalDigest:X16}, {world:X16}/{hostEvidence.FinalWorldHash:X16})" );
		Require( level.CaptureJoinState().Encode().AsSpan().SequenceEqual( File.ReadAllBytes( Path.Combine( evidenceDirectory, "host-final-state.bin" ) ) ),
			"the guest's session state ends as the host's" );
		var guestSave = ParkResumeSaveFile.Serialize( level.CaptureJoinSnapshot() );
		File.WriteAllBytes( Path.Combine( evidenceDirectory, "join-final-save.json" ), guestSave );
		var differences = new List<string>();
		using ( var hostSave = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( evidenceDirectory, "host-final-save.json" ) ) ) )
		using ( var joined = JsonDocument.Parse( guestSave ) )
			Differences( hostSave.RootElement, joined.RootElement, "$", differences );
		saveDifferences = differences.ToArray();
		Require( differences.Count == 0, $"the guest's save is the host's outside presentation ({differences.Count} differences; first: {string.Join( "; ", differences.Take( 5 ) )})" );
		RequireJoinTimings( joinSession! );
		WriteJson( "join-evidence.json", JoinEvidence( completed: true, failure: null ) );
		Advance( JoinStep.Done );
		Complete( $"{runner} co-op join smoke (join): {string.Join( "; ", joinSteps )}; ended at tick {level.TickCount} with the host's digest {digest:X16}." );
	}

	/// <summary>The paths where two JSON values differ, the presentation parts left out (at most 50).</summary>
	private static void Differences( JsonElement expected, JsonElement actual, string path, List<string> found )
	{
		if ( found.Count >= 50 )
			return;
		if ( expected.ValueKind != actual.ValueKind )
		{
			found.Add( $"{path}: {expected.ValueKind} / {actual.ValueKind}" );
			return;
		}
		switch ( expected.ValueKind )
		{
			case JsonValueKind.Object:
				var names = expected.EnumerateObject().Select( property => property.Name ).Union( actual.EnumerateObject().Select( property => property.Name ) );
				foreach ( var name in names )
				{
					if ( PresentationParts.Contains( $"{path}.{name}" ) )
						continue;
					var inExpected = expected.TryGetProperty( name, out var left );
					var inActual = actual.TryGetProperty( name, out var right );
					if ( inExpected != inActual )
						found.Add( $"{path}.{name}: {(inExpected ? "only the host's" : "only the guest's")}" );
					else
						Differences( left, right, $"{path}.{name}", found );
				}
				break;
			case JsonValueKind.Array:
				if ( expected.GetArrayLength() != actual.GetArrayLength() )
				{
					found.Add( $"{path}: {expected.GetArrayLength()} / {actual.GetArrayLength()} items" );
					return;
				}
				for ( var index = 0; index < expected.GetArrayLength(); index++ )
					Differences( expected[index], actual[index], $"{path}[{index}]", found );
				break;
			default:
				if ( expected.GetRawText() != actual.GetRawText() )
					found.Add( $"{path}: {Clip( expected.GetRawText() )} / {Clip( actual.GetRawText() )}" );
				break;
		}
	}

	private static string Clip( string text ) => text.Length <= 60 ? text : text[..57] + "...";

	private CoopSmokeJoinEvidence JoinEvidence( bool completed, string? failure ) => new( completed, failure, replay?.Downloads.Count ?? 0, joinSession?.FailedJoins ?? 0, joinSteps.ToArray(),
		joinFinal.Digest, joinFinal.World, saveDifferences, measuring ? "measure" : "join", joinSession?.JoinTimings.ToArray() ?? Array.Empty<CoopJoinTimings>(), updateFrames.ToArray(),
		CoopSmokeEnvironment.Of( flow ), swapMarks?.ToArray(), buildMarks?.ToArray() );

	/// <summary>Every update frame of the phase, timed from before the smoke's driver to after the game's update (docs/COOP.md, "Measurements").</summary>
	private readonly List<CoopSmokeFrame> updateFrames = new();
	private long frameStarted;

	/// <summary>Measuring, the named work of the swaps' frames (null when not recorded); the builds seen in it so far, and the build whose swap's next frame is recorded next.</summary>
	private List<CoopSmokeFrameMarks>? swapMarks;
	private int marksBuilds;
	private string? marksAfter;
	/// <summary>Whether the runner clears the frame's marks at each frame's start itself (a page does, before the frame).</summary>
	private bool runnerClearsMarks;
	/// <summary>Measuring, the builds' long frames with their named work (null when the swaps' work is not recorded either).</summary>
	private List<CoopSmokeBuildFrame>? buildMarks;

	/// <summary>A build's update frame this long or longer is recorded with its named work, measuring: past twice the slice's budget, a step of it ran long.</summary>
	internal const double LongBuildFrameMilliseconds = 30;

	/// <summary>The garbage collections by generation when the frame started (<see cref="CoopSmokeBuildFrame.Collections"/>).</summary>
	private int[] collectionsAtStart = Array.Empty<int>();

	private static int[] Collections() => Enumerable.Range( 0, GC.MaxGeneration + 1 ).Select( GC.CollectionCount ).ToArray();

	private void FrameStarts()
	{
		frameStarted = System.Diagnostics.Stopwatch.GetTimestamp();
		if ( buildMarks != null )
			collectionsAtStart = Collections();
		if ( swapMarks == null || done )
			return;
		// on while a park is built and for the frame after its swap, so the following frames' timings carry no marks' cost
		var wanted = joinStep is JoinStep.Joining or JoinStep.Resyncing || marksAfter != null;
		if ( wanted != FrameMarks.Enabled )
			FrameMarks.Enable( wanted );
		else if ( wanted && !runnerClearsMarks )
			FrameMarks.BeginFrame();
	}

	private void FrameEnds()
	{
		var milliseconds = frameStarted != 0 ? CoopClock.Since( frameStarted ) : 0d;
		if ( frameStarted != 0 )
			updateFrames.Add( new CoopSmokeFrame( frame, joinStep.ToString(), milliseconds ) );
		if ( swapMarks == null || !FrameMarks.Enabled || joinSession == null )
			return;
		// a build that ended in this frame was checked and swapped in by it (its timings close there); the next frame is the first after the swap
		if ( joinSession.JoinTimings.Count > marksBuilds )
		{
			marksBuilds = joinSession.JoinTimings.Count;
			if ( joinSession.JoinTimings[^1].Outcome is "joined" or "resynced" )
			{
				swapMarks.Add( new CoopSmokeFrameMarks( joinSession.JoinTimings[^1].Outcome, "swap", frame, FrameMarks.Snapshot() ) );
				marksAfter = joinSession.JoinTimings[^1].Outcome;
			}
		}
		else if ( marksAfter != null )
		{
			swapMarks.Add( new CoopSmokeFrameMarks( marksAfter, "after", frame, FrameMarks.Snapshot() ) );
			marksAfter = null;
		}
		// a long frame of a build that goes on: what it, and so its long last step, was made of
		else if ( buildMarks != null && milliseconds >= LongBuildFrameMilliseconds && (joinStep is JoinStep.Joining or JoinStep.Resyncing)
			&& joinSession.Building?.Timings?.Stages.LastOrDefault( stage => stage.Steps > 0 ) is { } stage )
			buildMarks.Add( new CoopSmokeBuildFrame( joinStep == JoinStep.Joining ? "joined" : "resynced", frame, milliseconds, stage.Name, stage.Steps, FrameMarks.Snapshot(),
				Collections().Zip( collectionsAtStart, ( now, then ) => now - then ).ToArray() ) );
	}

	/// <summary>The recording of the swaps' work ends with the run, whatever ends it.</summary>
	private void StopMarks()
	{
		if ( swapMarks != null && FrameMarks.Enabled )
			FrameMarks.Enable( false );
		marksAfter = null;
	}
}
