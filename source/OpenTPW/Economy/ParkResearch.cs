namespace OpenTPW;

/// <summary>A researchable unlock: an object (<see cref="Level"/> 0) or one of a ride's upgrade levels (1, 2).</summary>
public sealed record ResearchItem( int InfoId, int Level, ResearchCategory Category, int Group, int Cost, int RequiresInfoId );

/// <summary>
/// The research lab. Data-driven: items, their categories and groups (<c>Research.Category</c>,
/// <c>Research.Group</c> — group 0 "available initially"), costs (<c>Upgrades[n].CostOfResearch</c>,
/// "research points taken for item to be researched"), the starting effort per category
/// (<c>ResearchCategories[n].Effort</c>), researcher ability per grade (<c>ResearchAbility</c>) and
/// the group thresholds (<c>ResearchTech[g].PercentageForThisTech</c>). Group 0 items of the ride,
/// shop, sideshow and feature categories start researched whatever their cost.
/// <b>Approximations</b>: each researcher adds its grade's <c>ResearchAbility</c> points per game day,
/// split over the categories by effort; a group opens once that percentage of the previous group of
/// the same category is researched; items are researched cheapest first within the open groups;
/// ride upgrade levels and add-on objects form the "upgrade" category; Instant Action research runs
/// at one grade-2 researcher's ability without staff. <c>Research.StartingWorkLoad</c> and researcher
/// <c>WorkDuration</c> are not used (meaning unknown).
/// </summary>
public sealed class ParkResearch
{
	public const int PointScale = 1000;
	private readonly BalanceSettings settings;
	private readonly List<ResearchItem> items;
	private readonly HashSet<(int InfoId, int Level)> completed = new();
	private readonly Dictionary<(int InfoId, int Level), long> progress = new();
	private readonly int[] effort;
	private readonly HashSet<(int InfoId, int Level)> itemKeys = new();
	private readonly Dictionary<ResearchCategory, List<ResearchItem>> byCategory = new();
	private readonly Dictionary<(ResearchCategory Category, int Group), (int Count, int Done)> groupStats = new();

	public ParkResearch( BalanceSettings settings, IEconomyObjectCatalog catalog )
	{
		this.settings = settings;
		effort = settings.ResearchEffort.ToArray();
		items = new List<ResearchItem>();
		foreach ( var info in catalog.Objects.OrderBy( info => info.InfoId ) )
		{
			if ( !info.IsBuyable || info.Upgrades.Count == 0 )
				continue;
			// [APPROX:ECON-018] ride upgrade levels and add-on objects form the "upgrade" research category — evidence needed: research lab capture
			var category = info.Kind == ParkObjectKind.Upgrade ? ResearchCategory.Upgrade : info.ResearchCategory;
			var group = info.Kind == ParkObjectKind.Upgrade ? 0 : info.ResearchGroup;
			items.Add( new ResearchItem( info.InfoId, 0, category, group, info.Upgrades[0].CostOfResearch, info.Kind == ParkObjectKind.Upgrade ? info.AddOnTargetId : 0 ) );
			if ( info.Kind != ParkObjectKind.Ride )
				continue;
			for ( var level = 1; level < info.Upgrades.Count; level++ )
				items.Add( new ResearchItem( info.InfoId, level, ResearchCategory.Upgrade, 0, info.Upgrades[level].CostOfResearch, info.InfoId ) );
		}
		foreach ( var item in items )
		{
			itemKeys.Add( (item.InfoId, item.Level) );
			if ( !byCategory.TryGetValue( item.Category, out var list ) )
				byCategory[item.Category] = list = new List<ResearchItem>();
			list.Add( item );
		}
		foreach ( var list in byCategory.Values )
			list.Sort( ( a, b ) => (a.Group, a.Cost, a.InfoId, a.Level).CompareTo( (b.Group, b.Cost, b.InfoId, b.Level) ) );
		// "Research.Group 0 = Available initially" (category files); add-ons are always group 0 but carry a research cost.
		foreach ( var item in items.Where( item => item.Group == 0 && item.Level == 0 && item.Category != ResearchCategory.Upgrade ) )
			completed.Add( (item.InfoId, item.Level) );
		RebuildGroupStats();
		PropagateFreeItems();
	}

	public IReadOnlyList<ResearchItem> Items => items;
	public IReadOnlyCollection<(int InfoId, int Level)> Completed => completed;
	public IReadOnlyList<int> Effort => effort;

	public bool IsAvailable( int infoId, int level = 0 ) => completed.Contains( (infoId, level) ) || !itemKeys.Contains( (infoId, level) );

	public void SetEffort( ResearchCategory category, int value ) => effort[(int)category] = Math.Clamp( value, 0, 100 );

	public long GetProgress( ResearchItem item ) => progress.GetValueOrDefault( (item.InfoId, item.Level) );

	public bool IsAllResearched => completed.Count == items.Count;

	/// <summary>Percentage of a category's group that is researched (100 for an empty group).</summary>
	public int PercentResearched( ResearchCategory category, int group ) =>
		groupStats.TryGetValue( (category, group), out var stats ) && stats.Count > 0 ? stats.Done * 100 / stats.Count : 100;

	private void RebuildGroupStats()
	{
		groupStats.Clear();
		foreach ( var item in items )
		{
			var stats = groupStats.GetValueOrDefault( (item.Category, item.Group) );
			groupStats[(item.Category, item.Group)] = (stats.Count + 1, stats.Done + (completed.Contains( (item.InfoId, item.Level) ) ? 1 : 0));
		}
	}

	private void MarkCompleted( ResearchItem item )
	{
		if ( !completed.Add( (item.InfoId, item.Level) ) )
			return;
		var stats = groupStats[(item.Category, item.Group)];
		groupStats[(item.Category, item.Group)] = (stats.Count, stats.Done + 1);
	}

	// [APPROX:ECON-016] group g opens when PercentageForThisTech % of group g-1 of the same category is researched — evidence needed: capture of new research groups appearing
	public bool IsGroupOpen( ResearchCategory category, int group )
	{
		if ( group <= 0 )
			return true;
		var index = Math.Min( group, settings.ResearchTechPercentage.Count - 1 );
		return IsGroupOpen( category, group - 1 ) && PercentResearched( category, group - 1 ) >= settings.ResearchTechPercentage[index];
	}

	/// <summary>The item a category is currently researching, or null when nothing is researchable.</summary>
	// [APPROX:ECON-017] items are researched cheapest first within open groups — evidence needed: capture of research order
	public ResearchItem? Current( ResearchCategory category )
	{
		if ( !byCategory.TryGetValue( category, out var list ) )
			return null;
		foreach ( var item in list )
		{
			if ( !completed.Contains( (item.InfoId, item.Level) ) && IsGroupOpen( category, item.Group ) && PrerequisiteMet( item ) )
				return item;
		}
		return null;
	}

	private bool PrerequisiteMet( ResearchItem item )
	{
		if ( item.Level > 0 )
			return IsAvailable( item.InfoId, item.Level - 1 );
		return item.RequiresInfoId == 0 || IsAvailable( item.RequiresInfoId );
	}

	/// <summary>Daily research points (×<see cref="PointScale"/>) produced by the given researchers.</summary>
	// [APPROX:ECON-015] each researcher adds ResearchAbility points per game day, split by effort — evidence needed: capture of research progress over time
	public long DailyPoints( IEnumerable<StaffMember> researchers, bool automatic )
	{
		long points = researchers.Where( member => member.State != StaffState.OnStrike ).Sum( member => (long)settings.ResearchAbility[member.Grade] );
		if ( automatic && points == 0 )
			// [APPROX:ECON-019] Instant Action research runs at one grade-2 researcher without staff — evidence needed: Instant Action capture
			points = settings.ResearchAbility[2];
		return points * PointScale;
	}

	/// <summary>Advances research by one day's points; returns the items completed today.</summary>
	public IReadOnlyList<ResearchItem> AdvanceDay( long scaledPoints )
	{
		var done = new List<ResearchItem>();
		var active = Enum.GetValues<ResearchCategory>().Select( category => (Category: category, Item: Current( category )) )
			.Where( entry => entry.Item != null && effort[(int)entry.Category] > 0 ).ToList();
		var totalEffort = active.Sum( entry => effort[(int)entry.Category] );
		if ( scaledPoints <= 0 || totalEffort == 0 )
			return done;
		foreach ( var (category, item) in active )
		{
			var key = (item!.InfoId, item.Level);
			var value = progress.GetValueOrDefault( key ) + scaledPoints * effort[(int)category] / totalEffort;
			if ( value >= (long)item.Cost * PointScale )
			{
				progress.Remove( key );
				MarkCompleted( item );
				done.Add( item );
			}
			else
				progress[key] = value;
		}
		if ( done.Count > 0 )
			PropagateFreeItems( done );
		return done;
	}

	/// <summary>Marks items with zero research cost whose prerequisites are available (e.g. easy-mode ride upgrades).</summary>
	private void PropagateFreeItems( List<ResearchItem>? done = null )
	{
		bool changed;
		do
		{
			changed = false;
			foreach ( var item in items )
			{
				if ( item.Cost == 0 && !completed.Contains( (item.InfoId, item.Level) ) && IsGroupOpen( item.Category, item.Group ) && PrerequisiteMet( item ) )
				{
					MarkCompleted( item );
					done?.Add( item );
					changed = true;
				}
			}
		}
		while ( changed );
	}

	internal void Restore( IEnumerable<(int InfoId, int Level)> restoredCompleted, IEnumerable<((int InfoId, int Level) Key, long Points)> restoredProgress, IReadOnlyList<int> restoredEffort )
	{
		completed.Clear();
		foreach ( var key in restoredCompleted )
		{
			if ( !itemKeys.Contains( key ) )
				throw new InvalidDataException( $"Saved research refers to unknown item {key.InfoId} level {key.Level}." );
			completed.Add( key );
		}
		RebuildGroupStats();
		progress.Clear();
		foreach ( var (key, points) in restoredProgress )
			progress[key] = points;
		for ( var index = 0; index < effort.Length; index++ )
			effort[index] = Math.Clamp( restoredEffort[index], 0, 100 );
	}

	internal IEnumerable<((int InfoId, int Level) Key, long Points)> ProgressEntries => progress.Select( pair => (pair.Key, pair.Value) );
}
