using Veldrid;

namespace OpenTPW.UI.Original;

/// <summary>
/// Original string tables of the selected language (UITEXT, UIHELPTEXT, THEMENAMES, OBJECT_NAMES)
/// plus the OpenTPW supplementary labels.
/// </summary>
public sealed class UiStringTable
{
	private readonly string[] uiText;
	private readonly string[] helpText;
	private readonly string[] themeNames;
	private readonly string[] objectNames;
	private readonly string[] lenderNames;
	private readonly string[] ingredientNames;
	private readonly StringTemplate[] uiTemplates;
	private readonly StringTemplate[] helpTemplates;
	private readonly StringFile? kidStates;
	private readonly string[] thoughtNames;

	public UiStringTable( string language, string[] uiText, string[] helpText, string[] themeNames, string[] objectNames, string[]? lenderNames = null,
		StringTemplate[]? uiTemplates = null, string[]? ingredientNames = null, StringFile? kidStates = null, string[]? thoughtNames = null,
		StringTemplate[]? helpTemplates = null )
	{
		this.helpTemplates = helpTemplates ?? Array.Empty<StringTemplate>();
		this.kidStates = kidStates;
		this.thoughtNames = thoughtNames ?? Array.Empty<string>();
		Language = language;
		this.uiText = uiText;
		this.helpText = helpText;
		this.themeNames = themeNames;
		this.objectNames = objectNames;
		this.lenderNames = lenderNames ?? Array.Empty<string>();
		this.uiTemplates = uiTemplates ?? Array.Empty<StringTemplate>();
		this.ingredientNames = ingredientNames ?? Array.Empty<string>();
	}

	public static UiStringTable Load( GameLanguage language )
	{
		var uiText = language.LoadStrings( "UITEXT.str" );
		var helpText = language.LoadStrings( "UIHELPTEXT.str" );
		return new( language.Name, uiText.Entries, helpText.Entries,
			language.LoadStrings( "THEMENAMES.str" ).Entries, language.LoadStrings( "OBJECT_NAMES.str" ).Entries,
			language.LoadStrings( "LOANNAMES.str" ).Entries, uiText.Templates, OptionalStrings( language, "INGREDIENT.str" ),
			OptionalFile( language, "KIDSTATES.str" ), OptionalStrings( language, "THOUGHTS.str" ), helpText.Templates );
	}

	private static StringFile? OptionalFile( GameLanguage language, string name )
	{
		try { return language.LoadStrings( name ); }
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException ) { return null; }
	}

	private static string[]? OptionalStrings( GameLanguage language, string name )
	{
		try { return language.LoadStrings( name ).Entries; }
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException ) { return null; }
	}

	public string Language { get; }
	public string this[UIStrings id] => Get( uiText, (int)id );
	public string Help( int index ) => Get( helpText, index );
	/// <summary>
	/// UIHELPTEXT record <paramref name="index"/> with its parameter slots filled by <paramref name="values"/> (e.g. 580, "&lt;ride&gt;: Left-click to ride"); a table
	/// loaded without the records' parameter chunks appends the values to the entry's text.
	/// </summary>
	public string HelpFormat( int index, params string[] values ) => index >= 0 && index < helpTemplates.Length
		? helpTemplates[index].Fill( values ) : Get( helpText, index ) + string.Concat( values );
	/// <summary>UITEXT record <paramref name="index"/> by the file's own numbering (Theme Park Inc's table has 994 records with ids unlike Theme Park World's, so no <see cref="UIStrings"/> name applies); empty when out of range.</summary>
	public string Entry( int index ) => Get( uiText, index );
	public string Theme( int index ) => Get( themeNames, index );
	public string Object( int index ) => Get( objectNames, index );
	public string Lender( int index ) => Get( lenderNames, index );
	/// <summary>INGREDIENT.str: "Amount of fat", "Amount of salt", "Amount of ice", "Amount of sugar" for <c>UsageInfo.SpecialIngredient</c> 1..4 at index 0..3.</summary>
	public string Ingredient( int index ) => Get( ingredientNames, index );
	/// <summary>THOUGHTS.str: the text of a visitor's thought, indexed by the thought id (<see cref="GuestThought"/>); empty for none.</summary>
	public string Thought( int index ) => Get( thoughtNames, index );

	/// <summary>
	/// KIDSTATES.str: the status text of a visitor in the state <paramref name="state"/> (the state word +0x220); the "Using: " record (16) takes the name of the
	/// attraction the visitor uses as its parameter.
	/// </summary>
	public string KidState( int state, string using_ = "" ) => kidStates == null || state < 0 || state >= kidStates.Entries.Length ? ""
		: kidStates.Templates.Length > state ? kidStates.Templates[state].Fill( using_ ) : kidStates.Entries[state];

	public string Extra( OpenTpwText key ) => SupplementaryStrings.Get( key, Language );

	/// <summary>
	/// A UITEXT record with its parameter slots filled by <paramref name="values"/> (the original's formatter); a table loaded without the
	/// records' parameter chunks appends the values to the entry's text.
	/// </summary>
	public string Format( UIStrings id, params string[] values ) => Format( (int)id, values );

	/// <summary>UITEXT record <paramref name="index"/> filled by <paramref name="values"/>; also the records whose first text run is empty (<see cref="UiTextRecord"/>).</summary>
	public string Format( int index, params string[] values ) => index >= 0 && index < uiTemplates.Length
		? uiTemplates[index].Fill( values ) : Get( uiText, index ) + string.Concat( values );

	/// <summary>
	/// UITEXT record <paramref name="index"/> with each parameter slot filled by the value for its <em>type</em> (the original's formatter matches the caller's typed 'MRAV' arguments to the record's
	/// parameter chunks by type, 0x6acd60), so a language that orders the slots differently still gets each number in its place; a slot with no value for its type fills nothing.
	/// </summary>
	public string FormatByType( int index, IReadOnlyDictionary<int, string> valuesByType )
	{
		if ( index < 0 || index >= uiTemplates.Length )
			return Get( uiText, index ) + string.Concat( valuesByType.Values );
		var builder = new System.Text.StringBuilder();
		foreach ( var part in uiTemplates[index].Parts )
		{
			if ( !part.IsParameter )
				builder.Append( part.Text );
			else if ( valuesByType.TryGetValue( part.ParameterType, out var value ) )
				builder.Append( value );
		}
		return builder.ToString();
	}

	/// <summary>A UITEXT entry without the leading space the original uses for option values.</summary>
	public string Value( UIStrings id ) => this[id].Trim();

	private static string Get( string[] table, int index ) => index >= 0 && index < table.Length ? table[index] : "";
}

/// <summary>
/// UITEXT records that start with a parameter, so their first text run is empty and the <see cref="UIStrings"/> enum (whose every entry
/// has text) does not name them; read them with <see cref="UiStringTable.Format(int, string[])"/>. The values are OpenTPW's UITEXT
/// indices (tp.exe's ids above 207 are one more, the 474-entry edition).
/// </summary>
public static class UiTextRecord
{
	/// <summary>"%d hours" (tp.exe 0x1b9).</summary>
	public const int Hours = 440;
	/// <summary>"%d day" (tp.exe 0x1ba).</summary>
	public const int Day = 441;
	/// <summary>"%d days" (tp.exe 0x1bb).</summary>
	public const int Days = 442;
	/// <summary>
	/// The save date record "%d.%d.%d" (tp.exe 0x1c0): its parameters 16, 17 and 18 take the day, month and year of the save file's local
	/// last-write time (0x49ec80 fills the tags 0x10, 0x11 and 0x12 from the SYSTEMTIME's day, month and year, 0x5fc660).
	/// </summary>
	public const int SaveDate = 447;
	/// <summary>The golden-ticket price "%d gt" (tp.exe 0x1c7; parameter 23): the buy list's price cell of a row that costs tickets (0x490650).</summary>
	public const int GoldenTicketPrice = 454;
}

/// <summary>
/// Font size tier. The original ships three sizes of its menu fonts (MENU/TITLE/SESH/CASH
/// SMALL, MED and BIG) and picks a font bank by screen mode (tp.exe 0x419710, UI-002); OpenTPW's
/// pick by output size does not follow that rule.
/// </summary>
public enum UiFontTier { Small, Medium, Big }

/// <summary>The original BF4 fonts used by the screens for one size tier (docs/UI.md).</summary>
public sealed class UiFonts
{
	private readonly Func<string, FontAtlas> load;

	public UiFonts( Func<string, FontAtlas> load, UiFontTier tier = UiFontTier.Medium )
	{
		this.load = load;
		Tier = tier;
		// [EXT:ui-font-tier-by-scale] which shipped font serves which role per tier is OpenTPW's choice: the original's controls name a bank slot (0x485a70: menu lines 0, HUD cash 1, HUD date 3, window labels 6, list rows 7) whose fonts differ per bank (docs/UI.md), and OpenTPW has no mode-to-bank rule at its output sizes
		var (title, menu, label, small, cash, heading) = tier switch
		{
			UiFontTier.Small => ("TITLESMALL", "MENUSMALL", "GAME10AA", "GAME8AA", "GAMEBOLD10", "SESHSMALL"),
			UiFontTier.Big => ("TITLEBIG", "MENUBIG", "GAME12AA", "GAME10AA", "GAMEBOLD12", "SESHBIG"),
			_ => ("TITLEMED", "MENUMED", "GAME12AA", "GAME8AA", "GAMEBOLD12", "SESHMED"),
		};
		Title = Load( title );
		Menu = Load( menu );
		Label = Load( label );
		Small = Load( small );
		// [EXT:ui-font-korean-bold] the Korean CD's language folder has no GAMEBOLD9/10/12 (nor MATISSE18/36): the bold game font falls back to the
		// language's regular label font, the same size, instead of failing the screen
		try { Cash = Load( cash ); }
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException ) { Cash = Load( label ); }
		Heading = Load( heading );
		// [EXT:ui-font-korean-heading] the Korean CD's SESH fonts draw only Latin letters (229 glyphs), so a heading in Hangul gives way to the label font;
		// the Hebrew folder's SESH fonts likewise have no Hebrew letter (208 glyphs of 329), so a heading in Hebrew does too
		if ( DrawnGlyphs( Heading ) * 2 < DrawnGlyphs( Small ) || LacksHebrew( Heading ) )
			Heading = Label;
		// [EXT:ui-font-tier-by-scale] the shipped CASHSMALL/CASHMED/CASHBIG fonts for the bank balance (the HUD builder's cash font slot 1; the original's banks 0-3 hold CASHSMALL/CASHMED/CASHBIG/CASHBIG, 0x419710)
		var balance = tier switch { UiFontTier.Small => "CASHSMALL", UiFontTier.Big => "CASHBIG", _ => "CASHMED" };
		try { Balance = Load( balance ); }
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException ) { Balance = Cash; }
		// [DATA:Mac UI font table (docs/reverse/PPC-ui.md):slot 3 DATETINY/DATESMALL/DATEMED/DATEBIG] the HUD date font, one per
		// bank ([EXT:ui-font-tier-by-scale] for the tier→bank pairing). Only official Patch 2 and the Mac version ship these files
		// (docs/PATCH-2.md); the 1999 data falls back to Small, which in the Medium tier is the same file as DATESMALL.
		var date = tier switch { UiFontTier.Small => "DATETINY", UiFontTier.Big => "DATEMED", _ => "DATESMALL" };
		try { Date = Load( date ); }
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException ) { Date = Small; }
		// [EXT:ui-font-korean-date] the Korean CD's DATE fonts draw only the Latin, Greek and Cyrillic glyphs (356 of their 17,270 records have a bitmap),
		// so a date written in Hangul would be blank: a date font that draws fewer than half of what the regular label font draws gives way to it
		// the same for the Hebrew folder's DATE fonts, which have no Hebrew letter (a date in Hebrew words)
		if ( DrawnGlyphs( Date ) * 2 < DrawnGlyphs( Small ) || LacksHebrew( Date ) )
			Date = Small;
	}

	private static int DrawnGlyphs( FontAtlas font ) => font.Glyphs.Values.Count( glyph => glyph.Width > 0 && glyph.Height > 0 );

	/// <summary>Whether the language's body font draws Hebrew letters and <paramref name="font"/> draws none (the Hebrew SESH, DATE and CASH fonts).</summary>
	private bool LacksHebrew( FontAtlas font ) => DrawsHebrew( Small ) && !DrawsHebrew( font );

	private static bool DrawsHebrew( FontAtlas font ) => font.Glyphs.Values.Any( glyph => glyph.Width > 0 && glyph.Height > 0 && TextVisualOrder.IsHebrew( glyph.Character ) );

	// Shipped BF4 families from largest to smallest; used to shrink text that does not fit (UiTextFit).
	private static readonly string[][] Families =
	{
		new[] { "TITLEBIG", "TITLEMED", "TITLESMALL" },
		new[] { "MENUBIG", "MENUMED", "MENUSMALL" },
		new[] { "SESHBIG", "SESHMED", "SESHSMALL" },
		new[] { "GAMEBOLD12", "GAMEBOLD10" },
		new[] { "GAME12AA", "GAME10AA", "GAME8AA" },
	};

	private readonly Dictionary<FontAtlas, string> names = new( ReferenceEqualityComparer.Instance );

	private FontAtlas Load( string name )
	{
		var font = load( name + ".bf4" );
		names[font] = name;
		return font;
	}

	/// <summary>The smaller sizes of <paramref name="font"/>'s family, largest first; missing fonts are skipped.</summary>
	public IEnumerable<FontAtlas> Smaller( FontAtlas font )
	{
		if ( !names.TryGetValue( font, out var name ) )
			yield break;
		var family = Families.FirstOrDefault( entry => entry.Contains( name, StringComparer.OrdinalIgnoreCase ) );
		if ( family == null )
			yield break;
		foreach ( var smaller in family.SkipWhile( entry => !string.Equals( entry, name, StringComparison.OrdinalIgnoreCase ) ).Skip( 1 ) )
		{
			FontAtlas? atlas = null;
			try
			{
				atlas = Load( smaller );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException )
			{
			}
			if ( atlas != null )
				yield return atlas;
		}
	}

	public static Func<string, FontAtlas> CachedLoader( GameLanguage language )
	{
		var cache = new Dictionary<string, FontAtlas>( StringComparer.OrdinalIgnoreCase );
		return name => cache.TryGetValue( name, out var atlas ) ? atlas : cache[name] = new FontAtlas( language.LoadFont( name ) );
	}

	public static UiFonts Load( GameLanguage language, UiFontTier tier = UiFontTier.Medium ) => new( CachedLoader( language ), tier );

	/// <summary>The same fonts in another tier (shares the loader cache).</summary>
	public UiFonts WithTier( UiFontTier tier ) => tier == Tier ? this : new UiFonts( load, tier );

	public UiFontTier Tier { get; }
	public FontAtlas Title { get; }
	public FontAtlas Menu { get; }
	public FontAtlas Label { get; }
	public FontAtlas Small { get; }
	public FontAtlas Cash { get; }
	/// <summary>Font of the HUD bank balance (the original's dedicated cash fonts; <see cref="Cash"/> when they are missing).</summary>
	public FontAtlas Balance { get; }
	/// <summary>Font of the HUD date (the original's DATE fonts; <see cref="Small"/> when they are missing).</summary>
	public FontAtlas Date { get; }
	public FontAtlas Heading { get; }

	public IEnumerable<FontAtlas> All => new[] { Title, Menu, Label, Small, Cash, Heading };

	/// <summary>Every BF4 file any tier uses.</summary>
	public static readonly string[] FileNames =
	{
		"TITLESMALL.bf4", "TITLEMED.bf4", "TITLEBIG.bf4", "MENUSMALL.bf4", "MENUMED.bf4", "MENUBIG.bf4",
		"SESHSMALL.bf4", "SESHMED.bf4", "SESHBIG.bf4", "GAME8AA.bf4", "GAME10AA.bf4", "GAME12AA.bf4", "GAMEBOLD10.bf4", "GAMEBOLD12.bf4"
	};
}

/// <summary>Colours used by the original-style screens (OpenTPW choices, see docs/UI.md).</summary>
// [BIN:TPW-EXE:0x65c5d5 text control colour] the original gives each text control an RGBA colour (renderer +0x48, kept as ARGB at +0x78, 0x48fd07) and renders its glyphs white (0x48fa96) unless the control keeps the font's own colours (+0xdd); the buy list rows are (255,255,255,255) (0x4ad340)
// [APPROX:UI-006] the other UI text/backdrop colours (values, highlight, disabled, costs, backdrops) are read off 720p video frames (+-15 per channel); the title colour is exact — evidence needed: which screen each remaining 0x65c5d5 call colours (134 sites, see the register row), the backdrop colours
public static class UiColors
{
	public static readonly RgbaByte Text = new( 255, 255, 255, 255 );
	public static readonly RgbaByte Highlight = new( 255, 230, 70, 255 );
	public static readonly RgbaByte Disabled = new( 140, 140, 160, 255 );
	/// <summary>The window title colour.</summary>
	// [BIN:TPW-EXE:0x492d20 title colour] the shared title colour setter gives a title control (font slot 5 or the like) the colour (234, 239, 102, 255) = 0xea, 0xef, 0x66 (0x492d35); its fifteen callers are the window and lobby builders (0x485d20, 0x48d2e0, 0x48d370, 0x499060, 0x4990f0, 0x4a6580, 0x4a6e40, 0x4a8140, 0x4ae560, 0x4b6110, 0x4b8520 the lobby island name, 0x4ba200, 0x4ba980, 0x4bb070, 0x4c0ae0)
	public static readonly RgbaByte Title = new( 234, 239, 102, 255 );
	public static readonly RgbaByte Value = new( 150, 255, 120, 255 );
	/// <summary>The pause menu's text lines: cyan, white while focused (video 27:42).</summary>
	// [BIN:TPW-EXE:0x48b2b9 pause line colour] the line procedure 0x48b2a0 gives a line the colour (0, 175, 190, 255) on message 0x11 and again when it loses the focus (0x48b220)
	public static readonly RgbaByte MenuItem = new( 0, 175, 190, 255 );
	public static readonly RgbaByte MenuItemFocused = new( 255, 255, 255, 255 );
	/// <summary>Red text of costs (the hire window's mini balance rows, video 12:00).</summary>
	public static readonly RgbaByte Negative = new( 255, 40, 40, 255 );
	/// <summary>Dark label text inside the light-green option bars (black).</summary>
	// [BIN:TPW-EXE:0x4a43b0 options labels] the original's options labels (and the slider labels, 0x4a4490) get the colour (0,0,0,255) (0x4a441e, 0x4a44fe) and font slot 6 ([0x7537b4])
	// [BIN:TPW-EXE:0x58a010 option label colour] the control's colour (0,0,0,255) is the diffuse colour of its sprite quads (0x58a019), drawn with the fixed state word 0x7060556 (stage 0 colour = texture MODULATE diffuse: 0x567620, 0x566840), so every texel's RGB is white coverage x black = (0,0,0); the (16,16,48) read off the video earlier was its chroma
	public static readonly RgbaByte OptionText = new( 0, 0, 0, 255 );
	public static readonly RgbaByte Shadow = new( 0, 0, 40, 200 );
	public static readonly RgbaByte Backdrop = new( 0, 0, 30, 120 );
	public static readonly RgbaByte HelpBackground = new( 20, 30, 90, 230 );
	/// <summary>The popup help box's backdrop: translucent black (video: about 65-70 % black over grass).</summary>
	public static readonly RgbaByte HelpBox = new( 0, 0, 0, 170 );
}
