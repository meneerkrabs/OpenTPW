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

	public UiStringTable( string language, string[] uiText, string[] helpText, string[] themeNames, string[] objectNames )
	{
		Language = language;
		this.uiText = uiText;
		this.helpText = helpText;
		this.themeNames = themeNames;
		this.objectNames = objectNames;
	}

	public static UiStringTable Load( GameLanguage language ) => new( language.Name,
		language.LoadStrings( "UITEXT.str" ).Entries, language.LoadStrings( "UIHELPTEXT.str" ).Entries,
		language.LoadStrings( "THEMENAMES.str" ).Entries, language.LoadStrings( "OBJECT_NAMES.str" ).Entries );

	public string Language { get; }
	public string this[UIStrings id] => Get( uiText, (int)id );
	public string Help( int index ) => Get( helpText, index );
	public string Theme( int index ) => Get( themeNames, index );
	public string Object( int index ) => Get( objectNames, index );
	public string Extra( OpenTpwText key ) => SupplementaryStrings.Get( key, Language );

	/// <summary>A UITEXT entry without the leading space the original uses for option values.</summary>
	public string Value( UIStrings id ) => this[id].Trim();

	private static string Get( string[] table, int index ) => index >= 0 && index < table.Length ? table[index] : "";
}

/// <summary>
/// Font size tier. The original ships three sizes of its menu fonts (MENU/TITLE/SESH/CASH
/// SMALL, MED and BIG); picking one by output size is inferred from that, not from code.
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
		// [APPROX:UI-002] which shipped font serves which role per tier — evidence needed: binary font use / captures
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
		Cash = Load( cash );
		Heading = Load( heading );
	}

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
// [APPROX:UI-006] all UI text/backdrop colours — evidence needed: captures of original screens
public static class UiColors
{
	public static readonly RgbaByte Text = new( 255, 255, 255, 255 );
	public static readonly RgbaByte Highlight = new( 255, 230, 70, 255 );
	public static readonly RgbaByte Disabled = new( 140, 140, 160, 255 );
	public static readonly RgbaByte Title = new( 255, 214, 64, 255 );
	public static readonly RgbaByte Value = new( 150, 255, 120, 255 );
	/// <summary>Dark label text inside the light-green option bars (near-black navy).</summary>
	// [APPROX:UI-037] option bar label colour (16,16,48) read off a capture by eye — evidence needed: exact pixel colour from a capture or the font palette
	public static readonly RgbaByte OptionText = new( 16, 16, 48, 255 );
	public static readonly RgbaByte Shadow = new( 0, 0, 40, 200 );
	public static readonly RgbaByte Backdrop = new( 0, 0, 30, 120 );
	public static readonly RgbaByte HelpBackground = new( 20, 30, 90, 230 );
}
