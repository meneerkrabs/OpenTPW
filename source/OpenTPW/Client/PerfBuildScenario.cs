using OpenTPW.Hud;

namespace OpenTPW;

/// <summary>
/// [EXT:perf-run] <c>--perf-build</c> (docs/ENGINE-PERFORMANCE.md, "Start and build hitches"): once an original level's park and its interface are up, does
/// what a player does when building, one step every <see cref="StepFrames"/> frames: opens the buy window, shows each of its tabs and pages through each
/// tab's rows (the preview icons are made as rows come into view), selects a row (its turning preview), closes the window, then places one object of each
/// of several types near the camera's target through the build tool's own command. Each step labels the next frames for <see cref="HitchReport"/>; the
/// window closes after the last step. The park is a measurement park afterwards, not a run to keep.
/// </summary>
internal sealed class PerfBuildScenario
{
	/// <summary>Frames between two steps, so a step's cost and the frames it delays stay apart in the report.</summary>
	public const int StepFrames = 20;

	private readonly GameFlow flow;
	private readonly HitchReport report;
	private readonly int placements;
	private readonly Action finished;
	private IEnumerator<string?>? steps;
	private int waited;

	public PerfBuildScenario( GameFlow flow, HitchReport report, int placements, Action finished )
	{
		this.flow = flow;
		this.report = report;
		this.placements = placements;
		this.finished = finished;
	}

	/// <summary>Call once per frame after the update (<c>Renderer.PostUpdate</c>).</summary>
	public void Update()
	{
		if ( flow.Hud is not { } hud || flow.Level is not { OriginalPark: not null } level )
			return;
		if ( ++waited < StepFrames )
			return;
		waited = 0;
		steps ??= Steps( hud, level ).GetEnumerator();
		if ( !steps.MoveNext() )
		{
			steps = Enumerable.Empty<string?>().GetEnumerator();
			finished();
			return;
		}
		if ( steps.Current is { } name )
			report.Label( name );
	}

	private IEnumerable<string?> Steps( ParkHud hud, Level level )
	{
		// every type the level offers is listed and can be built, as after the research cheat
		level.Park?.Economy.Research.MakeEveryItemAvailable();
		hud.OpenBuyWindow();
		yield return "buy window opened";
		foreach ( var category in Enum.GetValues<BuildCategory>() )
		{
			hud.SelectCategory( category );
			yield return $"buy tab {category}, rows 1..{Math.Min( hud.BuyItems.Count, BuyListModel.VisibleRows )} of {hud.BuyItems.Count}";
			for ( var first = BuyListModel.VisibleRows; first < hud.BuyItems.Count; first += BuyListModel.VisibleRows )
			{
				hud.SetBuyFirstRow( first );
				yield return $"buy tab {category}, rows {first + 1}..{Math.Min( hud.BuyItems.Count, first + BuyListModel.VisibleRows )}";
			}
			if ( hud.BuyItems.Count > 0 )
			{
				hud.SetBuyFirstRow( 0 );
				hud.SelectBuyItem( 0 );
				yield return $"buy tab {category}, first row selected ({hud.BuyItems[0].Id})";
				// the turning preview is made once the selection has rested
				yield return null;
			}
		}
		hud.CloseBuyWindow();
		yield return "buy window closed";
		if ( Camera.Mode is not ParkCameraMode camera || !level.TryGetGridCell( camera.Target.X, camera.Target.Y, out var centreX, out var centreY ) )
			yield break;
		var placed = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var category in Enum.GetValues<BuildCategory>() )
		{
			hud.OpenBuyWindow();
			hud.SelectCategory( category );
			var items = hud.BuyItems.Where( item => item.Entry != null ).ToList();
			hud.CloseBuyWindow();
			var count = 0;
			foreach ( var item in items )
			{
				if ( count >= placements )
					break;
				var entry = item.Entry!;
				if ( !placed.Add( entry.ArchivePath ) || entry.AuxiliaryModels.Count > 1 )
					continue;
				(int X, int Y)? found;
				using ( FrameMarks.Measure( "perf.site-search" ) )
					found = FindSite( level, entry, centreX, centreY );
				if ( found is not { } site )
					continue;
				ParkCommandResult result;
				// the placement the build tool's click makes (its command), measured on its own
				using ( FrameMarks.Measure( "perf.place" ) )
					result = level.Commands.Issue( level.BuildCommand( entry, site.X, site.Y, 0 ) );
				count++;
				yield return $"placed {category} {item.Id}{(result.Accepted ? "" : " (refused)")}";
			}
		}
	}

	private static (int X, int Y)? FindSite( Level level, ObjectCatalogEntry entry, int centreX, int centreY )
	{
		for ( var radius = 0; radius <= 30; radius++ )
			for ( var dy = -radius; dy <= radius; dy++ )
				for ( var dx = -radius; dx <= radius; dx++ )
				{
					if ( Math.Max( Math.Abs( dx ), Math.Abs( dy ) ) != radius )
						continue;
					var (x, y) = (centreX + dx, centreY + dy);
					if ( x >= 0 && y >= 0 && level.CheckFreeObject( entry, x, y, 0 ) == OriginalPlacementResult.Allowed
						&& (level.Park == null || level.Park.Economy.CheckBuild( entry.InfoId, false ) == ParkEconomy.PurchaseResult.Ok) )
						return (x, y);
				}
		return null;
	}
}
