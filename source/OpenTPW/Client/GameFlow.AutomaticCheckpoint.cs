using OpenTPW.FrontEnd;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

internal sealed partial class GameFlow
{
	private Action? automaticExitQueued;
	private (AutomaticParkContinueEntry Entry, AutomaticCheckpointSession Binding, FrontEndMenu? Menu)? automaticOffer;
	internal Func<Level, ParkResumeSnapshot>? AutomaticCaptureOverride { get; set; }
	internal Action<string>? AutomaticRetireOverride { get; set; }
	internal Action<ParkResumeSnapshot>? AutomaticResumeOverride { get; set; }
	// [EXT:online-coop-lockstep] a co-op guest never saves: its park is the host's (no automatic checkpoint, autosave or exit checkpoint; it leaves as from a visit)
	private static bool AutomaticEligible( Level level ) => GameEditions.Current == GameEdition.ThemeParkWorld && GameEditions.Capabilities.ParkSaves &&
	 !level.IsReadOnlyVisit && !level.IsThemeParkIncSlice && level.CoopRole != CoopRole.Guest && level.OriginalPark != null && level.Guests != null &&
	 level.Park is { Start.Kind: ParkStartKind.FullSimulation or ParkStartKind.InstantAction } && ObjectCatalog.Themes.Contains( level.LevelName, StringComparer.Ordinal );

	private string? AutomaticProgressReceipt( PlayerProgressRuntimeOwner? owner, string? unbound )
	{
		if ( !ReferenceEquals( owner, progressOwner ) ) throw new InvalidOperationException( "The progress owner changed before checkpoint capture." );
		var previousError = owner?.Error;
		RequireProgressSaved( newPark: true );
		if ( previousError != null && owner?.Error == null && progressNotice == previousError ) { progressNotice = null; shownProgressNotice = null; }
		if ( owner?.Dirty == true ) throw new PlayerProgressSaveException( owner.Error ?? "Progress is still unsaved." );
		return owner?.CommittedDigest ?? unbound;
	}

	internal void RequestAutomaticExit( Level level, ParkHud hud )
	{
		var screen = hud.Stack.Top;
		if ( BuildingPark || ProgressCaptured() || !ReferenceEquals( Level, level ) || !ReferenceEquals( Hud, hud ) || screen == null || screen.IsRemoved ||
		 automaticExitQueued != null && ReferenceEquals( pending, automaticExitQueued ) ) return;
		if ( !AutomaticEligible( level ) ) { QueueProgressTransition( () => RetireAutomaticPark( level.LevelName ) ); return; }
		var runtime = level.Park; var files = SaveFileSystem; var owner = progressOwner;
		bool Current() => !BuildingPark && !ProgressCaptured() && ReferenceEquals( Level, level ) && ReferenceEquals( Hud, hud ) && ReferenceEquals( level.Park, runtime ) &&
		 ReferenceEquals( SaveFileSystem, files ) && ReferenceEquals( owner, progressOwner ) && !screen.IsRemoved && ReferenceEquals( hud.Stack.Top, screen );
		AutomaticCheckpointSession? session = null;
		try
		{
			var initialReceipt = AutomaticProgressReceipt( owner, null );
			session = new( files, level.LevelName, () => SaveFileSystem, Current );
			if ( !string.Equals( session.BoundProfileDigest, owner == null ? session.BoundProfileDigest : initialReceipt, StringComparison.OrdinalIgnoreCase ) )
				throw new InvalidDataException( "The checkpoint profile does not match its owned progress state." );
			Action? queued = null;
			queued = () =>
			{
				if ( ReferenceEquals( automaticExitQueued, queued ) ) automaticExitQueued = null;
				if ( !Current() || ProgressCaptured() ) return;
				try
				{
					var receipt = AutomaticProgressReceipt( owner, session.BoundProfileDigest );
					var executionSession = new AutomaticCheckpointSession( files, level.LevelName, () => SaveFileSystem, Current );
					if ( !string.Equals( executionSession.BoundProfileDigest, receipt, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidDataException( "The checkpoint profile changed before capture." );
					session = executionSession;
					session.TryCheckpoint( () => AutomaticProgressReceipt( owner, receipt ),
			() =>
			{
				// The player is leaving: a response still being spoken (the level-start welcome runs for about half a minute) ends here instead of refusing the checkpoint
				if ( Advisor?.IsAttached == true ) Advisor.EndSpeechForExit();
				return AutomaticCaptureOverride?.Invoke( level ) ?? level.CaptureResumeSnapshot( Advisor?.IsAttached == true ? Advisor : null, level.LevelName, savedUtc: DateTimeOffset.UtcNow );
			},
			() => RetireAutomaticPark( level.LevelName ) );
				}
				catch ( Exception error ) when ( !session.CheckpointCommitted && ProgressFailure( error ) ) { ShowAutomaticFailure( level, hud, screen, error ); }
				// the checkpoint is safely written: a failure while leaving the park is reported, not thrown out of the frame
				catch ( Exception error ) when ( session.CheckpointCommitted && ProgressFailure( error ) )
				{
					Log.Warning( $"The automatic checkpoint was saved, but leaving the park failed: {error.Message}" );
					ShowProgressFailure( error );
				}
			};
			automaticExitQueued = queued; Queue( queued );
		}
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowAutomaticFailure( level, hud, screen, error ); }
	}

	private void RetireAutomaticPark( string theme ) { if ( AutomaticRetireOverride != null ) AutomaticRetireOverride( theme ); else ShowFrontEnd( theme ); }

	private void ShowAutomaticFailure( Level level, ParkHud hud, UiScreen source, Exception error )
	{
		Log.Warning( $"Automatic checkpoint refused: {error.Message}" );
		if ( progressOwner?.Error == error.Message ) shownProgressNotice = progressNotice;
		if ( !ReferenceEquals( Level, level ) || !ReferenceEquals( Hud, hud ) || source.IsRemoved || !ReferenceEquals( hud.Stack.Top, source ) ) return;
		ProgressChoiceScreen? failure = null;
		bool Current() => !failure!.IsRemoved && ReferenceEquals( hud.Stack.Top, failure ) && ReferenceEquals( Level, level ) && ReferenceEquals( Hud, hud );
		failure = ProgressChoiceScreen.Message( "automaticFailed", () => Strings.Extra( OpenTpwText.AutomaticCheckpointFailed ) + "\n" + error.Message,
		 (() => Strings.Extra( OpenTpwText.RetryCheckpoint ), () => { if ( Current() && !ProgressCaptured() && failure!.TryActivate( hud.Stack ) ) { hud.Stack.Pop(); RequestAutomaticExit( level, hud ); } }
		),
		 (() => Strings.Extra( OpenTpwText.Back ), () => { if ( Current() ) hud.Stack.Pop(); }
		) );
		hud.Stack.Push( failure );
	}

	internal AutomaticParkContinueEntry? FindAutomaticContinueEntry( string theme )
	{
		automaticOffer = null;
		if ( Level != null || progressOwner?.Dirty == true ) return null; // Menu probing is read-only.
		var entry = AutomaticThemeContinuation.Newest( SaveFileSystem, theme ); if ( entry == null ) return null;
		try { automaticOffer = (entry, new( SaveFileSystem, theme, () => SaveFileSystem, () => true ), Menu); return entry; }
		catch ( Exception error ) when ( ProgressFailure( error ) ) { Log.Warning( $"Automatic continuation unavailable: {error.Message}" ); return null; }
	}
	internal void RequestAutomaticContinue( FrontEndMenu menu, AutomaticParkContinueEntry entry )
	{
		var screen = menu.Stack.Top;
		var offer = automaticOffer;
		// [EXT:save-extension-switch] a confirmed switch arrives as a copy of the offered entry that differs only in its switch
		bool Current() => !ProgressCaptured() && offer.HasValue && (ReferenceEquals( offer.Value.Entry, entry ) || entry.Switch != null && offer.Value.Entry == entry with { Switch = null }) && ReferenceEquals( offer.Value.Menu, menu ) && ReferenceEquals( Menu, menu ) &&
		 menu.Selected.Level == entry.Theme && screen != null && !screen.IsRemoved && ReferenceEquals( menu.Stack.Top, screen ) && ReferenceEquals( SaveFileSystem, entry.Namespace );
		if ( BuildingPark || ProgressCaptured() || !Current() ) return;
		try
		{
			ValidateAutomaticOffer( entry, offer!.Value.Binding.BoundProfileDigest );
			QueueProgressLevel( entry.Theme, () => { if ( Current() ) TryAutomaticContinue( entry, offer.Value.Binding.BoundProfileDigest, snapshot => { if ( AutomaticResumeOverride != null ) AutomaticResumeOverride( snapshot ); else ResumePark( snapshot, extensionSwitch: entry.Switch ); } ); } );
		}
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); }
	}
	private void ValidateAutomaticOffer( AutomaticParkContinueEntry entry, string? digest )
	{
		var current = new AutomaticCheckpointSession( SaveFileSystem, entry.Theme, () => SaveFileSystem, () => true );
		if ( !string.Equals( current.BoundProfileDigest, digest, StringComparison.OrdinalIgnoreCase ) ) throw new InvalidDataException( "The automatic continuation profile binding changed. Reopen the choice." );
	}
	internal bool TryAutomaticContinue( AutomaticParkContinueEntry entry, string? digest, Action<ParkResumeSnapshot> resume )
	{
		try { ValidateAutomaticOffer( entry, digest ); resume( AutomaticThemeContinuation.Read( SaveFileSystem, entry ) ); return true; }
		catch ( Exception error ) when ( ProgressFailure( error ) ) { ShowProgressFailure( error ); return false; }
	}
}
