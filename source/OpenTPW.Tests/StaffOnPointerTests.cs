using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// A candidate an original save held on the pointer (<c>+0xB</c>) stays in the pool without expiring until a placement tool ends (tp.exe 0x5084F0 skips a flagged candidate when it expires the pool, 0x5083B0
/// clears the flag and, with the placement made, removes it): SAVE-004.
/// </summary>
[TestClass]
public class StaffOnPointerTests
{
	private static ParkEconomy Imported( bool flagged, out int flaggedId )
	{
		var park = EconomyTestData.Park();
		park.Staff.RestoreOriginalPool( new[]
		{
			(StaffType.Handyman, 1, 1, 5L, 2L, 0),
			(StaffType.Handyman, 2, 1, 5L, 2L, 0),
		}, 5, flagged ? new[] { false, true } : null );
		flaggedId = park.Staff.StaleOnPointerId;
		return park;
	}

	[TestMethod]
	public void AFlaggedCandidateIsKeptPastItsTimeoutAndTheOtherIsNot()
	{
		var park = Imported( true, out var flaggedId );
		Assert.AreNotEqual( 0, flaggedId );
		var late = park.Staff.Candidates.Max( candidate => candidate.ExpiresTick ) + 1000;
		park.Staff.UpdatePool( park.World, late );
		Assert.IsTrue( park.Staff.Candidates.Any( candidate => candidate.Id == flaggedId ), "a flagged candidate never leaves the pool" );
		Assert.IsFalse( park.Staff.Candidates.Any( candidate => candidate.NameIndex == 1 && candidate.Id != flaggedId && candidate.ExpiresTick <= late ), "the unflagged one expired" );
	}

	[TestMethod]
	public void WithoutAFlagBothCandidatesExpire()
	{
		var park = Imported( false, out var flaggedId );
		Assert.AreEqual( 0, flaggedId );
		var late = park.Staff.Candidates.Max( candidate => candidate.ExpiresTick ) + 1000;
		park.Staff.UpdatePool( park.World, late );
		Assert.IsFalse( park.Staff.Candidates.Any( candidate => candidate.NameIndex is 1 or 2 && candidate.ExpiresTick <= late ) );
	}

	[TestMethod]
	public void ThePlacementThatEndsClearsTheFlagAndAPlacementMadeRemovesTheCandidate()
	{
		var cancelled = Imported( true, out var flaggedId );
		cancelled.Staff.EndPlacement( placed: false );
		Assert.AreEqual( 0, cancelled.Staff.StaleOnPointerId );
		Assert.IsTrue( cancelled.Staff.Candidates.Any( candidate => candidate.Id == flaggedId ), "a cancelled tool only clears the flag (0x5083B0 with 1)" );
		var placed = Imported( true, out flaggedId );
		placed.Staff.EndPlacement( placed: true );
		Assert.IsFalse( placed.Staff.Candidates.Any( candidate => candidate.Id == flaggedId ), "a placement made removes the flagged candidate (0x5083B0 with 0)" );
	}

	[TestMethod]
	public void TheCandidateBeingHiredIsLeftToTheHirePath()
	{
		var park = Imported( true, out var flaggedId );
		park.Staff.PendingPlacementId = flaggedId;
		park.Staff.EndPlacement( placed: true );
		Assert.IsTrue( park.Staff.Candidates.Any( candidate => candidate.Id == flaggedId ), "the hire path takes it" );
	}
}
