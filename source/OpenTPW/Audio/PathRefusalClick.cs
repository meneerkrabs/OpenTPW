namespace OpenTPW;

/// <summary>
/// When the path tool's refused click (<see cref="GameAudio.PathRefusedEvent"/>) sounds. The original plays it in the park view's
/// left-button release handler, only for a release that follows a press the park view's press handler took, and only outside a
/// layout replay. OpenTPW commits the tool's click on the press; the refusal it finds there is kept and sounds at the release.
/// </summary>
// [BIN:TPW-EXE:0x46c210 park view buttons] message 0xF0 calls the mode object's slot +4 on a press and slot +8 on a release; the path tool's object (vtable 0x6fe9e0) routes them to 0x46c5d0 -> 0x524790 (press) and 0x46c610 -> 0x524960 (release)
// [BIN:TPW-EXE:0x52479a press latch] the press handler sets byte 0x8186d8; every exit of the release handler clears it (0x524ae6 and the tool switch's exits 0x525a0d..0x52765e)
// [BIN:TPW-EXE:0x524a6f refused-click gates] the release plays cat_ui 0xAF only when the layout-replay word 0x785a2c is 0 (the action recorder 0x785a08 +0x24, set by 0x404140 "Starting layout replay"; OpenTPW has no layout replay, so it is always 0 here) and the press latch is set
public sealed class PathRefusalClick
{
	private bool pressSeen;
	private (int X, int Y)? refusedCell;

	/// <summary>The park view's press handler took a left press (byte 0x8186d8 set).</summary>
	public void Press() => pressSeen = true;

	/// <summary>The click this press committed was refused at <paramref name="x"/>, <paramref name="y"/> (the refusal latch 0x816d48 the release reads).</summary>
	public void Refused( int x, int y ) => refusedCell = (x, y);

	/// <summary>
	/// The left button's release: the cell the refused click sounds at, or null. The press latch and the refusal are consumed either
	/// way, as every exit of the release handler clears the latch.
	/// </summary>
	public (int X, int Y)? Release( bool layoutReplay = false )
	{
		var cell = pressSeen && !layoutReplay ? refusedCell : null;
		pressSeen = false;
		refusedCell = null;
		return cell;
	}
}
