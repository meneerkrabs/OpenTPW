namespace OpenTPW;

/// <summary>A structural section/object join; matching attraction ids require the same cell and Info.Id.
/// This does not identify the controller's station or assign runtime track nodes.</summary>
public sealed record ImportedCoasterSection( SaveCoasterSection Section, IReadOnlyList<SaveObject> Objects,
	IReadOnlyList<SaveAttraction> Attractions );

/// <summary>A Theme Park Inc RideSystem scene record joined to its attraction thing: the same mesh instance id (the record's u32 after its leading byte, the thing's MeshInstanceID) and Info.Id, and for a placed object the same cell.</summary>
public readonly record struct ImportedThemeParkIncObject( SaveObject Record, ThemeParkIncAttraction Attraction );

/// <summary>
/// One kart or water track cell of a save's structure layer (the 31-byte track cell, base <c>0x4D0B30</c> and the segment <c>0x50C200</c>): the origin cell of a tile (type 11 or 13 kart, 16 or 18 water) or one of its three
/// extension cells (type 12, whose parent is the origin). <see cref="Parent"/> is the cell word y × 128 + x + 1 of the origin, 0 for none.
/// </summary>
public readonly record struct SaveTrackCell( int X, int Y, byte Type, byte Mask, byte Link, ushort Flags, ushort Parent, int Family, int Piece, int Rotation, short Overlap, byte Alternate, ushort Segment, uint MeshInstance );

/// <summary>A placed object whose footprint was cross-checked against the save cell grid.</summary>
public readonly record struct ImportedParkObject( SaveObject Record, int MinX, int MinY, int MaxX, int MaxY );

/// <summary>
/// Read-only park state built from a decoded original save payload and its level MAP
/// (docs/reverse/SAVE-plan.md §8.1): the writer-order block split, the World block (variables, calendar, cell
/// map and the closed thing list) and the typed things later import steps use. Path cells, placed object records
/// with verified footprints and fixed items are cross-checked against the map. Nothing is written back.
/// </summary>
public sealed class OriginalParkImport
{
	private readonly SaveWorld? world;
	private readonly SaveEconomyRecords? economy;
	private readonly SaveCellGrid cells;

	private OriginalParkImport( MapFile map, SavePayloadLayout layout, SaveWorld world, IReadOnlyList<(int X, int Y)> pathCells,
		IReadOnlyList<ImportedParkObject> placedObjects, IReadOnlyList<SaveObject> fixedItems, IReadOnlyList<SaveObject> unresolvedObjects,
		IReadOnlyList<SaveAttraction> attractions, SaveEconomyRecords economy, IReadOnlyList<SaveStaff> staff, IReadOnlyList<SaveGuest> guests, SaveStaffHq? staffHq,
		SaveAdvisorController? advisor, SaveResearchLab? researchLab, SaveCoasterBlock coasters, SaveRideSystemBlock? rideSystem )
		: this( map, layout, world.Cells, pathCells, placedObjects, fixedItems, unresolvedObjects )
	{
		this.world = world;
		Attractions = attractions;
		this.economy = economy;
		Staff = staff;
		Guests = guests;
		StaffHq = staffHq;
		Advisor = advisor;
		ResearchLab = researchLab;
		CoasterBlock = coasters;
		RideSystem = rideSystem;
		var objectsByCell = placedObjects.Select( item => item.Record ).Concat( unresolvedObjects ).Concat( fixedItems )
			.ToLookup( record => (record.X, record.Y) );
		var attractionsByCell = attractions.ToLookup( attraction => (attraction.CellX, attraction.CellY) );
		var sections = coasters.Coasters.SelectMany( coaster => coaster.Sections ).ToArray();
		// Stacked sections may repeat a cell. Share its matches instead of copying the scene for every section.
		var matchesByCell = sections.Select( section => (section.CellX, section.CellY) ).Distinct().ToDictionary( cell => cell, cell =>
		{
			var objects = objectsByCell[cell].ToArray();
			var ids = objects.Select( record => record.InfoId ).ToHashSet();
			var matching = attractionsByCell[cell].Where( attraction => ids.Contains( attraction.InfoId ) ).ToArray();
			return (Objects: objects, Attractions: matching);
		} );
		CoasterSections = sections.Select( section =>
		{
			var matches = matchesByCell[(section.CellX, section.CellY)];
			return new ImportedCoasterSection( section, matches.Objects, matches.Attractions );
		} ).ToArray();
	}

	private OriginalParkImport( MapFile map, SavePayloadLayout layout, SaveCellGrid cells, IReadOnlyList<(int X, int Y)> pathCells,
		IReadOnlyList<ImportedParkObject> placedObjects, IReadOnlyList<SaveObject> fixedItems, IReadOnlyList<SaveObject> unresolvedObjects )
	{
		Map = map;
		Layout = layout;
		this.cells = cells;
		Attractions = Array.Empty<SaveAttraction>();
		Staff = Array.Empty<SaveStaff>();
		Guests = Array.Empty<SaveGuest>();
		PathCells = pathCells;
		PlacedObjects = placedObjects;
		FixedItems = fixedItems;
		UnresolvedObjects = unresolvedObjects;
	}

	public MapFile Map { get; }
	public SavePayloadLayout Layout { get; }
	public SaveWorld World => world ?? throw new InvalidOperationException( "The Theme Park Inc viewer does not decode save World state; see ThemeParkIncWorld." );
	/// <summary>The typed World of a Theme Park Inc save (variables, controls, staff pool, calendar, cells, things, special rides); null for a Theme Park World save.</summary>
	public ThemeParkIncSaveWorld? ThemeParkIncWorld { get; private init; }
	/// <summary>The Theme Park Inc scene records joined to their attraction things (empty for a Theme Park World save).</summary>
	public IReadOnlyList<ImportedThemeParkIncObject> ThemeParkIncObjects { get; private init; } = Array.Empty<ImportedThemeParkIncObject>();
	/// <summary>
	/// The Theme Park Inc attraction things no scene record names. Only the lake mission obstacle (Info.Id 1703) may be one; the import throws for any other.
	/// </summary>
	public IReadOnlyList<ThemeParkIncAttraction> UnrecordedThemeParkIncAttractions { get; private init; } = Array.Empty<ThemeParkIncAttraction>();

	/// <summary>The Theme Park Inc attraction of a scene record (see <see cref="ImportedThemeParkIncObject"/>); null when none was joined.</summary>
	public ThemeParkIncAttraction? FindThemeParkIncAttraction( SaveObject record )
	{
		foreach ( var item in ThemeParkIncObjects )
			if ( item.Record.Offset == record.Offset )
				return item.Attraction;
		return null;
	}
	public SaveCellGrid Cells => cells;

	private IReadOnlyList<SaveTrackCell>? trackCells;

	/// <summary>
	/// The kart and water track cells of the structure layer (a Theme Park World save), in row order: cells whose structure type is a track tile origin (11, 13, 16, 18), and the type-12 extension cells whose parent word names such an origin
	/// (a hoarding's extension cells share type 12 and name a post).
	/// </summary>
	public IReadOnlyList<SaveTrackCell> TrackCells => trackCells ??= world == null ? Array.Empty<SaveTrackCell>() : ReadTrackCells();

	/// <summary>
	/// The structure types of a track tile's origin cell: the kart tile 11 and station 13, the water tile 16 and station 18. Type 25 (0x19) is a hoarding's post (<see cref="SaveCell.HoardingType"/>), which the
	/// walk's second water type shares: the structure layer of every corpus save holds posts of it and no tile.
	/// </summary>
	public static bool IsTrackOriginType( uint type ) => type is 11 or 13 or 16 or 18;

	private IReadOnlyList<SaveTrackCell> ReadTrackCells()
	{
		var found = new List<SaveTrackCell>();
		for ( var y = 0; y < cells.CountY; y++ )
		{
			for ( var x = 0; x < cells.CountX; x++ )
			{
				var cell = cells[x, y];
				var type = cell.StructureType;
				if ( !IsTrackOriginType( type ) )
				{
					if ( type != SaveCell.HoardingExtensionType || cell.StructureParentId == 0 )
						continue;
					var parent = cell.StructureParentId - 1;
					if ( parent / 128 >= cells.CountY || parent % 128 >= cells.CountX || !IsTrackOriginType( cells[parent % 128, parent / 128].StructureType ) )
						continue;
				}
				var tile = cell.StructureTile;
				found.Add( new SaveTrackCell( x, y, (byte)type, cell.StructureMask, cell.StructureDirection, cell.StructureFlags, cell.StructureParentId, tile.Family, tile.Piece, tile.Rotation,
					cell.StructureOverlapCounter, cell.StructureAlternate, cell.StructureSegment, cell.StructureMeshInstance ) );
			}
		}
		return found;
	}

	/// <summary>The TrackRides block as a chunk stream (<see cref="SaveTrackRideBlock"/>), or null when it does not read in the writer's shapes (<see cref="TrackRidesProblem"/> says why).</summary>
	public SaveTrackRideBlock? TrackRides { get; private init; }

	/// <summary>Why <see cref="TrackRides"/> is null; null when the block read.</summary>
	public string? TrackRidesProblem { get; private init; }

	private IReadOnlyList<SaveLitter>? litter;

	/// <summary>The litter the cell grid holds (a Theme Park World save), in row order: a cell whose <c>mLitter</c> word is not 0.</summary>
	public IReadOnlyList<SaveLitter> Litter => litter ??= world == null ? Array.Empty<SaveLitter>() : ReadLitter();

	private IReadOnlyList<SaveLitter> ReadLitter()
	{
		var found = new List<SaveLitter>();
		for ( var y = 0; y < cells.CountY; y++ )
		{
			for ( var x = 0; x < cells.CountX; x++ )
			{
				var cell = cells[x, y];
				if ( cell.Litter != 0 )
					found.Add( new SaveLitter( x, y, cell.Litter, cell.LitterCollector, cell.LitterScript, cell.LitterMarkedTurn ) );
			}
		}
		return found;
	}
	/// <summary>Model-3 things in list order (rides, shops, sideshows, features and fixed items).</summary>
	public IReadOnlyList<SaveAttraction> Attractions { get; }
	public SaveEconomyRecords Economy => economy ?? throw new InvalidOperationException( "The Theme Park Inc viewer does not decode save economy state." );
	/// <summary>Staff things (models 4–8) in list order.</summary>
	public IReadOnlyList<SaveStaff> Staff { get; }
	/// <summary>The saved guests (thing model 1) in thing-list order.</summary>
	public IReadOnlyList<SaveGuest> Guests { get; }
	public SaveStaffHq? StaffHq { get; }
	public SaveAdvisorController? Advisor { get; }
	/// <summary>The referenced TPW analyser's recent-research rings; absent when no analyser is named, or for Theme Park Inc.</summary>
	public IReadOnlyList<RecentResearchRing>? RecentResearch { get; private init; }
	/// <summary>The referenced TPW or TPI analyser's validated monthly histories and host continuation inputs; null without an analyser.</summary>
	public NativeParkStatistics? Statistics { get; private init; }
	/// <summary>The referenced TPW or TPI analyser's signed arrival group sizes, oldest first (at most five); null without an analyser, an empty list for a known empty ring.</summary>
	public IReadOnlyList<int>? ArrivalGroups { get; private init; }
	/// <summary>The referenced TPW analyser's six saved time stamps (100 ns since 1601-01-01), the object and hire stamps the advisor counts days from among them; null without an analyser, or for Theme Park Inc.</summary>
	public SaveAnalyserStamps? AnalyserStamps { get; private init; }
	/// <summary>The referenced TPW analyser's three misbehaving-kids words (the guards' chases this month, the abandoned ones, all of them); null without an analyser, or for Theme Park Inc.</summary>
	public SaveAnalyserCounters? AnalyserCounters { get; private init; }
	/// <summary>
	/// The advisor clock the save carries: the second word of the clock block that follows the MESS delimiter (writer 0x402E00 after the
	/// snapshot 0x402E60, which stores the scaled clock at +0x4C and the advisor clock at +0x74; reader 0x402E30 behind the marker check 0x41555A).
	/// Null for the Theme Park Inc viewer profile.
	/// </summary>
	public uint? SavedAdvisorClockMilliseconds { get; private init; }
	public SaveResearchLab? ResearchLab { get; }
	public int GuestCount => Guests.Count;
	public IReadOnlyList<(int X, int Y)> PathCells { get; }
	public IReadOnlyList<ImportedParkObject> PlacedObjects { get; }
	public IReadOnlyList<SaveObject> FixedItems { get; }
	/// <summary>Placed records whose rotation/size combination has no verified footprint rule.</summary>
	public IReadOnlyList<SaveObject> UnresolvedObjects { get; }
	/// <summary>
	/// The parsed Coasters block (between the stored EMAK and SAOC delimiters): count, globals and, for each saved
	/// coaster, its header, auxiliary links, track sections with links and the raw train payload. Null for the
	/// Theme Park Inc viewer, whose coaster semantics are separate (no Theme Park Inc coaster chunk is inferred).
	/// </summary>
	public SaveCoasterBlock? CoasterBlock { get; }
	/// <summary>
	/// The parsed RideSystem block (writer 0x464140, loader 0x4647a0): its used/free slot counts, first free slot
	/// and the framed walk's typed records (scene-mesh instances of kinds 815/826/865 with their embedded nodes,
	/// node states and animation time controls). Null for the Theme Park Inc viewer, whose RideSystem bytes carry
	/// a separate SYSG record shape.
	/// </summary>
	/// <remarks>
	/// The animation control count of a record is not stored in the save; the original loader reads it from the
	/// live instance (which 0x4557c0 fills from the object type's entry, built by 0x4629d0). The walk derives each count from the
	/// block's one complete parse, or from the caller's table of the objects' control counts when one is given (see
	/// <see cref="SaveRideSystemBlock"/>); a block with more than one complete parse is refused.
	/// </remarks>
	public SaveRideSystemBlock? RideSystem { get; }
	/// <summary>The clock, vanilla-time, game-system, cheat, advisor-scoring, camera and UI-count blocks of a Theme Park World save, read for the import report; null for Theme Park Inc.</summary>
	public SaveRuntimeBlocks? RuntimeBlocks { get; private init; }
	/// <summary>The Sound block (loader tp.exe 0x51C400), or null for the Theme Park Inc viewer and for a block that does not read as the original writer's.</summary>
	public SaveSoundData? Sound { get; private init; }
	/// <summary>The RSSE block's script records, read in full (null for the Theme Park Inc viewer).</summary>
	public SaveRsseBlock? Rsse { get; private init; }
	/// <summary>The Particles block (loader tp.exe 0x51F7A0): the pool's globals, emitters and effectors (null for the Theme Park Inc viewer).</summary>
	public SaveParticlesBlock? Particles { get; private init; }
	/// <summary>The Scripts block (loader tp.exe 0x475730): the sprite-script slots and their VM records (null for the Theme Park Inc viewer).</summary>
	public SaveScriptsBlock? Scripts { get; private init; }
	/// <summary>Original TPW track sections joined to scene records and matching attraction things.
	/// Missing or ambiguous matches stay visible; neither record index nor header words are guessed as station ids.</summary>
	public IReadOnlyList<ImportedCoasterSection> CoasterSections { get; } = Array.Empty<ImportedCoasterSection>();

	/// <summary>
	/// The saved attraction thing a coaster block binds to: the one whose mesh instance id is the header's station mesh id (controller <c>+0x10</c>). Null when no
	/// attraction or more than one carries it.
	/// </summary>
	public SaveAttraction? CoasterStation( SaveCoaster coaster )
	{
		ArgumentNullException.ThrowIfNull( coaster );
		var matches = Attractions.Where( attraction => attraction.MeshInstanceId == coaster.StationMeshInstanceId ).Take( 2 ).ToList();
		return matches.Count == 1 ? matches[0] : null;
	}

	/// <summary>
	/// Guard before constructing runtime objects from an original TPW park: every saved coaster must bind to exactly one saved attraction thing by its station mesh id,
	/// the controller the original's loader binds it to. Where tp.exe would crash the park is refused with the reason instead; the level restores the bound coasters.
	/// Theme Park Inc has a separate viewer/import contract and is not inferred through this guard.
	/// </summary>
	// [BIN:TPW-EXE:0x437CBF coaster bind] the loader walks the controller list 0x790FE0 for the controller whose station mesh id (+0x10) is the header's +0xA, which the RideSystem loader 0x4647A0 made when it recreated the station's mesh instance; none matching dereferences a null pointer (0x437CD5), so the original crashes on such a park
	public void RequireSupportedCoasterRuntime()
	{
		if ( CoasterBlock is not { Count: > 0 } block )
			return;
		foreach ( var coaster in block.Coasters )
		{
			if ( CoasterStation( coaster ) == null )
				throw new InvalidDataException( $"The save's coaster at offset {coaster.Offset} binds to station mesh {coaster.StationMeshInstanceId}, which {(Attractions.Any( attraction => attraction.MeshInstanceId == coaster.StationMeshInstanceId ) ? "more than one" : "no")} saved attraction carries; the original game would crash loading this park (0x437CD5), so it is refused." );
		}
	}

	public bool IsOccupiedByObject( int x, int y )
	{
		foreach ( var item in PlacedObjects )
		{
			if ( x >= item.MinX && x <= item.MaxX && y >= item.MinY && y <= item.MaxY )
				return true;
		}
		return false;
	}

	/// <summary>
	/// The attraction thing of a RideSystem record: same Info.Id, and for placed objects the same cell
	/// (fixed items store 128, 128 and are matched by Info.Id alone). Null when none or more than one matches.
	/// </summary>
	public SaveAttraction? FindAttraction( SaveObject record )
	{
		SaveAttraction? found = null;
		foreach ( var attraction in Attractions )
		{
			if ( attraction.InfoId != record.InfoId || (!record.IsFixedItem && (attraction.CellX != record.X || attraction.CellY != record.Y)) )
				continue;
			if ( found != null )
				return null;
			found = attraction;
		}
		return found;
	}

	private IReadOnlyList<SaveObject>? chainOrder;
	private string? chainOrderProblem;

	/// <summary>
	/// The object records (placed, unverified footprints, fixed items) in the order the original created their things, oldest first: the order of the saved object chain read backwards (the world's
	/// first-object word +0x1DA746, each attraction's <c>mNext</c> +0xC). A save whose chain does not name every record's attraction once (a record with no single matching attraction, a thing
	/// that is not an attraction, a loop, a chain that stops short) keeps the record order (placed, unverified, fixed) and says why in <see cref="ChainOrderProblem"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x00519D80 object list insert] the constructor 0x004DB090 pushes each new object at the head of the list (game +0x1DA746, next +0xC); the saver 0x00516C80 and the loader 0x005179C0 keep the head word and each next word as they are (the attraction serializer 0x004DB7D0), so the list from the head is the objects newest first, whatever slots their things took
	public IReadOnlyList<SaveObject> ObjectsInChainOrder => chainOrder ??= ComputeChainOrder();

	/// <summary>Why <see cref="ObjectsInChainOrder"/> is the record order; null when the saved chain gave the order.</summary>
	public string? ChainOrderProblem
	{
		get
		{
			_ = ObjectsInChainOrder;
			return chainOrderProblem;
		}
	}

	private IReadOnlyList<SaveObject> ComputeChainOrder()
	{
		var records = PlacedObjects.Select( item => item.Record ).Concat( UnresolvedObjects ).Concat( FixedItems ).ToList();
		if ( world == null )
		{
			chainOrderProblem = "the Theme Park Inc viewer does not decode the world's object chain";
			return records;
		}
		var byThing = Attractions.GroupBy( attraction => attraction.Id ).ToDictionary( group => group.Key, group => group.ToList() );
		// the chain from the head, newest first
		var chain = new List<int>();
		var seen = new HashSet<int>();
		for ( int thing = World.Variables.FirstObject; thing != 0; )
		{
			if ( !byThing.TryGetValue( thing, out var found ) || found.Count != 1 || !seen.Add( thing ) )
			{
				chainOrderProblem = $"the object chain reaches thing {thing}, which is {(found == null || found.Count == 0 ? "no attraction" : found.Count > 1 ? "named by more than one attraction" : "met twice")}";
				return records;
			}
			chain.Add( thing );
			thing = found[0].Next;
		}
		var ages = new Dictionary<SaveObject, int>();
		foreach ( var record in records )
		{
			var position = FindAttraction( record ) is { } attraction ? chain.IndexOf( attraction.Id ) : -1;
			if ( position < 0 )
			{
				chainOrderProblem = $"the record of Info.Id {record.InfoId} at ({record.X}, {record.Y}) has no single attraction on the chain";
				return records;
			}
			if ( !ages.TryAdd( record, chain.Count - 1 - position ) )
			{
				chainOrderProblem = $"two records of Info.Id {record.InfoId} at ({record.X}, {record.Y}) are alike";
				return records;
			}
		}
		if ( ages.Values.Distinct().Count() != records.Count )
		{
			chainOrderProblem = "two records name the same attraction";
			return records;
		}
		return records.OrderBy( record => ages[record] ).ToList();
	}

	/// <summary>
	/// Every recognized saved object record (placed, unresolved or fixed) whose leading cell is (<paramref name="x"/>,
	/// <paramref name="y"/>). A saved coaster section's cell joins to the park through this: on the sibling Theme Park
	/// Inc prebuilt save the one saved section's cell (65, 67) carries its own 1×1 record (Info.Id 1181), while the
	/// station object (1180) stands apart — which section is the station node is not qualified, so no such claim is
	/// made here. Only recognized scene kinds (815/865) are joined: other kinds, including the sibling pylon kind 1018, are not scanned.
	/// </summary>
	public IReadOnlyList<SaveObject> ObjectsAt( int x, int y )
	{
		var records = new List<SaveObject>();
		foreach ( var item in PlacedObjects )
			if ( item.Record.X == x && item.Record.Y == y )
				records.Add( item.Record );
		foreach ( var record in UnresolvedObjects.Concat( FixedItems ) )
			if ( record.X == x && record.Y == y )
				records.Add( record );
		return records;
	}

	/// <summary>Every attraction thing whose buildable position is cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
	public IReadOnlyList<SaveAttraction> AttractionsAt( int x, int y ) =>
		Attractions.Where( attraction => attraction.CellX == x && attraction.CellY == y ).ToList();

	/// <summary>
	/// Imports <paramref name="payload"/> against <paramref name="map"/>. Throws
	/// <see cref="InvalidDataException"/> when the payload does not split into its blocks, the thing list does not
	/// close at the World delimiter, the save's cell attributes differ from the map (wrong level), a placed object's
	/// footprint is outside the grid or not marked occupied, or the Coasters block does not follow its traced contract.
	/// </summary>
	/// <param name="coasterTrains">
	/// The coaster type's train layout (<see cref="SaveCoasterTrainLayout"/>) by the station's Info.Id. The saved coasters' train payloads span the type's train slots and
	/// cars, which the file does not carry (tp.exe 0x437B60 reads them from the live type): with it the Coasters block's trains are decoded and a block of more than
	/// one coaster reads; without it only a single coaster's trains stay raw. A coaster's station is the saved attraction whose mesh instance id equals the header's
	/// controller +0x10.
	/// </param>
	/// <param name="controlCounts">
	/// The animation control count of the object with an Info.Id (<c>max(1, UsageInfo.NumSimultAnims)</c> of its .sam), for <see cref="SaveRideSystemBlock.Parse"/>; null derives every count from the RideSystem block alone.
	/// </param>
	public static OriginalParkImport Import( ReadOnlyMemory<byte> payload, MapFile map, Func<int, SaveCoasterTrainLayout?>? coasterTrains = null, Func<int, int?>? controlCounts = null )
	{
		ArgumentNullException.ThrowIfNull( map );
		var layout = SavePayloadLayout.Parse( payload.Span, coasterTrains );
		var world = SaveWorld.Parse( payload, layout, map.CellCountX, map.CellCountY );
		var cells = world.Cells;
		var paths = new List<(int X, int Y)>();
		for ( var y = 0; y < map.CellCountY; y++ )
		{
			for ( var x = 0; x < map.CellCountX; x++ )
			{
				var cell = cells[x, y];
				if ( cell.Attributes != map.GetCellAt( x, y ) )
					throw new InvalidDataException( $"Save cell ({x}, {y}) attributes differ from the level map; the save belongs to another map." );
				if ( cell.IsPath )
					paths.Add( (x, y) );
			}
		}

		var placed = new List<ImportedParkObject>();
		var fixedItems = new List<SaveObject>();
		var unresolved = new List<SaveObject>();
		// The RideSystem block is framed: the walk is the original writer's (u32 length, used/free/first-free
		// counts, then one presence byte per slot), and its records carry kinds 815, 826 and 865 of which only
		// 815 and 865 name scene-mesh objects; the others (drawn instance kinds) are skipped.
		var rideSystem = SaveRideSystemBlock.Parse( payload.Span, layout, controlCounts );
		foreach ( var record in rideSystem.Records )
		{
			if ( !record.IsPlacedObject && !record.IsFixedItem )
				continue;
			// Scene records (815 and 865) are validated before they are routed: the old signature scan's checks on the
			// Info.Id, the cell, the size, the index and the rotation, applied to the raw unsigned words.
			var saveObject = record.ToSaveObject();
			var invalid = saveObject.Validate( map.CellCountX, map.CellCountY );
			if ( invalid != null )
				throw new InvalidDataException( $"Save object {saveObject.Index} (Info.Id {saveObject.InfoId}, kind {saveObject.Kind}, slot {record.Slot}) is invalid: {invalid}." );
			if ( record.IsFixedItem )
			{
				fixedItems.Add( saveObject );
				continue;
			}
			if ( !saveObject.TryGetFootprint( out var minX, out var minY, out var maxX, out var maxY ) )
			{
				unresolved.Add( saveObject );
				continue;
			}
			if ( minX < 0 || minY < 0 || maxX >= map.CellCountX || maxY >= map.CellCountY )
				throw new InvalidDataException( $"Save object {saveObject.Index} (Info.Id {saveObject.InfoId}) footprint lies outside the map." );
			for ( var y = minY; y <= maxY; y++ )
			{
				for ( var x = minX; x <= maxX; x++ )
				{
					if ( !cells[x, y].IsOccupied )
						throw new InvalidDataException( $"Save object {saveObject.Index} (Info.Id {saveObject.InfoId}) footprint cell ({x}, {y}) is not occupied in the cell grid." );
				}
			}
			placed.Add( new ImportedParkObject( saveObject, minX, minY, maxX, maxY ) );
		}
		var span = payload.Span;
		var analyserId = world.Variables.ParkAnalyser;
		var analyser = analyserId == 0 ? (SaveThing?)null : world.Find( analyserId )
			?? throw new InvalidDataException( "The save names no existing park analyser thing." );
		var recentResearch = analyser is { } researchThing ? SaveRecentResearch.Read( span, researchThing ) : null;
		var statistics = analyser is { } statisticsThing ? SaveParkStatistics.Read( span, statisticsThing ) : null;
		var arrivalGroups = analyser is { } arrivalThing ? SaveArrivalRing.Read( span, arrivalThing ) : null;
		var analyserStamps = analyser is { } stampThing ? SaveAnalyserStamps.Read( span, stampThing ) : (SaveAnalyserStamps?)null;
		var analyserCounters = analyser is { } counterThing ? SaveAnalyserCounters.Read( span, counterThing ) : (SaveAnalyserCounters?)null;
		var staff = world.Things.Where( thing => thing.Model is >= SaveThingModel.Mechanic and <= SaveThingModel.Researcher ).ToList()
			.Select( thing => SaveStaff.Read( payload.Span, thing ) ).ToList();
		var guests = world.OfModel( SaveThingModel.Guest ).ToList().Select( thing => SaveGuest.Read( payload.Span, thing ) ).ToList();
		var hq = world.Variables.StaffHq == 0 ? null : SaveStaffHq.Read( span, world.Find( world.Variables.StaffHq )!.Value );
		var advisor = world.OfModel( SaveThingModel.AdvisorController ).Any() ? SaveAdvisorController.Read( span, world.Single( SaveThingModel.AdvisorController ) ) : null;
		var lab = world.Variables.ResearchLab == 0 ? null : SaveResearchLab.Read( span, world.Find( world.Variables.ResearchLab )!.Value );
		// The coaster block's reader is as strict as the rest of the import: a block that does not follow the traced
		// contract refuses the save instead of importing a guessed track.
		var attractions = SaveAttractionList.Parse( span, world );
		Func<ushort, SaveCoasterTrainLayout?>? trainLayouts = coasterTrains == null ? null : station =>
		{
			var matches = attractions.Where( attraction => attraction.MeshInstanceId == station ).Take( 2 ).ToList();
			return matches.Count == 1 ? coasterTrains( matches[0].InfoId ) : null;
		};
		var coasters = SaveCoasterBlock.Parse( span, layout, map.CellCountX, map.CellCountY, trainLayouts );
		var clockBlock = layout[SaveBlock.Clock];
		var savedClock = clockBlock.Length == SavePayloadLayout.FixedBlockBytes[SaveBlock.Clock] ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( clockBlock.Offset + 4, 4 ) ) : (uint?)null;
		SaveTrackRideBlock? trackRides = null;
		string? trackRidesProblem = null;
		try
		{
			trackRides = SaveTrackRideBlock.ParseContainer( layout.Slice( span, SaveBlock.TrackRides ) );
		}
		catch ( InvalidDataException problem )
		{
			trackRidesProblem = problem.Message;
		}
		return new OriginalParkImport( map, layout, world, paths.AsReadOnly(), placed.AsReadOnly(), fixedItems.AsReadOnly(), unresolved.AsReadOnly(),
			attractions, SaveEconomyRecords.Parse( span, world ), staff.AsReadOnly(), guests.AsReadOnly(), hq, advisor, lab, coasters, rideSystem )
		{
			TrackRides = trackRides,
			TrackRidesProblem = trackRidesProblem,
			RuntimeBlocks = SaveRuntimeBlocks.Read( span, layout ),
			Sound = OriginalSavePayload.TryReadSound( span, layout ),
			Rsse = SaveRsseBlock.Read( span, layout ),
			Particles = SaveParticlesBlock.Parse( span, layout ),
			Scripts = SaveScriptsBlock.Parse( span, layout ),
			SavedAdvisorClockMilliseconds = savedClock,
			RecentResearch = recentResearch,
			Statistics = statistics,
			ArrivalGroups = arrivalGroups,
			AnalyserStamps = analyserStamps,
			AnalyserCounters = analyserCounters,
		};
	}

	/// <summary>
	/// Imports a Theme Park Inc save (the prebuilt profile): the payload is split by the Theme Park Inc block readers, its World block is read into typed records
	/// (<see cref="ThemeParkIncSaveWorld"/>: the status-byte cell map, the marker-checked thing chain), and the RideSystem block's SYSG object headers (an extra u32 before Info.Id)
	/// are joined to the attraction things by mesh instance id and Info.Id. Objects without a fully occupied, verified footprint remain in <see cref="UnresolvedObjects"/> with
	/// their saved coordinates. Throws <see cref="InvalidDataException"/> when the World does not read exactly, the cell attributes differ from the map, or a scene record names
	/// no attraction thing.
	/// </summary>
	// [DATA:PreBuilt/prebuilt.TPWS:SYSG] the RideSystem block's scene records are still found by signature (its record walk is not decoded); each must name exactly one attraction thing of the typed World, which replaces the former requirement of exactly 23 records with 14 fixed items
	/// <summary>The Info.Ids of attraction things that may have no scene record: the lake mission obstacle, whose RideSystem entry is not an 815/865 scene record.</summary>
	private static readonly HashSet<int> UnrecordedAttractionInfoIds = new() { 1703 };

	public static OriginalParkImport ImportThemeParkInc( ReadOnlyMemory<byte> payload, MapFile map )
	{
		ArgumentNullException.ThrowIfNull( map );
		var layout = SavePayloadLayout.ParseThemeParkInc( payload.Span );
		var tpiWorld = ThemeParkIncSaveWorld.Parse( payload, layout, map.CellCountX, map.CellCountY );
		var statistics = SaveThemeParkIncStatistics.Read( tpiWorld );
		var arrivalGroups = SaveThemeParkIncArrivalRing.Read( tpiWorld );
		var cells = tpiWorld.Cells;
		var paths = new List<(int X, int Y)>();
		for ( var y = 0; y < map.CellCountY; y++ )
		{
			for ( var x = 0; x < map.CellCountX; x++ )
			{
				var cell = cells[x, y];
				if ( cell.Attributes != map.GetCellAt( x, y ) )
					throw new InvalidDataException( $"Theme Park Inc save cell ({x}, {y}) attributes differ from the level map; the save belongs to another map." );
				if ( cell.IsPath )
					paths.Add( (x, y) );
			}
		}

		var placed = new List<ImportedParkObject>();
		var fixedItems = new List<SaveObject>();
		var unresolved = new List<SaveObject>();
		var joined = new List<ImportedThemeParkIncObject>();
		var named = new HashSet<int>();
		var span = payload.Span;
		foreach ( var record in ThemeParkIncSaveObjectList.Parse( span, layout, map.CellCountX, map.CellCountY ) )
		{
			// the record's u32 after its leading byte is the scene mesh instance, the thing's MeshInstanceID
			var mesh = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( span[(record.Offset + 1)..] );
			var matches = tpiWorld.Attractions.Where( attraction => attraction.MeshInstanceId == mesh && attraction.InfoId == record.InfoId
				&& (record.IsFixedItem || attraction.CellX == record.X && attraction.CellY == record.Y) ).Take( 2 ).ToList();
			if ( matches.Count != 1 )
				throw new InvalidDataException( $"Theme Park Inc scene record {record.Index} (mesh {mesh}, Info.Id {record.InfoId}) matches {matches.Count} attraction things; expected one." );
			if ( !named.Add( matches[0].Id ) )
				throw new InvalidDataException( $"Theme Park Inc scene record {record.Index} (mesh {mesh}, Info.Id {record.InfoId}) names attraction thing {matches[0].Id}, which another scene record already names." );
			joined.Add( new ImportedThemeParkIncObject( record, matches[0] ) );
			if ( record.IsFixedItem )
			{
				fixedItems.Add( record );
				continue;
			}
			if ( !record.TryGetFootprint( out var minX, out var minY, out var maxX, out var maxY )
				|| minX < 0 || minY < 0 || maxX >= map.CellCountX || maxY >= map.CellCountY )
			{
				unresolved.Add( record );
				continue;
			}
			var occupied = true;
			for ( var y = minY; y <= maxY; y++ )
				for ( var x = minX; x <= maxX; x++ )
					occupied &= cells[x, y].IsOccupied;
			if ( occupied )
				placed.Add( new ImportedParkObject( record, minX, minY, maxX, maxY ) );
			else
				unresolved.Add( record );
		}
		var unrecorded = tpiWorld.Attractions.Where( attraction => !named.Contains( attraction.Id ) ).ToList();
		// [DATA:PreBuilt/prebuilt.TPWS:SYSG] the lake obstacle (Info.Id 1703) is the one attraction the prebuilt's scene records do not name; any other attraction without a record means the signature scan lost a damaged record, so the save is refused
		foreach ( var attraction in unrecorded )
			if ( !UnrecordedAttractionInfoIds.Contains( attraction.InfoId ) )
				throw new InvalidDataException( $"Theme Park Inc attraction thing {attraction.Id} (Info.Id {attraction.InfoId}, mesh {attraction.MeshInstanceId}) has no scene record; the RideSystem record for it is damaged or missing." );
		return new OriginalParkImport( map, layout, cells, paths.AsReadOnly(), placed.AsReadOnly(), fixedItems.AsReadOnly(), unresolved.AsReadOnly() )
		{
			ThemeParkIncWorld = tpiWorld,
			Statistics = statistics,
			ArrivalGroups = arrivalGroups,
			ThemeParkIncObjects = joined.AsReadOnly(),
			UnrecordedThemeParkIncAttractions = unrecorded.AsReadOnly(),
		};
	}
}

/// <summary>One litter item of the saved cell grid: its type (1 to 8 as the original's <c>mLitter</c>, anything else is kept as read), the thing id of the handyman who claimed it (0: none), its sprite's script pointer and the game turn of the claim.</summary>
public readonly record struct SaveLitter( int X, int Y, uint Type, ushort Collector, uint Script, uint MarkedTurn );
