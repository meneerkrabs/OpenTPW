namespace OpenTPW;

/// <summary>
/// The umbrellas extension's approximation register (docs/UMBRELLAS.md). Each entry is tagged <c>[APPROX:UMB-NNN]</c> at its code site; the words before the colon name the topic. Everything else of the extension is
/// traced from the Theme Park Inc no-CD <c>Game.exe</c> (<c>[BIN:TPI-EXE:0x...]</c> labels) or is OpenTPW's own, with <c>[EXT:umbrellas-*]</c> labels; it is not Theme Park World's behaviour, which has no umbrellas.
/// </summary>
public static class UmbrellaApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("UMB-004", "UMB-PULL: Theme Park Inc pulls the umbrella horizontally 10 % of the way to the camera (0x60B236 and 0x60B274, GenericUmbrellaModifiers [1] at 0xA01E0C and [3] at 0xA01E14, both 0.1 in levels/Standard.sam) and lifts it by (camera height - 40.0) x GenericUmbrellaModifiers [2] (YAdjustCamera, 0xA01E10, at 0x60B252, 0.1 in Standard.sam), so its hand point moves 10 % of the way along the line to the camera except that the lift uses the fixed height 40 where a sight-line pull would use the hand's own, 0.1 x (40 - hand height) lower; OpenTPW pulls the umbrella along the sight line instead, by PullShare = min(0.1, 1.5 * ModelScale / depth) (UmbrellaRenderer.PullShare: CameraPull 0.1 is the upper share, and MaxPullUnits 1.5 original units times ObjectPlacement.ModelScale over the depth in engine units, EXT:umbrellas-pull-cap, so a roof that hides the guest mostly hides its umbrella too), so the screen position of the hand stays and the quad shrinks by the same share; the original's own hand anchors are TPI sprite data OpenTPW does not have, so the lift's offset from the sight line cannot be applied to the measured anchors and the placement stays an approximation decided by the user on 2026-10-18"),
	};
}
