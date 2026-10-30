using System.Text.Json.Serialization;

namespace OpenTPW;

/// <summary>One saved track cell: the map position and the layer record's fields (the cell index and the mesh instance are not saved).</summary>
internal sealed record TrackCellSaveData( int X, int Y, byte Type, byte Mask, byte Link, byte Flags, int Parent, int Family, int Piece, int Rotation, short Overlap, short AltMask, int Segment, int Owner = 0,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )] byte BaseType = 0 );

/// <summary>
/// One saved section (the circuit walk rebuilds the sections, so they are verified against the walk on load, never trusted), with the one runtime flag a section carries: a
/// Space Racers Teleport waiting to go back to its idle clip (<see cref="TrackSection.AddOnPassing"/>, written only when set).
/// </summary>
internal sealed record TrackSectionSaveData( int Kind, bool NoBuoy, int X, int Y, int Segment,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )] bool AddOnPassing = false );

/// <summary>One saved boat or kart: every integer field of the vehicle record and the guests aboard; the trailing fields are the kart's drawn heading, yaw, animation, effect latch and the vehicle's last drawn height. The height is presentation state written by
/// TrackRenderer for every family (zero in a headless run and in saves from before the kart rides) and not hashed: a rendered and a headless run of the same simulation
/// save the same park except for it, and it only seeds the next probe that finds no surface and the effects' height.</summary>
internal sealed record BumpVehicleSaveData( int Slot, int Flags, int Variant, int X, int Y, int VX, int VY, int VX0, int VY0, int Speed, int Heading, int Spin, int Roll,
	int Radius, int AccelBonus, int TargetX, int TargetY, int JitterX, int JitterY, int TargetBuoy, int Laps, int BuoyCountdown, int Age, int ObjectIndex,
	int WallHits, int WallWindow, int Sprays, List<int> Riders, int VisualHeading = 0, int YawRate = 0, int YawDelta = 0, int Animation = 0, int EffectRequest = 0, int EffectLatch = 0, float Height = 0, int ChaseTarget = -1, bool Engine = false, bool Smoke = false, int ObjectAttraction = -1,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] List<int>? SeatNumbers = null );

/// <summary>
/// One saved controller joined to its station by attraction id: the scalars the script set, the waiting and leaving guests and the boats; a save of the shared module adds
/// the pool slots of its collision objects and its live buoy count, so a restore can tell a build the shared pools refused (no objects and no buoys) from one that fitted.
/// </summary>
internal sealed record BumpControllerSaveData( int AttractionId, int Type, int Laps, int Performance, int Phase, int State, long TickCount, double TickRemainder, bool WaterRunning,
	bool IsTrackValid, List<int> Pending, List<int> Leaving, List<TrackSectionSaveData> Sections, List<BumpVehicleSaveData> Vehicles, List<int>? ObjectSlots = null, int? BuoyCount = null, int StationLayout = 0,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] int? VehicleLimit = null,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] bool? TrackWordWritten = null,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] int? ModelBase = null,
	[property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull )] int? ModelCount = null );

/// <summary>The kart and water tracks of one saved park (the park-resume envelope's optional track part): the cell layer, the controllers and the cosmetic generator.</summary>
internal sealed record TrackWorldSaveData
{
	public required List<TrackCellSaveData> Cells { get; init; }
	public required List<BumpControllerSaveData> Controllers { get; init; }
	public required uint CosmeticState { get; init; }
	/// <summary>The module frame counter and clock (absent in saves from before the shared module: the first controller's are taken).</summary>
	public long? FrameCount { get; init; }
	public double? TickRemainder { get; init; }
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
			item.Cell.Family, item.Cell.Piece, item.Cell.Rotation, item.Cell.Overlap, item.Cell.AltMask, item.Cell.Segment, item.Cell.Owner, item.Cell.BaseType ) ).ToList();
		// the module's clock and generator outlive its last ride, so a park that once had one keeps them in its save
		if ( tiles.Count == 0 && controllers.Count == 0 && world.FrameCount == 0 && world.TickRemainder == 0 && world.CosmeticState == 0 )
			return null;
		return new TrackWorldSaveData { Cells = tiles, Controllers = controllers, CosmeticState = world.CosmeticState, FrameCount = world.FrameCount, TickRemainder = world.TickRemainder };
	}

	internal static BumpControllerSaveData CaptureController( BumpController controller, int attractionId ) => new(
		attractionId, controller.Type, controller.Laps, controller.Performance, controller.Phase, controller.State, controller.TickCount, controller.TickRemainder,
		controller.WaterRunning, controller.IsTrackValid, controller.Pending.ToList(), controller.Leaving.ToList(),
		controller.Sections.Select( section => new TrackSectionSaveData( section.Kind, section.NoBuoy, section.X, section.Y, section.Segment, section.AddOnPassing ) ).ToList(),
		controller.Vehicles.Select( vehicle => new BumpVehicleSaveData( vehicle.Slot, vehicle.Flags, vehicle.Variant, vehicle.X, vehicle.Y, vehicle.VX, vehicle.VY, vehicle.VX0, vehicle.VY0,
			vehicle.Speed, vehicle.Heading, vehicle.Spin, vehicle.Roll, vehicle.Radius, vehicle.AccelBonus, vehicle.TargetX, vehicle.TargetY, vehicle.JitterX, vehicle.JitterY,
			vehicle.TargetBuoy, vehicle.Laps, vehicle.BuoyCountdown, vehicle.Age, vehicle.ObjectIndex, vehicle.WallHits, vehicle.WallWindow, vehicle.Sprays, vehicle.Riders.ToList(), vehicle.VisualHeading, vehicle.YawRate, vehicle.YawDelta, vehicle.Animation, vehicle.EffectRequest, vehicle.EffectLatch, vehicle.Height, vehicle.ChaseTarget, vehicle.Engine, vehicle.Smoke, vehicle.ObjectOwner?.AttractionId ?? -1,
			vehicle.SeatNumbers is { } seats && seats.Length == vehicle.Riders.Count ? seats.ToList() : null ) ).ToList(),
		controller.Objects.Select( item => item.ModuleSlot ).ToList(), controller.Buoys.Count, controller.LegacyStationLayout ? 0 : 1,
		// [EXT:cheats-ride-vehicles] written only for a raised limit, so every other save keeps its bytes
		controller.VehicleLimit != controller.Template.MaxVehicles ? controller.VehicleLimit : null,
		// written only for a ride no track edit has walked, so every other save keeps its bytes
		controller.TrackWordWritten ? null : false,
		// written only for a ride whose model words are not its template's (an original save's ride chunk gives them), so every other save keeps its bytes
		controller.HasTemplateModelWords ? null : controller.ModelBase, controller.HasTemplateModelWords ? null : controller.ModelCount );

	/// <summary>The module part of a park-resume restore, once every controller is back: the cosmetic generator, the module clock and the objects' saved pool slots.</summary>
	public static void RestoreModule( BumpWorld module, TrackWorldSaveData saved, IReadOnlyList<(BumpController Controller, IReadOnlyList<int>? Slots)> controllers )
	{
		ArgumentNullException.ThrowIfNull( module );
		ArgumentNullException.ThrowIfNull( saved );
		module.CosmeticState = saved.CosmeticState;
		module.FinishRestore( saved.FrameCount, saved.TickRemainder, controllers );
	}

	/// <summary>
	/// Writes the saved cells into the (freshly restored) track layer and runs the bridge pass over them (<see cref="TrackBuilder.RefreshAll"/>), so a
	/// save from before the bridges over terrain water were dropped (TRACK-010) loads with plain pieces there; the map's type bytes were restored with the layout.
	/// </summary>
	public static void RestoreCells( ParkCellMap cells, TrackWorldSaveData saved )
	{
		foreach ( var item in saved.Cells )
		{
			cells.Tracks.Set( item.X, item.Y, new TrackCell
			{
				Type = item.Type, Mask = item.Mask, Link = item.Link, Flags = item.Flags, Parent = item.Parent, Family = item.Family, Piece = item.Piece,
				Rotation = item.Rotation, Overlap = item.Overlap, AltMask = item.AltMask, Segment = item.Segment, Owner = item.Owner, BaseType = item.BaseType
			} );
		}
		TrackBuilder.RefreshAll( cells.Tracks, cells );
	}
}
