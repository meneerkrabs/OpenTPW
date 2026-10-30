using System;

namespace OpenTPW.Tests;

/// <summary>The world's walk step as the renderer tests stage it: the member moves, faces the way he moved and asks for the walk template (the staff world does the same, StaffWorldSimulation.Walk).</summary>
internal static class StaffTestWalk
{
	/// <param name="walks">Whether the step is a walk step (a member at work, sweeping or repairing, does not walk).</param>
	public static void Step( this StaffMember member, float dx, float dy = 0, bool walks = true )
	{
		member.CellX += dx;
		member.CellY += dy;
		var length = MathF.Sqrt( dx * dx + dy * dy );
		if ( length <= 0 )
			return;
		(member.HeadingX, member.HeadingY) = (dx / length, dy / length);
		if ( walks )
			StaffSpriteEvents.Walking( member );
	}
}
