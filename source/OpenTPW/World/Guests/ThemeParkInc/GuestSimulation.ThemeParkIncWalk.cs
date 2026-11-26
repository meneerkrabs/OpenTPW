namespace OpenTPW;

// Theme Park Inc's walk step for the guests of a park with Theme Park Inc's navigator (the hidden switch --tpi-native-walk, docs/reverse/TPI-NAVIGATOR.md, NV3): every guest's walk step, a native
// guest's (TpiWalk) and a Theme Park World-rule guest's in a Theme Park Inc park alike, is 0x60B460 (ThemeParkIncWalkStep); the staff take the same routine (ThemeParkIncStaffWorld.Walk).
public sealed partial class GuestSimulation
{
	/// <summary>Whether the park walks with Theme Park Inc's navigator (<see cref="ParkCellMap.EdgeRules"/>, the hidden switch <c>--tpi-native-walk</c>): the walk step is then <see cref="StepThemeParkInc"/>.</summary>
	private bool WalksThemeParkInc => Grid.Cells.EdgeRules == WalkEdgeRules.ThemeParkInc;

	/// <summary>
	/// The walk step <c>0x60B460</c> for a guest (<see cref="ThemeParkIncWalkStep"/>): the steering step, the cell chain, the stranded mark cleared, then the result with the heading word on arrival
	/// and while walking (D1) and, while walking, the walk or run template (D2: only a kid runs) and the interval. The interpolation's start (<c>mPreviousX/Y</c>, <see cref="Guest.StepFromX"/>) is
	/// the copy the speed step took, which the step leaves.
	/// </summary>
	// [BIN:TPI-EXE:0x60B4CA stranded mark] the walk step clears +0x1B0 on every call, before its result (tp.exe 0x4FA30B clears +0x198 the same way; OpenTPW's Theme Park World walk does not, a
	// Theme Park World follow-up)
	private WalkResult StepThemeParkInc( Guest guest )
	{
		var navigation = guest.Navigation;
		var (x0, y0) = (navigation.X, navigation.Y);
		var (fromX, fromY) = (guest.X, guest.Y);
		stepping = navigation;
		var result = navigation.Step( guest.MaxSpeed, guest.MaxForce, Grid.Cells, isOpenForStep, separationForStep );
		stepping = null;
		// [BIN:TPI-EXE:0x60B4BB cell chain] 0x637B70(x1 >> 8, y1 >> 8): mX/mY and the cell's thing chain (tp.exe 0x50B6A0)
		Occupy( guest );
		guest.StrandedMark = 0;
		var (x1, y1) = (navigation.X, navigation.Y);
		if ( result != WalkResult.Stuck )
		{
			var heading = ThemeParkIncWalkStep.Heading( x0, y0, x1, y1 );
			if ( guest.Tpi is { } tpi )
				tpi.Heading = heading;
			(guest.HeadingX, guest.HeadingY) = ThemeParkIncWalkStep.Vector( heading );
		}
		if ( result == WalkResult.Walking )
		{
			// [EXT:tpi-twp-guest-rules] the body plays Theme Park World's sprite programs (the walk 1 and the run 2 are the same programs under Theme Park Inc's numbers, ThemeParkIncStaffWorld.ProgramOf);
			// Theme Park Inc's two-slot sprite commit 0x5D1080 is presentation and not ported
			var run = ThemeParkIncWalkStep.Runs( guest.ToiletBonus, ThemeParkIncWalkStep.GuestKind, guest.Tpi?.Generation ?? TpiGeneration( guest ) );
			guest.BodySprite.RequestWalkTemplate( run ? VisitorSpriteTemplate.Run : VisitorSpriteTemplate.Walk );
			guest.BodySprite.QueueInterval( ThemeParkIncWalkStep.Interval( x0, y0, x1, y1, run ) );
		}
		// presentation: the distance walked (the walk cycle)
		var dx = guest.X - fromX;
		var dy = guest.Y - fromY;
		var step = MathF.Sqrt( dx * dx + dy * dy );
		guest.IsMoving = step > 0;
		guest.DistanceWalked += step;
		return result;
	}

	/// <summary>
	/// The sprite placement <c>0x60AE00</c>'s simulation part, as the guest constructor's tail calls it (<c>0x60CAC9</c>): the counter raised, a mark above it cleared. The placement itself is
	/// presentation; its once-per-frame calls from the render pass do not count (<c>[EXT:tpi-stamp-counter-sim-only]</c>, <see cref="ParkCellMap.CountsSimulationCalls"/>).
	/// </summary>
	// [BIN:TPI-EXE:0x60AFF1 placement stamp] 0x5D81F0 below +0x1B0 (jae) clears it; while it is nonzero the debug marker 0x687970 is queued (shown only with the flag 0x9D6C28)
	private void ThemeParkIncPlacementStamp( Guest guest )
	{
		if ( Grid.Cells.NextStamp() < guest.StrandedMark )
			guest.StrandedMark = 0;
	}
}
