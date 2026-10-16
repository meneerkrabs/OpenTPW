namespace OpenTPW;

/// <summary>A structural section/object join; matching attraction ids require the same cell and Info.Id.
/// This does not identify the controller's station or assign runtime track nodes.</summary>
public sealed record ImportedCoasterSection( SaveCoasterSection Section, IReadOnlyList<SaveObject> Objects,
	IReadOnlyList<SaveAttraction> Attractions );

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
		SaveAdvisorController? advisor, SaveResearchLab? researchLab, SaveCoasterBlock coasters )
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
	public SaveWorld World => world ?? throw new InvalidOperationException( "The Theme Park Inc viewer does not decode save World state." );
	public SaveCellGrid Cells => cells;
	/// <summary>Model-3 things in list order (rides, shops, sideshows, features and fixed items).</summary>
	public IReadOnlyList<SaveAttraction> Attractions { get; }
	public SaveEconomyRecords Economy => economy ?? throw new InvalidOperationException( "The Theme Park Inc viewer does not decode save economy state." );
	/// <summary>Staff things (models 4–8) in list order.</summary>
	public IReadOnlyList<SaveStaff> Staff { get; }
	/// <summary>The saved guests (thing model 1) in thing-list order.</summary>
	public IReadOnlyList<SaveGuest> Guests { get; }
	public SaveStaffHq? StaffHq { get; }
	public SaveAdvisorController? Advisor { get; }
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
	/// <summary>The clock, vanilla-time, game-system, cheat, advisor-scoring, camera and UI-count blocks of a Theme Park World save, read for the import report; null for Theme Park Inc.</summary>
	public SaveRuntimeBlocks? RuntimeBlocks { get; private init; }
	/// <summary>Original TPW track sections joined to scene records and matching attraction things.
	/// Missing or ambiguous matches stay visible; neither record index nor header words are guessed as station ids.</summary>
	public IReadOnlyList<ImportedCoasterSection> CoasterSections { get; } = Array.Empty<ImportedCoasterSection>();

	/// <summary>Guard before constructing runtime objects from an original TPW park. Structural inspection remains
	/// available, but nonempty original controllers cannot safely be resumed until station joins and train data are decoded.
	/// Theme Park Inc has a separate viewer/import contract and is not inferred through this guard.</summary>
	// [APPROX:SAVE-920] a park whose Coasters block lists a coaster is refused: the block is decoded (headers, sections, trains and cars) but no runtime coaster is rebuilt from it, where tp.exe's loader 0x437B60 restores each controller, its track nodes and links, its trains and cars — evidence needed: a Theme Park World save with a built coaster and the restore of a CoasterRide from the decoded block
	public void RequireSupportedCoasterRuntime()
	{
		if ( CoasterBlock is { Count: > 0 } )
			throw new InvalidDataException( "Original TPW coaster runtime import is unsupported: the saved controller, its track sections and its trains are decoded, but no runtime track, controller or train is rebuilt from them. Use the structural CoasterBlock and CoasterSections for inspection." );
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
	public static OriginalParkImport Import( ReadOnlyMemory<byte> payload, MapFile map, Func<int, SaveCoasterTrainLayout?>? coasterTrains = null )
	{
		ArgumentNullException.ThrowIfNull( map );
		var layout = SavePayloadLayout.Parse( payload.Span );
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
		foreach ( var record in SaveObjectList.Parse( payload.Span, layout, map.CellCountX, map.CellCountY ) )
		{
			if ( record.IsFixedItem )
			{
				fixedItems.Add( record );
				continue;
			}
			if ( !record.TryGetFootprint( out var minX, out var minY, out var maxX, out var maxY ) )
			{
				unresolved.Add( record );
				continue;
			}
			if ( minX < 0 || minY < 0 || maxX >= map.CellCountX || maxY >= map.CellCountY )
				throw new InvalidDataException( $"Save object {record.Index} (Info.Id {record.InfoId}) footprint lies outside the map." );
			for ( var y = minY; y <= maxY; y++ )
			{
				for ( var x = minX; x <= maxX; x++ )
				{
					if ( !cells[x, y].IsOccupied )
						throw new InvalidDataException( $"Save object {record.Index} (Info.Id {record.InfoId}) footprint cell ({x}, {y}) is not occupied in the cell grid." );
				}
			}
			placed.Add( new ImportedParkObject( record, minX, minY, maxX, maxY ) );
		}
		var span = payload.Span;
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
		return new OriginalParkImport( map, layout, world, paths.AsReadOnly(), placed.AsReadOnly(), fixedItems.AsReadOnly(), unresolved.AsReadOnly(),
			attractions, SaveEconomyRecords.Parse( span, world ), staff.AsReadOnly(), guests.AsReadOnly(), hq, advisor, lab, coasters )
		{
			RuntimeBlocks = SaveRuntimeBlocks.Read( span, layout ),
		};
	}

	/// <summary>
	/// Imports the observed Theme Park Inc prebuilt-save profile. Cell records are 88/98 bytes and
	/// SYSG object headers have an extra u32 before Info.Id. Objects without a fully occupied,
	/// verified footprint remain in <see cref="UnresolvedObjects"/> with their saved coordinates.
	/// </summary>
	public static OriginalParkImport ImportThemeParkInc( ReadOnlyMemory<byte> payload, MapFile map )
	{
		ArgumentNullException.ThrowIfNull( map );
		var layout = SavePayloadLayout.ParseThemeParkInc( payload.Span );
		var worldBlock = layout[SaveBlock.World];
		var cells = SaveCellGrid.ParseThemeParkInc( payload, worldBlock.Offset, worldBlock.End, map.CellCountX, map.CellCountY );
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
		var records = ThemeParkIncSaveObjectList.Parse( payload.Span, layout, map.CellCountX, map.CellCountY );
		// [DATA:PreBuilt/prebuilt.TPWS:SYSG] Fail closed if the only supported prebuilt profile was only partly scanned.
		if ( records.Count != 23 || records.Count( record => record.IsFixedItem ) != 14 )
			throw new InvalidDataException( "Unsupported Theme Park Inc prebuilt SYSG object profile." );
		foreach ( var record in records )
		{
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
		return new OriginalParkImport( map, layout, cells, paths.AsReadOnly(), placed.AsReadOnly(), fixedItems.AsReadOnly(), unresolved.AsReadOnly() );
	}
}
