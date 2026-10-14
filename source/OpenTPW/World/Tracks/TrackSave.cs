namespace OpenTPW;

/// <summary>One saved track cell: the map position and the layer record's fields (the cell index and the mesh instance are not saved).</summary>
internal sealed record TrackCellSaveData( int X, int Y, byte Type, byte Mask, byte Link, byte Flags, int Parent, int Family, int Piece, int Rotation, short Overlap, short AltMask, int Segment );

/// <summary>One saved section (the circuit walk rebuilds the sections, so they are verified against the walk on load, never trusted).</summary>
internal sealed record TrackSectionSaveData( int Kind, bool NoBuoy, int X, int Y, int Segment );

/// <summary>One saved boat: every integer field of the vehicle record and the guests aboard.</summary>
internal sealed record BumpVehicleSaveData( int Slot, int Flags, int Variant, int X, int Y, int VX, int VY, int VX0, int VY0, int Speed, int Heading, int Spin, int Roll,
	int Radius, int AccelBonus, int TargetX, int TargetY, int JitterX, int JitterY, int TargetBuoy, int Laps, int BuoyCountdown, int Age, int ObjectIndex,
	int WallHits, int WallWindow, int Sprays, List<int> Riders );

/// <summary>One saved controller joined to its station by attraction id: the scalars the script set, the waiting and leaving guests and the boats.</summary>
internal sealed record BumpControllerSaveData( int AttractionId, int Type, int Laps, int Performance, int Phase, int State, long TickCount, double TickRemainder, bool WaterRunning,
	bool IsTrackValid, List<int> Pending, List<int> Leaving, List<TrackSectionSaveData> Sections, List<BumpVehicleSaveData> Vehicles );

/// <summary>The kart and water tracks of one saved park (the park-resume envelope's optional track part): the cell layer, the controllers and the cosmetic generator.</summary>
internal sealed record TrackWorldSaveData
{
	public required List<TrackCellSaveData> Cells { get; init; }
	public required List<BumpControllerSaveData> Controllers { get; init; }
	public required uint CosmeticState { get; init; }
}

/// <summary>
/// Capture and restore of the track part of a park-resume envelope (docs/PARK-SAVES.md, docs/TRACKS.md): the cells of the track layer, every live
/// controller joined to its attraction id with its boats, and the module's cosmetic generator. A park without a track and without a controller
/// writes no track part at all, so every earlier save stays what it was.
/// </summary>
internal static class TrackSave
{
	public const int MaximumCells = 1024 * 1024;
	public const int MaximumControllers = 256;
	public const int MaximumSections = TrackSections.MaximumSections;
	public const int MaximumGuests = 4096;

	public static TrackWorldSaveData? Capture( ParkCellMap cells, BumpWorld world, IEnumerable<(int AttractionId, BumpController Controller)> stations )
	{
		ArgumentNullException.ThrowIfNull( cells );
		ArgumentNullException.ThrowIfNull( world );
		var live = world.Controllers.ToHashSet();
		var controllers = stations.OrderBy( station => station.AttractionId ).Where( station => live.Contains( station.Controller ) )
			.Select( station => CaptureController( station.Controller, station.AttractionId ) ).ToList();
		if ( controllers.Count != live.Count )
			throw new InvalidOperationException( $"The park holds {live.Count} bump controllers but {controllers.Count} placed kart or water stations; a controller lost its station." );
		var tiles = cells.Tracks.Cells().Select( item => new TrackCellSaveData( item.X, item.Y, item.Cell.Type, item.Cell.Mask, item.Cell.Link, item.Cell.Flags, item.Cell.Parent,
			item.Cell.Family, item.Cell.Piece, item.Cell.Rotation, item.Cell.Overlap, item.Cell.AltMask, item.Cell.Segment ) ).ToList();
		if ( tiles.Count == 0 && controllers.Count == 0 )
			return null;
		return new TrackWorldSaveData { Cells = tiles, Controllers = controllers, CosmeticState = world.CosmeticState };
	}

	internal static BumpControllerSaveData CaptureController( BumpController controller, int attractionId ) => new(
		attractionId, controller.Type, controller.Laps, controller.Performance, controller.Phase, controller.State, controller.TickCount, controller.TickRemainder,
		controller.WaterRunning, controller.IsTrackValid, controller.Pending.ToList(), controller.Leaving.ToList(),
		controller.Sections.Select( section => new TrackSectionSaveData( section.Kind, section.NoBuoy, section.X, section.Y, section.Segment ) ).ToList(),
		controller.Vehicles.Select( vehicle => new BumpVehicleSaveData( vehicle.Slot, vehicle.Flags, vehicle.Variant, vehicle.X, vehicle.Y, vehicle.VX, vehicle.VY, vehicle.VX0, vehicle.VY0,
			vehicle.Speed, vehicle.Heading, vehicle.Spin, vehicle.Roll, vehicle.Radius, vehicle.AccelBonus, vehicle.TargetX, vehicle.TargetY, vehicle.JitterX, vehicle.JitterY,
			vehicle.TargetBuoy, vehicle.Laps, vehicle.BuoyCountdown, vehicle.Age, vehicle.ObjectIndex, vehicle.WallHits, vehicle.WallWindow, vehicle.Sprays, vehicle.Riders.ToList() ) ).ToList() );

	/// <summary>Writes the saved cells into the (freshly restored) track layer; the map's type bytes were restored with the layout.</summary>
	public static void RestoreCells( ParkCellMap cells, TrackWorldSaveData saved )
	{
		foreach ( var item in saved.Cells )
		{
			cells.Tracks.Set( item.X, item.Y, new TrackCell
			{
				Type = item.Type, Mask = item.Mask, Link = item.Link, Flags = item.Flags, Parent = item.Parent, Family = item.Family, Piece = item.Piece,
				Rotation = item.Rotation, Overlap = item.Overlap, AltMask = item.AltMask, Segment = item.Segment
			} );
		}
	}
}
