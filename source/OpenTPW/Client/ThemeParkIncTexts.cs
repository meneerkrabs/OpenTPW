using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>The Theme Park Inc string tables the mission window and the objective letter read (the globals the language loader <c>0x4468E0</c> fills).</summary>
internal enum ThemeParkIncTable
{
	/// <summary><c>UITEXT.str</c> (<c>0xA0D5FC</c>).</summary>
	UiText,
	/// <summary><c>TAG_SYSTEM.str</c> (<c>0xA0D5F8</c>).</summary>
	TagSystem,
	/// <summary><c>THEMENAMES.str</c> (<c>0xA0D644</c>).</summary>
	ThemeNames,
	/// <summary><c>MISSION_DESC.str</c> (<c>0xA0D64C</c>).</summary>
	MissionDesc,
	/// <summary><c>MISSION_DESC_GOTPROMO.str</c> (<c>0xA0D650</c>).</summary>
	MissionDescGotPromo,
	/// <summary><c>OPERATION.str</c> (<c>0xA0D654</c>): the mission titles.</summary>
	Operation,
	/// <summary><c>PROMOTIONLEVELS.str</c> (<c>0xA0D658</c>).</summary>
	PromotionLevels,
	/// <summary><c>STAT_TITLES.str</c> (<c>0xA0D660</c>).</summary>
	StatTitles,
	/// <summary><c>OBJECTIVE_COMPLETE.str</c> (<c>0xA0D66C</c>).</summary>
	ObjectiveComplete
}

/// <summary>
/// The string formatter <c>0x8934A0(table, buffer, size, entry, args…)</c>: an entry of a table with its parameter slots filled by the typed arguments. An argument record is
/// <c>{vtable, 'MRAV', parameter index, value}</c>; the integer class (<c>0x93BE48</c>) prints its value with <c>"%d"</c> (<c>0x8942E0</c>, the format at <c>0x9B19D0</c>) and the
/// string class (<c>0x93BE3C</c>) its text, so a slot <c>{pN}</c> takes the argument whose index is N; a slot no argument names stays empty.
/// </summary>
internal interface IThemeParkIncTexts
{
	string Format( ThemeParkIncTable table, int entry, IReadOnlyDictionary<int, string>? values = null );
}

/// <summary>The retail string tables of the selected language.</summary>
internal sealed class ThemeParkIncTexts( UiStringTable strings, Func<string, StringFile> load ) : IThemeParkIncTexts
{
	private readonly Dictionary<ThemeParkIncTable, StringFile?> files = new();

	public static ThemeParkIncTexts For( UiStringTable strings, GameLanguage language ) => new( strings, name => language.LoadStrings( name ) );

	public string Format( ThemeParkIncTable table, int entry, IReadOnlyDictionary<int, string>? values = null )
	{
		var empty = new Dictionary<int, string>();
		switch ( table )
		{
			case ThemeParkIncTable.UiText:
				return strings.FormatByType( entry, values ?? empty );
			case ThemeParkIncTable.ThemeNames:
				return strings.Theme( entry );
		}
		if ( File( table ) is not { } file || entry < 0 || entry >= file.Templates.Length )
			return "";
		return file.Templates[entry].FillByType( values ?? empty );
	}

	private StringFile? File( ThemeParkIncTable table )
	{
		if ( files.TryGetValue( table, out var file ) )
			return file;
		var name = table switch
		{
			ThemeParkIncTable.TagSystem => "TAG_SYSTEM.str",
			ThemeParkIncTable.MissionDesc => "MISSION_DESC.str",
			ThemeParkIncTable.MissionDescGotPromo => "MISSION_DESC_GOTPROMO.str",
			ThemeParkIncTable.Operation => "OPERATION.str",
			ThemeParkIncTable.PromotionLevels => "PROMOTIONLEVELS.str",
			ThemeParkIncTable.StatTitles => "STAT_TITLES.str",
			_ => "OBJECTIVE_COMPLETE.str"
		};
		try
		{
			file = load( name );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException )
		{
			Log.Warning( $"{name} is not available for the Theme Park Inc mission window: {exception.Message}" );
			file = null;
		}
		files[table] = file;
		return file;
	}

	/// <summary>An integer argument as the integer class prints it.</summary>
	// [BIN:TPI-EXE:0x8942E0 integer argument] swprintf with "%d" (0x9B19D0)
	public static string Int( int value ) => value.ToString( System.Globalization.CultureInfo.InvariantCulture );
}
