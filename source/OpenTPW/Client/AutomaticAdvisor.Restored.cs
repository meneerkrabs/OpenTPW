namespace OpenTPW;

/// <summary>
/// The restored advice extension's side of the automatic advisor (docs/reverse/ADVISOR-SCAN.md, "Restored advice"): the connections from a running level (park events, the arrival vehicle, the scan's calls, the
/// windows and online screens) to <see cref="RestoredAdviceDriver"/>, which holds the logic. Every entry point is gated on <see cref="RestoredAdvice.Enabled"/> where it posts; none of it is saved or hashed.
/// </summary>
internal sealed partial class AutomaticAdvisor
{
	private RestoredAdviceDriver? restoredDriver;

	private RestoredAdviceDriver Restored => restoredDriver ??= new RestoredAdviceDriver( Controller, () => GameTick, () => GameType, () => TutorialEnabled );

	/// <summary>The read-only park view the extension's callbacks use; null while the extension is off.</summary>
	private IRestoredAdviceState? RestoredState( ParkEconomy economy ) => RestoredAdvice.Enabled ? new ParkRestoredAdviceState( economy, level ) : null;

	private void RestoredAttached() => Restored.Attached( GameOptions.Current.PopupHelp );

	private void RestoredDetached() => Restored.Reset();

	/// <summary>Withdraws the help of the restored windows when the park ends (they close without reporting, like the original's).</summary>
	private void WithdrawRestoredWindowHelp()
	{
		foreach ( var message in RestoredAdvice.WindowHelp.Values )
			Controller.Withdraw( message, AdvisorClock );
	}

	private void RestoredOnParkEvent( ParkEvent item )
	{
		if ( level != null && park is { } running )
			Restored.OnParkEvent( item, running.Economy );
	}

	/// <summary>The frame's observations: the arrival vehicle at work, and the golden tickets won since the last frame.</summary>
	private void RestoredObserve()
	{
		if ( level == null )
			return;
		Restored.ObserveVehicle( guests?.ArrivalVehicle ?? -1 );
	}

	/// <summary>The restored hints of the tools end with their mode (the blueprint and roller coaster hints).</summary>
	private bool WithdrawRestoredModeHelp()
	{
		var cancelled = false;
		foreach ( var message in RestoredAdvice.ModeHelp )
			cancelled |= Controller.Withdraw( message, AdvisorClock ).SpeechCancelled;
		return cancelled;
	}

	private void RestoredPoll( IRestoredAdviceState state, int steps ) => Restored.Poll( state, steps );

	/// <summary>A screen of the online extension opened over this park (<see cref="UI.OnlineHost.Advise"/>).</summary>
	public void OnOnlineScreenOpened( int message )
	{
		if ( level != null )
			Restored.OnlineScreen( message );
	}

	/// <summary>The restored help of a window the player opened (<see cref="RestoredAdvice.WindowHelp"/>), if it has one and the extension is on.</summary>
	private bool TryRestoredWindowHelp( string window, out int message ) =>
		RestoredAdvice.WindowHelp.TryGetValue( window, out message ) && RestoredAdvice.Enabled;
}
