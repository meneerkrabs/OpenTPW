namespace OpenTPW;

/// <summary>A researchable unlock: an object (<see cref="Level"/> 0) or one of a ride's upgrade levels (1, 2).</summary>
public sealed record ResearchItem( int InfoId, int Level, ResearchCategory Category, int Group, int Cost, int RequiresInfoId );

/// <summary>
/// The research lab. Data-driven: items, their categories and groups (<c>Research.Category</c>,
/// <c>Research.Group</c> — group 0 "available initially"), costs (<c>Upgrades[n].CostOfResearch</c>,
/// "research points taken for item to be researched"), the starting effort per category
/// (<c>ResearchCategories[n].Effort</c>), researcher ability per grade (<c>ResearchAbility</c>) and
/// the group thresholds (<c>ResearchTech[g].PercentageForThisTech</c>). An object whose research cost is 0
/// starts researched, whatever its group; any other object, group 0 included, is researched in the lab.
/// Traced in the Mac binary: every 20 park turns each researcher adds <c>ResearchAbility</c> ×
/// effort share × <c>Research.StartingWorkLoad</c> / 100 points to every category's current item; a
/// category's highest open group rises while the percentage of researched items in its open groups
/// reaches the next <c>ResearchTech</c> threshold; each category researches the first open item in table order.
/// <b>Approximations</b>: Instant Action research runs at one grade-2 researcher's ability without staff;
/// progress is kept in thousandths of a point. Researcher <c>WorkDuration</c> is not used.
/// </summary>
public sealed class ParkResearch
{
	public const int PointScale = 1000;
	private readonly BalanceSettings settings;
	private readonly IEconomyObjectCatalog catalog;
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
		this.catalog = catalog;
		effort = settings.ResearchEffort.ToArray();
		Workload = Math.Clamp( settings.ResearchStartingWorkLoad, 0, 100 );
		items = new List<ResearchItem>();
		// [BIN:TPW-EXE:0x00413140 category scan order] the object definitions are loaded Rides, Features, Upgrades, Shops, Sideshow, each directory in archive-name order, and the lab's fill
		// 0x005031A0 walks that list (0x00412E60, 0x00412E70) without sorting: the shipped Jungle lab holds 1140, 1150, 1108, 1180, 1109, 1113, 1110, 1160 (rides), 1414, 1429, 1424, ... (features), 1202, 1212, 1206 (shops), 1304, 1307
		foreach ( var info in catalog.Objects.Select( ( info, position ) => (Info: info, Position: position) ).OrderBy( entry => ScanRank( entry.Info.Kind ) ).ThenBy( entry => entry.Position ).Select( entry => entry.Info ) )
		{
			if ( !info.IsBuyable || info.Upgrades.Count == 0 )
				continue;
			// [BIN:TPW-EXE:0x005031A0 research table fill] an object's base project takes its own Research.Category (+0x17C) and Research.Group (+0x178), add-ons included
			// (every shipped add-on declares category 4 and group 0); a category-4 project is selectable once the object its AddOn.UpgradesId names (+0x284) is researched (0x005034F0, with its golden-ticket test below)
			var category = info.ResearchCategory;
			var baseItem = new ResearchItem( info.InfoId, 0, category, info.ResearchGroup, info.Upgrades[0].CostOfResearch, category == ResearchCategory.Upgrade ? info.AddOnTargetId : 0 );
			items.Add( baseItem );
			TableOf( category ).Add( baseItem );
			// [BIN:TPW-EXE:0x00504580 upgrade level project] an object whose own category is not 4 gets its next level's project in category 4, group 0, once the level below is
			// researched (levels 1 and 2; a level without research cost is researched at once); on the shipped data only rides have such levels
			if ( category == ResearchCategory.Upgrade )
				continue;
			for ( var level = 1; level < Math.Min( info.Upgrades.Count, 3 ); level++ )
				items.Add( new ResearchItem( info.InfoId, level, ResearchCategory.Upgrade, 0, info.Upgrades[level].CostOfResearch, info.InfoId ) );
			// [BIN:TPW-EXE:0x005031C5 research table fill] a base without research cost is researched at once and its next level's project joins the table at this point of the walk (0x00504580)
			if ( baseItem.Cost == 0 )
				AppendNextLevel( baseItem );
		}
		foreach ( var item in items )
			itemKeys.Add( (item.InfoId, item.Level) );
		// [BIN:TPW-EXE:0x005031E5 research table fill] the fill 0x005031A0 (from the lab's start 0x00502D60) marks a choosable type (+0x508 = 0, Info.WhichUIType below 4)
		// researched (its control record +0x10 = 1, 0x0050320A) only when Upgrades[0].CostOfResearch (+0x180) is 0, with no group, category or add-on target test;
		// every other type, group 0 included ("0 = Available initially" in the category files), becomes a lab project; the only other writer of the flag is the
		// research completion 0x00504630 (on the shipped data this keeps Jungle's Wooden Log, group 0 at cost 300, to be researched)
		foreach ( var item in items.Where( item => item.Level == 0 && item.Cost == 0 ) )
			completed.Add( (item.InfoId, item.Level) );
		RebuildGroupStats();
		OpenGroups();
		PropagateFreeItems();
		initialUpgradeTable = TableOf( ResearchCategory.Upgrade ).ToList();
	}

	private List<ResearchItem> initialUpgradeTable = new();

	private List<ResearchItem> TableOf( ResearchCategory category )
	{
		if ( !byCategory.TryGetValue( category, out var list ) )
			byCategory[category] = list = new List<ResearchItem>();
		return list;
	}

	/// <summary>
	/// Appends the project of the next level above a researched <paramref name="item"/> of an object that is no add-on to the end of the category-4 table: the first level above it
	/// with a research cost; a free level in between is researched at once (<see cref="PropagateFreeItems"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00504580 upgrade level project] an object whose own category is not 4 takes its next level above the researched one (+0x14) that has a research cost (+0x180 + 0x40 × level) as a new project (category 4, group 0, the cost, no progress); levels without a cost are marked researched on the way; the lab's completion handler 0x00503D40 calls it at 0x00503D8C, the table fill at its end
	private void AppendNextLevel( ResearchItem item )
	{
		if ( !catalog.TryGet( item.InfoId, out var info ) || info.ResearchCategory == ResearchCategory.Upgrade )
			return;
		for ( var next = item.Level + 1; next < Math.Min( info.Upgrades.Count, 3 ); next++ )
		{
			if ( info.Upgrades[next].CostOfResearch <= 0 )
				continue;
			var project = items.FirstOrDefault( entry => entry.InfoId == item.InfoId && entry.Level == next );
			var table = TableOf( ResearchCategory.Upgrade );
			if ( project != null && !table.Contains( project ) )
				table.Add( project );
			return;
		}
	}

	/// <summary>The category-4 table in order: the add-on projects and the level projects as they were appended (saved and restored with the lab).</summary>
	internal IReadOnlyList<(int InfoId, int Level)> UpgradeTable => TableOf( ResearchCategory.Upgrade ).Select( item => (item.InfoId, item.Level) ).ToList();

	/// <summary>
	/// The table to save: null while it is the one a restore rebuilds without it (a new park's, plus the level projects of the completed projects in scan order), so a park save
	/// carries the order only once the completions have made it differ.
	/// </summary>
	internal IReadOnlyList<(int InfoId, int Level)>? UpgradeTableToSave
	{
		get
		{
			var expected = initialUpgradeTable.ToList();
			var current = TableOf( ResearchCategory.Upgrade );
			var saved = current.ToList();
			current.Clear();
			current.AddRange( expected );
			foreach ( var item in items.Where( item => completed.Contains( (item.InfoId, item.Level) ) ).ToList() )
				AppendNextLevel( item );
			var rebuilt = current.Select( item => (item.InfoId, item.Level) ).ToList();
			current.Clear();
			current.AddRange( saved );
			var actual = saved.Select( item => (item.InfoId, item.Level) ).ToList();
			return rebuilt.SequenceEqual( actual ) ? null : actual;
		}
	}

	/// <summary>The position of a directory in the original's object scan (Rides, Features, Upgrades, Shops, Sideshow).</summary>
	private static int ScanRank( ParkObjectKind kind ) => kind switch
	{
		ParkObjectKind.Ride => 0,
		ParkObjectKind.Feature => 1,
		ParkObjectKind.Upgrade => 2,
		ParkObjectKind.Shop => 3,
		ParkObjectKind.Sideshow => 4,
		_ => 5
	};

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
		AppendNextLevel( item );
	}

	// [BIN:STP-PPC:0x100F15B4 research group opener] with h the category's highest open group, the items of groups <= h are counted; while done × 100 / count (unsigned) reaches ResearchTech[h + 1].PercentageForThisTech and h <= 6, h rises by one
	public bool IsGroupOpen( ResearchCategory category, int group ) => group <= highestGroup[(int)category];

	private void OpenGroups()
	{
		foreach ( var category in Enum.GetValues<ResearchCategory>() )
			OpenGroups( category );
	}

	/// <summary>Opens the groups the percentage of researched items allows; true when the category's highest open group rose.</summary>
	private bool OpenGroups( ResearchCategory category )
	{
		var risen = false;
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
			risen = true;
		}
		return risen;
	}

	private List<ResearchCategory> lastGroupsOpened = new();
	private List<ResearchItem> lastResearched = new();

	/// <summary>The categories whose highest open group rose with the completions of the last <see cref="AddResearcherPoints"/> (the original's group opener 0x503B80, run for each completion).</summary>
	public IReadOnlyList<ResearchCategory> LastGroupsOpened => lastGroupsOpened;
	/// <summary>The items the last <see cref="AddResearcherPoints"/> completed with research points (the original's completion message 0x11), not the free items that followed them.</summary>
	public IReadOnlyList<ResearchItem> LastResearched => lastResearched;

	/// <summary>Whether the category has an item that is not researched yet (a record of its category whose progress is below its target, 0x503BC0).</summary>
	public bool HasUnresearched( ResearchCategory category ) => items.Any( item => item.Category == category && !completed.Contains( (item.InfoId, item.Level) ) );

	/// <summary>Whether every item of every category is researched (the lab's check after a completion, 0x503E3F).</summary>
	public bool AllResearched => items.All( item => completed.Contains( (item.InfoId, item.Level) ) );

	/// <summary>The item a category is currently researching, or null when nothing is researchable.</summary>
	// [BIN:STP-PPC:0x100F0EF0 research cursor] each category researches the first item in table order whose group is open and which is not yet researched; cost is never compared
	// [APPROX:ECON-017] a new park's research table follows the original's object scan (Rides, Features, Upgrades, Shops, Sideshow, each directory in archive-name order: 0x413140, then the fill 0x5031A0 over 0x412E60 / 0x412E70 with no sort; the shipped Jungle Easymode lab's order matches), a level project joins the category-4 table at its end when the level below completes (0x503D40 → 0x504580 at 0x503D8C, or at the base's turn in the fill when the base is free), the cursor steps and moves on as the original's picker does (0x4AA0C0 → 0x5034F0 / 0x5036A0, 0x4AAEC0) and its add-on test holds a target with a golden-ticket cost to the ticket set (0x4D4B70 over 0x5AF780), but OpenTPW lists a project for every buyable object with research levels, where the fill skips a type whose word +0x508 is set (1 when the type's model load 0x4629D0 gave 0, 0x414305): the shipped Easymode lab holds 24 projects and none for the coasters 1185 and 1190, Sun God 1106 and seven of the features that OpenTPW lists, though the archives of all ten hold their main model (OpenTPW's catalogue needs it), so the loader's failure (0x004629D0 → the archive lookup 0x00461A60 and the parse 0x00461F10, stored at +0x490, then +0x508 = 1 at 0x00414305) is not a missing file — evidence needed: why those types have no model result in that game (the file names the object loader 0x00413C10 builds from the settings stack), and so which projects a normal game's lab holds
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
		!completed.Contains( (item.InfoId, item.Level) ) && IsGroupOpen( item.Category, item.Group ) && PrerequisiteMet( item )
		&& (item.Category != ResearchCategory.Upgrade || TableOf( ResearchCategory.Upgrade ).Contains( item ));

	/// <summary>Whether the player owns an object bought with golden tickets (the profile's set, tp.exe <c>0x004D4B70</c> over <c>0x00F7C58C</c>); null: every one.</summary>
	internal Func<int, bool>? OwnsTicketItem { get; set; }

	private bool PrerequisiteMet( ResearchItem item )
	{
		if ( item.Level > 0 )
			return IsAvailable( item.InfoId, item.Level - 1 );
		if ( item.RequiresInfoId == 0 )
			return true;
		if ( !IsAvailable( item.RequiresInfoId ) )
			return false;
		// [BIN:TPW-EXE:0x005034F0 add-on picker] a category-4 project is eligible when the object its AddOn.UpgradesId names (+0x284, else the project's own id) is researched and, when that
		// object has a golden-ticket cost (+0xC4 above 0), the player's set of ticket-bought items (0x005AF780 through 0x004D4B70) holds it
		return !(catalog.TryGet( item.RequiresInfoId, out var target ) && target.GoldenTicketCost > 0 && OwnsTicketItem?.Invoke( item.RequiresInfoId ) == false);
	}

	/// <summary>Whether a researcher in this state does research (the original skips states 3, 4 and 5 of 0x100F4170).</summary>
	// [BIN:TPW-EXE:0x00502960 researcher update] research is skipped in display states 3, 4 and 5 of 0x005064F0, which maps the internal state +0x19C to the STAFFSTATES index (0 Idle, 1 Patrolling, 2 and 3 Resting, 4 and 5 On strike, 7 Picked up, anything else Working); StaffState is that index, so walking to a rest area or a strike counts as Resting or On strike, as here; points come every 20th turn (the Mac 0x100F0284 and 0x100F4170)
	public static bool CanResearch( StaffMember member ) => member.State is not (StaffState.Resting or StaffState.OnStrike or StaffState.PickedUp);

	/// <summary>Adds one researcher's contribution of <paramref name="ability"/> points; returns the items completed.</summary>
	// [BIN:STP-PPC:0x100F0DF0 research points] for each category with effort and a current item: progress += (float)(ability × (effort / Σ effort of categories with an unfinished current item) × work load) / 100, in single precision
	public IReadOnlyList<ResearchItem> AddResearcherPoints( int ability )
	{
		var done = new List<ResearchItem>();
		var finished = new List<ResearchItem>();
		lastGroupsOpened = new List<ResearchCategory>();
		lastResearched = finished;
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
				finished.Add( item );
			}
			else
				progress[key] = value;
		}
		if ( done.Count > 0 )
		{
			foreach ( var category in done.Select( item => item.Category ).Distinct() )
				if ( OpenGroups( category ) )
					lastGroupsOpened.Add( category );
			PropagateFreeItems( done );
		}
		foreach ( var item in finished )
			AdvanceCursor( item );
		return done;
	}

	/// <summary>Moves a category's cursor on from its completed project: the next selectable project after it in table order, wrapping to the first.</summary>
	// [BIN:TPW-EXE:0x004AAEC0 research completed] the completion's message 0x11 (0x00504630) reaches the lab (0x00503D40), which opens the groups (0x00503B80) and posts
	// message 0x12 with the category; its handler (0x0050CA50 → 0x00481520) runs the forward picker 0x005034F0 when the cursor's project is finished (0x00504040): the first
	// eligible project after the cursor, else the first before it, else none
	private void AdvanceCursor( ResearchItem finished )
	{
		selected.Remove( finished.Category );
		if ( !byCategory.TryGetValue( finished.Category, out var list ) )
			return;
		var index = list.IndexOf( finished );
		var next = list.Skip( index + 1 ).FirstOrDefault( IsSelectable ) ?? list.Take( index ).FirstOrDefault( IsSelectable );
		if ( next != null )
			selected[finished.Category] = next;
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

	/// <summary>Whether the lab has an item of <paramref name="infoId"/> at <paramref name="level"/>.</summary>
	internal bool HasItem( int infoId, int level ) => itemKeys.Contains( (infoId, level) );

	/// <summary>Makes an imported project its category's current item when it may be researched now; false otherwise.</summary>
	internal bool TrySelect( ResearchCategory category, int infoId, int level )
	{
		var item = items.FirstOrDefault( item => item.Category == category && item.InfoId == infoId && item.Level == level );
		if ( item == null || !IsSelectable( item ) )
			return false;
		selected[category] = item;
		return true;
	}

	internal void Restore( IEnumerable<(int InfoId, int Level)> restoredCompleted, IEnumerable<((int InfoId, int Level) Key, long Points)> restoredProgress, IReadOnlyList<int> restoredEffort, IEnumerable<(ResearchCategory Category, int InfoId, int Level)>? restoredCurrent = null,
		IReadOnlyList<(int InfoId, int Level)>? upgradeOrder = null )
	{
		selected.Clear();
		completed.Clear();
		foreach ( var key in restoredCompleted )
		{
			if ( !itemKeys.Contains( key ) )
				throw new InvalidDataException( $"Saved research refers to unknown item {key.InfoId} level {key.Level}." );
			completed.Add( key );
		}
		RestoreUpgradeTable( upgradeOrder );
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

	/// <summary>
	/// Puts the category-4 table back: in the saved order when there is one (a lab's saved projects, a park save's field), else the table a new park starts with plus the level projects the
	/// completed projects append, in scan order (the order of completion is not kept in older saves).
	/// </summary>
	private void RestoreUpgradeTable( IReadOnlyList<(int InfoId, int Level)>? upgradeOrder )
	{
		var table = TableOf( ResearchCategory.Upgrade );
		table.Clear();
		if ( upgradeOrder != null )
		{
			foreach ( var key in upgradeOrder )
			{
				var project = items.FirstOrDefault( entry => entry.InfoId == key.InfoId && entry.Level == key.Level && entry.Category == ResearchCategory.Upgrade );
				if ( project == null )
					throw new InvalidDataException( $"Saved research table refers to unknown category-4 project {key.InfoId} level {key.Level}." );
				if ( !table.Contains( project ) )
					table.Add( project );
			}
			return;
		}
		table.AddRange( initialUpgradeTable );
		foreach ( var item in items.Where( item => completed.Contains( (item.InfoId, item.Level) ) ).ToList() )
			AppendNextLevel( item );
	}

	internal IEnumerable<(ResearchCategory Category, int InfoId, int Level)> CurrentEntries =>
		selected.Where( entry => IsSelectable( entry.Value ) ).OrderBy( entry => entry.Key )
			.Select( entry => (entry.Key, entry.Value.InfoId, entry.Value.Level) );

	internal IEnumerable<((int InfoId, int Level) Key, long Points)> ProgressEntries => progress.Select( pair => (pair.Key, pair.Value) );
}
