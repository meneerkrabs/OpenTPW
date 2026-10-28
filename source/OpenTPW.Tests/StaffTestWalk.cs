using System;

namespace OpenTPW.Tests;

/// <summary>The world's walk step as the renderer tests stage it: the member moves, faces the way he moved and is walking (the staff world writes the same fields, StaffWorldSimulation.Walk).</summary>
internal static class StaffTestWalk
{
	public static void Step( this StaffMember member, float dx, float dy = 0 )
	{
		member.CellX += dx;
		member.CellY += dy;
		var length = MathF.Sqrt( dx * dx + dy * dy );
		if ( length <= 0 )
			return;
		(member.HeadingX, member.HeadingY) = (dx / length, dy / length);
		member.IsWalking = true;
	}
}
