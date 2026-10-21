using System.Text.Json;
using OpenTPW.Online.Coop;

namespace OpenTPW;

internal sealed partial class CoopJoinSmokeTest
{
	/// <summary>The host frames the replay delivers per join frame: the recorded pace, twice over (the guest catches up within its own budget).</summary>
	private const int Pace = 2;
	/// <summary>The most join frames the guest may take to reach the host's last tick once every frame is out.</summary>
	private const int CatchUpFrames = 3600;
	/// <summary>The save's parts a session's peers may hold apart: presentation, which no simulation reads and no digest covers (docs/COOP.md, "Checks").</summary>
	private static readonly HashSet<string> PresentationParts = new( StringComparer.Ordinal ) { "SavedUtc", "Particles", "SnowCover", "SoundSeed" };

	private enum JoinStep { FrontEnd, Joining, Following, Approaching, Resyncing, Resynced, CatchingUp, Done }

	private JoinStep joinStep;
	private CoopSmokeHostEvidence hostEvidence = null!;
	private CoopSmokeReplayTransport replay = null!;
	private CoopSession? joinSession;
	private long paceFrame;
	private int stepFrame;
	private List<Entity>? frontEndScene;
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
		var frames = ReadFrames();
		var joinedAt = hostEvidence.Cuts[0].Assigned;
		// the server relays a member the frames after its join
		var after = frames.Where( item => item.Seq > joinedAt ).ToList();
		Require( after.Count > 0 && after[^1].Seq == hostEvidence.LastFrame, "the recorded frames run from the member's join to the host's last frame" );
		replay = new CoopSmokeReplayTransport( after, joinedAt ) { Heard = Heard };
		checkpointsIn = after.ToDictionary( item => item.Seq, item => CoopStreamFrame.Decode( item.Payload ).Checkpoints.Count );
		paceFrame = hostEvidence.Cuts[0].AssignedHostFrame;
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
			if ( scriptFailure != null )
				throw new InvalidOperationException( $"Native co-op join smoke test failed: {scriptFailure}." );
			if ( joinStep == JoinStep.FrontEnd )
			{
				if ( frame < WarmUpFrames )
					return;
				Require( flow.Level == null && flow.Menu != null, "the game stands in the front end" );
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
					if ( session.Phase == CoopGuestPhase.Following && flow.Level != null )
					{
						// the first answer's wrong digest failed its check at the cut; the second built and swapped in
						Require( requestsAnswered == 2 && session.FailedJoins == 1, $"one failed check before the swap ({session.FailedJoins} failed joins, {requestsAnswered} answers)" );
						Require( ReferenceEquals( flow.Level, session.Level ) && flow.HeldJoin == null && flow.Menu == null && flow.Hud != null, "the join's park is the game's, with its HUD" );
						Require( frontEndScene != null && frontEndScene.Count == 0 && !ReferenceEquals( Entity.All, frontEndScene ), "the swap retired the front end's scene" );
						Require( flow.Level!.IsCoopGuestPark && flow.Level.CoopSession == session, "a guest's park in the session" );
						firstPark = flow.Level;
						joinSteps.Add( $"joined at tick {flow.Level.TickCount} after one failed check" );
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
					// the host assigns the member the second capture (it asked for one), then answers it once uploaded
					Answer( new CoopSnapshotAssigned( resync.Assigned ) { Generation = 3 } );
					var payload = Rewrite( File.ReadAllBytes( Path.Combine( evidenceDirectory, resync.File ) ), 3 );
					var sha = replay.Serve( payload );
					pendingAnswers.Add( (resync.ReadyHostFrame, new CoopSnapshotReady( resync.Records, resync.Ticks, resync.Frame, resync.Digest, resync.WorldHash, sha, payload.Length ) { Generation = 3 }) );
					Advance( JoinStep.Resyncing );
					return;
				}
				case JoinStep.Resyncing:
					if ( session.Phase == CoopGuestPhase.Following && session.GuestStream?.Generation == 3 )
					{
						Require( !ReferenceEquals( flow.Level, firstPark ) && ReferenceEquals( flow.Level, session.Level ) && flow.Hud != null, "the resync swapped the host's park in over the old one" );
						Require( session.FailedJoins == 1, "the resync was no failed join" );
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
			case CoopSnapshotRequest { Reason: CoopSnapshotReason.Join } when requestsAnswered == 0:
			{
				requestsAnswered = 1;
				var digest = cut.Digest ^ 0x5A5A_5A5A_5A5A_5A5AUL;
				var tampered = Rewrite( File.ReadAllBytes( Path.Combine( evidenceDirectory, cut.File ) ), 1, digest );
				var sha = replay.Serve( tampered );
				Answer( new CoopSnapshotAssigned( cut.Assigned ) { Generation = 1 } );
				pendingAnswers.Add( (cut.ReadyHostFrame, new CoopSnapshotReady( cut.Records, cut.Ticks, cut.Frame, digest, cut.WorldHash, sha, tampered.Length ) { Generation = 1 }) );
				break;
			}
			case CoopSnapshotRequest { Reason: CoopSnapshotReason.FailedCheck, Known: 1 } when requestsAnswered == 1:
			{
				requestsAnswered = 2;
				var payload = Rewrite( File.ReadAllBytes( Path.Combine( evidenceDirectory, cut.File ) ), 2 );
				var sha = replay.Serve( payload );
				Answer( new CoopSnapshotAssigned( cut.Assigned ) { Generation = 2 } );
				Answer( new CoopSnapshotReady( cut.Records, cut.Ticks, cut.Frame, cut.Digest, cut.WorldHash, sha, payload.Length ) { Generation = 2 } );
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
		WriteJson( "join-evidence.json", JoinEvidence( completed: true, failure: null ) );
		Advance( JoinStep.Done );
		Complete( $"Native co-op join smoke (join): {string.Join( "; ", joinSteps )}; ended at tick {level.TickCount} with the host's digest {digest:X16}." );
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
					if ( PresentationParts.Contains( name ) )
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
		joinFinal.Digest, joinFinal.World, saveDifferences );
}
