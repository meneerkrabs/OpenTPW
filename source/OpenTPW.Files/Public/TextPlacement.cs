namespace OpenTPW;

/// <summary>Horizontal alignment of text inside a box, as the text library's mode (0 left, 2 centre, 1 right).</summary>
public enum TextAlignment { Left, Center, Right }

/// <summary>
/// Where tp.exe's text library puts a text block inside a rectangle and a line inside the block, in whole pixels.
/// </summary>
// [BIN:TPW-EXE:0x6aeb10 block box] the block is lines x line height tall and the widest line wide; vertically it is centred as
// (bottom - top - block height) / 2 + top (signed division, truncating toward zero, also when the block is taller than the
// box), or bottom aligned as bottom - block height; horizontally centred as (right - left - block width) / 2 + left or right
// aligned as right - block width. [BIN:TPW-EXE:0x6ae550 line offset] inside the block each line moves by
// (block width - line width) >> 1 (centre) or by the whole difference (right).
public static class TextPlacement
{
	/// <summary>Left edge of a block of <paramref name="blockWidth"/> in a box from <paramref name="left"/> to <paramref name="right"/>.</summary>
	public static int BlockLeft( TextAlignment alignment, int left, int right, int blockWidth ) => alignment switch
	{
		TextAlignment.Right => right - blockWidth,
		TextAlignment.Center => (right - left - blockWidth) / 2 + left,
		_ => left
	};

	/// <summary>Top edge of a block of <paramref name="blockHeight"/> centred between <paramref name="top"/> and <paramref name="bottom"/>.</summary>
	public static int BlockTopCentered( int top, int bottom, int blockHeight ) => (bottom - top - blockHeight) / 2 + top;

	/// <summary>Top edge of a block of <paramref name="blockHeight"/> aligned to the bottom of the box.</summary>
	public static int BlockTopBottom( int bottom, int blockHeight ) => bottom - blockHeight;

	/// <summary>Shift of one line inside the block: centred lines by half the difference (unsigned shift), right aligned by all of it.</summary>
	public static int LineOffset( TextAlignment alignment, int blockWidth, int lineWidth ) => alignment switch
	{
		TextAlignment.Right => blockWidth - lineWidth,
		TextAlignment.Center => (int)((uint)(blockWidth - lineWidth) >> 1),
		_ => 0
	};
}
