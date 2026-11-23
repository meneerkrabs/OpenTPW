using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// <c>levels/Standard.sam</c>'s <c>ShareInfo</c> words the shares game reads (the Standard object's <c>+0x708</c>, <c>+0x70C</c> and <c>+0x710</c>: <c>0xA02168</c>, <c>0xA0216C</c>,
/// <c>0xA02170</c>); a key the files lack reads 0, as the reset object's word does.
/// </summary>
// [BIN:TPI-EXE:0x996684 Standard schema] "4,CompanySharesIssued;4,InitialSharesOwned;4,InitialSharePrice;4,AnnRptSimilarPercentage;4,AnnRptBigChangePercentage;1,ShareInfo" after the loans: the
// readers 0x620930 (0xA0216C, 0xA02168, 0xA02170), 0x621CE0 and 0x5EFFBA (0xA02168) fix the first three words at 0xA02168..0xA02170
public readonly record struct ThemeParkIncShareSettings( int CompanySharesIssued, int InitialSharesOwned, int InitialSharePrice )
{
	public static ThemeParkIncShareSettings Read( SamSettings standard )
	{
		ArgumentNullException.ThrowIfNull( standard );
		return new ThemeParkIncShareSettings( standard.GetInt( "ShareInfo.CompanySharesIssued", 0, optional: true ), standard.GetInt( "ShareInfo.InitialSharesOwned", 0, optional: true ),
			standard.GetInt( "ShareInfo.InitialSharePrice", 0, optional: true ) );
	}
}

/// <summary>
/// Theme Park Inc's shares game (<c>CSharesGame</c>, the global object <c>0xAA0128</c>; docs/reverse/TPI-AWARDS-SHARES.md): the player's holding (shares owned and the percentage of the company), the
/// company (its share price and name) and the three annual-report slots, kept as the 182 bytes the Global Master Save holds (<c>0x620A30</c>). There is no rival holder and no buying or selling: the
/// holding grows only by a challenge's share prize and the debug cheats, the price moves by a challenge's win and first loss and by the annual report.
/// </summary>
// [BIN:TPI-EXE:0x620870 shares constructor] [0xAA0128] = a 0x48-byte owner record (+0 mNumberOwned, +4 mPctCompanySharesOwned, +8 a name copied from 0xA0B714, ""), [0xAA012C] = a 0x44-byte company
// record (+0 mCurrentValue, +4 mCompanyName[31]), both -1; the three annual-report slot pointers +8, +0xC and +0x10 null
public sealed class ThemeParkIncSharesGame
{
	/// <summary>The bytes of the Global Master Save's shares section (<c>ThemeParkIncGlobalSave.SharesBytes</c>).</summary>
	public const int Bytes = ThemeParkIncGlobalSave.SharesBytes;
	private const int OwnedAt = 108, PercentAt = 112, NameAt = 116, PriceAt = 178;
	private const int NameCharacters = 31;

	private readonly byte[] raw = new byte[Bytes];

	/// <summary>A shares game as the reset leaves it (<see cref="Reset"/>).</summary>
	public ThemeParkIncSharesGame() => Reset();

	/// <summary><c>mNumberOwned</c> (owner record <c>+0</c>): the shares the player holds; -1 before the start-up gave any.</summary>
	public int Owned
	{
		get => BinaryPrimitives.ReadInt32LittleEndian( raw.AsSpan( OwnedAt ) );
		private set => BinaryPrimitives.WriteInt32LittleEndian( raw.AsSpan( OwnedAt ), value );
	}

	/// <summary><c>mPctCompanySharesOwned</c> (owner record <c>+4</c>): the percentage the last change of the holding computed.</summary>
	public int PercentOwned
	{
		get => BinaryPrimitives.ReadInt32LittleEndian( raw.AsSpan( PercentAt ) );
		private set => BinaryPrimitives.WriteInt32LittleEndian( raw.AsSpan( PercentAt ), value );
	}

	/// <summary><c>mCurrentValue</c> (company record <c>+0</c>): the share price; -1 before the start-up gave one.</summary>
	public int Price
	{
		get => BinaryPrimitives.ReadInt32LittleEndian( raw.AsSpan( PriceAt ) );
		private set => BinaryPrimitives.WriteInt32LittleEndian( raw.AsSpan( PriceAt ), value );
	}

	/// <summary>Whether annual-report slot <paramref name="theme"/> (0 to 2) holds a report (its saved present word is 1).</summary>
	public bool HasAnnualReport( int theme ) => theme is >= 0 and < 3 && BinaryPrimitives.ReadInt32LittleEndian( raw.AsSpan( theme * 36 ) ) == 1;

	/// <summary>
	/// The company name (<c>0x622450</c>): the company record's text, or <paramref name="defaultName"/> (the language's <c>TAG_SYSTEM.str</c> entry 0xAC, at most 30 characters) while it is empty.
	/// </summary>
	// [BIN:TPI-EXE:0x622450 company name] an empty mCompanyName (wcslen 0x86FEE7) is first formatted from TAG_SYSTEM entry 0xAC (0x8934A0 with the limit 0x1E), then returned
	public string CompanyName( string defaultName )
	{
		ArgumentNullException.ThrowIfNull( defaultName );
		var name = StoredName();
		return name.Length > 0 ? name : defaultName.Length > 0x1E ? defaultName[..0x1E] : defaultName;
	}

	/// <summary>The holding's value (<c>0x6220D0</c>): shares owned × price, or -1 while either is -1 (32-bit).</summary>
	// [BIN:TPI-EXE:0x6220D0 holding value] owned == -1 answers -1; [0xAA012C]'s price == -1 answers -1; else imul owned × price
	public int HoldingValue => Owned == -1 ? -1 : Price == -1 ? -1 : unchecked( Owned * Price );

	/// <summary>
	/// The reset (<c>0x621210</c>, from the career reset <c>0x74BB40</c>): no annual report, the holding, the percentage and the price -1, the name empty.
	/// </summary>
	// [BIN:TPI-EXE:0x621210 shares reset] the owner record (-1, -1, L"" from 0xA0B714) and the company record (-1, L"") are rebuilt and the three slot pointers cleared
	// [EXT:original-undefined-reads] the name's characters after its terminator are uninitialised heap in the original: 0 here
	public void Reset()
	{
		Array.Clear( raw );
		Owned = -1;
		PercentOwned = -1;
		Price = -1;
	}

	/// <summary>
	/// The start-up (<c>0x620930</c>, run by every world build at <c>0x648676</c>; the load's <c>0x622060</c> and <c>0x6223D0</c> are the same steps): a holding of -1 takes
	/// <c>InitialSharesOwned</c> when that is above 0; a holding above 0 recomputes the percentage when <c>CompanySharesIssued</c> is above 0; an empty name takes <paramref name="defaultName"/> (the
	/// language's <c>TAG_SYSTEM.str</c> entry 0xAC, at most 30 characters); a price of -1 takes <c>InitialSharePrice</c> when that is above 0.
	/// </summary>
	// [BIN:TPI-EXE:0x620930 shares init] logs "### Shares Game Init! ###"; owner +0 == -1 and [0xA0216C] > 0: +0 = it; +0 > 0 and [0xA02168] > 0 (signed): +4 = +0 × 100 / [0xA02168] (cltd, idivl);
	// an empty company name is formatted from TAG_SYSTEM entry 0xAC (0x620987: 0x8934A0, limit 0x1E); company +0 == -1 and [0xA02170] > 0: +0 = it
	// [BIN:TPI-EXE:0x622060 load percentage] [BIN:TPI-EXE:0x6223D0 load company] the load 0x620E30 ends with the same three steps
	public void Initialize( ThemeParkIncShareSettings settings, string defaultName = "" )
	{
		ArgumentNullException.ThrowIfNull( defaultName );
		if ( Owned == -1 && settings.InitialSharesOwned > 0 )
			Owned = settings.InitialSharesOwned;
		if ( Owned > 0 && settings.CompanySharesIssued > 0 )
			PercentOwned = unchecked( Owned * 100 ) / settings.CompanySharesIssued;
		FormatName( defaultName );
		if ( Price == -1 && settings.InitialSharePrice > 0 )
			Price = settings.InitialSharePrice;
	}

	/// <summary>
	/// Shares bought for nothing (<c>0x621CE0</c>): a challenge's <c>SharePrize</c> and the debug cheats. Nothing for <paramref name="shares"/> of 0 or less; else the holding grows without a limit
	/// and the percentage is recomputed over <c>CompanySharesIssued</c>.
	/// </summary>
	// [BIN:TPI-EXE:0x621CE0 add shares] n > 0 (signed): owner +0 += n, +4 = +0 × 100 / [0xA02168] (cltd, idivl, 32-bit products); then the panel's dirty flag 0x582780 (presentation)
	public void AddShares( int shares, ThemeParkIncShareSettings settings )
	{
		if ( shares <= 0 )
			return;
		if ( settings.CompanySharesIssued == 0 )
			throw new InvalidOperationException( "ShareInfo.CompanySharesIssued is 0: the original's share count (0x621CE0) divides by it and faults." );
		Owned = unchecked( Owned + shares );
		PercentOwned = unchecked( Owned * 100 ) / settings.CompanySharesIssued;
	}

	/// <summary>
	/// The share price moves by <paramref name="percent"/> (<c>0x621D30</c>): a challenge's <c>SharePriceEffectIfWon</c>, the negated <c>SharePriceEffectIfLost</c> and the cheat's 5. Nothing for a price
	/// or an effect of 0; else the price gains <c>price × percent / 100</c> (32-bit, the division toward zero) and keeps the result only when it is above 0.
	/// </summary>
	// [BIN:TPI-EXE:0x621D30 price effect] price = [[0xAA012C]]; price == 0 or n == 0: return; imul price × n (32 bits), × 0x51EB851F, sar 5 plus the sign bit (÷ 100 toward zero), added to the price;
	// stored only when above 0 (signed jle); then the panel's dirty flag 0x582780 (presentation)
	public void MovePrice( int percent )
	{
		var price = Price;
		if ( price == 0 || percent == 0 )
			return;
		var moved = unchecked( price + unchecked( price * percent ) / 100 );
		if ( moved > 0 )
			Price = moved;
	}

	/// <summary>
	/// The annual report's score (<c>0x621F50</c>): the sum of the signs of the five changes against the previous report (money in, the record's <c>+8</c> word, the park value, <c>+0xC</c> and
	/// <c>+0x10</c>), at most 3 either way.
	/// </summary>
	// [BIN:TPI-EXE:0x621F50 report score] the delta record (slot +0x28): +0x18 (double) > 0 adds 1, < 0 (or unordered) takes 1; +8 (int) > 0 / < 0; +0x20 (double); +0xC (int); +0x10 (int); clamped to 3
	// above and -3 below
	public static int AnnualScore( double moneyIn, int word8, double parkValue, int wordC, int word10 )
	{
		static int Sign( double value ) => value > 0 ? 1 : value == 0 ? 0 : -1;
		var score = Sign( moneyIn ) + Math.Sign( word8 ) + Sign( parkValue ) + Math.Sign( wordC ) + Math.Sign( word10 );
		return score > 3 ? 3 : score < -3 ? -3 : score;
	}

	/// <summary>The annual report's price step (<c>0x621300</c> after the score): a nonzero price moves by the score as a percentage when the score is not 0 (<see cref="MovePrice"/>'s rule).</summary>
	// [BIN:TPI-EXE:0x62191F report price] after the score (0x62191A -> 0x621F50): price = [[+4]]; price != 0 and score != 0: price + price × score / 100 (idiv), stored when above 0; then 0x582780
	public void ApplyAnnualScore( int score ) => MovePrice( score );

	/// <summary>The 182 bytes the Global Master Save writes (<c>0x620A30</c>): an empty name first takes <paramref name="defaultName"/>, as the writer formats it into the company record.</summary>
	// [BIN:TPI-EXE:0x620A30 shares write] per slot 0..2: the present word (1 or 0), then year +4, +8, +0xC, +0x10 and the doubles +0x18, +0x20 (36 zero bytes for a null slot); then +0 and +4 of the owner,
	// mCompanyName[31] (an empty one first formatted from TAG_SYSTEM 0xAC, 0x620CEF..0x620D0A) and the price
	public byte[] ToBytes( string defaultName )
	{
		ArgumentNullException.ThrowIfNull( defaultName );
		FormatName( defaultName );
		return (byte[])raw.Clone();
	}

	// [BIN:TPI-EXE:0x620987 name format] an empty mCompanyName (wcslen 0x86FEE7 == 0) is formatted in place from TAG_SYSTEM entry 0xAC with the limit 0x1E (0x8934A0); the writer does the same at 0x620CEF
	private void FormatName( string defaultName )
	{
		if ( StoredName().Length != 0 || defaultName.Length == 0 )
			return;
		var name = defaultName.Length > 0x1E ? defaultName[..0x1E] : defaultName;
		for ( var index = 0; index < name.Length; index++ )
			BinaryPrimitives.WriteUInt16LittleEndian( raw.AsSpan( NameAt + 2 * index ), name[index] );
	}

	/// <summary>Takes the 182 bytes of a Global Master Save or a park save (<c>0x620E30</c> reads them field by field); the start-up's steps (<see cref="Initialize"/>) follow when the park is built.</summary>
	// [BIN:TPI-EXE:0x620E30 shares load] each present slot is rebuilt (two 0x30 records: the report and its empty delta), then the owner's two words, the name and the price; 0x622060 and 0x6223D0 follow
	public void Load( byte[] bytes )
	{
		ArgumentNullException.ThrowIfNull( bytes );
		if ( bytes.Length != Bytes )
			throw new InvalidDataException( $"The shares section holds {bytes.Length} bytes, not {Bytes}." );
		bytes.CopyTo( raw, 0 );
	}

	/// <summary>The 182 bytes as they are now (the park save's copy; the name is not formatted).</summary>
	public byte[] Capture() => (byte[])raw.Clone();

	private string StoredName()
	{
		var characters = new char[NameCharacters];
		for ( var index = 0; index < NameCharacters; index++ )
			characters[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian( raw.AsSpan( NameAt + 2 * index ) );
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? NameCharacters : end );
	}
}
