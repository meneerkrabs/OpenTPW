namespace OpenTPW;

/// <summary>
/// The art a terrain cell carries while the procedural terrain textures are on (tp.exe <c>0x56F090</c>, the descriptor of a cell whose word has bit 0x80): the texture of the cell's slot as the
/// composite reads it (<see cref="ObjectFloorBase.FlipRows"/>), and the cell's word, whose bits 0x78 turn the base in the composite and the texture coordinates of the cell when it is drawn.
/// </summary>
/// <param name="Key">Names the art for the comparison of two cells' state (the path texture's index): a cell whose key and word are unchanged is not built again.</param>
/// <param name="Rgba">The art's texels as the composite reads them: RGBA, <paramref name="Width"/> × <paramref name="Height"/>.</param>
/// <param name="Word">The cell's landscape word (<see cref="PathCellWord"/>).</param>
internal sealed record ProceduralCellArt( int Key, byte[] Rgba, int Width, int Height, uint Word )
{
	/// <summary>Whether two cells' art builds the same texture.</summary>
	public static bool Same( ProceduralCellArt? first, ProceduralCellArt? second )
		=> ReferenceEquals( first, second ) || (first != null && second != null && first.Key == second.Key && first.Word == second.Word && first.Width == second.Width && first.Height == second.Height);
}
