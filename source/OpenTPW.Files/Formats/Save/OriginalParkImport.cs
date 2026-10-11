namespace OpenTPW;

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
		IReadOnlyList<SaveAttraction> attractions, SaveEconomyRecords economy, IReadOnlyList<SaveStaff> staff, SaveStaffHq? staffHq,
		SaveAdvisorController? advisor, SaveResearchLab? researchLab )
	{
		Map = map;
		Layout = layout;
		this.world = world;
		cells = world.Cells;
		Attractions = attractions;
		this.economy = economy;
		Staff = staff;
		StaffHq = staffHq;
		Advisor = advisor;
		ResearchLab = researchLab;
		PathCells = pathCells;
		PlacedObjects = placedObjects;
		FixedItems = fixedItems;
		UnresolvedObjects = unresolvedObjects;
	}

	private OriginalParkImport( MapFile map, SavePayloadLayout layout, SaveCellGrid cells, IReadOnlyList<(int X, int Y)> pathCells,
		IReadOnlyList<ImportedParkObject> placedObjects, IReadOnlyList<SaveObject> fixedItems, IReadOnlyList<SaveObject> unresolvedObjects )
	{
		Map = map;
		Layout = layout;
		this.cells = cells;
		Attractions = Array.Empty<SaveAttraction>();
		Staff = Array.Empty<SaveStaff>();
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
	public SaveStaffHq? StaffHq { get; }
	public SaveAdvisorController? Advisor { get; }
	public SaveResearchLab? ResearchLab { get; }
	public int GuestCount => World.OfModel( SaveThingModel.Guest ).Count();
	public IReadOnlyList<(int X, int Y)> PathCells { get; }
	public IReadOnlyList<ImportedParkObject> PlacedObjects { get; }
	public IReadOnlyList<SaveObject> FixedItems { get; }
	/// <summary>Placed records whose rotation/size combination has no verified footprint rule.</summary>
	public IReadOnlyList<SaveObject> UnresolvedObjects { get; }

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
	/// Imports <paramref name="payload"/> against <paramref name="map"/>. Throws
	/// <see cref="InvalidDataException"/> when the payload does not split into its blocks, the thing list does not
	/// close at the World delimiter, the save's cell attributes differ from the map (wrong level) or a placed object's
	/// footprint is outside the grid or not marked occupied.
	/// </summary>
	public static OriginalParkImport Import( ReadOnlyMemory<byte> payload, MapFile map )
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
		var hq = world.Variables.StaffHq == 0 ? null : SaveStaffHq.Read( span, world.Find( world.Variables.StaffHq )!.Value );
		var advisor = world.OfModel( SaveThingModel.AdvisorController ).Any() ? SaveAdvisorController.Read( span, world.Single( SaveThingModel.AdvisorController ) ) : null;
		var lab = world.Variables.ResearchLab == 0 ? null : SaveResearchLab.Read( span, world.Find( world.Variables.ResearchLab )!.Value );
		return new OriginalParkImport( map, layout, world, paths.AsReadOnly(), placed.AsReadOnly(), fixedItems.AsReadOnly(), unresolved.AsReadOnly(),
			SaveAttractionList.Parse( span, world ), SaveEconomyRecords.Parse( span, world ), staff.AsReadOnly(), hq, advisor, lab );
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
