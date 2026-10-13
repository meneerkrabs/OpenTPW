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
/// Traced in the Mac binary: every 20 park turns each researcher adds <c>ResearchAbility</c> ×
/// effort share × <c>Research.StartingWorkLoad</c> / 100 points to every category's current item; a
/// category's highest open group rises while the percentage of researched items in its open groups
/// reaches the next <c>ResearchTech</c> threshold; each category researches the first open item in table order.
/// <b>Approximations</b>: the table is in info-id order; ride upgrade levels and add-on objects form the
/// "upgrade" category; Instant Action research runs at one grade-2 researcher's ability without staff;
/// progress is kept in thousandths of a point. Researcher <c>WorkDuration</c> is not used.
/// </summary>
public sealed class ParkResearch
{
	public const int PointScale = 1000;
	private readonly BalanceSettings settings;
	private readonly List<ResearchItem> items;
	private readonly HashSet<(int InfoId, int Level)> completed = new();
	private readonly Dictionary<(int InfoId, int Level), long> progress = new();
	private readonly int[] effort;
	private readonly Dictionary<ResearchCategory, ResearchItem> selected = new();
	private readonly HashSet<(int InfoId, int Level)> itemKeys = new();
	private readonly Dictionary<ResearchCategory, List<ResearchItem>> byCategory = new();
	private readonly Dictionary<(ResearchCategory Category, int Group), (int Count, int Done)> groupStats = new();
	private readonly int[] highestGroup = new int[Enum.GetValues<ResearchCategory>().Length];
	/// <summary>The original raises a category's highest open group to at most 7.</summary>
	public const int MaximumOpenGroup = 7;
	/// <summary>Research is added every this many park turns.</summary>
	public const int TurnsPerResearch = 20;

	public ParkResearch( BalanceSettings settings, IEconomyObjectCatalog catalog )
	{
		this.settings = settings;
		effort = settings.ResearchEffort.ToArray();
		Workload = Math.Clamp( settings.ResearchStartingWorkLoad, 0, 100 );
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
		// "Research.Group 0 = Available initially" (category files); add-ons are always group 0 but carry a research cost.
		foreach ( var item in items.Where( item => item.Group == 0 && item.Level == 0 && item.Category != ResearchCategory.Upgrade ) )
			completed.Add( (item.InfoId, item.Level) );
		RebuildGroupStats();
		OpenGroups();
		PropagateFreeItems();
	}

	/// <summary>The lab's work load percentage (<c>Research.StartingWorkLoad</c>, 0–100).</summary>
	public int Workload { get; private set; }

	// [BIN:STP-PPC:0x100F130C research work load] the setter stores the percentage the research points are scaled by
	public void SetWorkload( int value ) => Workload = Math.Clamp( value, 0, 100 );

	public int HighestOpenGroup( ResearchCategory category ) => highestGroup[(int)category];

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

	// [BIN:STP-PPC:0x100F15B4 research group opener] with h the category's highest open group, the items of groups <= h are counted; while done × 100 / count (unsigned) reaches ResearchTech[h + 1].PercentageForThisTech and h <= 6, h rises by one
	public bool IsGroupOpen( ResearchCategory category, int group ) => group <= highestGroup[(int)category];

	private void OpenGroups()
	{
		foreach ( var category in Enum.GetValues<ResearchCategory>() )
			OpenGroups( category );
	}

	private void OpenGroups( ResearchCategory category )
	{
		ref var highest = ref highestGroup[(int)category];
		while ( highest < MaximumOpenGroup )
		{
			var count = 0;
			var done = 0;
			foreach ( var ((itemCategory, group), stats) in groupStats )
			{
				if ( itemCategory != category || group > highest )
					continue;
				count += stats.Count;
				done += stats.Done;
			}
			var percent = count == 0 ? 0 : done * 100 / count;
			// Thresholds past the data's ResearchTech entries read as 0, like the zero-filled balance block.
			var threshold = highest + 1 < settings.ResearchTechPercentage.Count ? settings.ResearchTechPercentage[highest + 1] : 0;
			if ( percent < threshold )
				break;
			highest++;
		}
	}

	/// <summary>The item a category is currently researching, or null when nothing is researchable.</summary>
	// [BIN:STP-PPC:0x100F0EF0 research cursor] each category researches the first item in table order whose group is open and which is not yet researched; cost is never compared
	// [APPROX:ECON-017] the research table is in info-id order — evidence needed: the table fill order (FUN_100c9064) and the next/previous control
	// Host UI selection below retains the native default cursor; correspondence with native player selection remains unverified.
	public ResearchItem? Current( ResearchCategory category )
	{
		if ( selected.TryGetValue( category, out var item ) && IsSelectable( item ) )
			return item;
		selected.Remove( category );
		return byCategory.TryGetValue( category, out var list ) ? list.FirstOrDefault( IsSelectable ) : null;
	}

	/// <summary>Unfinished projects with an open group and available prerequisites, in table order.</summary>
	public IReadOnlyList<ResearchItem> SelectableItems( ResearchCategory category ) =>
		byCategory.TryGetValue( category, out var list ) ? list.Where( IsSelectable ).ToArray() : Array.Empty<ResearchItem>();

	/// <summary>Steps the active project with wraparound, retaining every project's accumulated points.</summary>
	public bool MoveCurrent( ResearchCategory category, int direction )
	{
		var choices = SelectableItems( category );
		if ( direction == 0 || choices.Count < 2 )
			return false;
		var current = Current( category );
		var index = 0;
		while ( index < choices.Count && choices[index] != current )
			index++;
		var next = (int)(((long)index + direction % choices.Count + choices.Count) % choices.Count);
		if ( choices[next] == current )
			return false;
		selected[category] = choices[next];
		return true;
	}

	private bool IsSelectable( ResearchItem item ) =>
		!completed.Contains( (item.InfoId, item.Level) ) && IsGroupOpen( item.Category, item.Group ) && PrerequisiteMet( item );

	private bool PrerequisiteMet( ResearchItem item )
	{
		if ( item.Level > 0 )
			return IsAvailable( item.InfoId, item.Level - 1 );
		return item.RequiresInfoId == 0 || IsAvailable( item.RequiresInfoId );
	}

	/// <summary>Whether a researcher in this state does research (the original skips states 3, 4 and 5 of 0x100F4170).</summary>
	// [BIN:TPW-EXE:0x00502960 researcher update] research is skipped in display states 3, 4 and 5 of 0x005064F0, which maps the internal state +0x19C to the STAFFSTATES index (0 Idle, 1 Patrolling, 2 and 3 Resting, 4 and 5 On strike, 7 Picked up, anything else Working); StaffState is that index, so walking to a rest area or a strike counts as Resting or On strike, as here; points come every 20th turn (the Mac 0x100F0284 and 0x100F4170)
	public static bool CanResearch( StaffMember member ) => member.State is not (StaffState.Resting or StaffState.OnStrike or StaffState.PickedUp);

	/// <summary>Adds one researcher's contribution of <paramref name="ability"/> points; returns the items completed.</summary>
	// [BIN:STP-PPC:0x100F0DF0 research points] for each category with effort and a current item: progress += (float)(ability × (effort / Σ effort of categories with an unfinished current item) × work load) / 100, in single precision
	public IReadOnlyList<ResearchItem> AddResearcherPoints( int ability )
	{
		var done = new List<ResearchItem>();
		var active = Enum.GetValues<ResearchCategory>().Select( category => (Category: category, Item: Current( category )) )
			.Where( entry => entry.Item != null ).ToList();
		var totalEffort = active.Sum( entry => (long)effort[(int)entry.Category] );
		if ( totalEffort == 0 )
			return done;
		foreach ( var (category, item) in active )
		{
			if ( effort[(int)category] == 0 )
				continue;
			var points = (float)((double)((float)ability * ((float)effort[(int)category] / (float)totalEffort) * (float)Workload) / 100.0);
			var key = (item!.InfoId, item.Level);
			var value = progress.GetValueOrDefault( key ) + (long)Math.Round( points * (double)PointScale );
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
		{
			foreach ( var category in done.Select( item => item.Category ).Distinct() )
				OpenGroups( category );
			PropagateFreeItems( done );
		}
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
					OpenGroups( item.Category );
					done?.Add( item );
					changed = true;
				}
			}
		}
		while ( changed );
	}

	internal void Restore( IEnumerable<(int InfoId, int Level)> restoredCompleted, IEnumerable<((int InfoId, int Level) Key, long Points)> restoredProgress, IReadOnlyList<int> restoredEffort, IEnumerable<(ResearchCategory Category, int InfoId, int Level)>? restoredCurrent = null )
	{
		selected.Clear();
		completed.Clear();
		foreach ( var key in restoredCompleted )
		{
			if ( !itemKeys.Contains( key ) )
				throw new InvalidDataException( $"Saved research refers to unknown item {key.InfoId} level {key.Level}." );
			completed.Add( key );
		}
		RebuildGroupStats();
		Array.Clear( highestGroup );
		OpenGroups();
		progress.Clear();
		foreach ( var (key, points) in restoredProgress )
			progress[key] = points;
		for ( var index = 0; index < effort.Length; index++ )
			effort[index] = Math.Clamp( restoredEffort[index], 0, 100 );
		foreach ( var (category, infoId, level) in restoredCurrent ?? Enumerable.Empty<(ResearchCategory, int, int)>() )
		{
			var item = items.FirstOrDefault( item => item.Category == category && item.InfoId == infoId && item.Level == level );
			if ( item == null || !IsSelectable( item ) || !selected.TryAdd( category, item ) )
				throw new InvalidDataException( $"Saved research cursor refers to an invalid project {infoId} level {level}." );
		}
	}

	internal IEnumerable<(ResearchCategory Category, int InfoId, int Level)> CurrentEntries =>
		selected.Where( entry => IsSelectable( entry.Value ) ).OrderBy( entry => entry.Key )
			.Select( entry => (entry.Key, entry.Value.InfoId, entry.Value.Level) );

	internal IEnumerable<((int InfoId, int Level) Key, long Points)> ProgressEntries => progress.Select( pair => (pair.Key, pair.Value) );
}
