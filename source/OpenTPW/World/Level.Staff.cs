namespace OpenTPW;

public partial class Level
{
	public StaffWorldSimulation? StaffWorld { get; private set; }
	/// <summary>The hire and pick-up tools of the staff (docs/STAFF-PLACEMENT.md); null without a park economy.</summary>
	public StaffPlacement? StaffPlacement { get; private set; }
	/// <summary>Draws the placed staff and the tool's dangling sprite.</summary>
	public StaffRenderer? StaffRenderer { get; private set; }

	private void SetupStaff()
	{
		if ( Park == null || Guests == null ) return;
		var lane = Guests.Settings.ArrivalLaneA ?? Guests.Settings.ArrivalLaneB;
		StaffWorld = new StaffWorldSimulation( Guests.Grid, () => Park.Economy.Staff.Members, StaffJobAccess,
			lane is { Length: > 0 } ? lane[^1] : null );
		StaffWorld.Toilets = ToiletSites;
		// the guards' pranksters and the entertainers' audience; a prank reaches the staff HQ's guard choice at once (StaffWorldSimulation.ReportPrank)
		StaffWorld.Guests = Guests;
		// the rest areas, the gates a striker walks out through, and the staff room's count of resting staff
		StaffWorld.RestAreas = RestSites;
		StaffWorld.GatesOpen = () => Gate?.GateStatusIsOpen == true;
		StaffWorld.RestingStaff = SetRestingStaff;
		Guests.IsEscorted = StaffWorld.IsEscorting;
		Guests.Pranked += ( guest, _ ) =>
		{
			if ( Park != null )
				StaffWorld?.ReportPrank( Park.Economy, guest.Id );
		};
		AttachStaffWorld();
		StaffPlacement = new StaffPlacement( () => Park?.Economy, () => Guests?.Grid.Cells, IsPathBlocked );
		if ( OriginalPark != null )
		{
			StaffSpriteSet sprites;
			using ( LoadProfile.Measure( "staff-sprites" ) )
				sprites = StaffSpriteSet.Load( LevelName );
			StaffRenderer = new StaffRenderer( () => Park.Economy.Staff.Members, OriginalPark.Heightfield, sprites, StaffPlacement );
		}
	}

	/// <summary>One park tool at a time: ends the path, queue, track, coaster, placing and remove tools before a staff tool starts.</summary>
	public void EndBuildTools()
	{
		CellTool.Cancel();
		ExitCoasterTool();
		BuildEntry = null;
		IsPlacing = false;
		IsRemovingObjects = false;
	}

	/// <summary>The economy's staff worlds; a park load replaces the economy, so it runs again then.</summary>
	private void AttachStaffWorld()
	{
		if ( Park == null || StaffWorld == null )
			return;
		Park.Economy.MechanicJobWorld = StaffWorld;
		Park.Economy.StaffWorld = StaffWorld;
	}

	/// <summary>The staff's rest areas: objects with <c>UsageInfo.ChillsYouOut</c> and an economy instance, their cell and the path cell in front of them.</summary>
	// [DATA:<object>.sam:UsageInfo.ChillsYouOut] type record +0xFC, the object flag +0x32 bit 1 the rest finder 0x00506910 tests (the Staff Room of each theme)
	private IReadOnlyList<RestSite> RestSites()
	{
		var sites = new List<RestSite>();
		if ( Guests == null )
			return sites;
		foreach ( var item in Objects.Objects )
		{
			if ( item.IsDeleted || !item.Entry.Settings.GetBool( "UsageInfo.ChillsYouOut" ) || !economyInstances.TryGetValue( item, out var instance ) )
				continue;
			if ( AccessCell( item, Guests.Grid ) is { } access )
				sites.Add( new RestSite( instance, (item.Placement.X, item.Placement.Y), access ) );
		}
		return sites;
	}

	/// <summary>The staff room's <c>VAR_STAFFIN</c> (its script's only variable, index 0 in every theme): the members resting in it.</summary>
	// [BIN:TPW-EXE:0x00505FE0 rest area count] the arrival adds 1 to the rest area's script variable 0 (0x0055A0B0), the rest's end (0x00506D10) takes 1 away
	private void SetRestingStaff( IReadOnlyDictionary<int, int> counts )
	{
		foreach ( var (item, id) in economyInstances )
			if ( !item.IsDeleted && item.Entry.Settings.GetBool( "UsageInfo.ChillsYouOut" ) )
				item.Runtime.SetVariable( "VAR_STAFFIN", counts.GetValueOrDefault( id ) );
	}

	/// <summary>The toilets for the handymen: relief objects with an economy instance, their cell and the path cell in front of them.</summary>
	private IReadOnlyList<ToiletSite> ToiletSites()
	{
		var sites = new List<ToiletSite>();
		foreach ( var (item, instance) in economyInstances )
		{
			if ( item.IsDeleted || !item.Runtime.Visitors.ProvidesRelief || !item.Runtime.Visitors.HasCells )
				continue;
			var access = item.Runtime.Visitors.JoinCell ?? item.Runtime.Visitors.EntranceCell;
			// Approximation (ECON-022, registered at StaffWorldSimulation.UpdateStaff): a toilet's access cell (thing +0x36) is its queue's join cell or the path cell in front of its entrance, as for the mechanics' arrival
			if ( Guests!.Grid.IsWalkable( access.X, access.Y ) )
				sites.Add( new ToiletSite( instance, (item.Placement.X, item.Placement.Y), access ) );
		}
		return sites.OrderBy( site => site.Id ).ToList();
	}

	private (int X, int Y)? StaffJobAccess( int instance )
	{
		var item = economyInstances.FirstOrDefault( entry => entry.Value == instance ).Key;
		var bridge = item?.Visitors ?? (prototypeInstance == instance ? PlacedRide?.Visitors : null);
		if ( bridge == null || !bridge.HasCells ) return null;
		var cell = bridge.JoinCell ?? bridge.EntranceCell;
		return Guests!.Grid.IsWalkable( cell.X, cell.Y ) ? cell : null;
	}

	/// <summary>Actual staff actor position; false when there is no actor or no terrain.</summary>
	public bool TryGetStaffWorldPosition( int id, out Vector3 position )
	{
		position = default;
		StaffWorld?.Synchronize();
		if ( StaffWorld == null || OriginalPark == null ) return false;
		foreach ( var actor in StaffWorld.Snapshots )
		{
			if ( actor.Id != id ) continue;
			var field = OriginalPark.Heightfield;
			var x = (int)MathF.Floor( actor.CellX );
			var y = (int)MathF.Floor( actor.CellY );
			if ( x < 0 || y < 0 || x >= field.CellCountX || y >= field.CellCountZ ) return false;
			var center = OriginalParkPlacement.GetCellCenter( field, x, y );
			position = new Vector3( center.X + (actor.CellX - x - 0.5f) * field.CellSizeX * OriginalParkPlacement.ModelScale,
				center.Y + (actor.CellY - y - 0.5f) * field.CellSizeZ * OriginalParkPlacement.ModelScale, center.Z );
			return true;
		}
		return false;
	}
}
