namespace OpenTPW;

/// <summary>Which letter a tag tube row was filed from: the text it carries is composed from the letter (see <see cref="ThemeParkIncTagTubeRow"/>).</summary>
public enum ThemeParkIncLetterSource
{
	/// <summary>The mission controller's "Objective Complete!" letter (<c>0x5CEAE0</c>, kind 4); the argument is the completed mission.</summary>
	Objective = 1,
	/// <summary>The zone arrival's first briefing (<c>0x5CD6E0(0x5C, 5, 1)</c>, a tag letter of tag 0x5C).</summary>
	Introduction = 2,
	/// <summary>A notification letter (<c>0x5CEE50</c>): the bank's six-months-in-the-red letter (id 8) or the challenge system's firing letter (id 7).</summary>
	Notification = 3,
	/// <summary>A staff member's wage demand (<c>0x5CEFB0</c>, kind 0xA or 0xB); the argument is the kind, the key (<c>+0xE14</c>) the member's thing and the go-to argument (<c>+0xE18</c>) the demand.</summary>
	WageDemand = 4,
	/// <summary>A training letter (<c>0x5CEFB0</c>, kind 0xC finished or 0xD suspended); the argument is the kind, the key the member's thing and the ability is kept in <see cref="ThemeParkIncTagTubeRow.Ability"/>.</summary>
	Training = 5,
	/// <summary>An advice's caption (<c>0x635D80</c>, branch 0x18): the argument is the tag, whose record gives the texts.</summary>
	Advice = 6
}

/// <summary>
/// One row of the tag tube: the tag system's <c>0xE2C</c>-byte record. The original files the letter's composed texts in the record (<c>+0x8</c> body, <c>+0x808</c> sender,
/// <c>+0xA06</c> addressee, <c>+0xC04</c> subject); a row here keeps the letter it was filed from (<see cref="Source"/>, <see cref="Argument"/>), whose texts the client composes
/// from the same string tables, and every numeric field of the record.
/// </summary>
public sealed class ThemeParkIncTagTubeRow
{
	/// <summary><c>+0x000</c>: the row's tag (the letter's tag, or 0x8A, 0x5E, 0x5F written by the row fill).</summary>
	public int Tag { get; internal set; }
	/// <summary><c>+0x004</c>: the record's kind word, which the row fill writes (0x97 for every ported letter).</summary>
	public int Kind { get; internal set; }
	/// <summary>The letter the row was filed from.</summary>
	public ThemeParkIncLetterSource Source { get; internal set; }
	/// <summary>The letter's argument: the completed mission of an objective letter, the tag of a tag letter, the notification id (7 or 8) of a notification letter.</summary>
	public int Argument { get; internal set; }
	/// <summary><c>+0xE02</c>: the sender's picture word (the mugshot shows it less one; 0 shows none).</summary>
	public int Picture { get; internal set; }
	/// <summary><c>+0xE04</c>: the expiry in units of four park turns; 0 never expires.</summary>
	public int ExpiryUnits { get; internal set; }
	/// <summary><c>+0xE08</c>: the park turn the row expires at (the clock plus <see cref="ExpiryUnits"/> × 4 when filed or loaded).</summary>
	public uint ExpiryTurn { get; internal set; }
	/// <summary><c>+0xE0C</c>: the row was read (or announced: the letters that open at once are filed read).</summary>
	public bool Read { get; internal set; }
	/// <summary><c>+0xE0E</c>: the unread category, the letter's <c>+0x10</c> word (0 for the objective and tag letters, 2 for the notification letters).</summary>
	public int Category { get; internal set; }
	/// <summary><c>+0xE10</c>: the go-to kind (0: none); the window's go-to button shows when it is set.</summary>
	public int GotoKind { get; internal set; }
	/// <summary><c>+0xE14</c>: the key word the duplicate test reads for go-to kind 1.</summary>
	public int Key { get; internal set; }
	/// <summary><c>+0xE18</c>: the go-to argument, which the duplicate test also reads.</summary>
	public int GotoArgument { get; internal set; }
	/// <summary><c>+0xE1C</c>: the insert serial (the global <c>0xAA0AF0</c>), which the delete matches.</summary>
	public uint Serial { get; internal set; }
	/// <summary><c>+0xE24</c>: the letter's date, <c>year &lt;&lt; 16 | (month - 1) &lt;&lt; 8 | day</c> (<c>0x518BC0</c>), the list's sort column.</summary>
	public int Date { get; internal set; }
	/// <summary>A training letter's ability (the letter's <c>+0x28</c>, which its body names); 0 for the other letters.</summary>
	public int Ability { get; internal set; }
	/// <summary><c>+0xE28</c>: flagged for deletion (deleted, expired or superseded); the list and the save skip it.</summary>
	public bool Flagged { get; internal set; }

	/// <summary>The date's day, month (1 to 12) and year, as <c>0x518C30</c> unpacks it (the month byte plus one).</summary>
	public (int Day, int Month, int Year) DateParts => (Date & 0xFF, ((Date >> 8) & 0xFF) + 1, Date >> 16);
}

/// <summary>A saved tag tube row: the fields the serializer <c>0x6355D0</c> writes and the load reads back.</summary>
public sealed record ThemeParkIncTagTubeRowSnapshot( int Tag, int Kind, ThemeParkIncLetterSource Source, int Argument, int Picture, int ExpiryUnits, bool Read, int Category, int GotoKind, int Key,
	int GotoArgument, int Date, [property: System.Text.Json.Serialization.JsonIgnore( Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault )] int Ability = 0 );

/// <summary>The tag tube in a park save: its unflagged rows, newest first (the <c>SGAT</c> chunk's <c>numtags</c> and <c>MailMessage</c> records).</summary>
public sealed record ThemeParkIncTagTubeSnapshot( ThemeParkIncTagTubeRowSnapshot[] Rows );

/// <summary>
/// Theme Park Inc's tag tube (docs/reverse/TPI-TAG-TUBE.md): the tag system thing's mail list. Every letter the message centre carries (type 0x19) becomes a row (<c>0x635D80</c>);
/// the letters of kinds 6 to 8 also open the letter window at once and are filed read. The rows keep the newest first, expire by their own timers, are deleted from the tube window,
/// and an unread counter per category raises the advisor's "unread mail" message at 3, 6, 12, ... unread letters.
/// </summary>
public sealed class ThemeParkIncTagTube
{
	/// <summary>The number of unread categories the counter keeps (<c>0x54F130</c> allocates four counts).</summary>
	public const int Categories = 4;

	/// <summary>The counter's first threshold (<c>0x54F13B</c>).</summary>
	public const int FirstThreshold = 3;

	/// <summary>The advisor messages <c>0x54F1A0</c> posts when the unread total reaches the threshold: the first (threshold 3), then every later one.</summary>
	public const int FirstUnreadAdvice = 0x198, UnreadAdvice = 0x15D;

	private readonly List<ThemeParkIncTagTubeRow> rows = new();
	private readonly int[] categoryUnread = new int[Categories];
	private uint nextSerial;

	/// <summary>The rows, newest first, including flagged rows the next pass unlinks (<c>this+0x818</c>).</summary>
	public IReadOnlyList<ThemeParkIncTagTubeRow> Rows => rows;

	/// <summary>The rows the tube window lists: the unflagged ones, newest first (<c>0x636AD0</c>).</summary>
	public IEnumerable<ThemeParkIncTagTubeRow> Listed => rows.Where( row => !row.Flagged );

	/// <summary><c>this+0x820</c>: the earliest pending expiry turn, 0 for none.</summary>
	public uint EarliestExpiry { get; private set; }

	/// <summary><c>this+0x825</c>: some row was flagged since the last pass.</summary>
	public bool UnlinkPending { get; private set; }

	/// <summary>The counter's total unread letters (<c>[0]</c>).</summary>
	public int Unread { get; private set; }

	/// <summary>The counter's threshold (<c>[1]</c>): 3, doubled each time the total reaches it.</summary>
	public int Threshold { get; private set; } = FirstThreshold;

	/// <summary>The counter's unread letters by category (<c>[2]</c>).</summary>
	public IReadOnlyList<int> CategoryUnread => categoryUnread;

	/// <summary>Bumped whenever the window's dirty byte (<c>0xA60188</c>) is set (<c>0x552CD0</c>): by an insert, a delete and the auto-delete check.</summary>
	public int Revision { get; private set; }

	/// <summary>The advisor messages the counter posted (<see cref="FirstUnreadAdvice"/>, <see cref="UnreadAdvice"/>), oldest first; the client takes them.</summary>
	internal List<int> AdviceRequests { get; } = new();

	/// <summary>
	/// Files <paramref name="row"/> (<c>0x636880</c>): no expiry flag, the expiry turn from <paramref name="turn"/>, the next serial; a load appends it, a game insert puts it at the head and
	/// flags an older unflagged row it supersedes. An unread row then counts (<c>0x54F1A0</c>, silent on a load).
	/// </summary>
	// [BIN:TPI-EXE:0x636880 tube insert] +0xE28 = 0; +0xE04 > 0: +0xE08 = clock (world +0x1EAB40) + E04 * 4 and this+0x820 = the smaller of it and a set +0x820; +0xE1C = 0xAA0AF0++, +0xE20 = 0;
	// an empty list takes the row as its head; a load (param 2) walks to the tail and links it there; a game insert looks for the first unflagged row whose +0 equals the new row's +4, whose
	// +0xE18 equals the new +0xE18 and, when the new +0xE10 is 1, whose +0xE14 equals the new +0xE14, puts the new row at the head and flags that row (+0xE28 = 1, this+0x825 = 1); either way
	// the window is made dirty (0x552CD0 on 0xA60188); +0xE0C == 0: 0x54F1A0(+0xE0E, param 2) on the counter this+0x81C
	internal void Insert( ThemeParkIncTagTubeRow row, uint turn, bool load )
	{
		row.Flagged = false;
		if ( row.ExpiryUnits > 0 )
		{
			row.ExpiryTurn = unchecked( turn + (uint)row.ExpiryUnits * 4 );
			if ( EarliestExpiry == 0 || row.ExpiryTurn < EarliestExpiry )
				EarliestExpiry = row.ExpiryTurn;
		}
		row.Serial = nextSerial++;
		if ( load )
			rows.Add( row );
		else
		{
			var superseded = rows.FirstOrDefault( existing => !existing.Flagged && existing.Tag == row.Kind && (row.GotoKind != 1 || row.Key == existing.Key)
				&& row.GotoArgument == existing.GotoArgument );
			rows.Insert( 0, row );
			if ( superseded != null )
			{
				superseded.Flagged = true;
				UnlinkPending = true;
			}
		}
		Revision++;
		if ( !row.Read )
			CountUnread( row.Category, silent: load );
	}

	/// <summary>
	/// The player's delete (<c>0x636A40</c>): the first unflagged row with <paramref name="row"/>'s tag and serial is flagged; the next pass unlinks it (and counts it read).
	/// </summary>
	// [BIN:TPI-EXE:0x636A40 tube delete] walks the list from the head for a row whose +0 and +0xE1C equal the argument's and whose +0xE28 is 0: +0xE28 = 1, this+0x825 = 1, window dirty; the counter
	// is not touched here
	internal void Delete( ThemeParkIncTagTubeRow row )
	{
		if ( rows.FirstOrDefault( existing => existing.Tag == row.Tag && existing.Serial == row.Serial && !existing.Flagged ) is not { } match )
			return;
		match.Flagged = true;
		UnlinkPending = true;
		Revision++;
	}

	/// <summary>
	/// The delete by tag and key (<c>0x636C90</c>): every unflagged row with tag <paramref name="tag"/> and key <paramref name="key"/> is flagged; the next pass unlinks them. The count flagged.
	/// </summary>
	// [BIN:TPI-EXE:0x636C90 tube delete by tag] walks the list: +0 == tag, the word +0xE14 == key and +0xE28 == 0: +0xE28 = 1, this+0x825 = 1; any flagged: the window dirty (0x552CD0)
	internal int DeleteTagged( int tag, ushort key ) => Flag( row => row.Tag == tag && (ushort)row.Key == key );

	/// <summary>The delete by key (<c>0x636B10</c>): every unflagged row whose key is <paramref name="key"/> (a removed thing) is flagged. The count flagged.</summary>
	// [BIN:TPI-EXE:0x636B10 tube delete by key] walks the list: the word +0xE14 == key and +0xE28 == 0: +0xE28 = 1, this+0x825 = 1; any flagged: the window dirty (0x552CD0)
	internal int DeleteKeyed( ushort key ) => Flag( row => (ushort)row.Key == key );

	private int Flag( Func<ThemeParkIncTagTubeRow, bool> match )
	{
		var count = 0;
		foreach ( var row in rows.Where( row => !row.Flagged && match( row ) ) )
		{
			row.Flagged = true;
			UnlinkPending = true;
			count++;
		}
		if ( count != 0 )
			Revision++;
		return count;
	}

	/// <summary>
	/// The tube window's read (<c>0x553480</c>, the part on the row): an unread row is marked read and its category counts down (<c>0x636820</c> → <c>0x54F320</c>). False when it was read.
	/// </summary>
	// [BIN:TPI-EXE:0x5534DD row read] +0xE0C = 1, then 0x64EA60 (the tag thing, unused) and 0x636820(+0xE0E): with this+0x81C set, 0x54F320 decrements the total and the category
	internal bool MarkRead( ThemeParkIncTagTubeRow row )
	{
		if ( row.Read )
			return false;
		row.Read = true;
		UncountUnread( row.Category );
		// the read redraws the list (0x8543D4, 0x854E0A) but does not make it dirty: the selection stays
		return true;
	}

	/// <summary>
	/// The tag thing's update (<c>0x635AB0</c>): flagged rows leave the list (an unread one counts down first), then, when the earliest expiry has come, every unflagged row whose expiry
	/// turn has come is flagged and the earliest pending expiry is recomputed from the rest.
	/// </summary>
	// [BIN:TPI-EXE:0x635AB0 tube pass] stage 1 frees the to-be-deleted list (memory only); stage 2 (this+0x825): clears it and walks the list: each row with +0xE28 has, when +0xE0C is 0, +0xE0C = 1
	// and 0x54F320(+0xE0E) (with this+0x81C set), and is unlinked onto the to-be-deleted list; stage 3 (this+0x820 set and <= the clock): this+0x820 = 0, then each unflagged row with +0xE08:
	// above the clock keeps it ("leaving a non-expired e-mail") and this+0x820 takes the smallest such +0xE08, else +0xE28 = 1 and this+0x825 = 1 ("flagged an expired e-mail"); then 0x552CD0
	// (window dirty), which only this stage calls
	internal void Update( uint turn )
	{
		if ( UnlinkPending )
		{
			UnlinkPending = false;
			foreach ( var row in rows.Where( row => row.Flagged ).ToList() )
			{
				if ( !row.Read )
				{
					row.Read = true;
					UncountUnread( row.Category );
				}
				rows.Remove( row );
			}
		}
		if ( EarliestExpiry == 0 || EarliestExpiry > turn )
			return;
		EarliestExpiry = 0;
		foreach ( var row in rows )
		{
			if ( row.Flagged || row.ExpiryTurn == 0 )
				continue;
			if ( turn < row.ExpiryTurn )
			{
				if ( EarliestExpiry == 0 || row.ExpiryTurn < EarliestExpiry )
					EarliestExpiry = row.ExpiryTurn;
			}
			else
			{
				row.Flagged = true;
				UnlinkPending = true;
			}
		}
		Revision++;
	}

	// [BIN:TPI-EXE:0x54F1A0 unread up] total and the category's count go up; when the total equals the threshold: not silent posts advisor message 0x198 (threshold 3) or 0x15D (0x71A930,
	// 0x718820 on the advisor thing 0x64EA00), and the threshold doubles either way. With the native HUD's mail panel (0xA6011C, a [EXT:tpi-text-hud] stand-in here) it then shows the total,
	// the picture of the highest category with unread mail and, not silent, plays sound 0x1E of the bank 0xAE0124 (0x652860)
	private void CountUnread( int category, bool silent )
	{
		Unread++;
		categoryUnread[CheckCategory( category )]++;
		if ( Unread != Threshold )
			return;
		if ( !silent )
			AdviceRequests.Add( Threshold == FirstThreshold ? FirstUnreadAdvice : UnreadAdvice );
		Threshold <<= 1;
	}

	// [BIN:TPI-EXE:0x54F320 unread down] total and the category's count go down; the threshold stays; the HUD panel shows the total
	private void UncountUnread( int category )
	{
		Unread--;
		categoryUnread[CheckCategory( category )]--;
	}

	// the counter's four counts are indexed by the category word without a bound (0x54F1B7); every ported letter writes 0 or 2
	private static int CheckCategory( int category ) => category is >= 0 and < Categories
		? category
		: throw new InvalidOperationException( $"Tag tube category {category} lies outside the counter's four counts (the original would write past them)." );

	/// <summary>The saved rows: the unflagged ones, newest first (<c>0x6355D0</c> with flag 1).</summary>
	// [BIN:TPI-EXE:0x6355D0 tube save] counts the rows with +0xE28 == 0 (numtags), then writes each of them (MailMessage, 0xE2C bytes) in list order
	public ThemeParkIncTagTubeSnapshot Capture() => new( Listed.Select( row => new ThemeParkIncTagTubeRowSnapshot( row.Tag, row.Kind, row.Source, row.Argument, row.Picture, row.ExpiryUnits,
		row.Read, row.Category, row.GotoKind, row.Key, row.GotoArgument, row.Date, row.Ability ) ).ToArray() );

	/// <summary>
	/// The load (<c>0x6355D0</c> with flag 0): a new empty tube and counter, then each saved row appended in file order through the insert (its expiry restarts from <paramref name="turn"/>,
	/// an unread one counts silently).
	/// </summary>
	// [BIN:TPI-EXE:0x6355D0 tube load] this+0x818 = 0, this+0x81C = 0x54F130 (total 0, threshold 3, counts 0), this+0x820/+0x824/+0x825/+0x828 = 0; then numtags records, each through 0x636880(row, 1)
	public void Restore( ThemeParkIncTagTubeSnapshot snapshot, uint turn )
	{
		rows.Clear();
		Array.Clear( categoryUnread );
		Unread = 0;
		Threshold = FirstThreshold;
		EarliestExpiry = 0;
		UnlinkPending = false;
		AdviceRequests.Clear();
		foreach ( var saved in snapshot.Rows )
			Insert( new ThemeParkIncTagTubeRow
			{
				Tag = saved.Tag,
				Kind = saved.Kind,
				Source = saved.Source,
				Argument = saved.Argument,
				Picture = saved.Picture,
				ExpiryUnits = saved.ExpiryUnits,
				Read = saved.Read,
				Category = saved.Category,
				GotoKind = saved.GotoKind,
				Key = saved.Key,
				GotoArgument = saved.GotoArgument,
				Date = saved.Date,
				Ability = saved.Ability
			}, turn, load: true );
	}

	/// <summary>Whether a saved tube holds rows the ported letters can file.</summary>
	public static bool IsValid( ThemeParkIncTagTubeSnapshot? snapshot ) => snapshot is { Rows: { } saved } && saved.All( row => row != null && Enum.IsDefined( row.Source )
		&& row.Category is >= 0 and < Categories && row.ExpiryUnits is >= 0 and <= ushort.MaxValue && row.Picture is >= 0 and <= short.MaxValue && row.Date >= 0
		&& row.Source switch
		{
			ThemeParkIncLetterSource.Objective => row.Argument >= 0,
			ThemeParkIncLetterSource.Introduction => row.Argument == ThemeParkIncTagLetter.IntroductionTag,
			ThemeParkIncLetterSource.Training => row.Argument is ThemeParkIncTagLetter.TrainingFinished or ThemeParkIncTagLetter.TrainingSuspended
				&& row.Tag == (row.Argument == ThemeParkIncTagLetter.TrainingFinished ? ThemeParkIncTagLetter.TrainingFinishedTag : ThemeParkIncTagLetter.TrainingSuspendedTag)
				&& row.GotoKind == ThemeParkIncTagLetter.TrainingGoto && row.Key is > 0 and <= ushort.MaxValue && row.Ability is >= 0 and < ThemeParkIncCareer.AbilityCount,
			ThemeParkIncLetterSource.Advice => ThemeParkIncAdviceTag.For( row.Argument ) is { } tag && row.Tag == tag.Tag && row.Kind == tag.Kind && row.GotoKind == tag.GotoKind
				&& row.Picture == ThemeParkIncAdviceTag.SenderPicture && row.Key is >= 0 and <= ushort.MaxValue,
			ThemeParkIncLetterSource.WageDemand => row.Argument is ThemeParkIncTagLetter.WageDemandWithStrike or ThemeParkIncTagLetter.WageDemandWithoutStrike
				&& row.Tag == ThemeParkIncTagLetter.WageDemandTag && row.GotoKind == ThemeParkIncTagLetter.WageDemandGoto && row.Key is > 0 and <= ushort.MaxValue,
			_ => row.Argument is ThemeParkIncTagLetter.RedSixMonthsId or ThemeParkIncTagLetter.ChallengeLostId
		} );
}
