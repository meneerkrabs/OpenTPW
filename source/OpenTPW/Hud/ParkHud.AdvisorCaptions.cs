namespace OpenTPW.Hud;

public sealed partial class ParkHud
{
	/// <summary>
	/// Posts the caption of an advisor response to the message tube: the text of <c>TAG_SYSTEM.str</c> entry <paramref name="textEntry"/>, as the original's tag system
	/// shows the tag the advisor sends when it says a response, with <paramref name="values"/> in the parameter slots (<see cref="AdvisorCaptionArguments"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00509BF0 tag system] a category-24 message shows the text of its tag's record (0x0050AF40, field +0xC the TAG_SYSTEM entry) through 0x00509E10, which fills the record's parameter slots from the message's words, and posts it to the message tube (0x00481580)
	internal void PostAdvisorCaption( int textEntry, IReadOnlyDictionary<int, string> values )
	{
		string? text;
		try
		{
			text = AdvisorCaptionText( GameLanguage.Current.LoadStrings( "TAG_SYSTEM.str" ), textEntry, values );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or KeyNotFoundException )
		{
			Log.Warning( $"TAG_SYSTEM.str is not available for an advisor caption: {exception.Message}" );
			return;
		}
		if ( text != null )
			PostGameMessage( text );
	}

	/// <summary>The text of a caption entry with each parameter slot filled by the value of its type (a slot without a value stays empty), or null for an entry that is missing or empty.</summary>
	internal static string? AdvisorCaptionText( StringFile file, int textEntry, IReadOnlyDictionary<int, string>? values = null )
	{
		if ( textEntry < 0 || textEntry >= file.Entries.Length )
			return null;
		var template = textEntry < file.Templates.Length ? file.Templates[textEntry] : null;
		var text = template?.FillByType( values ?? new Dictionary<int, string>() ) ?? file.Entries[textEntry];
		return string.IsNullOrWhiteSpace( text ) ? null : text;
	}
}
