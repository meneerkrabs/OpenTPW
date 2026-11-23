namespace OpenTPW;

/// <summary>The eight park awards in the checker's order (<c>0x606BE0</c>), which is the order of the career's bytes <c>+0x2A + i</c> and of <c>UITEXT.str</c>'s names 453 to 460.</summary>
public enum ThemeParkIncAward
{
	/// <summary>"Highest Coaster" (<c>ParkAward.CoasterHeight</c>).</summary>
	CoasterHeight = 0,
	/// <summary>"Fastest Coaster" (<c>ParkAward.CoasterSpeed</c>).</summary>
	CoasterSpeed = 1,
	/// <summary>"Cleanest Park" (<c>ParkAward.Hygiene</c> and <c>HygieneMinParkSize</c>).</summary>
	Hygiene = 2,
	/// <summary>"Most Decoration" (<c>ParkAward.Decoration</c>).</summary>
	Decoration = 3,
	/// <summary>"Best Security" (<c>ParkAward.Security</c>).</summary>
	Security = 4,
	/// <summary>"Best Staff Training" (<c>ParkAward.Training</c> and <c>TrainingMinStaff</c>).</summary>
	Training = 5,
	/// <summary>"Fastest Go-kart Track" (<c>ParkAward.GokartSpeed</c>).</summary>
	GokartSpeed = 6,
	/// <summary>"Longest Water Ride" (<c>ParkAward.WaterLength</c>).</summary>
	WaterLength = 7
}

/// <summary>
/// <c>levels/Standard.sam</c>'s <c>ParkAward</c> thresholds (the Standard object's <c>+0xE5C</c>.. : <c>0xA028BC</c> <c>CoasterHeight</c> to <c>0xA028E0</c> <c>WaterLength</c>); a key the files lack
/// reads 0, as the reset object's word does. <c>MinCellsOwned</c> and <c>MinCellsCovered</c> (<c>0xA028E4</c>, <c>0xA028E8</c>) have no reader.
/// </summary>
// [BIN:TPI-EXE:0x996684 Standard schema] "4,CoasterHeight;4,CoasterSpeed;4,Hygiene;4,HygieneMinParkSize;4,Security;4,Decoration;4,Training;4,TrainingMinStaff;4,GokartSpeed;4,WaterLength;
// 4,MinCellsOwned;4,MinCellsCovered;1,ParkAward": twelve words at 0xA028BC..0xA028E8, which the checker 0x606BE0 compares in its order (0xA028BC, C0, C4, C8, D0, CC, D4, D8, DC, E0)
public readonly record struct ThemeParkIncAwardSettings( int CoasterHeight, int CoasterSpeed, int Hygiene, int HygieneMinParkSize, int Security, int Decoration, int Training, int TrainingMinStaff,
	int GokartSpeed, int WaterLength )
{
	public static ThemeParkIncAwardSettings Read( SamSettings standard )
	{
		ArgumentNullException.ThrowIfNull( standard );
		int Word( string key ) => standard.GetInt( $"ParkAward.{key}", 0, optional: true );
		return new ThemeParkIncAwardSettings( Word( "CoasterHeight" ), Word( "CoasterSpeed" ), Word( "Hygiene" ), Word( "HygieneMinParkSize" ), Word( "Security" ), Word( "Decoration" ),
			Word( "Training" ), Word( "TrainingMinStaff" ), Word( "GokartSpeed" ), Word( "WaterLength" ) );
	}
}

/// <summary>A staff thing as the award metrics read it: its role, its trained ability (<c>+0x218</c>) and its patrol rectangle (<c>+0x24C..+0x258</c>).</summary>
public readonly record struct ThemeParkIncAwardStaff( ThemeParkIncStaffRole Role, int Ability, (int X0, int Y0, int X1, int Y1) PatrolArea );

/// <summary>
/// An attraction thing (class 3) as the award metrics read it: its type's <c>Info.WhichUIType</c> (<c>+0x600</c>) and track type (<c>+0x14C</c>, <c>Bumper.WhichTrackType</c>), whether it is open
/// (the open word <c>+0x7C</c>), its <c>Info.Shape</c>'s width and height (the shape record's <c>+0</c> and <c>+4</c>) and, for a track ride, its circuit's longest run of straights and its sections
/// (<c>0x694530</c>'s second output and its result).
/// </summary>
public readonly record struct ThemeParkIncAwardAttraction( int WhichUIType, int TrackType, bool Open, int ShapeWidth, int ShapeHeight, int LongestRun, int Sections );

/// <summary>The park as the award checker reads it: the map's cells, its staff and its attractions. A level answers it; a headless economy has none and checks no award.</summary>
public interface IThemeParkIncAwardPark
{
	/// <summary>The cell's type word (<c>+8</c>), 0 outside the map.</summary>
	byte CellType( int x, int y );
	/// <summary>Whether the cell holds litter (the litter type <c>+0x30</c> is nonzero).</summary>
	bool HasLitter( int x, int y );
	/// <summary>Whether the cell's second-layer record is a track kind (<c>0x67F890</c>: a kart or water tile or station, or a fence post).</summary>
	bool HasTrackRecord( int x, int y );
	/// <summary>The cell's security word of the region grid (the halfword <c>+6</c> of <c>0x5D7420</c>'s ten-byte record).</summary>
	short Security( int x, int y );
	/// <summary>The staff things (kinds 4 to 8 and 0x14).</summary>
	IReadOnlyList<ThemeParkIncAwardStaff> Staff { get; }
	/// <summary>The attraction things (class 3), in any order (the metrics keep counts and maxima).</summary>
	IReadOnlyList<ThemeParkIncAwardAttraction> Attractions { get; }
}

/// <summary>An award decided by a check (<c>0x606EC0</c>): the award, whether it was won (1) or moved to this zone from another (2), and the value it was stored with.</summary>
public readonly record struct ThemeParkIncAwardNotice( ThemeParkIncAward Award, bool Moved, int Value )
{
	/// <summary>The advisor message the original posts for it (<see cref="ThemeParkIncParkAwards.WonAdvice"/>, <see cref="ThemeParkIncParkAwards.MovedAdvice"/>).</summary>
	public int AdviceId => (Moved ? ThemeParkIncParkAwards.MovedAdvice : ThemeParkIncParkAwards.WonAdvice)[(int)Award];
}

/// <summary>
/// Theme Park Inc's park awards (<c>CParkAwardControl</c>; docs/reverse/TPI-AWARDS-SHARES.md): every 100 world ticks the checker measures the current park against the eight
/// <c>ParkAward</c> thresholds in order and stores the first award whose threshold is met and that the career's storage changes; at most one award is decided per check.
/// </summary>
public static class ThemeParkIncParkAwards
{
	/// <summary>The world ticks between two checks (<c>0x5D6600</c>).</summary>
	public const int CheckTicks = 100;

	/// <summary>The map side the metrics walk (<c>0x7F</c> down to 0 both ways).</summary>
	private const int MapSide = 128;

	/// <summary>The advisor message of a won award, by award (<c>0x606EC0</c>'s case 1).</summary>
	// [BIN:TPI-EXE:0x6074BE award won advice] the jump table 0x607AB0: 0x607847 (0xBF), 0x6074CE (0x13A), 0x60757B (0x13B), 0x60762E (0x13C), 0x6076E1 (0x13D), 0x607794 (0x13E), 0x6078FA (0xC0),
	// 0x6079AD (0xC1), each through 0x71A930
	public static readonly IReadOnlyList<int> WonAdvice = new[] { 0xBF, 0x13A, 0x13B, 0x13C, 0x13D, 0x13E, 0xC0, 0xC1 };

	/// <summary>The advisor message of an award that moved to this zone, by award (<c>0x606EC0</c>'s case 2, the jump table <c>0x607A90</c>).</summary>
	// [BIN:TPI-EXE:0x607A90 award moved advice] 0x6072A7 (0xC3), 0x606F2F (0x142), 0x606FDC (0x143), 0x60708F (0x144), 0x607141 (0x145), 0x6071F4 (0x146), 0x60735A (0xC4), 0x607407 (0xC5)
	public static readonly IReadOnlyList<int> MovedAdvice = new[] { 0xC3, 0x142, 0x143, 0x144, 0x145, 0x146, 0xC4, 0xC5 };

	/// <summary>Whether the world tick <paramref name="tick"/> runs a check (<c>0x5D6600</c>: the tick's unsigned remainder by 100 is 0).</summary>
	// [BIN:TPI-EXE:0x5D6600 award gate] [[0xAA131C] + 0x1EAB40] divl 100: a remainder of 0 and the screen state 0x6A1C70 ([0xB8B91C]) not 0x11 call 0x606B30 with the world-update object's +0x448;
	// the screen table 0x6A0FB0 fills gives slot 0x11 no handler, and OpenTPW's park runs in the in-park screen 10
	public static bool IsCheckTick( long tick ) => unchecked( (uint)tick ) % CheckTicks == 0;

	/// <summary>
	/// The check (<c>0x606BE0</c>): award by award in order, a metric at or above its threshold (and the second test of the hygiene and training awards) goes to the career's storage
	/// (<see cref="ThemeParkIncCareer.StoreAward"/>); the first that the storage wins or moves is the check's result, and the later awards are not looked at.
	/// The coaster awards are not measured (see the gate below). Null when no award was decided.
	/// </summary>
	// [BIN:TPI-EXE:0x606B30 award control] a first-call static init of the mode word (0x6A5EF0), then 0x606BE0 only when the mode word [0xC65B80] is 0: OpenTPW's slice is the career game, mode 0
	// [BIN:TPI-EXE:0x606BE0 award check] ECX = the analyser (0x64EAC0); in order: 0x5B7210 >= [0xA028BC] -> 0x74C760(0, v); 0x5B7380 >= [0xA028C0] -> (1, v); 100 - 0x5B80D0 >= [0xA028C4] and
	// 0x5B8E70 >= [0xA028C8] -> (2, 100 - x); 0x5B5680 >= [0xA028D0] -> (3, v); 0x5B8A70(7, 0) + 0x5B8D10 >= [0xA028CC] -> (4, sum); 0x5B3460 >= [0xA028D4] and 0x5B2F40 >= [0xA028D8] -> (5, the
	// percentage); 0x5B6D20 >= [0xA028DC] -> (6, v); 0x5B6FA0(.., 2) >= [0xA028E0] -> (7, v); every comparison signed (jl); a nonzero storage result goes to the event 0x606EC0(i, result), then the
	// debug line "You have %d total Park Awards" (0x74C860), and the scan ends
	// [EXT:tpi-capability-gates] the coaster awards (0 and 1) are not measured: 0x5B7210 and 0x5B7380 keep a coaster only when its thing's byte +0x3F is set, which only the attraction window
	// (0x52F570, 0x52F870) sets, and the byte +0x40, which the month message sets (0x5DF04C); neither byte is kept by OpenTPW, nor is the coaster track record's +0x110/+0x114 (0x480C00 over
	// 0x476AE0) checked against Theme Park World's rating; a coaster award is never won, and the check goes on to the hygiene award
	public static ThemeParkIncAwardNotice? Check( ThemeParkIncCareer career, IThemeParkIncAwardPark park, ThemeParkIncAwardSettings settings )
	{
		ArgumentNullException.ThrowIfNull( career );
		ArgumentNullException.ThrowIfNull( park );
		var hygiene = 100 - LitterPercent( park );
		if ( hygiene >= settings.Hygiene && CoveredCells( park ) >= settings.HygieneMinParkSize && Store( career, ThemeParkIncAward.Hygiene, hygiene ) is { } cleanest )
			return cleanest;
		var decoration = DecorationPercent( park );
		if ( decoration >= settings.Decoration && Store( career, ThemeParkIncAward.Decoration, decoration ) is { } decorated )
			return decorated;
		var security = unchecked( PatrolPercent( park ) + CameraPercent( park ) );
		if ( security >= settings.Security && Store( career, ThemeParkIncAward.Security, security ) is { } secure )
			return secure;
		var trained = TrainedPercent( park );
		if ( trained >= settings.Training && park.Staff.Count >= settings.TrainingMinStaff && Store( career, ThemeParkIncAward.Training, trained ) is { } training )
			return training;
		var straight = LongestTrackRide( park, 1, sections: false );
		if ( straight >= settings.GokartSpeed && Store( career, ThemeParkIncAward.GokartSpeed, straight ) is { } karts )
			return karts;
		var water = LongestTrackRide( park, 2, sections: true );
		if ( water >= settings.WaterLength && Store( career, ThemeParkIncAward.WaterLength, water ) is { } ride )
			return ride;
		return null;
	}

	private static ThemeParkIncAwardNotice? Store( ThemeParkIncCareer career, ThemeParkIncAward award, int value ) => career.StoreAward( (int)award, value ) switch
	{
		0 => null,
		var result => new ThemeParkIncAwardNotice( award, result == 2, value )
	};

	/// <summary>
	/// The littered share (<c>0x5B80D0</c>): of the cells that are paths (type 1), queues (3), queue ends (9) or hold litter, the percentage that hold litter; 0 when there are none.
	/// The hygiene award measures 100 less this.
	/// </summary>
	// [BIN:TPI-EXE:0x5B80D0 littered share] over the 128 × 128 cells (park +0x460 + 0x48 × (word - 1)): a = 0x67F5B0 (type 1) or 0x67F5D0 (type 3 or 9); c = the litter type +0x30 != 0; the
	// counted cells n += (a or c), the littered l += c; n == 0 answers 0, else l × 100 / n (cltd, idivl)
	public static int LitterPercent( IThemeParkIncAwardPark park )
	{
		int counted = 0, littered = 0;
		ForEachCell( ( x, y ) =>
		{
			var type = park.CellType( x, y );
			var litter = park.HasLitter( x, y );
			if ( type is 1 or 3 or 9 || litter )
				counted++;
			if ( litter )
				littered++;
		} );
		return counted == 0 ? 0 : littered * 100 / counted;
	}

	/// <summary>
	/// The covered cells (<c>0x5B8E70</c>): the queue ends and exits (types 9 and 10), the object cells (4), the track connections (0x15) and the cells over a track record.
	/// </summary>
	// [BIN:TPI-EXE:0x5B8E70 covered cells] over the 128 × 128 cells: 0x67F600 (type 9), 0x67F620 (10), 0x67F6B0 (4), 0x67F890 (the track record: 0x5C17B0, a record of kind 0xC or 0x11 through
	// its parent word +0x10, none when 0, then 0x6856D0: kinds 0xB, 0xD, 0x10, 0x12, 0x19) or 0x67F7B0 (0x15) counts
	public static int CoveredCells( IThemeParkIncAwardPark park )
	{
		var covered = 0;
		ForEachCell( ( x, y ) =>
		{
			if ( IsCovered( park, x, y ) )
				covered++;
		} );
		return covered;
	}

	private static bool IsCovered( IThemeParkIncAwardPark park, int x, int y ) => park.CellType( x, y ) is 9 or 10 or 4 or ParkCellTypes.TrackConnection || park.HasTrackRecord( x, y );

	/// <summary>
	/// The decoration (<c>0x5B5680</c>): the features' footprint (the width × height of each feature's <c>Info.Shape</c>, <c>Info.WhichUIType</c> 3) as a percentage of the covered cells; 0 without
	/// any feature.
	/// </summary>
	// [BIN:TPI-EXE:0x5B5680 decoration] the covered cells as 0x5B8E70 counts them; then every thing of class 3 whose type's +0x600 is 3 adds 0x5DF700 (the type's +0x1C shape record: +4 × +0);
	// a sum of 0 answers 0, else sum × 100 / covered (cltd, idivl)
	public static int DecorationPercent( IThemeParkIncAwardPark park )
	{
		var covered = CoveredCells( park );
		var footprint = 0;
		foreach ( var attraction in park.Attractions )
		{
			if ( attraction.WhichUIType == 3 )
				footprint = unchecked( footprint + attraction.ShapeWidth * attraction.ShapeHeight );
		}
		if ( footprint == 0 )
			return 0;
		if ( covered == 0 )
			throw new InvalidOperationException( "Features stand in a park with no covered cell: the original's decoration measure (0x5B5680) divides by the covered cells and faults." );
		return unchecked( footprint * 100 ) / covered;
	}

	/// <summary>
	/// The patrolled share (<c>0x5B8A70(7, 0)</c>): the guards' patrol rectangles (those not the whole map) marked on the cells (<c>0x5B86C0(7)</c>), then of the land cells (not water, not
	/// types 7 and 0x1E) the percentage marked; 0 without land cells.
	/// </summary>
	// [BIN:TPI-EXE:0x5B86C0 patrol marks] every cell's +0x40 = 0; then each thing of class 4, 5, 6, 8, 7 or 0x14 equal to the kind (7, the guards) whose 0x62C5C0 is true (the corner words of
	// +0x24C/+0x250 and +0x254/+0x258 not 1 and 0x4000, the whole map) marks +0x40 = 1 on x from (a - 1) & 0x7F to (b - 1) & 0x7F and y from (a - 1) >> 7 to (b - 1) >> 7 (0x62C490's two words, inclusive)
	// [BIN:TPI-EXE:0x5B8A70 patrolled share] mode 0 (jump table 0x5B8C68): over the 128 × 128 cells neither 0x67F590 (type 2) nor 0x67F660 (7 or 0x1E) nor 0x67F690 (always false): n += 1, and m += 1
	// when +0x40 > 0 (signed); n == 0 answers 0, else m × 100 / n
	public static int PatrolPercent( IThemeParkIncAwardPark park )
	{
		var marked = new bool[MapSide * MapSide];
		foreach ( var member in park.Staff )
		{
			var (x0, y0, x1, y1) = member.PatrolArea;
			if ( member.Role != ThemeParkIncStaffRole.Guard || (x0, y0, x1, y1) == (0, 0, MapSide - 1, MapSide - 1) )
				continue;
			for ( var x = x0; x <= x1; x++ )
			{
				for ( var y = y0; y <= y1; y++ )
				{
					if ( (uint)x < MapSide && (uint)y < MapSide )
						marked[y * MapSide + x] = true;
				}
			}
		}
		int land = 0, patrolled = 0;
		ForEachCell( ( x, y ) =>
		{
			if ( !IsLand( park.CellType( x, y ) ) )
				return;
			land++;
			if ( marked[y * MapSide + x] )
				patrolled++;
		} );
		return land == 0 ? 0 : patrolled * 100 / land;
	}

	/// <summary>The secured share (<c>0x5B8D10</c>): of the land cells, the percentage whose region-grid security word is above 0; 0 without land cells.</summary>
	// [BIN:TPI-EXE:0x5B8D10 secured share] over the 128 × 128 cells neither type 2 nor 7 nor 0x1E: n += 1, and m += 1 when the halfword +6 of 0x5D7420(word) (the grid at park +0x1C1104 + 0x460, ten bytes a
	// cell) is above 0 (signed cmpw, jle); n == 0 answers 0, else m × 100 / n
	public static int CameraPercent( IThemeParkIncAwardPark park )
	{
		int land = 0, secured = 0;
		ForEachCell( ( x, y ) =>
		{
			if ( !IsLand( park.CellType( x, y ) ) )
				return;
			land++;
			if ( park.Security( x, y ) > 0 )
				secured++;
		} );
		return land == 0 ? 0 : secured * 100 / land;
	}

	// [BIN:TPI-EXE:0x67F590 water] type 2; [BIN:TPI-EXE:0x67F660 no terrain] type 7 or 0x1E; [BIN:TPI-EXE:0x67F690 never] answers 0
	private static bool IsLand( byte type ) => type is not (ParkCellTypes.Water or ParkCellTypes.NoTerrain or ParkCellTypes.FixedWalkway);

	/// <summary>The trained share (<c>0x5B3460</c>): of the staff things, the percentage with a trained ability (<c>+0x218</c> nonzero); 0 without staff.</summary>
	// [BIN:TPI-EXE:0x5B3460 trained share] every thing of class 4, 5, 6, 8, 7 or 0x14: n += 1, and m += 1 when 0x62B8E0 (+0x218) is nonzero; n == 0 answers 0, else m × 100 / n
	public static int TrainedPercent( IThemeParkIncAwardPark park )
	{
		var staff = park.Staff.Count;
		var trained = park.Staff.Count( member => member.Ability != 0 );
		return staff == 0 ? 0 : trained * 100 / staff;
	}

	/// <summary>
	/// The best track ride of <paramref name="trackType"/> (<c>0x5B6D20</c> for the karts, 1; <c>0x5B6FA0(.., 2)</c> for the water rides): among the open rides (<c>Info.WhichUIType</c> 0) of that track
	/// type, the most straights in a row of the circuit (<paramref name="sections"/> false) or the most sections (true); 0 without one.
	/// </summary>
	// [BIN:TPI-EXE:0x5B6D20 best kart] every thing of class 3 whose 0x5E3C20 is 0 (the open word +0x7C set), type +0x600 == 0 and +0x14C == 1: 0x694530(+0x30, 0, &run, 0, 0) and its second output, the
	// first kept and a higher one (signed jle) replacing it
	// [BIN:TPI-EXE:0x5B6FA0 best track ride] the same with +0x14C == the argument (2 from the checker) and 0x694530's result, the sections
	// [BIN:TPI-EXE:0x694530 circuit counts] the track record of the handle (table 0xB8A280, 0xD0 bytes, generation checked) +0xBC: two passes over the section list (next +0x24) by the kind +4: 9 and
	// 10 extend the run, 11 extends it and counts a crossing, 12 and 13 extend it (and count an add-on when +8 is 0), any other ends it (the longest kept) and counts; every section counts; the outputs
	// are the others / 2, the longest run, the crossings / 2, the add-ons, and the result the sections / 2: Theme Park World's 0x545310 (BumpController.SectionStatistics) for the two read here
	public static int LongestTrackRide( IThemeParkIncAwardPark park, int trackType, bool sections )
	{
		var best = 0;
		foreach ( var attraction in park.Attractions )
		{
			if ( !attraction.Open || attraction.WhichUIType != 0 || attraction.TrackType != trackType )
				continue;
			var value = sections ? attraction.Sections : attraction.LongestRun;
			if ( value > best )
				best = value;
		}
		return best;
	}

	private static void ForEachCell( Action<int, int> visit )
	{
		for ( var y = MapSide - 1; y >= 0; y-- )
		{
			for ( var x = MapSide - 1; x >= 0; x-- )
				visit( x, y );
		}
	}
}
