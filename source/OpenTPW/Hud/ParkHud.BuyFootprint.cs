using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>The buy window's footprint thumbnail: the selected item's shape as an 8 x 8 grid of translucent cells under its preview.</summary>
public sealed partial class ParkHud
{
	/// <summary>The cells of the thumbnail grid.</summary>
	public const int BuyFootprintCellsPerSide = 8;

	/// <summary>The colour class of a shape cell type for the thumbnail (0 draws nothing).</summary>
	// [BIN:TPW-EXE:0x52c5b0 footprint classes] the buy window copies the type's footprint into a 16 x 16 grid of classes: the cell types 1 and 23 and 4 give the orange classes 4 and 1, 9 (an entrance) class 2 and 10 (an exit) class 3 through the byte table 0x52c6a4 (type - 1) and the jump table 0x52c68c; every other type (free, queue, upgrade, 16) stays 0
	// [BIN:TPW-EXE:0x4ab3d2 footprint colours] 0x4ab1b0 fills each class's cell with a half-transparent colour: classes 1 and 4 (255, 170, 30), class 2 (50, 220, 15), class 3 (15, 100, 220), all with alpha 0x80, bytes in the engine's R, G, B, A order
	public static RgbaByte? BuyFootprintColor( int cellType ) => cellType switch
	{
		4 or 23 or 1 => new RgbaByte( 255, 170, 30, 128 ),
		9 => new RgbaByte( 50, 220, 15, 128 ),
		10 => new RgbaByte( 15, 100, 220, 128 ),
		_ => null
	};

	/// <summary>The coloured cells of the thumbnail: column, row from the bottom (the shape's V, row 0 is the front) and colour, inside the 8 x 8 grid.</summary>
	// [BIN:TPW-EXE:0x4ab1b0 footprint grid] the loops run over the footprint's width (columns, x grows to the right) and height (rows, row 0 at the bottom of the control's bitmap); a cell is the bitmap's size / 8 (integer pixels) square and a footprint beyond 8 cells is cut off by the bitmap
	public static IReadOnlyList<(int Column, int Row, RgbaByte Color)> BuyFootprintCells( ObjectShape? shape )
	{
		var cells = new List<(int, int, RgbaByte)>();
		if ( shape == null )
			return cells;
		foreach ( var cell in shape.Cells )
		{
			if ( cell.U >= BuyFootprintCellsPerSide || cell.V >= BuyFootprintCellsPerSide || BuyFootprintColor( cell.CellType ) is not { } color )
				continue;
			cells.Add( (cell.U, cell.V, color) );
		}
		return cells;
	}

	/// <summary>The control 0x1eb of the buy window (table record 0x754d50, (440,407)-(594,561)).</summary>
	private sealed class BuyFootprint : UiElement
	{
		private readonly ParkHud owner;
		public BuyFootprint( ParkHud owner ) => this.owner = owner;

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			if ( owner.BuyPreviewItem is not { } item || owner.IsMysteryRow( item ) || item.Entry?.Shape is not { } shape )
				return;
			var rect = ScreenRect( context.Canvas );
			var width = (int)rect.Width;
			var height = (int)rect.Height;
			var cellWidth = width / BuyFootprintCellsPerSide;
			var cellHeight = height / BuyFootprintCellsPerSide;
			foreach ( var (column, row, color) in BuyFootprintCells( shape ) )
			{
				var left = rect.X + cellWidth * column;
				var top = rect.Y + height - cellHeight - row * cellHeight - 1;
				context.Batch.AddRectangle( new UiRect( left, top, cellWidth, cellHeight ), color );
			}
		}
	}
}
