namespace OpenTPW;

/// <summary>A placed object whose footprint was cross-checked against the save cell grid.</summary>
public readonly record struct ImportedParkObject( SaveObject Record, int MinX, int MinY, int MaxX, int MaxY );

/// <summary>
/// Read-only park state built from a decoded original save payload and its level MAP. Only
/// fields with cross-format evidence are imported (docs/TPWS-PAYLOAD.md): path cells, placed
/// object records with verified footprints, and fixed items (reported, not footprinted).
/// Money, time, guests, staff, ride state and terrain edits are not imported.
/// </summary>
public sealed class OriginalParkImport
{
	private OriginalParkImport( MapFile map, SavePayloadLayout layout, SaveCellGrid cells, IReadOnlyList<(int X, int Y)> pathCells,
		IReadOnlyList<ImportedParkObject> placedObjects, IReadOnlyList<SaveObject> fixedItems, IReadOnlyList<SaveObject> unresolvedObjects )
	{
		Map = map;
		Layout = layout;
		Cells = cells;
		PathCells = pathCells;
		PlacedObjects = placedObjects;
		FixedItems = fixedItems;
		UnresolvedObjects = unresolvedObjects;
	}

	public MapFile Map { get; }
	public SavePayloadLayout Layout { get; }
	public SaveCellGrid Cells { get; }
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
	/// Imports <paramref name="payload"/> against <paramref name="map"/>. Throws
	/// <see cref="InvalidDataException"/> when the save's cell attributes differ from the map
	/// (wrong level) or a placed object's footprint is outside the grid or not marked occupied.
	/// </summary>
	public static OriginalParkImport Import( ReadOnlyMemory<byte> payload, MapFile map )
	{
		ArgumentNullException.ThrowIfNull( map );
		var layout = SavePayloadLayout.Parse( payload.Span );
		var cells = SaveCellGrid.Parse( payload, layout.PrefixLength, map.CellCountX, map.CellCountY );
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
		return new OriginalParkImport( map, layout, cells, paths.AsReadOnly(), placed.AsReadOnly(), fixedItems.AsReadOnly(), unresolved.AsReadOnly() );
	}
}
