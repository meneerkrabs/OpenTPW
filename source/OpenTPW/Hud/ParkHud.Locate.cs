namespace OpenTPW.Hud;

// [APPROX:UI-091] actual actor locators use host ID cycling and ground camera focus; original locate flow remains unqualified
public sealed partial class ParkHud
{
	private int lastLocatedVisitorId;
	public bool CanLocateVisitor => Camera.Mode is ParkCameraMode && level.OriginalPark != null &&
		ParkVisitorLocator.Next( level.Guests?.Guests ?? Array.Empty<Guest>(), lastLocatedVisitorId ) != null;

	private readonly Dictionary<StaffType, int> lastLocatedStaff = new();
	public bool CanLocateStaff( StaffType type ) => Camera.Mode is ParkCameraMode && StaffLocatorTarget( type ) is { } actor && level.TryGetStaffWorldPosition( actor.Id, out _ );
	private StaffWorldSnapshot? StaffLocatorTarget( StaffType type ) => ParkStaffLocator.Next(
		level.StaffWorld?.Snapshots ?? Array.Empty<StaffWorldSnapshot>(), type,
		showingEmployedStaff ? selectedStaffId : 0, lastLocatedStaff.GetValueOrDefault( type ) );

	public bool LocateStaff( StaffType type )
	{
		if ( Camera.Mode is not ParkCameraMode camera || StaffLocatorTarget( type ) is not { } actor ||
			!level.TryGetStaffWorldPosition( actor.Id, out var position ) ) return false;
		camera.Focus( position );
		lastLocatedStaff[type] = actor.Id;
		// Keep the employee selection when it already identifies this actor; locating another role must not replace it.
		CloseStaffWindow();
		return true;
	}

	/// <summary>Cycles visible visitors, focusing their actual simulated position on the terrain.</summary>
	public bool LocateVisitor()
	{
		if ( Camera.Mode is not ParkCameraMode camera || level.OriginalPark is not { } park )
			return false;
		var guest = ParkVisitorLocator.Next( level.Guests?.Guests ?? Array.Empty<Guest>(), lastLocatedVisitorId );
		if ( guest == null )
			return false;
		var position = GuestRenderer.CellToEngine( park.Heightfield, guest.X, guest.Y );
		camera.Focus( new Vector3( position.X, position.Y, position.Z ) );
		lastLocatedVisitorId = guest.Id;
		CloseStaffWindow();
		return true;
	}
}

internal static class ParkVisitorLocator
{
	public static Guest? Next( IEnumerable<Guest> guests, int previousId )
	{
		var visible = guests.Where( guest => guest.IsVisible && float.IsFinite( guest.X ) && float.IsFinite( guest.Y ) ).OrderBy( guest => guest.Id ).ToArray();
		return visible.FirstOrDefault( guest => guest.Id > previousId ) ?? visible.FirstOrDefault();
	}
}

internal static class ParkStaffLocator
{
	public static StaffWorldSnapshot? Next( IEnumerable<StaffWorldSnapshot> snapshots, StaffType type, int selectedId, int previousId )
	{
		var actors = snapshots.Where( actor => actor.Type == type && float.IsFinite( actor.CellX ) && float.IsFinite( actor.CellY ) ).OrderBy( actor => actor.Id ).ToArray();
		if ( actors.Length == 0 ) return null;
		foreach ( var actor in actors ) if ( actor.Id == selectedId ) return actor;
		foreach ( var actor in actors ) if ( actor.Id > previousId ) return actor;
		return actors[0];
	}
}
