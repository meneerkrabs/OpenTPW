namespace OpenTPW;

/// <summary>
/// The saved form of a native Theme Park Inc guest's own fields (<see cref="ThemeParkIncGuestRecord"/>, the optional <see cref="GuestSnapshot.Tpi"/>): every word of the constructor the shared guest does not hold, so
/// that the later phases of the port read their fields without a new member. Absent on every other guest; a save without it holds no native guest.
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
	/// <summary>The wander flag <c>+0xE8</c> (<see cref="ThemeParkIncGuestRecord.Wandered"/>); absent in a save of phase P1, whose guests never wandered.</summary>
	public bool Wandered { get; init; }
	/// <summary>Phase P3b's fields (<see cref="ThemeParkIncGuestRecord.Fee"/>, <see cref="ThemeParkIncGuestRecord.UseSucceeded"/>, <see cref="ThemeParkIncGuestRecord.BalloonColour"/>, the costume's sprite
	/// words); absent in a save of an earlier phase, whose guests never rode natively.</summary>
	public int Fee { get; init; }
	public bool UseSucceeded { get; init; }
	public int BalloonColour { get; init; }
	public int? SpriteCategory { get; init; }
	public int? SpriteVariant { get; init; }

	/// <summary>A copy of a guest's record.</summary>
	public static ThemeParkIncGuestSnapshot Capture( ThemeParkIncGuestRecord tpi ) => new()
	{
		State = tpi.State, Saved = tpi.Saved, Mode = tpi.Mode, Heading = tpi.Heading, FeesPaid = tpi.FeesPaid, ArrivalTurn = tpi.ArrivalTurn, Target = tpi.Target,
		Word206 = tpi.Word206, Word208 = tpi.Word208, Byte21B = tpi.Byte21B, Word21C = tpi.Word21C, Word220 = tpi.Word220, Word224 = tpi.Word224, Wait = tpi.Wait, Paid = tpi.Paid,
		SpotTurn = tpi.SpotTurn, Word234 = tpi.Word234, SpriteIndex = tpi.SpriteIndex, Byte28C = tpi.Byte28C, Word290 = tpi.Word290, HoldsBalloon = tpi.HoldsBalloon,
		BalloonCountdown = tpi.BalloonCountdown, Word29C = tpi.Word29C, Generation = tpi.Generation, PreferredExcitement = tpi.PreferredExcitement, BoredomThreshold = tpi.BoredomThreshold,
		Byte2B1 = tpi.Byte2B1, FirstAidWait = tpi.FirstAidWait, Word2B8 = tpi.Word2B8, Word2BC = tpi.Word2BC, Word2C8 = tpi.Word2C8, QueueLink = tpi.QueueLink, QueueSlot = tpi.QueueSlot,
		Words2D0 = tpi.Words2D0.ToArray(), WeatherDay = tpi.WeatherDay, Word1B8 = tpi.Word1B8, Word1DC = tpi.Word1DC, Words1EC = tpi.Words1EC.ToArray(), Word0E0 = tpi.Word0E0, Wandered = tpi.Wandered,
		Fee = tpi.Fee, UseSucceeded = tpi.UseSucceeded, BalloonColour = tpi.BalloonColour, SpriteCategory = tpi.SpriteCategory, SpriteVariant = tpi.SpriteVariant
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
			Word2C8 = Word2C8, QueueLink = QueueLink, QueueSlot = QueueSlot, WeatherDay = WeatherDay, Word1B8 = Word1B8, Word1DC = Word1DC, Word0E0 = Word0E0, Wandered = Wandered,
			Fee = Fee, UseSucceeded = UseSucceeded, BalloonColour = BalloonColour, SpriteCategory = SpriteCategory, SpriteVariant = SpriteVariant
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
		// a state this build cannot run would stop the guest's update mid-tick: a later phase's save, or an edited one
		if ( (uint)State < ThemeParkIncGuestStates.Count && !ThemeParkIncGuestStates.RunsInThisBuild( State ) )
			yield return $"Guest {guestId} Theme Park Inc state {State} ({ThemeParkIncGuestStates.Names[State]}) is not ported in this build.";
		if ( State == ThemeParkIncGuestStates.SpotAnimation && (uint)Saved < ThemeParkIncGuestStates.Count && !ThemeParkIncGuestStates.RunsInThisBuild( Saved ) )
			yield return $"Guest {guestId} Theme Park Inc spot animation returns to state {Saved} ({ThemeParkIncGuestStates.Names[Saved]}), which is not ported in this build.";
		if ( BalloonCountdown > 255 )
			yield return $"Guest {guestId} Theme Park Inc balloon countdown {BalloonCountdown} is above the balloon's life (0x9D0610, 255).";
		if ( SpriteCategory is { } category && category is not (0 or 1 or 2 or ThemeParkIncCostumeCategory) || SpriteCategory.HasValue != SpriteVariant.HasValue || SpriteVariant < 0 )
			yield return $"Guest {guestId} Theme Park Inc sprite words ({SpriteCategory}, {SpriteVariant}) are not a generation's or the costumes'.";
		if ( (uint)Generation > 2 )
			yield return $"Guest {guestId} Theme Park Inc generation {Generation} is not 0, 1 or 2.";
		if ( Words2D0 is not { Length: ThemeParkIncGuestRecord.Words2D0Count } || Words1EC is not { Length: ThemeParkIncGuestRecord.Words1ECCount } )
			yield return $"Guest {guestId} Theme Park Inc word blocks have the wrong lengths.";
		if ( ArrivalTurn < 0 || SpotTurn < 0 )
			yield return $"Guest {guestId} Theme Park Inc turn stamps are negative.";
	}

	/// <summary>The sprite category of the costumes (<c>0x9D7000</c>'s entry 6, "costumes").</summary>
	public const int ThemeParkIncCostumeCategory = 6;

	/// <summary>The number of words <see cref="CopyHashWords"/> writes.</summary>
	public const int HashWordCount = 1 + 38 + ThemeParkIncGuestRecord.Words2D0Count + ThemeParkIncGuestRecord.Words1ECCount + 5;

	/// <summary>The record's words in a fixed order, for the guest state hashes, into <paramref name="into"/> (at least <see cref="HashWordCount"/> long; nothing is allocated).</summary>
	public static void CopyHashWords( ThemeParkIncGuestRecord tpi, Span<long> into )
	{
		into[0] = 0x54504947L;
		into[1] = tpi.State;
		into[2] = tpi.Saved;
		into[3] = tpi.Mode;
		into[4] = tpi.Heading;
		into[5] = tpi.FeesPaid;
		into[6] = tpi.ArrivalTurn;
		into[7] = tpi.Target;
		into[8] = tpi.Word206;
		into[9] = tpi.Word208;
		into[10] = tpi.Byte21B;
		into[11] = tpi.Word21C;
		into[12] = tpi.Word220;
		into[13] = tpi.Word224;
		into[14] = tpi.Wait;
		into[15] = tpi.Paid ? 1 : 0;
		into[16] = tpi.SpotTurn;
		into[17] = tpi.Word234;
		into[18] = tpi.SpriteIndex;
		into[19] = tpi.Byte28C;
		into[20] = tpi.Word290;
		into[21] = tpi.HoldsBalloon ? 1 : 0;
		into[22] = tpi.BalloonCountdown;
		into[23] = tpi.Word29C;
		into[24] = tpi.Generation;
		into[25] = tpi.PreferredExcitement;
		into[26] = tpi.BoredomThreshold;
		into[27] = tpi.Byte2B1;
		into[28] = tpi.FirstAidWait;
		into[29] = tpi.Word2B8;
		into[30] = tpi.Word2BC;
		into[31] = tpi.Word2C8;
		into[32] = tpi.QueueLink;
		into[33] = tpi.QueueSlot;
		into[34] = tpi.WeatherDay;
		into[35] = tpi.Word1B8;
		into[36] = tpi.Word1DC;
		into[37] = tpi.Word0E0;
		into[38] = tpi.Wandered ? 1 : 0;
		for ( var index = 0; index < ThemeParkIncGuestRecord.Words2D0Count; index++ )
			into[39 + index] = tpi.Words2D0[index];
		for ( var index = 0; index < ThemeParkIncGuestRecord.Words1ECCount; index++ )
			into[39 + ThemeParkIncGuestRecord.Words2D0Count + index] = tpi.Words1EC[index];
		var tail = 39 + ThemeParkIncGuestRecord.Words2D0Count + ThemeParkIncGuestRecord.Words1ECCount;
		into[tail] = tpi.Fee;
		into[tail + 1] = tpi.UseSucceeded ? 1 : 0;
		into[tail + 2] = tpi.BalloonColour;
		into[tail + 3] = tpi.SpriteCategory ?? -1;
		into[tail + 4] = tpi.SpriteVariant ?? -1;
	}
}
