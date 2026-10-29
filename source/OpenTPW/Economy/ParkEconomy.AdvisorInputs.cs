namespace OpenTPW;

/// <summary>
/// The park statistics the advisor's last nine scan callbacks read (the guards' chase counters, the patrol coverage, the mean ride excitement and the first queue that joins no path),
/// kept or computed from the economy without writing anything the simulation reads: the counters are presentation words of the analyser, the rest are pure reads.
/// </summary>
public sealed partial class ParkEconomy
{
	/// <summary>
	/// A guard's chase ends (tp.exe <c>0x004C74F0</c>, called from <c>0x004D691B</c> with a nonzero argument when the guard catches the kid and from <c>0x004D6A1F</c> with 0 when he gives the chase up):
	/// both raise the month word (+0x21BFC) and the lifetime word (+0x21C04); only the abandoned chase raises the got-away word (+0x21C00).
	/// </summary>
	// [BIN:TPW-EXE:0x004C74F0 misbehaved] +0x21BFC and +0x21C04 are raised, +0x21C00 only when the argument is 0 (TEST EAX, EAX; JNE over the INC); nothing else in tp.exe writes them but the constructor (0x4C5BBE-0x4C5BCA), the month close and the save load
	internal void RecordGuardChase( bool caught )
	{
		Counters.Add( ParkCounters.MisbehavingKidsMonth, 1 );
		Counters.Add( ParkCounters.MisbehavingKidsEver, 1 );
		if ( !caught )
			Counters.Add( ParkCounters.MisbehavingKidsGotAway, 1 );
	}

	/// <summary>The analyser's misbehaving-kids words (<c>0x004C94A0</c>): the month word for 0, the got-away word for a nonzero argument.</summary>
	// [BIN:TPW-EXE:0x004C94A0 misbehaving kids] a nonzero argument returns +0x21C00 (the kids that got away this month), 0 returns +0x21BFC (the kids a guard went after this month)
	internal int MisbehavingKids( bool gotAway ) => unchecked( (int)Counters[gotAway ? ParkCounters.MisbehavingKidsGotAway : ParkCounters.MisbehavingKidsMonth] );

	/// <summary>
	/// The month close zeroes the month word and the got-away word, not the lifetime word.
	/// </summary>
	// [BIN:TPW-EXE:0x004C79EB month close] the month handler 0x004C7720 ends by storing 0 in +0x21BFC and +0x21C00
	private void CloseMisbehavingMonth()
	{
		Counters.Set( ParkCounters.MisbehavingKidsMonth, 0 );
		Counters.Set( ParkCounters.MisbehavingKidsGotAway, 0 );
	}

	/// <summary>
	/// The share of a class of cells that the patrol rectangles of a role's staff cover, in whole percent (tp.exe <c>0x004CD500</c>, arguments role and class); null without a cell map.
	/// </summary>
	/// <param name="role">The staff role whose patrol rectangles mark cells (the things of that model with an area of their own).</param>
	/// <param name="cellClass">0: the land cells (not the types 2, 7 and 30), 1: the built cells (types 1, 3, 9, 10, 4 and 21 and the structure cells), 2: the entrance cells (type 9), 3: the path and queue cells (types 1, 3 and 9).</param>
	// [BIN:TPW-EXE:0x004CD340 patrol marks] every cell's mark word (+0x3C of its 0x44-byte record) is cleared; then each staff thing whose model equals the role and whose area is not the whole map (0x506EB0) marks the cells of its rectangle, columns from the first corner's x to the second's, rows likewise, inclusive, with 1; a rectangle whose first column is past the second marks nothing
	// [BIN:TPW-EXE:0x004CD500 coverage] a jump table over the class (0x4CD708): 0 counts the cells that none of 0x536300 (type 2), 0x536360 (7 or 30) and 0x5D60C0 (always 0) answers for, 1 the cells for which 0x536310 (1), 0x536320 (3 or 9), 0x536340 (9), 0x536350 (10), 0x5D60C0, 0x536380 (4), 0x5363F0 (the structure test) or 0x5363E0 (21) answers, 2 the cells of 0x536340, 3 those of 0x536310 or 0x536320; the result is marked cells x 100 / counted cells (a signed division, IMUL by 25 and SHL 2), 0 when nothing is counted
	internal int? PatrolCoveragePercent( StaffType role, int cellClass )
	{
		if ( Litter.Cells is not { } cells )
			return null;
		var areas = Staff.OfType( role ).Where( member => member.HasPatrolArea ).Select( member => member.PatrolArea ).ToArray();
		int counted = 0, marked = 0;
		for ( var y = 0; y < cells.Height; y++ )
		{
			for ( var x = 0; x < cells.Width; x++ )
			{
				var (type, structure) = CellForValue( cells, x, y );
				var inClass = cellClass switch
				{
					0 => type is not (2 or 7 or 30),
					1 => structure || type is 1 or 3 or 9 or 10 or 4 or 21,
					2 => type == 9,
					3 => type is 1 or 3 or 9,
					_ => false,
				};
				if ( !inClass )
					continue;
				counted++;
				if ( areas.Any( area => x >= area.X0 && x <= area.X1 && y >= area.Y0 && y <= area.Y1 ) )
					marked++;
			}
		}
		return counted == 0 ? 0 : unchecked( marked * 100 ) / counted;
	}

	/// <summary>
	/// The live excitement of a ride's object (tp.exe <c>0x004E0860</c> with the argument 0) by its economy instance id; the level answers it from the ride's bridge. Null (the default) leaves the advisor's
	/// ride-fear line at 0 as a park without a world has no rides to read.
	/// </summary>
	public Func<int, int>? RideLiveExcitement { get; set; }

	/// <summary>
	/// The first attraction (in the level's thing order) with the choosable and the queue flags whose queue joins no path (<c>0x004CCB00</c>), other than the one the player has selected; its economy
	/// instance id, 0 when there is none. Null (the default) means no world answers.
	/// </summary>
	public Func<int>? FirstUnconnectedQueueRide { get; set; }

	private bool excitementCached;
	private int excitementValue;
	private long excitementStamp;

	/// <summary>
	/// The mean live excitement of the rides as the analyser's cached reader sees it (tp.exe <c>0x004CCBF0</c>): the cached mean while (turn >> 2) - (stamp >> 2) is at most
	/// <paramref name="maximumAge"/> (unsigned), else the byte of <c>0x004E0860(0)</c> of every ride object (the types with <c>Info.WhichUIType</c> 0) added up and divided by their number, a signed division, 0 without a ride, stamped
	/// with the current turn. Null without a world that reads the rides' excitement. Not saved: the original's park save holds no such cache, a load starts it invalid.
	/// </summary>
	// [BIN:TPW-EXE:0x004CCBF0 average excitement] valid byte +0x1F0FC, value +0x1F0F8, stamp +0x1F0F4 (0x41A960 stores the turn, 0x41A990 answers (turn >> 2) - (stamp >> 2)); the walk over the things takes the attractions (type 3) whose type record +0x4AC (WhichUIType) is 0, counts them and adds the low byte of 0x4E0860(thing, 0)
	internal int? AverageRideExcitement( uint maximumAge )
	{
		if ( RideLiveExcitement is not { } excitement )
			return null;
		var age = unchecked( (uint)(Turn >> 2) - (uint)(excitementStamp >> 2) );
		if ( !excitementCached || maximumAge < age )
		{
			int count = 0, sum = 0;
			foreach ( var item in objects.Values.OrderBy( item => item.Id ) )
			{
				if ( !Catalog.TryGet( item.InfoId, out var info ) || info.RatingType != 0 )
					continue;
				count++;
				sum = unchecked( sum + (excitement( item.Id ) & 0xFF) );
			}
			excitementValue = count > 0 ? sum / count : 0;
			excitementStamp = Turn;
			excitementCached = true;
		}
		return excitementValue;
	}
}
