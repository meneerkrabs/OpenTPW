namespace OpenTPW;

public sealed partial class OriginalEconomyImport
{
	// [BIN:TPW-EXE:0x005179C0 saved staff lists] world heads survive loading independently of the global thing chain;
	// the staff serializers restore +0x210 without invoking the construction-time prepend helpers.
	private static void ImportStaffRoleOrder( ParkStaff staff, OriginalParkImport save, IReadOnlyDictionary<int, StaffMember> membersByThing, List<string> evidence )
	{
		var variables = save.World.Variables;
		var heads = new[] { variables.FirstHandyman, variables.FirstMechanic, variables.FirstEntertainer, variables.FirstGuard, variables.FirstResearcher };
		var savedById = save.Staff.ToDictionary( member => member.Id );
		var models = save.World.Things.ToDictionary( thing => thing.Id, thing => thing.Model );
		var order = new int[BalanceSettings.StaffTypeCount][];
		for ( var role = 0; role < order.Length; role++ )
		{
			var type = (StaffType)role;
			var expectedModel = ParkStaff.ThingModelOf( type );
			// Save.Staff is already in global used-chain order. Never install a prefix of a malformed role chain.
			var fallback = save.Staff.Where( member => member.Model == expectedModel ).Select( member => membersByThing[member.Id].Id ).ToArray();
			var walked = new List<int>();
			var seen = new HashSet<int>();
			string? failure = fallback.Length > ThingPool.Capacity ? $"{fallback.Length} {type} members exceed the role bound {ThingPool.Capacity}" : null;
			var id = (int)heads[role];
			while ( failure == null && id != 0 )
			{
				if ( id < 1 || id > ThingPool.Capacity )
					failure = $"thing {id} is outside 1..{ThingPool.Capacity}";
				else if ( !seen.Add( id ) )
					failure = $"cycle at thing {id}";
				else if ( walked.Count >= save.Staff.Count || walked.Count >= ThingPool.Capacity )
					failure = $"chain exceeds its imported staff count or role bound {ThingPool.Capacity}";
				else if ( models.TryGetValue( id, out var model ) && model != expectedModel )
					failure = $"thing {id} is a {model}, expected {expectedModel}";
				else if ( !savedById.TryGetValue( id, out var saved ) || !membersByThing.TryGetValue( id, out var member ) )
					failure = $"missing staff thing {id}";
				else if ( saved.Model != expectedModel || member.Type != type )
					failure = $"thing {id} does not map to an imported {type} member";
				else
				{
					walked.Add( member.Id );
					id = saved.RoleNextId;
				}
			}
			if ( failure == null && walked.Count != fallback.Length )
				failure = $"chain covers {walked.Count} of {fallback.Length} {type} members";
			order[role] = failure == null ? walked.ToArray() : fallback;
			if ( failure != null )
				evidence.Add( $"Staff role order fallback for {type}: {failure}; using the saved global thing-chain projection for this role." );
		}
		staff.RestoreRoleOrder( order );
	}
}
