namespace OpenTPW;

/// <summary>What the exporter takes from outside the economy snapshot.</summary>
/// <param name="VisitorsToDate">The guest simulation's count of visitors (<c>mNumberOfVisitorsToDate</c>); null keeps the template's.</param>
/// <param name="Research">The park's research; when given, the research lab, object controls' researched bytes and analyser's recent-research rings are written from it (<see cref="OriginalResearchExport"/>), else the template's stay.</param>
/// <param name="Researchers">The number of hired researchers (the lab's active byte is set while there are any).</param>
/// <param name="RidesAboveOriginalLimits">[EXT:cheats-ride-vehicles] How many of the park's rides the vehicle cheat raised and that run above their type's original capacity maximum
/// (<c>Level.RidesAboveOriginalLimits</c>, which the caller passes: the economy snapshot holds no rides): such a park is refused, because the original keeps a ride's capacity in a byte and
/// clamps it to its type's own maximum. A raised ride whose capacity stays within the original's maximum is no obstacle.</param>
public sealed record OriginalParkExportOptions( uint? VisitorsToDate = null, ParkResearch? Research = null, int Researchers = 0, int RidesAboveOriginalLimits = 0 );

/// <summary>
/// Writes an OpenTPW park's economy back into an original-format park (the twin of <see cref="OriginalEconomyImport"/>): the park turn and calendar, the world generator
/// state, the open flag, the bank with its loans and the object controls' first-purchase turns replace the template's values and everything else is carried from the
/// <em>template</em>, an original save of the same level (the shipped <c>Easymode.TPWI</c>, a park the player loaded, or a <c>restart.INTS</c>).
/// <para>
/// What is not exported, because OpenTPW cannot yet produce the original's bytes for it: placed objects and their attraction things, the cell map's built cells (paths,
/// footprints), the RideSystem scene records, staff, guests, the advisor, the weather and the engine blocks. The research lab, object controls' researched bytes and recent-research rings are written when
/// the caller passes the park's research (<see cref="OriginalParkExportOptions.Research"/>). Exporting a park that built
/// something the template lacks therefore keeps the template's version of it, and <see cref="Export"/> says which values it took. The writer never runs the original game; the
/// result is checked by reading it back through <see cref="OriginalParkImport"/> and <see cref="OriginalEconomyImport"/> (docs/TPWS-PAYLOAD.md, "Writing original-format saves").
/// </para>
/// </summary>
public static class OriginalParkExport
{
	/// <summary>The original's object control record: <c>+0</c> the object id, <c>+0x1C</c> the turn of the first purchase (<c>0x4DB690</c>).</summary>
	private const int ControlIdOffset = 0, ControlFirstPurchaseOffset = 0x1C;

	/// <summary>
	/// <paramref name="template"/> with <paramref name="park"/>'s economy applied; <paramref name="template"/> is not changed. Throws <see cref="InvalidDataException"/> when a value does
	/// not fit the original's field (the bank words are 32-bit) or the template lacks the bank the economy is written into.
	/// </summary>
	public static OriginalSavePayload Export( OriginalSavePayload template, ParkSaveFile.Data park, out IReadOnlyList<string> evidence, OriginalParkExportOptions? options = null )
	{
		ArgumentNullException.ThrowIfNull( template );
		ArgumentNullException.ThrowIfNull( park );
		options ??= new OriginalParkExportOptions();
		// [EXT:cheats-export] the user's decision: a park whose rides hold more vehicles than the original's limits is not written in the original's format
		if ( options.RidesAboveOriginalLimits > 0 )
			throw new InvalidDataException( $"The park has {options.RidesAboveOriginalLimits} ride(s) whose vehicle limit a cheat raised and whose capacity is above the original's limit; the original game cannot hold them (docs/CHEATS.md)." );
		var lines = new List<string>();
		// a deep copy: the template's own bytes through the writer and reader
		var result = OriginalSavePayload.Read( template.Write() );
		var world = result.World;
		var turn = ParkCalendar.Turn( park.Tick );
		if ( turn > uint.MaxValue )
			throw new InvalidDataException( $"The park turn {turn} does not fit the original's 32-bit mGameTick." );
		// [BIN:TPW-EXE:0x516C80 World writer] mGameTick (u32), mParkClosed (u32), mNumberOfVisitorsToDate (u32) and mRandomSeed (u32, the world generator's whole state) are four of the 26 labelled variables
		world.Variables = world.Variables with
		{
			GameTick = (uint)turn,
			ParkClosed = park.ParkOpen ? 0u : 1u,
			NumberOfVisitorsToDate = options.VisitorsToDate ?? world.Variables.NumberOfVisitorsToDate,
			RandomSeed = park.WorldGenerator ?? world.Variables.RandomSeed,
		};
		lines.Add( $"World variables: park turn {turn}, park {(park.ParkOpen ? "open" : "closed")}, {world.Variables.NumberOfVisitorsToDate} visitors, world generator 0x{world.Variables.RandomSeed:X8}." );
		ExportCalendar( world, park, turn, lines );
		ExportCheats( result, park, lines );
		ExportBank( world, park, lines );
		ExportFirstPurchases( world, park, lines );
		if ( options.Research != null )
		{
			OriginalResearchExport.Apply( world, options.Research, options.Researchers, lines );
			var analyser = world.Things.FirstOrDefault( thing => thing.Id == world.Variables.ParkAnalyser )
				?? throw new InvalidDataException( "The template names no park analyser thing for the research history." );
			SaveRecentResearch.Write( analyser.Body, new SaveThing( (int)analyser.Id, analyser.Model, (int)analyser.Next, 0, 0, analyser.Body.Length ), options.Research.RecentItems );
			lines.Add( "Park analyser: four recent-research rings written; all other analyser bytes kept." );
		}
		evidence = lines.AsReadOnly();
		return result;
	}

	/// <summary>The calendar the park's turn implies: the constructor's calendar stays while the park has not run (turn 0), a played park carries its date caches (the import's check).</summary>
	// [BIN:TPW-EXE:0x4F7F30 calendar serializer] funny time start (8), session start (8), month at last update (4), day at last update (4), seconds per real second (4); the loader takes them back unchecked
	// [BIN:TPW-EXE:0x4F7E80 calendar constructor] start 2000-01-01, month cache 0, day cache -1, rate 15000; the first update fills the caches
	private static void ExportCalendar( OriginalSaveWorldData world, ParkSaveFile.Data park, long turn, List<string> lines )
	{
		if ( turn == 0 && world.Calendar.IsInitial )
		{
			lines.Add( "Calendar: the park has not run; the constructor's calendar is kept." );
			return;
		}
		var date = ParkCalendar.DateAtTurn( turn );
		var epoch = (ParkCalendar.Epoch - new DateTime( 1601, 1, 1 )).Ticks;
		world.Calendar = new SaveCalendar( 0, epoch, world.Calendar.SessionStart, date.Month - 1, date.Day, (int)(ParkCalendar.SecondsPerTurn * 4) );
		lines.Add( $"Calendar: {date:yyyy-MM-dd} (month cache {date.Month - 1}, day cache {date.Day}); the session start of the template is kept." );
	}

	private static void ExportBank( OriginalSaveWorldData world, ParkSaveFile.Data park, List<string> lines )
	{
		var bankThing = world.Things.FirstOrDefault( thing => thing.Id == world.Variables.BankAccount && thing.Model == SaveThingModel.Bank )
			?? throw new InvalidDataException( "The template names no bank account thing; the economy has nowhere to go." );
		var bank = SaveBank.Read( bankThing.Body, new SaveThing( (int)bankThing.Id, SaveThingModel.Bank, (int)bankThing.Next, 0, 0, bankThing.Body.Length ) );
		int Narrow( long value, string field ) => value is >= int.MinValue and <= int.MaxValue ? (int)value
			: throw new InvalidDataException( $"The bank's {field} {value} does not fit the original's 32-bit word." );
		var account = bank.Account with
		{
			AdmissionFee = Narrow( park.EntranceFee, "admission fee" ),
			Balance = Narrow( park.Ledger.Balance, "balance" ),
			BatchBalance = Narrow( park.BatchCredit, "batch balance" ),
			WithdrawalsEnabled = park.WithdrawalsEnabled ?? true,
			LastBalance = Narrow( park.LastBalance ?? park.Ledger.Balance, "last balance" ),
			TurnEnteredRed = park.TurnEnteredRed is { } red ? (uint)Math.Clamp( red, 0, uint.MaxValue ) : bank.Account.TurnEnteredRed,
			ProfitThisYear = Narrow( park.Ledger.ProfitThisYear, "profit this year" ),
		};
		var loans = new List<SaveLoanRecord>();
		foreach ( var offer in bank.Loans )
		{
			var bought = park.Loans.FirstOrDefault( loan => loan.OfferIndex == offer.Index );
			var available = park.AvailableLoanOffers?.Contains( offer.Index ) ?? offer.Available;
			loans.Add( bought == null
				? offer with { Available = available, Bought = false, MonthsRepaid = 0 }
				: offer with
				{
					Available = available, Bought = true, Amount = Narrow( bought.OriginalAmount, "loan amount" ), AprPercent = bought.AprPercent, Months = bought.Months,
					MonthlyRepayment = Narrow( bought.MonthlyRepayment, "loan repayment" ), MonthsRepaid = bought.Months - bought.MonthsRemaining, LenderNameIndex = bought.LenderNameIndex,
				} );
		}
		var written = bank with { Account = account, Loans = loans };
		bankThing.Body = written.ToBody();
		lines.Add( $"Bank: balance {account.Balance}, admission fee {account.AdmissionFee}, profit this year {account.ProfitThisYear}, {loans.Count( loan => loan.Bought )} bought loans." );
	}

	/// <summary>Each object type's first-purchase turn (<c>+0x1C</c> of its control record, stamped by <c>0x4DB690</c> while 0) from the economy's counters; types the economy never bought keep the record's word.</summary>
	// [BIN:TPW-EXE:0x4D3AA0 object controls] the control manager's 150 records of 32 bytes, then the used count and the previous search key
	private static void ExportFirstPurchases( OriginalSaveWorldData world, ParkSaveFile.Data park, List<string> lines )
	{
		var controls = world.Controls;
		var changed = 0;
		for ( var index = 0; index < controls.Count && index < SaveObjectControls.Slots; index++ )
		{
			var record = controls.Records.AsSpan( index * SaveObjectControl.Bytes, SaveObjectControl.Bytes );
			var id = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( record[ControlIdOffset..] );
			if ( !park.Counters.TryGetValue( ParkCounters.FirstPurchaseTurn( id ), out var first ) || first <= 0 )
				continue;
			if ( first > uint.MaxValue )
				throw new InvalidDataException( $"The first purchase turn {first} of object {id} does not fit the original's word." );
			var word = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( record[ControlFirstPurchaseOffset..] );
			if ( word == (uint)first )
				continue;
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( record[ControlFirstPurchaseOffset..], (uint)first );
			changed++;
		}
		lines.Add( $"Object controls: {changed} first-purchase turns written." );
	}

	/// <summary>
	/// The <c>CHTS</c> chunk from the park's own cheat mode, not the template's (a template taken from a cheated save must not mark a clean park): <c>01 01</c> for a cheated park, the value the
	/// shipped seed <c>Easymode.TPWI</c> carries (the eleven corpus saves carry <c>00 00</c>), and <c>00 00</c> otherwise.
	/// </summary>
	// [BIN:TPW-EXE:0x4055D0 cheat save] the chunk is the mode byte +0xA and the byte +0xB; [EXT:cheats-export] OpenTPW writes 01 01 for a cheated park (the shipped seed's value; no tp.exe code writes +0xB, so its meaning for another build is unknown) and 00 00 for any other
	private static void ExportCheats( OriginalSavePayload result, ParkSaveFile.Data park, List<string> lines )
	{
		var cheated = park.Cheats?.Mode == true;
		result.Cheat = cheated ? new byte[] { 1, 1 } : new byte[] { 0, 0 };
		lines.Add( $"Cheat chunk: {Convert.ToHexString( result.Cheat )} ({(cheated ? "a cheated park" : "no cheats")})." );
	}

	/// <summary>
	/// Writes the exported park as an original container: the template container's language, banner and bitmap (<paramref name="container"/>) around the payload
	/// <see cref="Export"/> builds. The caller names the file; an existing file is never replaced.
	/// </summary>
	public static byte[] ExportFile( SaveContainerFields container, OriginalSavePayload template, ParkSaveFile.Data park, out IReadOnlyList<string> evidence, OriginalParkExportOptions? options = null ) =>
		SaveContainerWriter.Write( container, Export( template, park, out evidence, options ).Write() );
}
