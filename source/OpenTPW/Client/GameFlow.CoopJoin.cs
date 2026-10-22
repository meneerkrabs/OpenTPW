using System.Diagnostics;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.Online.Coop;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-snapshot-join] The game's side of a guest's join (docs/COOP.md, "Joining a running session"): the join host that builds the host's park from its join snapshot over
/// frames, in the join restore mode, in a scene set aside beside the live one, and swaps it in at the session's step; and the lifecycle of a join that has no park on this machine yet.
/// The screens that start a join are C2d's: <see cref="JoinSharedPark(CoopLink)"/> and <see cref="FollowSharedPark(CoopLink)"/> are their entries.
/// </summary>
internal sealed partial class GameFlow
{
	/// <summary>The reason a session ends with when the player goes elsewhere while it is joined or built.</summary>
	internal const string LeftSharedPark = "The player left the shared park.";

	/// <summary>A shared park's session that has no park on this machine yet (a join under way); null otherwise. Its park, once swapped in, holds it as <see cref="Level.CoopSession"/>.</summary>
	private CoopSession? coopJoin;

	/// <summary>Set while this game ends a join itself (the player went elsewhere): its end shows no failure then.</summary>
	private bool endingJoin;

	/// <summary>This game's asset manifest as the last join hashed it, kept as <see cref="Level.AutosaveAssets"/> keeps a park's (the catalogue and its entry count).</summary>
	private (ObjectCatalog Catalog, int Entries, IReadOnlyDictionary<string, string> Manifest)? joinAssets;

	/// <summary>The join under way without a park (tests and the smoke).</summary>
	internal CoopSession? HeldJoin => coopJoin;

	/// <summary>Test seam: builds a join's candidate instead of the game's restore (the asset-free tests' replay twin, staged in a scene of its own).</summary>
	internal Func<CoopJoinPayload, IGameJoinBuild>? CoopJoinBuildOverride { get; set; }

	/// <summary>Test seam: the clock of the sessions this game joins (the session's own otherwise).</summary>
	internal Func<double>? CoopSessionClock { get; set; }

	/// <summary>
	/// Joins the running session <paramref name="link"/> is in: the guest asks for the host's park, builds it over frames and enters it, leaving the front end. Refused while a park is
	/// open, a join is held, a park is being built or a transition waits.
	/// </summary>
	internal CoopSession JoinSharedPark( CoopLink link ) => JoinSharedPark( link, () => ModsMatchHost( link ) );

	/// <summary><see cref="JoinSharedPark(CoopLink)"/> over any transport, with the check that this game's simulation mods are the host's.</summary>
	internal CoopSession JoinSharedPark( ICoopStreamTransport link, Func<bool> modsMatch )
	{
		ArgumentNullException.ThrowIfNull( modsMatch );
		if ( Level != null )
			throw new InvalidOperationException( "Leave the park before joining a shared park." );
		RequireNoTransition();
		var session = CoopSession.JoinRunning( link, new CoopJoinHost( this, modsMatch ), CoopSessionClock );
		coopJoin = session;
		session.Ended += reason => OnJoinEnded( session, reason );
		return session;
	}

	/// <summary>
	/// Follows the session <paramref name="link"/> is in with the open park, which must be the host's park at the session's identical start (C2b): with the game's join host, a park that
	/// loses the stream gets the host's park again and swaps it in.
	/// </summary>
	internal CoopSession FollowSharedPark( CoopLink link ) => FollowSharedPark( link, () => ModsMatchHost( link ) );

	/// <summary><see cref="FollowSharedPark(CoopLink)"/> over any transport.</summary>
	internal CoopSession FollowSharedPark( ICoopStreamTransport link, Func<bool> modsMatch )
	{
		ArgumentNullException.ThrowIfNull( modsMatch );
		var level = Level ?? throw new InvalidOperationException( "Open the shared park's start before following it." );
		RequireNoTransition();
		var session = CoopSession.Join( level, link, CoopSessionClock, new CoopJoinHost( this, modsMatch ) );
		session.Ended += reason => OnJoinEnded( session, reason );
		return session;
	}

	private void RequireNoTransition()
	{
		if ( coopJoin != null )
			throw new InvalidOperationException( "A shared park is being joined." );
		if ( parkBuild != null || pending != null )
			throw new InvalidOperationException( "A park is being started." );
	}

	/// <summary>The link's join already required the host's simulation mods to be this game's (decision Q16); the build checks it again at the capture it builds.</summary>
	private static bool ModsMatchHost( CoopLink link ) => link.HostMods is { } host && host.SequenceEqual( link.Identity().Mods );

	/// <summary>
	/// The co-op session's step, once a frame right after the online pump and before the HUD's and the level's update. Not while a park is built over frames or a transition waits: one
	/// construction scene at a time, and a queued transition would tear the guest's park down after the swap (the transition itself ends a join, <see cref="LeaveJoinBuild"/>).
	/// </summary>
	internal void StepCoopSession()
	{
		if ( parkBuild != null || pending != null )
			return;
		(Level?.CoopSession ?? coopJoin)?.Step();
	}

	/// <summary>
	/// A transition to another park (a park start, a load, a visit) ends the join it leaves before it builds anything: a held join that has no park yet, and a session whose build holds
	/// the one construction scene (a resyncing guest's).
	/// </summary>
	private void LeaveJoinBuild()
	{
		if ( coopJoin is { } joining )
			EndJoin( joining, LeftSharedPark );
		else if ( Level?.CoopSession is { IsBuilding: true } building )
			EndJoin( building, LeftSharedPark );
	}

	/// <summary>Ends a session this game leaves itself: its end shows no failure.</summary>
	private void EndJoin( CoopSession session, string reason )
	{
		endingJoin = true;
		try { session.End( reason ); }
		finally { endingJoin = false; }
	}

	/// <summary>
	/// A session this game joined ended. One that never had a park on this machine leaves the player in the front end, with the reason unless this game ended it: on the menu's stack, or
	/// with the front end queued when a failed swap already retired it.
	/// </summary>
	private void OnJoinEnded( CoopSession session, string reason )
	{
		if ( ReferenceEquals( coopJoin, session ) )
			coopJoin = null;
		if ( endingJoin || session.Level != null || Level != null )
			return;
		Log.Warning( $"Joining the shared park ended: {reason}" );
		if ( Menu != null )
			ShowJoinFailure( Menu.Stack, reason );
		else
			Queue( () =>
			{
				ShowFrontEnd();
				if ( Menu != null )
					ShowJoinFailure( Menu.Stack, reason );
			} );
	}

	private void ShowJoinFailure( UiScreenStack stack, string reason )
	{
		UiScreen? screen = null;
		screen = UiDialogs.Message( "coopJoinEnded", () => reason, (() => Strings.Extra( OpenTpwText.Back ), () =>
		{
			if ( !screen!.IsRemoved && ReferenceEquals( stack.Top, screen ) )
				stack.Pop();
		}) );
		stack.Push( screen );
	}

	/// <summary>The live park's camera wherever it is kept now: the camera itself, or the one set aside while the globe is shown over the park, the ride view is open or the player walks.</summary>
	private ParkCameraMode? LiveParkCamera( Level level ) =>
		Camera.Mode as ParkCameraMode ?? parkCamera as ParkCameraMode ?? level.ParkCameraWhileRiding ?? level.ParkCameraWhileWalking;

	/// <summary>
	/// The swap (docs/COOP.md, "Joining a running session"): the live scene (the front end, or a resyncing guest's old park, which the session detached from first) is torn down as a park
	/// entry tears it down, without ending the joining session, and the candidate's scene is activated and entered. Only work that cannot throw follows the activation: a throw before it
	/// leaves the candidate the session's to abandon. Not run: the progress (a guest's park is the host's), the restart snapshot and a saved sound (a guest's park has neither).
	/// </summary>
	private void InstallJoin( Level? old, IGameJoinBuild build )
	{
		// a resync keeps the player's view: read before the teardown leaves the ride view and the free walk and drops the camera the globe set aside
		var pose = old != null && LiveParkCamera( old ) is { } camera ? (camera.Target, camera.Height, camera.Yaw) : default( (Vector3 Target, float Height, float Yaw)? );
		// a first join's session has no park yet: the teardown below must not end it
		coopJoin = null;
		var entry = build.Stage();
		var retired = Advisor;
		TearDown( applyPendingLanguage: false );
		Advisor = null;
		if ( retired != null && !ReferenceEquals( retired, entry.Advisor ) )
		{
			try { retired.Dispose(); }
			catch ( Exception error ) when ( error is not OutOfMemoryException ) { Log.Warning( $"The advisor of the park left could not be released: {error.Message}" ); }
		}
		entry.Scene.Activate();
		build.MarkInstalled();
		var level = entry.Level;
		Level = level;
		StartKind = entry.StartKind;
		// what it took from the input of the frame it was made in is taken from this one
		entry.Hud.RefreshEntryInput();
		Hud = entry.Hud;
		Advisor = entry.Advisor;
		// a resync greets as an entry does: a fresh advisor, as an entry decides it (the host's advice is its own player's)
		if ( entry.AdvisorTried )
			advisorUnavailable = entry.Advisor == null;
		AttachGuestSounds( level );
		try
		{
			GameAudio.EnterPark( entry.Theme, entry.SoundSeed );
			GameAudio.Events?.Reseed( entry.SoundSeed );
			// the restored scream handles had no chain in the candidate: they scream again now that the park's audio runs
			level.ResumeHeldScreams();
		}
		catch ( Exception error ) when ( error is not OutOfMemoryException )
		{
			Log.Warning( $"Shared park entered; audio could not start: {error.Message}" );
		}
		if ( pose is { } kept && Camera.Mode is ParkCameraMode fresh )
			fresh.SetPose( kept.Target, kept.Height, kept.Yaw );
		Log.Trace( $"Entered the shared park ({entry.Theme}) at tick {level.TickCount}: {level.Objects.Objects.Count} objects, {level.Guests?.Guests.Count ?? 0} guests." );
	}

	/// <summary>[EXT:online-coop-snapshot-join] The game's join host of one session: its builds and its swap.</summary>
	private sealed class CoopJoinHost( GameFlow flow, Func<bool> modsMatch ) : ICoopJoinHost
	{
		public Func<bool> ModsMatch { get; } = modsMatch;

		public ICoopJoinBuild Build( CoopJoinPayload payload ) => flow.CoopJoinBuildOverride?.Invoke( payload ) ?? new CoopJoinBuild( flow, this, payload );

		public void Install( Level? old, ICoopJoinBuild build ) => flow.InstallJoin( old, (IGameJoinBuild)build );
	}

	/// <summary>
	/// The game's build of a join snapshot over frames, within the park build's frame budget, in <see cref="ResumeStrict"/>'s order: the save parsed; the mods compared; the level's files
	/// waited for; this game's files hashed (once a catalogue) and compared; the snapshot validated; the economy restored; then, in a scene set aside, the level's construction in the
	/// join restore mode, the park HUD and a fresh advisor. Between slices the scene waits, paused, and the live one runs its frames; it waits so until the swap.
	/// </summary>
	private sealed class CoopJoinBuild : IGameJoinBuild
	{
		private readonly GameFlow flow;
		private readonly CoopJoinHost host;
		private readonly CoopJoinPayload payload;
		private readonly IEnumerator<bool> steps;
		private ParkResumeSnapshot? snapshot;
		private EntityConstructionScope? scene;
		private Level? candidate;
		private ParkHud? hud;
		private int hudLanguage;
		private AutomaticAdvisor? advisor;
		private bool advisorTried;

		public int LevelSteps { get; private set; }
		private bool complete;
		private bool abandoned;
		private bool installed;

		public CoopJoinBuild( GameFlow flow, CoopJoinHost host, CoopJoinPayload payload )
		{
			this.flow = flow;
			this.host = host;
			this.payload = payload;
			steps = Steps().GetEnumerator();
		}

		public Level Candidate => complete && candidate != null ? candidate : throw new InvalidOperationException( "The shared park is not built yet." );

		public bool Step()
		{
			if ( abandoned || installed )
				throw new InvalidOperationException( "The shared park's build is over." );
			if ( complete )
				return true;
			var started = Stopwatch.GetTimestamp();
			bool OverBudget() => Stopwatch.GetElapsedTime( started ).TotalMilliseconds >= ParkBuildBudgetMilliseconds;
			scene?.Resume();
			try
			{
				while ( !OverBudget() )
				{
					if ( !steps.MoveNext() )
					{
						complete = true;
						break;
					}
				}
			}
			finally
			{
				// the live scene runs its frames between the build's: the candidate waits set aside, until the swap once it is complete
				scene?.Pause();
			}
			// the check at the cut and the swap get a frame of their own when this one is spent
			return complete && !OverBudget();
		}

		private IEnumerable<bool> Steps()
		{
			var saved = ParkResumeSaveFile.Deserialize( payload.Save );
			if ( saved.Gate == null || saved.Staff == null )
				throw new InvalidDataException( "The shared park's save is missing its gate or staff-world state." );
			// a completed comparison: every build of a capture of this session would meet the difference
			if ( !host.ModsMatch() )
				throw new CoopJoinRefusedException( "This game's simulation mods differ from the host's." );
			try { ParkResumeMods.Require( saved.Mods, OpenTpwExtensions.Active ); }
			catch ( InvalidDataException error ) { throw new CoopJoinRefusedException( $"This game's simulation mods differ from the host's park: {error.Message}" ); }
			snapshot = saved;
			yield return true;
			// a browser copies a level's files on first use: the build waits for them
			while ( LevelDataReady?.Invoke( saved.Theme ) == false )
				yield return true;
			var easy = saved.Economy.Easy;
			ObjectCatalog? loaded = null;
			foreach ( var step in ObjectCatalog.LoadSteps( saved.Theme, easy ) )
			{
				loaded = step;
				yield return true;
			}
			var catalog = loaded ?? throw new InvalidOperationException( "The shared park's object catalogue did not load." );
			var settings = BalanceSettings.Load( saved.Theme, easy );
			var economyCatalog = EconomyObjectCatalog.Load( saved.Theme, easy );
			yield return true;
			IReadOnlyDictionary<string, string> assets;
			if ( flow.joinAssets is { } kept && ReferenceEquals( kept.Catalog, catalog ) && kept.Entries == catalog.Entries.Count )
				assets = kept.Manifest;
			else
			{
				var entries = catalog.Entries.Count;
				var manifest = new SortedDictionary<string, string>( StringComparer.Ordinal );
				foreach ( var step in ParkResumeAssetManifest.CaptureSteps( catalog, settings.Theme, settings.IsEasy, manifest ) )
					yield return step;
				assets = manifest;
				// the catalogue did not change while the steps ran between frames
				if ( catalog.Entries.Count == entries )
					flow.joinAssets = (catalog, entries, manifest);
			}
			// both manifests are complete here: a difference is this game's files, and every build of a capture would meet it
			try { ParkResumeSaveFile.RequireAssets( saved, catalog, assets ); }
			catch ( InvalidDataException error ) { throw new CoopJoinRefusedException( $"This game's files differ from the host's: {error.Message}" ); }
			foreach ( var step in ParkResumeSaveFile.ValidateSnapshotSteps( saved, catalog, settings, economyCatalog, assets ) )
				yield return step;
			var economy = ParkSaveFile.Restore( saved.Economy, settings, economyCatalog );
			yield return true;
			scene = new EntityConstructionScope();
			candidate = Level.BeginJoinOverFrames( saved, economy, flow.OnlineFolders, payload.State );
			candidate.ShowDeveloperPanels = false;
			yield return true;
			while ( candidate.Step() )
			{
				LevelSteps++;
				yield return true;
			}
			// in the candidate's scene, as an entry makes them in the park's: the HUD, then a fresh advisor as an entry decides it
			hud = flow.CreateParkHud( candidate, flow.HudStatusFor( candidate ) );
			hudLanguage = GameLanguage.Version;
			yield return true;
			if ( flow.AutomaticAdvisorEnabled && !flow.advisorUnavailable )
			{
				try
				{
					advisor = AutomaticAdvisor.TryCreate();
					// an entry's rule: a game whose advisor cannot be made tries no more (the swap records it); a throw below is this build's alone
					advisorTried = true;
					if ( advisor != null )
					{
						advisor.AttachLevel( candidate );
						advisor.CaptionSink = hud.PostAdvisorCaption;
						advisor.PlayerTickets = flow.PlayerTickets;
					}
				}
				catch ( Exception error ) when ( error is not OutOfMemoryException )
				{
					// the shared park goes on without advice rather than fail its join, and the game's later parks try again
					advisor?.Dispose();
					advisor = null;
					advisorTried = false;
					Log.Warning( $"The shared park's advisor could not start: {error.Message}" );
				}
			}
		}

		public GameJoinEntry Stage()
		{
			if ( !complete || abandoned || installed || scene == null || candidate == null || hud == null || snapshot == null )
				throw new InvalidOperationException( "The shared park is not built." );
			scene.Resume();
			// the front end's own rebuild may have switched the language since the HUD was made: it is made again in the new one
			if ( hudLanguage != GameLanguage.Version )
			{
				// the park lives on: the old HUD must not go on hearing it
				var retired = hud;
				hud = null;
				retired.Retire();
				hud = flow.CreateParkHud( candidate, flow.HudStatusFor( candidate ) );
				hudLanguage = GameLanguage.Version;
				if ( advisor != null )
					advisor.CaptionSink = hud.PostAdvisorCaption;
			}
			scene.Detach();
			return new GameJoinEntry( scene, candidate, hud, advisor, advisorTried, snapshot.Theme, snapshot.SoundSeed, snapshot.StartKind );
		}

		public void MarkInstalled() => installed = true;

		public void Abandon()
		{
			if ( abandoned || installed )
				return;
			abandoned = true;
			try
			{
				advisor?.Dispose();
				hud?.Retire();
			}
			finally
			{
				advisor = null;
				hud = null;
				steps.Dispose();
				// the scene disposes the candidate and its entities and brings the live scene back, in any state it was left in
				scene?.Dispose();
			}
		}
	}
}

/// <summary>[EXT:online-coop-snapshot-join] What entering a built join candidate needs (<see cref="IGameJoinBuild.Stage"/>).</summary>
internal sealed record GameJoinEntry( EntityConstructionScope Scene, Level Level, ParkHud Hud, AutomaticAdvisor? Advisor, bool AdvisorTried, string Theme, uint SoundSeed,
	ParkStartKind StartKind );

/// <summary>[EXT:online-coop-snapshot-join] A join's build as the game installs it.</summary>
internal interface IGameJoinBuild : ICoopJoinBuild
{
	/// <summary>
	/// Makes the completed candidate ready to be activated: its scene detached with the live scene current, the HUD made again if the language changed since. Called once, by the swap,
	/// right before the live scene is torn down.
	/// </summary>
	GameJoinEntry Stage();

	/// <summary>The candidate is the game's park now: <see cref="ICoopJoinBuild.Abandon"/> changes nothing from here on.</summary>
	void MarkInstalled();
}
