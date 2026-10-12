namespace OpenTPW;

/// <summary>
/// The choice loop of the original's major decision (<c>0xe8ff4</c>, docs/reverse/ORACLE.md F24): attractions are visited in list order, each
/// scored by <see cref="AttractionScore"/>; the best score wins.
/// </summary>
public static class AttractionChoice
{
	/// <summary>One candidate in list order. <paramref name="Passes"/> runs the counts-test and the reachability test, in that order, only when the score would win.</summary>
	public readonly record struct Candidate( int Id, bool Choosable, bool HasQueueCell, uint Score, Func<bool> Passes );

	/// <summary>
	/// A choosable attraction with a back-of-queue cell is scored; a score below <see cref="AttractionScore.CandidateFloor"/> (unsigned) is ignored.
	/// It replaces the best so far (initially 0, signed compare) when it is greater, or equal on an odd world turn (the later of equal scores wins then),
	/// and then only if <see cref="Candidate.Passes"/> holds. Returns the winner's id, 0 when there is none.
	/// </summary>
	// [BIN:STP-PPC:0x100E90A8 choice loop] cmplwi 10 unsigned, cmpw signed against the best, the equal case gated by game +0x1da70c bit 0, then 0xdc834 and the path test 0xe67c8
	public static int Pick( IEnumerable<Candidate> candidates, long worldTurn )
	{
		var best = 0;
		var chosen = 0;
		foreach ( var candidate in candidates )
		{
			if ( !candidate.Choosable || !candidate.HasQueueCell || candidate.Score < AttractionScore.CandidateFloor )
				continue;
			var signed = unchecked( (int)candidate.Score );
			if ( signed < best || (signed == best && (worldTurn & 1) == 0) )
				continue;
			if ( !candidate.Passes() )
				continue;
			best = signed;
			chosen = candidate.Id;
		}
		return chosen;
	}
}
