namespace OpenTPW;

/// <summary>
/// Approximation register of the build-tool blueprint markers (docs/reverse/BLUEPRINT.md). Each entry is tagged
/// <c>[APPROX:BLUEPRINT-NNN]</c> at its code site; the rest of the markers is traced from the Mac binary.
/// </summary>
public static class BlueprintApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("BLUEPRINT-001", "HEIGHT: every marker cell takes its corner height rounded up to a multiple of 10; the original uses 0 for plain cells and raises cells next to rides and track"),
		("BLUEPRINT-002", "UV: each marker face shows its whole texture once and both sides are drawn; the original's per-corner coordinates come from a runtime table"),
		("BLUEPRINT-003", "CELLS: a path or queue line marks every cell up to the refused one, existing cells blue; an object's footprint is blue when the build rule allows it and red otherwise"),
	};
}
