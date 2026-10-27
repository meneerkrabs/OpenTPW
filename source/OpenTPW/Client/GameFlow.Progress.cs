using OpenTPW.FrontEnd;
using OpenTPW.UI.Original;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	private PlayerProgressRuntimeOwner? progressOwner;
	private InstantActionTicketAccount? instantAccount;
	private string? progressNotice;
	private string? shownProgressNotice;
	private string? displayedProgressDetail;
	internal Func<bool>? ProgressCaptureOverride { get; set; }
	// Candidate-only seam: the normal implementation always uses the existing isolated park builder.
	internal Action<string, ParkStartKind, Action>? ProgressBuildOverride { get; set; }
	private string ProgressPath => SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName );

	private bool ProgressCaptured()
	{
		if ( ProgressCaptureOverride != null ) return ProgressCaptureOverride();
		if ( Input.TextEntryActive ) return true;
		if ( ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero ) return false;
		var io = ImGuiNET.ImGui.GetIO();
		return io.WantCaptureMouse || io.WantCaptureKeyboard || io.WantTextInput;
	}

	internal static int TrustedThemeEntryCost( string theme )
	{
		if ( !ObjectCatalog.Themes.Contains( theme, StringComparer.Ordinal ) ) throw new InvalidDataException( "Unknown Theme Park World theme." );
		var path = $"/levels/{theme}/global.sam";
		var settings = new SamSettings( new[] { SamDocument.Parse( FileSystem.ReadAllText( path ), path ) } );
		try
		{
			var cost = settings.GetInt( "Keys.CostToEnter" );
			if ( cost < 0 ) throw new InvalidDataException( "The theme entry cost must be nonnegative." );
			return cost;
		}
		catch ( Exception error ) when ( error is KeyNotFoundException or FormatException or OverflowException )
		{ throw new InvalidDataException( "The theme has no valid entry cost.", error ); }
	}

	internal void RequestParkStart( string theme, GameMode mode, FrontEndMenu? owner = null )
	{
		if ( BuildingPark || ProgressCaptured() || owner != null && !ReferenceEquals( owner, Menu ) ) return;
		var screen = owner?.Stack.Top;
		var currentLevel = Level;
		bool Owns( UiScreen? lease ) => ReferenceEquals( Level, currentLevel ) && (owner == null ||
			ReferenceEquals( Menu, owner ) && owner.Selected.Level == theme && lease != null && !lease.IsRemoved && ReferenceEquals( owner.Stack.Top, lease ));
		try
		{
			RequireProgressSaved( newPark: true );
			// Instant Action keeps no progress, so it starts in every edition as before; only Full Simulation needs Theme Park World's progression
			if ( mode == GameMode.InstantAction )
			{
				QueueLevel( theme, () => { if ( Owns( screen ) ) StartInstantPark( theme ); }, prepareBuyWindow: true );
				if ( owner != null ) BeginLobbyEnter( theme );
				return;
			}
			if ( !GameEditions.Capabilities.Economy || GameEditions.IsThemeParkInc || !ObjectCatalog.Themes.Contains( theme, StringComparer.Ordinal ) )
				throw new NotSupportedException( "Application-owned progression is available only for Theme Park World." );
			if ( mode != GameMode.FullSimulation ) throw new InvalidDataException( "Unknown park mode." );
			var state = PlayerTicketFile.ReadIfPresent( ProgressPath );
			var offeredCost = FileSystem.FileExists( $"/levels/{theme}/global.sam" ) ? TrustedThemeEntryCost( theme ) : (int?)null;
			void Admit( UiScreen? lease )
			{
				if ( ProgressCaptured() || !Owns( lease ) ) return;
				try
				{
					RequireProgressSaved( newPark: true );
					var admission = new PlayerProgressAdmission( ProgressPath, theme, state, offeredCost, () => TrustedThemeEntryCost( theme ),
						() => Owns( lease ), () => RequireProgressSaved( newPark: true ) );
					if ( offeredCost.HasValue ) admission.Validate();
					QueueLevel( theme, () => TryBeginProgressPark( admission ), prepareBuyWindow: true );
					if ( owner != null ) BeginLobbyEnter( theme );
				}
				catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); }
			}
			if ( state != null ) { Admit( screen ); return; }
			if ( owner == null || screen == null ) throw new InvalidOperationException( "Choose New Full Simulation in the lobby to confirm new OpenTPW progress." );
			// A theme that costs more than the one starter key (Fantasy 3, Space 5 in the shipped global.sam) is refused before the consent, which could not admit it
			if ( offeredCost is int cost && cost > PlayerProgressAdmission.StarterKeys )
				throw new PlayerProgressKeysException( cost, PlayerProgressAdmission.StarterKeys );
			ProgressChoiceScreen? confirmation = null;
			confirmation = ProgressChoiceScreen.Message( "newProgress", () => Strings.Extra( OpenTpwText.NewProgressConsent ),
				(() => Strings.Extra( OpenTpwText.StartNewProgress ), () => { if ( confirmation!.TryActivate( owner.Stack ) ) Admit( confirmation ); }
			),
				(() => Strings.Extra( OpenTpwText.Back ), () => { if ( !confirmation!.IsRemoved && ReferenceEquals( owner.Stack.Top, confirmation ) ) owner.Stack.Pop(); }
			) );
			owner.Stack.Push( confirmation );
		}
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); }
	}

	internal bool TryBeginProgressPark( PlayerProgressAdmission admission )
	{
		if ( !admission.IsActive ) return false;
		try
		{
			RequireProgressSaved( newPark: true );
			admission.Validate(); // Before scene, level, objects, guests or VM construction.
			if ( ProgressBuildOverride != null ) ProgressBuildOverride( admission.Theme, ParkStartKind.FullSimulation, admission.BeforeRetirement );
			else StartLevel( admission.Theme, original: true, developerPanels: false, overFrames: true, start: ParkStartKind.FullSimulation,
				beforeEnter: _ => admission.BeforeRetirement() );
			return true;
		}
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); return false; }
	}

	private void StartInstantPark( string theme, bool overFrames = true )
	{
		try
		{
			RequireProgressSaved();
			if ( ProgressBuildOverride != null ) ProgressBuildOverride( theme, ParkStartKind.InstantAction, () => RequireProgressSaved() );
			else StartLevel( theme, original: true, developerPanels: false, overFrames: overFrames, start: ParkStartKind.InstantAction );
		}
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); }
	}

	private static bool ProgressFailure( Exception error ) => error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException;
	private void ShowProgressFailure( Exception error ) => ShowProgressFailure( error.Message,
		error is PlayerProgressKeysException keys ? string.Format( Strings.Extra( OpenTpwText.ProgressKeysNeeded ), keys.Required, keys.Available ) :
		error is PlayerProgressModeException ? Strings.Extra( OpenTpwText.ProgressModeKept ) : null );
	private void ShowProgressFailure( string detail, string? message = null )
	{
		Log.Warning( $"OpenTPW progress: {detail}" );
		if ( progressNotice == detail ) shownProgressNotice = detail;
		var stack = Hud?.Stack ?? Menu?.Stack;
		if ( stack == null ) return;
		if ( stack.Top?.Name == "progressFailed" && displayedProgressDetail == detail ) return;
		displayedProgressDetail = detail;
		UiScreen? screen = null;
		screen = UiDialogs.Message( "progressFailed", () => message ?? Strings.Extra( progressOwner?.Dirty == true ? OpenTpwText.ProgressUnsaved : OpenTpwText.ProgressActionFailed ), (() => Strings.Extra( OpenTpwText.Back ), () => { if ( !screen!.IsRemoved && ReferenceEquals( stack.Top, screen ) ) { stack.Pop(); displayedProgressDetail = null; } }) );
		stack.Push( screen );
	}

	private void RequireProgressSaved( bool newPark = false )
	{
		if ( progressOwner?.TryFlush( requireUnchangedWhenClean: newPark ) == false )
			throw new PlayerProgressSaveException( progressOwner.Error ?? "OpenTPW progress could not be saved. The running park is kept." );
	}

	private void QueueProgressTransition( Action transition )
	{
		try { RequireProgressSaved(); Queue( () => { try { RequireProgressSaved(); transition(); } catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); } } ); }
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); }
	}

	private void QueueProgressLevel( string theme, Action transition )
	{
		try { RequireProgressSaved(); QueueLevel( theme, () => { try { RequireProgressSaved(); transition(); } catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); } }, prepareBuyWindow: true ); }
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); }
	}

	private void RequestProgressQuit()
	{
		// [EXT:online-coop-snapshot-join] a shared park leaves its session first, so the check below saves what the hand-back gave back
		LeaveCoopSession( "The player quit the game." );
		try { RequireProgressSaved(); Quit(); }
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); }
	}

	internal void AttachProgress( Level level )
	{
		DetachProgress();
		// [EXT:online-coop-snapshot-join] a co-op guest's park is the host's: it never takes part in this player's progress
		if ( !ReferenceEquals( Level, level ) || level.IsReadOnlyVisit || level.IsCoopGuestPark || level.IsThemeParkIncSlice || !GameEditions.Capabilities.Economy || !ObjectCatalog.Themes.Contains( level.LevelName, StringComparer.Ordinal ) )
			return;
		if ( level.Park is { Start.Kind: ParkStartKind.InstantAction } instant && instant.Economy.Mode == ParkGameMode.InstantAction )
		{
			// Instant Action earns no tickets or keys, but its research completion still marks the player's park record (InstantActionResearchRecords), and it spends from the player's
			// ticket account (tp.exe 0x4D4AD0 skips it only in game type 1, the online visit; the HUD counter 0x4A10CD reads it in every game type)
			level.ResearchRecords = new InstantActionResearchRecords( ProgressPath, () => ReferenceEquals( Level, level ) && ReferenceEquals( level.Park, instant ) && level.CoopSession == null );
			try
			{
				if ( PlayerTicketFile.ReadIfPresent( ProgressPath ) is { } account && level.CoopSession == null )
					instantAccount = new InstantActionTicketAccount( ProgressPath, account, instant, () => ReferenceEquals( Level, level ) && ReferenceEquals( level.Park, instant ) && !level.IsReadOnlyVisit && level.CoopSession == null,
						message => Log.Warning( $"OpenTPW ticket account: {message}" ) );
			}
			catch ( Exception error ) when ( ProgressFailure( error ) ) { Log.Warning( $"OpenTPW ticket account unavailable: {error.Message}" ); }
			return;
		}
		if ( level.Park is not { Start.Kind: ParkStartKind.FullSimulation } runtime || runtime.Economy.Mode != ParkGameMode.FullSimulation ) return;
		try
		{
			var state = PlayerTicketFile.ReadIfPresent( ProgressPath );
			if ( state?.Ledger.Mode != ParkGameMode.FullSimulation ) { progressNotice = Strings.Extra( OpenTpwText.ProgressTrackingUnavailable ); return; }
			var owner = progressOwner = new( ProgressPath, level.LevelName, state, runtime,
				() => ReferenceEquals( Level, level ) && ReferenceEquals( level.Park, runtime ) && !level.IsReadOnlyVisit && !level.IsCoopGuestPark,
				message => { progressNotice = message; shownProgressNotice = null; }, () => level.CoopSession != null );
			level.ResearchRecords = new OwnerResearchRecords( owner );
		}
		catch ( Exception error ) when ( ProgressFailure( error ) ) { Log.Warning( $"OpenTPW tracking unavailable: {error.Message}" ); progressNotice = Strings.Extra( OpenTpwText.ProgressTrackingUnavailable ); }
	}

	/// <summary>A Full Simulation park's records: the attached progress owner's ledger, saved as the owner saves.</summary>
	private sealed class OwnerResearchRecords( PlayerProgressRuntimeOwner owner ) : IPlayerResearchRecords
	{
		public void RecordCompleted( string theme ) => owner.RecordResearchCompleted();
		public bool CompletedEverywhereWith( string theme ) => owner.ResearchCompletedEverywhere;
	}

	/// <summary>
	/// An Instant Action park's records: its research completion marks the park's record in the player's progress file when there is one, as the original's completion handler
	/// marks the current player's record in every game type; an Instant Action park never reads them (it starts its finale anyway).
	/// </summary>
	// [EXT:no-player-profiles] without an own progress file there is no player whose record could be marked (the file is made only with the player's consent), so nothing is written
	// [EXT:online-coop-snapshot-join] a park in a co-op session leaves the player's records alone, as the Full Simulation's owner does
	internal sealed class InstantActionResearchRecords( string path, Func<bool> active ) : IPlayerResearchRecords
	{
		public void RecordCompleted( string theme )
		{
			if ( !active() ) return;
			try
			{
				if ( PlayerTicketFile.ReadIfPresent( path ) is not { } state || state.Ledger.Mode != ParkGameMode.FullSimulation || !state.Ledger.RecordResearchCompleted( theme ) )
					return;
				PlayerTicketFile.SaveIfUnchanged( path, state.Ledger.Capture(), state.Digest );
			}
			catch ( Exception error ) when ( ProgressFailure( error ) ) { Log.Warning( $"OpenTPW progress: the research record of {theme} was not saved: {error.Message}" ); }
		}

		public bool CompletedEverywhereWith( string theme ) => false;
	}

	/// <summary>The keys of the attached player ledger (the HUD's key counter), null while none is attached.</summary>
	private int? PlayerKeys() => progressOwner?.Ledger.Keys;

	private IReadOnlyList<int>? themeEntryCosts;

	/// <summary>The player's tickets as the advisor's golden ticket advice counts them (the park's own tickets counted in, what each park costs to enter in keys), null while no ledger is attached or a park's cost cannot be read.</summary>
	private AdvisorPlayerTickets? PlayerTickets( IReadOnlyCollection<GoldenTicketKind> parkTickets )
	{
		if ( progressOwner is not { } owner || Level is not { } level )
			return null;
		try
		{
			themeEntryCosts ??= ObjectCatalog.Themes.Where( theme => FileSystem.FileExists( $"/levels/{theme}/global.sam" ) ).Select( TrustedThemeEntryCost ).ToList();
			return owner.Ledger.AdvisorView( level.LevelName, parkTickets, themeEntryCosts );
		}
		catch ( Exception error ) when ( error is InvalidDataException or IOException )
		{
			return null;
		}
	}

	private void DetachProgress()
	{
		if ( Level is { } level ) level.ResearchRecords = null;
		instantAccount?.Dispose(); instantAccount = null;
		progressOwner?.Dispose(); progressOwner = null; progressNotice = null; shownProgressNotice = null; displayedProgressDetail = null;
	}
	private void PumpProgress()
	{
		progressOwner?.Pump();
		instantAccount?.Pump();
		if ( progressNotice == null || progressNotice == shownProgressNotice ) return;
		shownProgressNotice = progressNotice;
		ShowProgressFailure( progressNotice, progressOwner == null ? progressNotice : null );
	}
}
