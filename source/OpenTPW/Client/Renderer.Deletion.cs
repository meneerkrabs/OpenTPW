namespace OpenTPW;

partial class Renderer
{
	private List<Action> _deletionQueue = [];

	/// <summary>
	/// The time the end of a frame spends on scheduled deletions in the browser, in milliseconds; what is left waits for the next frames. Deleting
	/// a retired scene's textures there is a WebGL call each (about 2.5 ms for the lobby's, in the frame that enters a park), and a later deletion
	/// changes nothing that is drawn. The desktop runs every deletion at once, as before.
	/// </summary>
	internal const double BrowserDeletionBudgetMilliseconds = 1;

	public void ScheduleDelete( Action action )
	{
		_deletionQueue.Add( action );
	}

	private void ProcessDeletionQueue()
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		RunDeletions( _deletionQueue, () => System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds,
			OperatingSystem.IsBrowser() ? BrowserDeletionBudgetMilliseconds : double.PositiveInfinity );
	}

	/// <summary>
	/// Runs the oldest scheduled deletions, in the order they were scheduled, until <paramref name="budgetMilliseconds"/> has passed (at least one
	/// runs, so the queue always drains), and removes them; returns how many ran. A deletion that schedules another adds it behind the rest.
	/// </summary>
	internal static int RunDeletions( List<Action> queue, Func<double> elapsedMilliseconds, double budgetMilliseconds )
	{
		var ran = 0;
		while ( ran < queue.Count && (ran == 0 || elapsedMilliseconds() < budgetMilliseconds) )
			queue[ran++].Invoke();
		queue.RemoveRange( 0, ran );
		return ran;
	}
}
