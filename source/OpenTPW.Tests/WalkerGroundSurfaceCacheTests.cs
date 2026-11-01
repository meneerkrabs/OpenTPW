using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

// Uses the asset-free Level fixture of the track add-on tests (it skips the Level constructor).
public partial class TrackAddOnLevelTests
{
	[TestMethod]
	public void ALevelBuiltWithoutItsInitialisersSamplesAWalkersRideWithoutThrowing()
	{
		// the surface cache was created by a field initialiser, so the first terrain-standing walker of a co-op rig threw inside the tick
		var (level, entry) = CoasterMarkerLevel();
		var station = level.Objects.TryPlace( entry, 20, 16, 0, out _ );
		Assert.IsNotNull( station );
		level.WalkerGroundOf( station! );
	}
}
