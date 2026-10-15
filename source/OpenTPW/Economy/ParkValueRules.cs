namespace OpenTPW;

/// <summary>
/// The object part of the park value (<c>0xc1d4c</c>, docs/reverse/ORACLE.md F29, ECON-026). The original adds, over the 128 × 128 map cells and then over every object, a
/// number of points per cell and, per object, the costs of its upgrade levels scaled by its state of repair. OpenTPW has the object part: its cell map has no ownership
/// (PATH-007), so the cell points (2 per owned empty cell, 3 per path, 5 per queue, and so on) are not added.
/// </summary>
public static class ParkValueRules
{
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
