namespace OpenTPW;

/// <summary>
/// The park value (net worth, <c>0xc1d4c</c>, tp.exe <c>0x004C7DB0</c>, docs/reverse/ORACLE.md F29): over the 128 × 128 map cells a number of points per owned
/// cell (<see cref="CellPoints"/>), then, per object, the costs of its upgrade levels scaled by its state of repair (<see cref="ObjectValue"/>).
/// </summary>
public static class ParkValueRules
{
	/// <summary>
	/// The points of one cell without the unowned flag 0x40: 2 for an empty cell, 3 for a path, 5 for a queue, 6 for an entrance, 1 for water and for an exit,
	/// plus 50 when its structure record is a track or hoarding tile (<paramref name="structure"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x004C7DB0 net worth] for every cell without flag 0x40 (0x00535DB0): +1 when the type is 2 (0x00536300), +3 for 1 (0x00536310), +5 for 3 or 9 (0x00536320), +1 for 9 (0x00536340), +1 for 10 (0x00536350), +10 when 0x005D60C0 answers (it returns 0), +2 for 0 (0x00536390), +50 when the structure record, or for a record of type 12 or 17 the record its +0x10 names, is of type 11, 13, 16, 18 or 25 (0x005363F0)
	public static int CellPoints( byte type, bool structure )
	{
		var points = 0;
		if ( type == 2 )
			points += 1;
		if ( type == 1 )
			points += 3;
		if ( type is 3 or 9 )
			points += 5;
		if ( type == 9 )
			points += 1;
		if ( type == 10 )
			points += 1;
		if ( type == 0 )
			points += 2;
		// [EXT:pools-value] pool water is worth what the land under it was: 2, as for an empty cell (a pool cell would otherwise score 0 and lower the park value, docs/POOLS.md)
		if ( type == ParkCellTypes.PoolWater )
			points += 2;
		return structure ? points + 50 : points;
	}

	/// <summary>Whether a structure record type counts its 50 points (after following a type-12 or type-17 record to the record it names).</summary>
	public static bool IsCountedStructure( byte type ) => type is 11 or 13 or 16 or 18 or 25;

	/// <summary>An object in state 4 (condemned) adds nothing to the value (<c>0xc1ef4</c>: <c>0xdfe20</c> is tested before the costs are summed).</summary>
	public static bool CountsForValue( int state ) => state != RideObjectStates.Condemned;

	/// <summary>
	/// What one object adds: the costs of its levels 0 up to the current one (<c>Upgrades[n].CostOfUpgrade</c>, the type record words <c>+440 + 64 · n</c>, a 32-bit sum), times the
	/// state of repair <c>+64</c> truncated and taken as a byte (a 32-bit product), divided by 100 toward zero.
	/// </summary>
	// [BIN:STP-PPC:0x100C1F00 park value objects] mullw by the gauge byte, then the multiply-high division by 100 (0x51EB851F); executed by the oracle target park-value
	public static int ObjectValue( IEnumerable<int> levelCosts, float stateOfRepair )
	{
		var total = 0;
		foreach ( var cost in levelCosts )
			total = unchecked( total + cost );
		return unchecked( total * RideBreakdownRules.GaugeByte( stateOfRepair ) ) / 100;
	}
}
