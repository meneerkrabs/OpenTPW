namespace OpenTPW;

/// <summary>What a row of a ride's upgrade list does (tp.exe <c>0x004AE140</c> fills the list, <c>0x004AE430</c> answers a click).</summary>
public enum RideUpgradeRowKind
{
	/// <summary>Buys an add-on of the ride: the build tool places it (the row's value is the add-on's type id).</summary>
	AddOn,
	/// <summary>Asks for the ride's next upgrade level (the row's value is minus that level).</summary>
	Level,
	/// <summary>Cancels the upgrade the ride waits for (the row's value is −12999).</summary>
	CancelLevel,
}

/// <summary>
/// A row of a ride's upgrade list: an add-on with its price, the next level (0-based <see cref="Level"/>) with its price, or the cancel row with the level the ride waits for.
/// </summary>
public readonly record struct RideUpgradeRow( RideUpgradeRowKind Kind, ObjectCatalogEntry? AddOn, int Level, long Price );

/// <summary>A ride's upgrade list: its rows, or, when it has none, the UITEXT message the list shows line by line instead.</summary>
public sealed record RideUpgradeOffer( IReadOnlyList<RideUpgradeRow> Rows, UIStrings? Message );

/// <summary>
/// The track add-ons (docs/TRACKS.md "Add-ons"): the add-on rows of a ride's upgrade list, the build tool's add-on mode, the circuit walks after an add-on's placement and
/// sale, the add-on sections' links, the add-ons' open ends and the vehicles' surface on them.
/// </summary>
public partial class Level
{
	private OriginalObject? buildAddOnRide;
	/// <summary>The ride the armed build tool places an add-on for (an add-on row of its upgrade list), or null.</summary>
	public OriginalObject? BuildAddOnRide => BuildEntry != null ? buildAddOnRide : null;
	/// <summary>An add-on being removed and the ride it stood on, from <see cref="ParkObjects.ObjectRemoving"/> to <see cref="ParkObjects.ObjectRemoved"/>.</summary>
	private (OriginalObject AddOn, OriginalObject Ride)? addOnRemoval;
	/// <summary>The sampler surface of each catalogue entry's model that a probe has met (<see cref="ObjectSurfaceAt"/>; named for the add-ons it first served).</summary>
	private readonly Dictionary<ObjectCatalogEntry, TrackSurface?> addOnSurfaces = new();

	/// <summary>
	/// The rows of <paramref name="ride"/>'s upgrade list: one per add-on of the ride that the park may buy (its <c>AddOn.UpgradesId</c> names the ride's type and it is
	/// researched), priced at its build cost; then the next level with its cost while the ride waits for no upgrade, below level 2 and researched, or the cancel row while it waits
	/// for one. Instant Action lists nothing and shows UITEXT 27; without rows the list shows UITEXT 28 at level 2 and UITEXT 26 otherwise.
	/// </summary>
	// [BIN:TPW-EXE:0x004AE140 upgrade list] outside game type 2 (Instant Action, DAT_00FB3B7C): every type definition (0x00412E60/E70) whose model loaded (+0x508 = 0), whose +0x284 (AddOn.UpgradesId) is the ride's type and whose object control (0x004D3D10) has the researched byte +0x10 gets a row of its name (0x00412FB0) and the control's price +4, value = the type id; then, when the ride is not in state 2 (0x004E03E0) and its level byte +0x50 is below 2 and level + 1 is at most the control's researched level +0x14, a row of UITEXT 0x1D with level + 2 and the price at type +0x1B8 + 0x40 · (level + 1), value −(level + 1); in state 2 a row of UITEXT 0x1E with level + 2, price 0, value −12999 (0xFFFFCD39); a list without rows shows UITEXT 0x1B in Instant Action, 0x1C at level 2 and 0x1A otherwise, one row of value −7890 per line (0x0067C430 splits the text)
	// [EXT:upgrade-tier-runtime] the level row is listed only for a tier the ride's catalogue defaults and runtime can install (the next tier's record, Level.CanBuyCatalogueUpgrade), an OpenTPW requirement the original has no counterpart of
	public RideUpgradeOffer UpgradeOffer( OriginalObject ride )
	{
		ArgumentNullException.ThrowIfNull( ride );
		var rows = new List<RideUpgradeRow>();
		if ( Park?.Economy is not { } economy || !economy.Features.Upgrades )
			return new RideUpgradeOffer( rows, UIStrings.UpgradesNotAvailable );
		foreach ( var entry in Objects.Catalog?.Entries ?? Array.Empty<ObjectCatalogEntry>() )
		{
			if ( entry.Settings.GetInt( "AddOn.UpgradesId" ) != ride.Entry.InfoId || !economy.Catalog.TryGet( entry.InfoId, out var addOn ) )
				continue;
			if ( !economy.Rules.Research || economy.Research.IsAvailable( entry.InfoId ) )
				rows.Add( new RideUpgradeRow( RideUpgradeRowKind.AddOn, entry, 0, addOn.PurchaseCost ) );
		}
		var level = 0;
		if ( GetEconomyInstance( ride ) is int instance && economy.TryGetObject( instance, out var state ) && state.Kind == ParkObjectKind.Ride && economy.Catalog.TryGet( state.InfoId, out var info ) )
		{
			level = state.Level;
			if ( ParkEconomy.ObjectState( state ) == RideObjectStates.Upgrading )
				rows.Add( new RideUpgradeRow( RideUpgradeRowKind.CancelLevel, null, level + 1, 0 ) );
			// a broken-down ride whose upgrade waits (OpenTPW's state keeps both, the original's one state word either) lists neither row: its level is asked for already
			else if ( state.PendingLevel <= state.Level && level < 2 && level + 1 < info.Upgrades.Count && info.Upgrades[level + 1].Level == level + 1 && economy.Research.IsAvailable( state.InfoId, level + 1 )
				&& !ride.IsDeleted && CanBuyCatalogueUpgrade( ride, state ) )
				rows.Add( new RideUpgradeRow( RideUpgradeRowKind.Level, null, level + 1, info.Upgrades[level + 1].CostOfUpgrade ) );
		}
		return rows.Count > 0 ? new RideUpgradeOffer( rows, null ) : new RideUpgradeOffer( rows, level == 2 ? UIStrings.RideFullyUpgraded : UIStrings.NoUpgrades );
	}

	/// <summary>
	/// Arms the build tool with an add-on of <paramref name="ride"/> (an add-on row of its upgrade list): a track add-on is placed on the ride's track (<see cref="BuildTarget"/>),
	/// any other add-on as an ordinary object.
	/// </summary>
	// [BIN:TPW-EXE:0x004AF480 add-on row] a row with a type id builds the build tool 0x0046C5A0(4, type) when the object control's price +4 is at most the bank balance, holds the ride (0x0048C940) and takes its cell (0x005276D0); a dearer one plays the error cue 0x1D (0x00486AE0) and builds nothing
	public bool BeginAddOn( ObjectCatalogEntry entry, OriginalObject ride )
	{
		ArgumentNullException.ThrowIfNull( entry );
		ArgumentNullException.ThrowIfNull( ride );
		if ( IsReadOnlyVisit || ride.IsDeleted || !TrackAddOns.IsAddOn( entry ) || entry.Settings.GetInt( "AddOn.UpgradesId" ) != ride.Entry.InfoId )
			return false;
		BuildEntry = entry;
		buildAddOnRide = ride;
		return true;
	}

	/// <summary>
	/// The build click and angle for <paramref name="entry"/> with the pointer on cell (<paramref name="x"/>, <paramref name="y"/>): the track add-on of <see cref="BuildAddOnRide"/>
	/// moves to its hover position on the ride's track (<see cref="TrackAddOns.HoverTarget"/>), any other entry keeps the cell and the build angle.
	/// </summary>
	public (int X, int Y, int Rotation) BuildTarget( ObjectCatalogEntry entry, int x, int y )
	{
		ArgumentNullException.ThrowIfNull( entry );
		if ( BuildAddOnRide is { } ride && TrackAddOns.IsTrackAddOn( entry ) && BuilderOf( ride ) is { } builder )
			return TrackAddOns.HoverTarget( entry.Settings, builder.Tracks, ride.Placement.X, ride.Placement.Y, ride.Placement.Rotation, x, y, BuildRotation );
		return (x, y, BuildRotation);
	}

	/// <summary>The build rule at a build target (<see cref="BuildTarget"/>): a track add-on's for <see cref="BuildAddOnRide"/> (<see cref="CheckAddOn"/>), else the object's.</summary>
	public OriginalPlacementResult CheckBuildTarget( ObjectCatalogEntry entry, int x, int y, int rotation ) =>
		BuildAddOnRide is { } ride && TrackAddOns.IsTrackAddOn( entry ) ? CheckAddOn( entry, x, y, rotation, ride ) : CheckObject( entry, x, y, rotation );

	/// <summary>
	/// Whether the build ghost at a build target is accepted: the build rule allowed it (<paramref name="placement"/>, <see cref="CheckBuildTarget"/>) and the park can buy the object.
	/// The HUD draws a refused ghost red, and the build click on it does not drop a pending queue start (<see cref="ClickBuildTool"/>).
	/// </summary>
	internal bool BuildGhostAllowed( ObjectCatalogEntry entry, OriginalPlacementResult placement ) =>
		placement == OriginalPlacementResult.Allowed && (Park == null || Park.Economy.CheckBuild( entry.InfoId, IsMoving ) == ParkEconomy.PurchaseResult.Ok);

	/// <summary>Whether the armed entry's ghost is refused where a build click at (<paramref name="x"/>, <paramref name="y"/>) would put it (<see cref="BuildGhostAllowed"/>): the refused-ghost latch a release reads.</summary>
	// [BIN:TPW-EXE:0x533933 build latch] the placement preview 0x528A70 runs the footprint through 0x532FC0, whose validator refusal sets the latch 0x816D48; the release handler tests it before its mode dispatch (0x524A63) for the build modes 4 and 0x3B too
	internal bool BuildClickRefused( int x, int y )
	{
		if ( BuildEntry is not { } entry )
			return false;
		var (targetX, targetY, rotation) = BuildTarget( entry, x, y );
		return !BuildGhostAllowed( entry, CheckBuildTarget( entry, targetX, targetY, rotation ) );
	}

	/// <summary>Builds the armed entry at a build target: the track add-on of <see cref="BuildAddOnRide"/> through <see cref="PlaceAddOn"/>, anything else through <see cref="PlaceObject"/>.</summary>
	private OriginalObject? PlaceBuildTarget( ObjectCatalogEntry entry, int x, int y, int rotation ) =>
		BuildAddOnRide is { } ride && TrackAddOns.IsTrackAddOn( entry ) ? PlaceAddOn( entry, x, y, rotation, ride ) : PlaceObject( entry, x, y, rotation );

	/// <summary>
	/// After a placement of a track add-on for <paramref name="ride"/>: the circuit is walked again (the boats or karts leave, the sections take the add-on), the pieces' clips start
	/// on a valid circuit and stop on an invalid one, and a valid ride opens when its state allows.
	/// </summary>
	// [BIN:TPW-EXE:0x00524B45 add-on click] with the upgrade selection set the build click finds the clicked tile's ride (its head's owner word, the thing table 0x7CFB90), ends its controller's vehicles (0x00545450), places the add-on (0x00528A70 with the type 4), walks the circuit (0x0052A970 at 0x00524D0A, the verdict to the ride's +0x2C), starts the pieces forward on a valid one (0x0052A490(ride, 1)) or stops them from both sides (0x0052A490 and 0x0052A700 with 0), and opens a valid ride whose guard allows it (0x004DF290, 0x004DF390); then a click without exactly Ctrl returns the tool to mode 0 and clears the selection (0x00524D6D..0x00524D81)
	// [EXT:online-coop-commands] the walk runs inside the command that places or removes the add-on, on every peer: it moves no track tool (WalkTrack, not RewalkTrack's tool step)
	private void WalkAfterAddOn( OriginalObject ride )
	{
		if ( WalkTrack( ride, TrackWalkCause.AddOn ) is not { IsValid: true } )
			return;
		if ( Gate?.MayOpen( ride.Runtime ) == true )
		{
			ride.Runtime.Open();
			SyncOpenState( ride.Runtime );
		}
	}

	/// <summary>The ride whose track holds the cell (x, y): the station whose owner id the tile's head carries (<see cref="TrackBuilder.OwnerOf"/>), or null.</summary>
	private OriginalObject? TrackRideAt( int x, int y )
	{
		foreach ( var (ride, station) in trackStations )
		{
			if ( BuilderOf( ride ) is not { } builder || builder.OriginOf( x, y ) is not { } origin )
				continue;
			if ( builder.Tracks.Get( origin.X, origin.Y ).Owner == builder.OwnerOf( station ) )
				return ride;
		}
		return null;
	}

	/// <summary>The ride a track add-on stands on: the ride of the track under its first '+' or '#' cell, or null.</summary>
	private OriginalObject? RideUnder( OriginalObject addOn )
	{
		foreach ( var (x, y, cell) in addOn.Cells )
			if ( cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash && TrackRideAt( x, y ) is { } ride )
				return ride;
		return null;
	}

	/// <summary>
	/// The object whose window a click on <paramref name="item"/> opens: a track add-on opens its ride's window (the ride under it, <see cref="RideUnder"/>) and none when it stands
	/// on no ride's track; any other object opens its own.
	/// </summary>
	// [BIN:TPW-EXE:0x00486920 object window] a ride-type object (+0x4AC 0) that is not an add-on of AddOn.UpgradeType 2: an object whose word +0x30 names a thing (0x004E0DF0) opens that thing's ride window (0x004AF980); the placement sets +0x30 to the clicked tile's ride for an add-on placed with the upgrade selection (0x0052A023 → 0x004E0DE0) and to 0 for anything else; without it an add-on (AddOn.UpgradesId, 0x004E2710) opens nothing and any other ride its own window
	public OriginalObject? WindowTarget( OriginalObject? item ) => item is not { IsDeleted: false } ? null : TrackAddOns.IsTrackAddOn( item.Entry ) ? RideUnder( item ) : item;

	/// <summary>The track add-on standing on cell (x, y), or null.</summary>
	private OriginalObject? AddOnAt( int x, int y ) => Objects.FindAt( x, y ) is { IsDeleted: false } item && TrackAddOns.IsTrackAddOn( item.Entry ) ? item : null;

	private void OnAddOnRemoving( OriginalObject item ) =>
		addOnRemoval = TrackAddOns.IsTrackAddOn( item.Entry ) && resumeState == null && RideUnder( item ) is { } ride ? (item, ride) : null;

	/// <summary>
	/// A removed track add-on: its tiles lose the add-on flag and its ride's circuit is walked again as after a placement (<see cref="WalkAfterAddOn"/>), so the tiles take their
	/// pieces and plain sections back; the walk ends the vehicles' runs, as every walk does, and opens a valid ride.
	/// </summary>
	// [EXT:addon-sale-walk] the original's demolition 0x00527EE0 clears the add-on flag of the track cells under a sold add-on (0x0052851E) but neither makes their pieces (0x0053AFA0) nor walks the circuit: the pieces come back with the circuit's next walk (0x0052B18E makes the piece of a plain tile that has none) and until then the sections keep the add-on's kind, which a save of OpenTPW's could not restore (the restore walks the cells); OpenTPW walks at once
	private void OnAddOnRemoved( OriginalObject item )
	{
		if ( addOnRemoval is not { } removal || !ReferenceEquals( removal.AddOn, item ) )
			return;
		addOnRemoval = null;
		if ( removal.Ride.IsDeleted || Guests?.Grid.Cells is not { } cells )
			return;
		// the flags come off before the walk reads them, whichever removal handler runs first (the footprint's own handler takes them off again)
		cells.ClearFootprint( item.Cells.Where( cell => cell.Cell.CellType is ObjectShapeSymbols.TypeUpgrade or ObjectShapeSymbols.TypeHash ) );
		WalkAfterAddOn( removal.Ride );
	}

	/// <summary>
	/// After a walk of <paramref name="ride"/>'s circuit: every add-on section is linked to the add-on standing on its tile (section +0xC and +0x10), and the add-ons' open ends
	/// are shown or hidden (<see cref="TrackAddOns.OpenEndsOf"/>).
	/// </summary>
	private void LinkAddOns( OriginalObject ride, TrackCircuit circuit )
	{
		if ( BuilderOf( ride ) is not { } builder || ride.Runtime.Bump is not { } bump )
			return;
		foreach ( var section in bump.Sections )
		{
			if ( section.Kind is 12 or 13 && AddOnAt( section.X / TrackSections.Cell, section.Y / TrackSections.Cell ) is { } addOn )
				section.AddOn = new AddOnLink( addOn.Runtime );
		}
		foreach ( var ends in TrackAddOns.OpenEndsOf( builder.Tracks, circuit.Steps, ( x, y ) => AddOnAt( x, y ) != null ) )
		{
			if ( AddOnAt( ends.X, ends.Y ) is not { } addOn )
				continue;
			var model = addOn.Runtime.Model;
			if ( TrackAddOns.EndNode( model, TrackAddOns.EndBehind ) is var behind and >= 0 )
				addOn.Runtime.Animator.SetNodeHidden( behind, ends.HideBehind );
			if ( TrackAddOns.EndNode( model, TrackAddOns.EndAhead ) is var ahead and >= 0 )
				addOn.Runtime.Animator.SetNodeHidden( ahead, ends.HideAhead );
		}
	}

	/// <summary>
	/// The object standing on a cell as the vehicles' height probes sample it (<see cref="TrackRenderer.ObjectSurfaceAt"/>): the cell's owner object in its own sampler mode, as the original's
	/// <c>0x452A60</c> hands <c>0x450EA0</c> the owner thing's instance for a cell whose track tile has no piece instance (an add-on's tile, a station tile) or that holds no track; null when no
	/// object stands there.
	/// </summary>
	// [BIN:TPW-EXE:0x00452A60 cell object] the cell's own instance (0x53BF30: +4 of the cell, its origin's for an extension cell), else the owner thing of the cell (0x527E80) when it is a thing of type 3, its instance +0x20; no thing, no object (0x450AC0 answers 0 and the probe keeps its height)
	private (TrackSurface Surface, System.Numerics.Matrix4x4 GridToModel, float BaseHeight)? ObjectSurfaceAt( int x, int y )
	{
		if ( Objects.FindAt( x, y ) is not { IsDeleted: false } addOn )
			return null;
		if ( !addOnSurfaces.TryGetValue( addOn.Entry, out var surface ) )
		{
			try
			{
				// [BIN:TPW-EXE:0x00413C10 object loader] a type that is no fixed item (Info.DontApplyOffset 0) is loaded with the ground node search (0x4629D0 flag 1) over the level's terrain grid; the search's node is the instance's +0x2C, which the mode-4 sampler reads
				var groundNode = -1;
				if ( !addOn.Entry.IsFixedItem && OriginalPark?.Heightfield is { } field )
					groundNode = ObjectHoardingGeometry.SelectGroundNode( addOn.Runtime.Model, ObjectMapGrid.ShapeOf( addOn.Entry.Settings ), HoardingTerrain.Of( field ) ).Node;
				surface = TrackSurface.FromObject( addOn.Runtime.Model, groundNode: groundNode );
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
			{
				Log.Warning( $"The surface of {addOn.Entry.DisplayName} could not be read: {exception.Message}" );
				surface = null;
			}
			addOnSurfaces[addOn.Entry] = surface;
		}
		if ( surface == null )
			return null;
		var placement = addOn.Placement;
		var modelToGrid = ObjectPlacement.CellRotation( placement.Rotation ) * System.Numerics.Matrix4x4.CreateTranslation( placement.X * 10, placement.Y * 10, 0 );
		return System.Numerics.Matrix4x4.Invert( modelToGrid, out var gridToModel )
			? (surface, gridToModel, placement.BaseHeight / ObjectPlacement.ModelScale)
			: null;
	}

	/// <summary>A placed track add-on as its sections see it (section +0xC, +0x10): its type and its animation channel 0.</summary>
	internal sealed class AddOnLink( OriginalObjectRuntime runtime ) : ITrackAddOn
	{
		public int TypeId => runtime.Entry.InfoId;
		public int Animation => runtime.Animator.GetAnimationId( OriginalObjectEffects.MainChannel );
		public int DeferredAnimation => runtime.Animator.GetDeferredAnimationId( OriginalObjectEffects.MainChannel );
		public void FlushDeferred() => runtime.Animator.Flush( OriginalObjectEffects.MainChannel );
		public void Queue( int animation, bool loop ) => runtime.TriggerAnimation( animation, 0, OriginalObjectEffects.MainChannel, loop, 1f, defer: true );
	}
}
