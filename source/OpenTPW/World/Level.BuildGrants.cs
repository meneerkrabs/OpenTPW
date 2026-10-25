namespace OpenTPW;

public partial class Level
{
	/// <summary>[EXT:online-coop-permissions] The guests' seats the host granted building (<see cref="SetBuildGrantCommand"/>); null until the first grant.</summary>
	private SortedSet<int>? buildGrants;

	/// <summary>[EXT:online-coop-permissions] The seats granted building, in order (empty without a session or a grant).</summary>
	internal IReadOnlyCollection<int> BuildGrants => (IReadOnlyCollection<int>?)buildGrants ?? Array.Empty<int>();

	/// <summary>[EXT:online-coop-permissions] Whether the guest's <paramref name="seat"/> may build.</summary>
	internal bool HasBuildGrant( int seat ) => buildGrants?.Contains( seat ) == true;

	/// <summary>[EXT:online-coop-permissions] Grants <paramref name="seat"/> building, or takes it back.</summary>
	internal void SetBuildGrant( int seat, bool granted )
	{
		if ( granted )
			(buildGrants ??= new SortedSet<int>()).Add( seat );
		else
			buildGrants?.Remove( seat );
	}
}
