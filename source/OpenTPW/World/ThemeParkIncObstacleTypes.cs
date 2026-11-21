namespace OpenTPW;

/// <summary>One cell of a Theme Park Inc map grid (<c>Info.Shape</c>, <c>Obstacle.AfterShape</c>): the symbol's value (a cell type, or 0x21 for "leave untouched") and its direction byte.</summary>
public readonly record struct ThemeParkIncMapCell( int Value, byte Direction );

/// <summary>
/// A map grid as Theme Park Inc's SAM parser builds it (<c>0x41AD90</c>): the rows between the <c>---</c> markers, each non-space character looked up in the symbol table, the widest row's
/// width, and the rows stored bottom first (row 0 is the last text row). Cells a row does not reach are 0.
/// </summary>
public sealed class ThemeParkIncMapGrid
{
	/// <summary>The value of <c>%</c>: a cell the obstacle's opening and placement leave untouched.</summary>
	public const int Untouched = 0x21;
	/// <summary>The parser's limit on rows and on cells per row.</summary>
	public const int MaximumSide = 40;

	/// <summary>The map symbol table <c>0x98DF88</c>, its first 24 rows (the count <c>0x419F70</c> passes): character, value, direction.</summary>
	// [BIN:TPI-EXE:0x98DF88 map symbols] 12-byte rows (char, value, direction): '.' 0, '*' 4, 'Q' 3, '@' 1, '+' 11, '#' 16, 'H' 8, 'X' 5, ',' 30, '=' 32, '%' 33, '8' 9/1, '6' 9/4, '2' 9/16,
	// '4' 9/64, 'N' 10/1, 'E' 10/4, 'S' 10/16, 'W' 10/64, 'D' 23, '>' 23/4, '<' 23/64, 'O' 27/4, 'I' 27/64; the SAM parser passes 24 rows for a map key of subtype 1 (0x419F6B)
	private static readonly (char Symbol, int Value, byte Direction)[] Symbols =
	{
		('.', 0, 0), ('*', 4, 0), ('Q', 3, 0), ('@', 1, 0), ('+', 11, 0), ('#', 16, 0), ('H', 8, 0), ('X', 5, 0), (',', 30, 0), ('=', 32, 0), ('%', Untouched, 0),
		('8', 9, 1), ('6', 9, 4), ('2', 9, 16), ('4', 9, 64), ('N', 10, 1), ('E', 10, 4), ('S', 10, 16), ('W', 10, 64), ('D', 23, 0), ('>', 23, 4), ('<', 23, 64),
		('O', 27, 4), ('I', 27, 64)
	};

	private readonly ThemeParkIncMapCell[] cells;

	private ThemeParkIncMapGrid( int width, int height, ThemeParkIncMapCell[] cells )
	{
		Width = width;
		Height = height;
		this.cells = cells;
	}

	public int Width { get; }
	public int Height { get; }

	/// <summary>The cell at <paramref name="column"/> of <paramref name="row"/>, row 0 being the last text row.</summary>
	public ThemeParkIncMapCell this[int column, int row] => column >= 0 && column < MaximumSide && row >= 0 && row < Height
		? cells[row * MaximumSide + column]
		: throw new ArgumentOutOfRangeException( nameof( column ) );

	/// <summary>Parses the rows of a <c>---</c> block (<c>0x41AD90</c>). Throws for a character outside the table and for more than 40 rows or 40 cells in a row, as the parser fails there.</summary>
	// [BIN:TPI-EXE:0x41AD90 map grid] per row: ' ' is skipped without a column; any other character is looked up in the table (absent: failure), a 41st cell fails, the value and the direction are
	// stored at row × 40 + column (8 bytes each); the width is the widest row; a 41st row fails ("Map too high"); at the closing marker the rows are swapped end for end (0x41AF89..0x41AFE6)
	public static ThemeParkIncMapGrid Parse( IReadOnlyList<string> rows, string name )
	{
		ArgumentNullException.ThrowIfNull( rows );
		if ( rows.Count > MaximumSide )
			throw new InvalidDataException( $"{name}: map too high ({rows.Count} rows)." );
		var grid = new ThemeParkIncMapCell[MaximumSide * MaximumSide];
		var width = 0;
		for ( var row = 0; row < rows.Count; row++ )
		{
			var column = 0;
			foreach ( var symbol in rows[row] )
			{
				if ( symbol == ' ' )
					continue;
				var found = Array.FindIndex( Symbols, entry => entry.Symbol == symbol );
				if ( found < 0 )
					throw new InvalidDataException( $"{name}: illegal map character '{symbol}'." );
				if ( column == MaximumSide )
					throw new InvalidDataException( $"{name}: map too wide." );
				grid[(rows.Count - 1 - row) * MaximumSide + column] = new ThemeParkIncMapCell( Symbols[found].Value, Symbols[found].Direction );
				column++;
			}
			width = Math.Max( width, column );
		}
		return new ThemeParkIncMapGrid( width, rows.Count, grid );
	}
}

/// <summary>
/// A Theme Park Inc obstacle object type (<c>Data\levels\&lt;zone&gt;\obstacles\*.wad</c>, docs/reverse/TPI-MISSIONS.md): the object info words the obstacle code reads.
/// </summary>
/// <param name="Shape"><c>Info.Shape</c> (info <c>+0x1C</c>), applied at the placement in a new world.</param>
/// <param name="AfterShape"><c>Obstacle.AfterShape</c> (info <c>+0x11C</c>), applied when the obstacle opens; null when the file has none.</param>
/// <param name="IsSupportingObstacle"><c>Info.IsSupportingObstacle</c> (info <c>+0x50</c>), kept raw: its readers are not traced.</param>
public sealed record ThemeParkIncObstacleType( int Id, string Archive, ThemeParkIncMapGrid? Shape, ThemeParkIncMapGrid? AfterShape, int MapOffsetX, int MapOffsetY,
	bool IsObstacle, int IsSupportingObstacle, bool IsOutsideFeature, int GateTo, bool IsSpecial, bool IsCameraPath, int ShapeOffsetX, int ShapeOffsetY, int EntranceX, int EntranceY,
	bool DeleteAfterUse, int ReplaceWithId, bool OpenedAutomatically );

/// <summary>The obstacle types of a Theme Park Inc zone.</summary>
public static class ThemeParkIncObstacleTypes
{
	/// <summary>
	/// The obstacle types of <paramref name="level"/> by <c>Info.Id</c>, read from the <c>.sam</c> with an <c>Info.Id</c> of each archive in <c>levels/&lt;level&gt;/obstacles</c>; empty when the
	/// zone has no obstacles folder.
	/// </summary>
	// [BIN:TPI-EXE:0x9A2D00 object info keys] the info's key words are stored 4 bytes apart from +0x10 in the key table's order: Info.Shape +0x1C, Info.MapOffsetX/Y +0x34/+0x38, Info.IsObstacle +0x4C,
	// Info.IsSupportingObstacle +0x50, Info.IsOutsideFeature +0x54, Info.GateTo +0x58, Info.IsSpecial +0x5C, Info.IsCameraPath +0x60, Obstacle.AfterShape +0x11C, Obstacle.ShapeOffsetX/Y
	// +0x120/+0x124, Obstacle.EntranceX/Y +0x128/+0x12C, Obstacle.DeleteAfterUse +0x130, Obstacle.ReplaceWithID +0x134, Obstacle.OpenedAutomaticly +0x138 (anchored by 0x647BF0 and 0x5E8BD0)
	public static IReadOnlyDictionary<int, ThemeParkIncObstacleType> Load( BaseFileSystem files, string level )
	{
		ArgumentNullException.ThrowIfNull( files );
		var types = new SortedDictionary<int, ThemeParkIncObstacleType>();
		var folder = $"/levels/{level}/obstacles";
		if ( !files.DirectoryExists( folder ) )
			return types;
		foreach ( var archive in files.GetDirectories( folder ).Select( path => files.GetRelativePath( files.GetAbsolutePath( path ) ) ).OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
		{
			foreach ( var file in files.GetFiles( archive ).Where( path => path.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) ) )
			{
				var settings = ObjectSettingsFile.Load( files, files.GetRelativePath( files.GetAbsolutePath( file ) ) );
				if ( !settings.Values.ContainsKey( "Info.Id" ) )
					continue;
				var type = Read( settings, archive );
				if ( !types.TryAdd( type.Id, type ) )
					throw new InvalidDataException( $"{archive}: obstacle type {type.Id} is defined twice in {level}." );
			}
		}
		return types;
	}

	private static ThemeParkIncObstacleType Read( ObjectSettingsFile settings, string archive )
	{
		ThemeParkIncMapGrid? Grid( string key ) => settings.Blocks.TryGetValue( key, out var rows ) ? ThemeParkIncMapGrid.Parse( rows, $"{archive} {key}" ) : null;
		// the SAM parser converts a value with atol; a missing key keeps the reset's 0
		int Int( string key ) => settings.Values.TryGetValue( key, out var value ) && int.TryParse( value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number ) ? number : 0;
		return new ThemeParkIncObstacleType( Int( "Info.Id" ), archive, Grid( "Info.Shape" ), Grid( "Obstacle.AfterShape" ), Int( "Info.MapOffsetX" ), Int( "Info.MapOffsetY" ),
			Int( "Info.IsObstacle" ) == 1, Int( "Info.IsSupportingObstacle" ), Int( "Info.IsOutsideFeature" ) == 1, Int( "Info.GateTo" ), Int( "Info.IsSpecial" ) == 1,
			Int( "Info.IsCameraPath" ) == 1, Int( "Obstacle.ShapeOffsetX" ), Int( "Obstacle.ShapeOffsetY" ), Int( "Obstacle.EntranceX" ), Int( "Obstacle.EntranceY" ),
			Int( "Obstacle.DeleteAfterUse" ) == 1, Int( "Obstacle.ReplaceWithID" ), Int( "Obstacle.OpenedAutomaticly" ) == 1 );
	}
}
