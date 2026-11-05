namespace OpenTPW;

/// <summary>
/// The park statistics the advisor's last nine scan callbacks read (the guards' chase counters, the patrol coverage, the mean ride excitement and the first queue that joins no path),
/// kept or computed from the economy without writing anything the simulation reads: the counters are presentation words of the analyser, the rest are pure reads.
/// </summary>
public sealed partial class ParkEconomy
{
	private int misbehavingMonth, misbehavingGotAway, misbehavingEver;

	/// <summary>
	/// A guard's chase ends (tp.exe <c>0x004C74F0</c>, called from <c>0x004D691B</c> with a nonzero argument when the guard catches the kid and from <c>0x004D6A1F</c> with 0 when he gives the chase up):
	/// both raise the month word (+0x21BFC) and the lifetime word (+0x21C04); only the abandoned chase raises the got-away word (+0x21C00).
	/// </summary>
	// [BIN:TPW-EXE:0x004C74F0 misbehaved] +0x21BFC and +0x21C04 are raised, +0x21C00 only when the argument is 0 (TEST EAX, EAX; JNE over the INC); nothing else in tp.exe writes them but the constructor (0x4C5BBE-0x4C5BCA), the month close and the save load
	// [BIN:TPW-EXE:0x004C94A0 misbehaving kids] only the advisor reads the three words (the getter 0x4C94A0 and the serializer 0x4C5D70): the simulation never does, so they are presentation state and stay out of the run hash, like the analyser's caches
	internal void RecordGuardChase( bool caught )
	{
		misbehavingMonth = unchecked( misbehavingMonth + 1 );
		misbehavingEver = unchecked( misbehavingEver + 1 );
		if ( !caught )
			misbehavingGotAway = unchecked( misbehavingGotAway + 1 );
	}

	/// <summary>The analyser's misbehaving-kids words (<c>0x004C94A0</c>): the month word for 0, the got-away word for a nonzero argument.</summary>
	// [BIN:TPW-EXE:0x004C94A0 misbehaving kids] a nonzero argument returns +0x21C00 (the kids that got away this month), 0 returns +0x21BFC (the kids a guard went after this month)
	internal int MisbehavingKids( bool gotAway ) => gotAway ? misbehavingGotAway : misbehavingMonth;

	/// <summary>The lifetime word (<c>+0x21C04</c>, <c>mMisbehavingKidsEver</c>): every chase a guard began to end, caught or given up.</summary>
	internal int MisbehavingKidsEver => misbehavingEver;

	/// <summary>Restores the three words as a save holds them (the month word, the got-away word, the lifetime word).</summary>
	internal void RestoreMisbehavingKids( int month, int gotAway, int ever ) => (misbehavingMonth, misbehavingGotAway, misbehavingEver) = (month, gotAway, ever);

	/// <summary>
	/// The month close zeroes the month word and the got-away word, not the lifetime word.
	/// </summary>
	// [BIN:TPW-EXE:0x004C79EB month close] the month handler 0x004C7720 ends by storing 0 in +0x21BFC and +0x21C00
	private void CloseMisbehavingMonth() => (misbehavingMonth, misbehavingGotAway) = (0, 0);

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
				for ( var index = 0; index < areas.Length; index++ )
				{
					var area = areas[index];
					if ( x >= area.X0 && x <= area.X1 && y >= area.Y0 && y <= area.Y1 )
					{
						marked++;
						break;
					}
				}
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

	/// <summary>
	/// The original thing id (the word the advisor's records and the tube's locate button carry) of the world object with this economy instance id; 0 for an object the world does not hold.
	/// Null (the default) means no world answers: the advisor's words stay 0, as for a park without a level. The economy's ids are not the thing ids (a ride's thing id is its pool slot).
	/// </summary>
	public Func<int, int>? ObjectThingId { get; set; }

	/// <summary>
	/// The cell word (<c>y × 128 + x + 1</c>) of the level's second bus stop (<c>FixedItemInfo.BusStopB</c>, tp.exe <c>0x004D8650</c> with the flag 1), null when the level names none or no world answers.
	/// </summary>
	public Func<int?>? ArrivalBusStopCell { get; set; }

	/// <summary>The thing id of an economy instance as the world names it (0 when no world answers or it holds no such object).</summary>
	internal ushort ThingOf( int instance ) => instance != 0 && ObjectThingId is { } probe ? unchecked( (ushort)probe( instance ) ) : (ushort)0;

	/// <summary>
	/// The dirtiest toilet (tp.exe <c>0x004CA220</c>): the walk over the things newest first takes the attractions with the relief flag and keeps the one whose filth, 100 minus the condition byte, is strictly the greatest, so the
	/// first met of equals; null without a toilet.
	/// </summary>
	// [BIN:TPW-EXE:0x004CA220 dirtiest toilet] the things from the head of the thing list (0x516130, 0x516160: the newest); type 3 with the flag +0x32 bit 0; filth = 100 minus the byte of __ftol(+0x44); the first qualifying thing is taken, then only a filth above the pick's (cmp eax, edi; jle skips)
	internal ParkObjectState? DirtiestToilet()
	{
		ParkObjectState? pick = null;
		var filth = 0;
		foreach ( var item in Toilets() )
		{
			var value = 100 - StaffVitals.Byte( (float)item.Repair );
			if ( pick == null || value > filth )
				(pick, filth) = (item, value);
		}
		return pick;
	}

	/// <summary>
	/// The scariest ride (tp.exe <c>0x004CCD10</c>): the attractions whose type has <c>WhichUIType</c> 0, newest first, by the low byte of their live excitement, strictly greater replacing and the first taken whatever its value;
	/// 0 without a ride or without a world that reads the excitement.
	/// </summary>
	// [BIN:TPW-EXE:0x004CCD10 scariest ride] the walk takes the first type 3 thing whose type record's +0x4AC is 0 (a handle of 0 is "none yet", so it is taken even at excitement 0), then each later one only above the pick's byte of 0x4E0860(thing, 0) (cmp eax, ebp; jle skips); the handle goes to the caller's word (the scan object's +4)
	internal int ScariestRide()
	{
		if ( RideLiveExcitement is not { } excitement )
			return 0;
		var pick = 0;
		var best = 0;
		foreach ( var item in objects.Values.OrderByDescending( item => item.Id ) )
		{
			if ( !Catalog.TryGet( item.InfoId, out var info ) || info.RatingType != 0 )
				continue;
			var value = excitement( item.Id ) & 0xFF;
			if ( pick == 0 || value > best )
				(pick, best) = (item.Id, value);
		}
		return pick;
	}

	/// <summary>The excitement cache (the park save), null until a read has counted it.</summary>
	internal (int Value, long Stamp)? CachedExcitement => excitementCached ? (excitementValue, excitementStamp) : null;

	/// <summary>Restores a saved excitement cache (<c>0x4C6243</c>, <c>0x4C6261</c>).</summary>
	internal void RestoreExcitementCache( int value, long stamp ) => (excitementValue, excitementStamp, excitementCached) = (value, stamp, true);

	private bool excitementCached;
	private int excitementValue;
	private long excitementStamp;

	/// <summary>
	/// The mean live excitement of the rides as the analyser's cached reader sees it (tp.exe <c>0x004CCBF0</c>): the cached mean while the quarter-turn age (turn >> 2) - (stamp >> 2) is at most
	/// <paramref name="maximumAge"/> (unsigned, 40 to 43 turns for 10), else the byte of <c>0x004E0860(0)</c> of every ride object (the types with <c>Info.WhichUIType</c> 0) added up and divided by their number, a signed division, 0 without a ride, stamped
	/// with the current turn. Null without a world that reads the rides' excitement. Saved with the analyser's caches (the original's serializer keeps the value, the valid byte and the stamp: 0x4C6243, 0x4C6261, 0x4C6E62), so a resumed park reads the same cached mean.
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
			foreach ( var item in objects.Values )
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
