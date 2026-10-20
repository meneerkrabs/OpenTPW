namespace OpenTPW;

public sealed partial class ParkStaff
{
	// [BIN:TPW-EXE:0x00482598 staff locator order] each role has its own world head and member +0x210 next link;
	// constructor helpers 0x519650/0x5197C0/0x519930/0x519AA0/0x519C10 prepend, their removal helpers splice.
	// This owner feeds presentation only. Neither roster iteration nor the global thing chain is reordered.
	private List<int>[]? roleOrder = EmptyRoleOrder();

	private static List<int>[] EmptyRoleOrder() => Enumerable.Range( 0, BalanceSettings.StaffTypeCount ).Select( _ => new List<int>() ).ToArray();

	/// <summary>The next current member of a role, independent of position/carry state; 0 at the tail or for a stale current. The locator supplies wrap.</summary>
	public int NextInRole( StaffType type, int current )
	{
		if ( !Enum.IsDefined( type ) ) return 0;
		var order = roleOrder ?? ProjectLegacyRoleOrder();
		var ids = order[(int)type];
		var index = current == 0 ? -1 : ids.IndexOf( current );
		if ( current != 0 && index < 0 ) return 0;
		return index + 1 < ids.Count ? ids[index + 1] : 0;
	}

	/// <summary>Copies the effective presentation order; reading a legacy owner must not freeze its not-yet-attached pool projection.</summary>
	internal int[][] CaptureRoleOrder() => (roleOrder ?? ProjectLegacyRoleOrder()).Select( ids => ids.ToArray() ).ToArray();

	/// <summary>Restores an exact native order, or leaves an older save unresolved until its first roster mutation.</summary>
	internal void RestoreRoleOrder( int[][]? order )
	{
		ValidateRoleOrder( order, members.Select( member => (member.Id, member.Type) ).ToArray() );
		roleOrder = order?.Select( ids => ids.ToList() ).ToArray();
	}

	internal static void ValidateRoleOrder( int[][]? order, IReadOnlyList<(int Id, StaffType Type)> roster )
	{
		if ( order == null ) return;
		if ( order.Length != BalanceSettings.StaffTypeCount || roster.Count > ThingPool.Capacity )
			throw new InvalidDataException( $"Staff role order must contain five lists and at most {ThingPool.Capacity} members." );
		var byId = new Dictionary<int, StaffType>();
		foreach ( var (id, type) in roster )
			if ( id <= 0 || !Enum.IsDefined( type ) || !byId.TryAdd( id, type ) )
				throw new InvalidDataException( $"Staff role order has an invalid roster member {id}." );
		var seen = new HashSet<int>();
		for ( var role = 0; role < order.Length; role++ )
		{
			var ids = order[role];
			if ( ids == null || ids.Length > roster.Count || ids.Length > ThingPool.Capacity - seen.Count )
				throw new InvalidDataException( $"Staff role order for {(StaffType)role} is null or exceeds the member bound." );
			foreach ( var id in ids )
				if ( !byId.TryGetValue( id, out var type ) || (int)type != role || !seen.Add( id ) )
					throw new InvalidDataException( $"Staff role order for {(StaffType)role} has duplicate, missing or wrong-role member {id}." );
		}
		if ( seen.Count != roster.Count )
			throw new InvalidDataException( $"Staff role order covers {seen.Count} of {roster.Count} members." );
	}

	private void MaterializeRoleOrder() => roleOrder ??= ProjectLegacyRoleOrder();

	private List<int>[] ProjectLegacyRoleOrder()
	{
		var result = EmptyRoleOrder();
		var seen = new HashSet<int>();
		if ( things != null )
		{
			var byThing = new Dictionary<int, StaffMember>();
			foreach ( var member in members )
				if ( member.ThingId > 0 ) byThing.TryAdd( member.ThingId, member );
			foreach ( var slot in things.UsedNewestFirst() )
				if ( !things.IsQueued( slot.Id ) && !things.IsHeld( slot.Id ) && byThing.TryGetValue( slot.Id, out var member )
					&& slot.Model == ThingModelOf( member.Type ) && seen.Add( member.Id ) )
					result[(int)member.Type].Add( member.Id );
		}
		// Legacy compatibility, not recovered original role links: pool-free or unsupported roster claims follow
		// in reverse roster order. Roster membership stays authoritative even when no position/pool entry is available.
		for ( var index = members.Count - 1; index >= 0; index-- )
			if ( seen.Add( members[index].Id ) ) result[(int)members[index].Type].Add( members[index].Id );
		return result;
	}
}
