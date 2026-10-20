using System;

namespace OpenTPW.Tests;

/// <summary>The staff tools over a park without a level: the tool's commands apply straight to their own <see cref="StaffHands"/>, as a level's command funnel would apply them.</summary>
internal static class StaffPlacementRig
{
	public static StaffPlacement Create( Func<ParkEconomy?> economy, Func<ParkCellMap?> cells, Func<int, int, bool> occupied )
	{
		var hands = new StaffHands( economy, cells, occupied );
		return new StaffPlacement( economy, cells, occupied, hands.Apply, hands.IsCarried );
	}
}
