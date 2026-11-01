using System.Globalization;

namespace OpenTPW;

/// <summary>
/// The arguments the tag system hands the parameter slots of an advisor caption: the case of the caption formatter (<c>0x509E10</c>) that the caption's tag selects
/// (<see cref="AdvisorCaption.Kind"/>, field +0x1C of the tag's record) over the two words the said advice record carries (<see cref="AdvisorPendingAdvice.ThingId"/>,
/// <see cref="AdvisorPendingAdvice.Secondary"/>). The advisor's rows use the cases 0, 2 and 5 to 8 only (every row of the response table at 0x768FB8 is checked by
/// <c>tools/ppc-analysis/lanes/advisor/tpw_exe_tables.py</c>), so the other cases of the formatter (a guest check, a signed number, the tutorial tags') are not ported.
/// </summary>
// [BIN:TPW-EXE:0x00509E10 caption formatter] the tag record's kind (+0x1C, at most 0x24) picks one of 23 cases through the byte table 0x50AEA0 and the jump table 0x50AE40; the arguments are the record 0x509CC6 builds from the category-24 message: the tag, the word +0xC (the record's id word) and the dword +0x14 (its cell word)
internal static class AdvisorCaptionArguments
{
	/// <summary>The case without arguments (the text has none, or leaves its slots empty).</summary>
	public const int NoArguments = 0;
	/// <summary>The case that names an object type: the low word of the record's second dword is an Info.Id.</summary>
	public const int ObjectName = 2;
	/// <summary>The local golden ticket cases: the visitors, guests in the park, yearly profit and recent visitors tickets.</summary>
	public const int VisitorsTicket = 5, PeopleTicket = 6, ProfitTicket = 7, RecentVisitorsTicket = 8;

	/// <summary>
	/// The values of the text's parameter slots by slot type (the slot's field is the index of the argument it takes); null when the formatter writes no text,
	/// which leaves the tag system to post what its buffer held before.
	/// </summary>
	/// <param name="typeName">The name of the object type with an Info.Id, as the type record holds it (+0x410); null when the park has no such type.</param>
	/// <param name="tickets">The park's golden ticket thresholds (<c>GoldenTicketLocal</c>); null without a park.</param>
	public static IReadOnlyDictionary<int, string>? For( int kind, AdvisorPendingAdvice advice, Func<int, string?> typeName, GoldenTicketSettings? tickets )
	{
		ArgumentNullException.ThrowIfNull( advice );
		ArgumentNullException.ThrowIfNull( typeName );
		static string Number( int value ) => value.ToString( CultureInfo.InvariantCulture );
		switch ( kind )
		{
			case ObjectName:
				// [BIN:TPW-EXE:0x00509F09 object name case] the id is the low word of the record's second dword: 0 logs and writes nothing (0x509F15); else the type record (0x412F50 over the table 0x7890A0) hands the formatter its name (+0x410) as slot 0
				var id = (ushort)advice.Secondary;
				if ( id == 0 )
					return null;
				return new Dictionary<int, string> { [0] = typeName( id ) ?? "" };
			case VisitorsTicket when tickets != null:
				// [BIN:TPW-EXE:0x0050A014 ticket cases] 5 gives slot 1 the visitors threshold word 0x785790, 6 the guests-in-the-park word 0x785794 (0x50A074), 7 the yearly profit word 0x7857A0 (0x50A0D4); case 8 gives slot 2 the months 0x7857A8 and slot 1 the visitors 0x7857A4 (0x50A134)
				return new Dictionary<int, string> { [1] = Number( tickets.Visitors ) };
			case PeopleTicket when tickets != null:
				return new Dictionary<int, string> { [1] = Number( tickets.PeopleInPark ) };
			case ProfitTicket when tickets != null:
				return new Dictionary<int, string> { [1] = Number( tickets.ProfitYear ) };
			case RecentVisitorsTicket when tickets != null:
				return new Dictionary<int, string> { [1] = Number( tickets.RecentVisitors ), [2] = Number( tickets.RecentVisitorMonths ) };
			case NoArguments:
				// [BIN:TPW-EXE:0x00509E6D plain case] the text is formatted with no arguments (0x6ACD60 with four stack arguments)
				return new Dictionary<int, string>();
			default:
				return null;
		}
	}
}
