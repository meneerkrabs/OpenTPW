namespace OpenTPW;

public partial class Level
{
	private ThemeParkIncAwardPark? tpiAwardPark;

	/// <summary>Gives the park's economy (whichever the runtime holds now: a load replaces it) the level as the park its award checker measures.</summary>
	private void AttachThemeParkIncAwardPark()
	{
		if ( IsThemeParkIncSlice && Park?.Economy is { TpiCareer: not null } economy && economy.TpiAwardPark == null )
			economy.TpiAwardPark = tpiAwardPark ??= new ThemeParkIncAwardPark( this );
	}

	/// <summary>The level's park as Theme Park Inc's award checker reads it (<see cref="IThemeParkIncAwardPark"/>).</summary>
	internal sealed class ThemeParkIncAwardPark( Level level ) : IThemeParkIncAwardPark
	{
		private ParkEconomy Economy => level.Park!.Economy;

		// [BIN:TPI-EXE:0x67F5B0 cell type] the cell record's dword +8: the cell's own type, which a track tile of the second layer never overwrites (the record is apart from the cell), so a cell
		// holding only a tile reads 0 or the base type the tile keeps (ParkCellMap.PrimaryTypeAt, not the type byte OpenTPW shows for the tile)
		public byte CellType( int x, int y ) => Economy.Litter.Cells?.PrimaryTypeAt( x, y ) ?? 0;

		// [BIN:TPI-EXE:0x5D8CC2 litter type] the cell record's +0x30, Theme Park World's +0x2C: the park's litter map (ParkLitter)
		public bool HasLitter( int x, int y ) => Economy.Litter.TypeAt( x, y ) != LitterType.None;

		// [BIN:TPI-EXE:0x67F890 track record] the second-layer record through an extension's parent, then the track kinds (0xB, 0xD, 0x10, 0x12, 0x19): Theme Park World's 0x5363F0, StaffPlacementRules
		public bool HasTrackRecord( int x, int y ) => Economy.Litter.Cells is { } cells && StaffPlacementRules.IsTrackRecordCell( cells, x, y );

		// [BIN:TPI-EXE:0x5D7420 region record] the ten-byte record of the cell; its halfword +6 is the security word of the region grid (ParkEnvironment, written by 0x5D7770)
		public short Security( int x, int y ) => Economy.Litter.Environment.At( x, y ).Security;

		// [BIN:TPI-EXE:0x5B2F40 staff things] the things of kinds 4, 5, 6, 8, 7 and 0x14: the placed staff members, as the challenge system counts them
		public IReadOnlyList<ThemeParkIncAwardStaff> Staff => Economy.TpiStaff?.Members.Select( member => new ThemeParkIncAwardStaff( member.Role, member.Ability, member.PatrolArea ) ).ToArray()
			?? Array.Empty<ThemeParkIncAwardStaff>();

		// [BIN:TPI-EXE:0x649060 thing walk] every thing of class 3: the placed objects that are not deleted (the object in the hand is not one)
		// [BIN:TPI-EXE:0x5E3C20 closed test] the open word +0x7C (mCanLoad) is read as the runtime's open state, the script's VAR_RIDECLOSED: OpenTPW keeps no separate open word, and Theme Park Inc's
		// open and close (0x5E1DC0, 0x5E3400) set both together
		public IReadOnlyList<ThemeParkIncAwardAttraction> Attractions => level.Objects.Objects.Where( item => !item.IsDeleted ).Select( Attraction ).ToArray();

		private static ThemeParkIncAwardAttraction Attraction( OriginalObject item )
		{
			var statistics = item.Runtime.Bump?.SectionStatistics();
			return Attraction( item.Entry.Settings, item.Runtime.IsOpen, statistics?.LongestRun ?? 0, statistics?.Sections ?? 0 );
		}

		/// <summary>An attraction's award record from its type's settings (<c>Info.WhichUIType</c>, <c>Bumper.WhichTrackType</c>, the shape's size) and its live state.</summary>
		internal static ThemeParkIncAwardAttraction Attraction( ObjectSettings settings, bool open, int longestRun, int sections )
		{
			var (width, height) = ShapeSize( settings );
			return new ThemeParkIncAwardAttraction( settings.GetInt( "Info.WhichUIType" ), settings.GetInt( "Bumper.WhichTrackType" ), open, width, height, longestRun, sections );
		}

		/// <summary>The width and height of the type's <c>Info.Shape</c> record as the map parser writes them: the most cells of a row (a space takes none) and the rows.</summary>
		// [BIN:TPI-EXE:0x41AF73 shape size] the map parser stores the widest row's column count (a space skipped) at the record's +0 and the row count at +4 (0x41AF7B, 0x41AF82), as Theme Park World's
		// 0x00402720 does (ObjectMapGrid)
		internal static (int Width, int Height) ShapeSize( ObjectSettings settings )
		{
			if ( !settings.Blocks.TryGetValue( "Info.Shape", out var rows ) )
				return (0, 0);
			var width = rows.Count == 0 ? 0 : rows.Max( row => row.Count( symbol => symbol != ' ' ) );
			return (width, rows.Count);
		}
	}
}
