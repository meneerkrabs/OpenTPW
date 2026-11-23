namespace OpenTPW;

/// <summary>
/// The saved form of a native Theme Park Inc guest's own fields (<see cref="ThemeParkIncGuestRecord"/>, guest snapshot schema 10): every word of the constructor the shared guest does not hold, so
/// that the later phases of the port read their fields without a new schema. Absent on every other guest; a save without it holds no native guest.
/// </summary>
public sealed record ThemeParkIncGuestSnapshot
{
	public required int State { get; init; }
	public required int Saved { get; init; }
	public required int Mode { get; init; }
	public required int Heading { get; init; }
	public required int FeesPaid { get; init; }
	public required long ArrivalTurn { get; init; }
	public required int Target { get; init; }
	public required int Word206 { get; init; }
	public required int Word208 { get; init; }
	public required int Byte21B { get; init; }
	public required int Word21C { get; init; }
	public required int Word220 { get; init; }
	public required int Word224 { get; init; }
	public required int Wait { get; init; }
	public required bool Paid { get; init; }
	public required long SpotTurn { get; init; }
	public required int Word234 { get; init; }
	public required int SpriteIndex { get; init; }
	public required int Byte28C { get; init; }
	public required int Word290 { get; init; }
	public required bool HoldsBalloon { get; init; }
	public required uint BalloonCountdown { get; init; }
	public required int Word29C { get; init; }
	public required int Generation { get; init; }
	public required int PreferredExcitement { get; init; }
	public required int BoredomThreshold { get; init; }
	public required int Byte2B1 { get; init; }
	public required int FirstAidWait { get; init; }
	public required int Word2B8 { get; init; }
	public required int Word2BC { get; init; }
	public required int Word2C8 { get; init; }
	public required int QueueLink { get; init; }
	public required int QueueSlot { get; init; }
	public required int[] Words2D0 { get; init; }
	public required int WeatherDay { get; init; }
	public required int Word1B8 { get; init; }
	public required int Word1DC { get; init; }
	public required int[] Words1EC { get; init; }
	public required int Word0E0 { get; init; }

	/// <summary>A copy of a guest's record.</summary>
	public static ThemeParkIncGuestSnapshot Capture( ThemeParkIncGuestRecord tpi ) => new()
	{
		State = tpi.State, Saved = tpi.Saved, Mode = tpi.Mode, Heading = tpi.Heading, FeesPaid = tpi.FeesPaid, ArrivalTurn = tpi.ArrivalTurn, Target = tpi.Target,
		Word206 = tpi.Word206, Word208 = tpi.Word208, Byte21B = tpi.Byte21B, Word21C = tpi.Word21C, Word220 = tpi.Word220, Word224 = tpi.Word224, Wait = tpi.Wait, Paid = tpi.Paid,
		SpotTurn = tpi.SpotTurn, Word234 = tpi.Word234, SpriteIndex = tpi.SpriteIndex, Byte28C = tpi.Byte28C, Word290 = tpi.Word290, HoldsBalloon = tpi.HoldsBalloon,
		BalloonCountdown = tpi.BalloonCountdown, Word29C = tpi.Word29C, Generation = tpi.Generation, PreferredExcitement = tpi.PreferredExcitement, BoredomThreshold = tpi.BoredomThreshold,
		Byte2B1 = tpi.Byte2B1, FirstAidWait = tpi.FirstAidWait, Word2B8 = tpi.Word2B8, Word2BC = tpi.Word2BC, Word2C8 = tpi.Word2C8, QueueLink = tpi.QueueLink, QueueSlot = tpi.QueueSlot,
		Words2D0 = tpi.Words2D0.ToArray(), WeatherDay = tpi.WeatherDay, Word1B8 = tpi.Word1B8, Word1DC = tpi.Word1DC, Words1EC = tpi.Words1EC.ToArray(), Word0E0 = tpi.Word0E0
	};

	/// <summary>A new record holding the saved fields.</summary>
	public ThemeParkIncGuestRecord Restore()
	{
		var tpi = new ThemeParkIncGuestRecord
		{
			State = State, Saved = Saved, Mode = Mode, Heading = Heading, FeesPaid = FeesPaid, ArrivalTurn = ArrivalTurn, Target = Target, Word206 = Word206, Word208 = Word208,
			Byte21B = Byte21B, Word21C = Word21C, Word220 = Word220, Word224 = Word224, Wait = Wait, Paid = Paid, SpotTurn = SpotTurn, Word234 = Word234, SpriteIndex = SpriteIndex,
			Byte28C = Byte28C, Word290 = Word290, HoldsBalloon = HoldsBalloon, BalloonCountdown = BalloonCountdown, Word29C = Word29C, Generation = Generation,
			PreferredExcitement = PreferredExcitement, BoredomThreshold = BoredomThreshold, Byte2B1 = Byte2B1, FirstAidWait = FirstAidWait, Word2B8 = Word2B8, Word2BC = Word2BC,
			Word2C8 = Word2C8, QueueLink = QueueLink, QueueSlot = QueueSlot, WeatherDay = WeatherDay, Word1B8 = Word1B8, Word1DC = Word1DC, Word0E0 = Word0E0
		};
		Words2D0.CopyTo( tpi.Words2D0, 0 );
		Words1EC.CopyTo( tpi.Words1EC, 0 );
		return tpi;
	}

	/// <summary>The problems that keep this block from restoring for guest <paramref name="guestId"/>; empty when it restores.</summary>
	public IEnumerable<string> Problems( int guestId )
	{
		if ( (uint)State >= ThemeParkIncGuestStates.Count )
			yield return $"Guest {guestId} Theme Park Inc state {State} is not one of the 31 states.";
		if ( (uint)Saved >= ThemeParkIncGuestStates.Count )
			yield return $"Guest {guestId} Theme Park Inc saved state {Saved} is not one of the 31 states.";
		if ( (uint)Generation > 2 )
			yield return $"Guest {guestId} Theme Park Inc generation {Generation} is not 0, 1 or 2.";
		if ( Words2D0 is not { Length: ThemeParkIncGuestRecord.Words2D0Count } || Words1EC is not { Length: ThemeParkIncGuestRecord.Words1ECCount } )
			yield return $"Guest {guestId} Theme Park Inc word blocks have the wrong lengths.";
		if ( ArrivalTurn < 0 || SpotTurn < 0 )
			yield return $"Guest {guestId} Theme Park Inc turn stamps are negative.";
	}

	/// <summary>The record's words in a fixed order, for the guest state hashes.</summary>
	public static IEnumerable<long> HashWords( ThemeParkIncGuestRecord tpi )
	{
		yield return 0x54504947L;
		foreach ( var word in new long[] { tpi.State, tpi.Saved, tpi.Mode, tpi.Heading, tpi.FeesPaid, tpi.ArrivalTurn, tpi.Target, tpi.Word206, tpi.Word208, tpi.Byte21B, tpi.Word21C,
			tpi.Word220, tpi.Word224, tpi.Wait, tpi.Paid ? 1 : 0, tpi.SpotTurn, tpi.Word234, tpi.SpriteIndex, tpi.Byte28C, tpi.Word290, tpi.HoldsBalloon ? 1 : 0, tpi.BalloonCountdown,
			tpi.Word29C, tpi.Generation, tpi.PreferredExcitement, tpi.BoredomThreshold, tpi.Byte2B1, tpi.FirstAidWait, tpi.Word2B8, tpi.Word2BC, tpi.Word2C8, tpi.QueueLink, tpi.QueueSlot,
			tpi.WeatherDay, tpi.Word1B8, tpi.Word1DC, tpi.Word0E0 } )
			yield return word;
		foreach ( var word in tpi.Words2D0 )
			yield return word;
		foreach ( var word in tpi.Words1EC )
			yield return word;
	}
}
