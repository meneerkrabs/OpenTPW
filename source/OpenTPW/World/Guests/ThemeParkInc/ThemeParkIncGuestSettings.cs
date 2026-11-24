namespace OpenTPW;

/// <summary>One weather row of a generation (heat wave, rain, storm, snow): <c>Peep*WeatherEffects[generation]</c>.</summary>
public readonly record struct ThemeParkIncWeatherRow( float HeatWave, float Rain, float Storm, float Snow )
{
	/// <summary>The column of weather state <paramref name="weather"/> (1 heat wave .. 4 snow, the jump table <c>0x611324</c>); 0 for any other state.</summary>
	public float Of( int weather ) => weather switch
	{
		UmbrellaRules.WeatherHeatWave => HeatWave,
		UmbrellaRules.WeatherRain => Rain,
		UmbrellaRules.WeatherStorm => Storm,
		UmbrellaRules.WeatherSnow => Snow,
		_ => 0f
	};
}

/// <summary>
/// The Standard.sam words Theme Park Inc's own guest states read, by their addresses in the Standard object <c>0xA01A60</c> (<see cref="ThemeParkIncStandard"/>, plan §3 of the guest states port):
/// the <c>PeepInfo</c> block, the three weather arrays, <c>FixedItemInfo</c>'s booths and stops and <c>FirstHour.Minutes</c>. The native rules never read Theme Park World's <see cref="GuestSettings"/>.
/// </summary>
public sealed record ThemeParkIncGuestSettings
{
	/// <summary>The object's address (<see cref="ThemeParkIncStandard.ObjectAddress"/>); a word's offset in the object is its address minus this.</summary>
	public const uint ObjectAddress = ThemeParkIncStandard.ObjectAddress;

	// [BIN:TPI-EXE:0x418F60 SAM binding] the addresses below are the binding of the Standard schema 0x996684 from ExitLevel (0xA01A6C) on; ThemeParkIncGuestSettingsTests checks each against its key
	public const uint SmallHappinessChangeAddress = 0xA01A78, MediumHappinessChangeAddress = 0xA01A7C, BigHappinessChangeAddress = 0xA01A80;
	public const uint ExcitementToCostDivisorAddress = 0xA01A90, MinimumEntryFeeAddress = 0xA01A94;
	public const uint CheapPriceMultiplierAddress = 0xA01A98, AveragePriceMultiplierAddress = 0xA01A9C, ExpensivePriceMultiplierAddress = 0xA01AA0;
	public const uint DecisionVariable1Address = 0xA01AA4, DecisionVariable2Address = 0xA01AA8, DecisionVarDistWeightAddress = 0xA01AC4;
	public const uint OpinionForMuchTooHighAddress = 0xA01AEC, OpinionForTooHighAddress = 0xA01AF0, OpinionForTooLowAddress = 0xA01AF4, OpinionForAboutRightAddress = 0xA01AF8;
	public const uint StinkbombLikelihoodAddress = 0xA01B00, PrankHappinessThresholdAddress = 0xA01B04, PrankLikelihoodAddress = 0xA01B08;
	public const uint UmbrellaModifierAddress = 0xA01B5C, WeatherModifierFrequencyAddress = 0xA01B60, SlowestWeatherChangeResponseAddress = 0xA01B64, DiscardBalloonPctModifierAddress = 0xA01B68;
	public const uint ThoughtBubbleHungryAddress = 0xA01B6C, ThoughtBubbleThirstyAddress = 0xA01B70, ThoughtBubbleNeedTheLooAddress = 0xA01B74, ThoughtBubbleSickAddress = 0xA01B78;
	public const uint ThoughtBubbleMessAddress = 0xA01B7C, ThoughtBubbleHappyAddress = 0xA01B80, ThoughtBubbleUnhappyAddress = 0xA01B84;
	public const uint HappinessWeatherAddress = 0xA01B88, ThirstWeatherAddress = 0xA01BBC, HungerWeatherAddress = 0xA01BF0;
	public const uint NumTicketBoothsAddress = 0xA025B4, BusStopAAddress = 0xA025B8, BusStopBAddress = 0xA025C0, BusStopCAddress = 0xA025C8, EntranceAAddress = 0xA025D0, EntranceBAddress = 0xA025D8;
	public const uint TicketBoothAddress = 0xA02610, FirstHourMinutesAddress = 0xA034D4;
	/// <summary>The bytes per <c>TicketBooth[]</c> element (<c>PosX</c>, <c>PosY</c>, <c>Angle</c>; <c>0x5D79B0</c> multiplies by 12) and the elements the schema holds.</summary>
	public const int TicketBoothStride = 12, TicketBoothCapacity = 3;
	/// <summary>The bytes per generation row of a weather array (four floats).</summary>
	public const int WeatherRowStride = 16, Generations = 3;

	/// <summary><c>PeepInfo.Small/Medium/BigHappinessChange</c>, read as bytes by the changers <c>0x6143A0</c> and <c>0x614460</c> (kinds 0, 1, 2).</summary>
	public int SmallHappinessChange { get; init; }
	public int MediumHappinessChange { get; init; }
	public int BigHappinessChange { get; init; }
	/// <summary><c>PeepInfo.ExcitementToCostDivisor</c> and <c>MinimumEntryFee</c>, read unsigned by the verdict <c>0x6157D0</c>.</summary>
	public uint ExcitementToCostDivisor { get; init; }
	public uint MinimumEntryFee { get; init; }
	/// <summary>The verdict's float bands.</summary>
	public float CheapPriceMultiplier { get; init; }
	public float AveragePriceMultiplier { get; init; }
	public float ExpensivePriceMultiplier { get; init; }
	/// <summary>The opinion bytes the verdict records for far too expensive, expensive, cheap and fair.</summary>
	/// <summary>
	/// <c>PeepInfo.DecisionVariable1</c> and <c>DecisionVariable2</c> (the age in days below which a ride counts as new, and its score multiplier) and the seven <c>DecisionVar*Weight</c> words
	/// from <c>0xA01AC4</c> (distance, queue, excitement, thirst, hunger, toilet, illness), which the attraction score <c>0x6101E0</c> reads. <c>DecisionVariable3</c> (indoors in rain) is unread.
	/// </summary>
	public DecisionVariables DecisionVariables { get; init; }
	public DecisionWeights DecisionWeights { get; init; }

	public int OpinionForMuchTooHigh { get; init; }
	public int OpinionForTooHigh { get; init; }
	public int OpinionForTooLow { get; init; }
	public int OpinionForAboutRight { get; init; }
	/// <summary>
	/// State 6's prank words: <c>StinkbombLikelihood</c> (case 0 of the prank switch, against <c>A % 100</c>, unsigned), <c>PrankHappinessThreshold</c> (the prank gate's happiness byte, signed) and
	/// <c>PrankLikelihood</c> (the gate's <c>A % 100</c>, unsigned).
	/// </summary>
	public int StinkbombLikelihood { get; init; }
	public int PrankHappinessThreshold { get; init; }
	public int PrankLikelihood { get; init; }
	/// <summary>The weather rule's <c>UmbrellaModifier</c>, <c>WeatherModifierFrequency</c>, <c>SlowestWeatherChangeResponse</c> and <c>DiscardBalloonPctModifier</c>.</summary>
	public int UmbrellaModifier { get; init; }
	public int WeatherModifierFrequency { get; init; }
	public int SlowestWeatherChangeResponse { get; init; }
	public int DiscardBalloonPctModifier { get; init; }
	/// <summary>The thought pick's thresholds (<c>0x60FC60</c>): integers but the mess share, a float.</summary>
	public int ThoughtBubbleHungry { get; init; }
	public int ThoughtBubbleThirsty { get; init; }
	public int ThoughtBubbleNeedTheLoo { get; init; }
	public int ThoughtBubbleSick { get; init; }
	public float ThoughtBubbleMess { get; init; }
	public int ThoughtBubbleHappy { get; init; }
	public int ThoughtBubbleUnhappy { get; init; }
	/// <summary>The weather rows by generation (kid, parent, grandparent) of happiness, thirst and hunger.</summary>
	public IReadOnlyList<ThemeParkIncWeatherRow> HappinessWeather { get; init; } = [];
	public IReadOnlyList<ThemeParkIncWeatherRow> ThirstWeather { get; init; } = [];
	public IReadOnlyList<ThemeParkIncWeatherRow> HungerWeather { get; init; } = [];
	/// <summary><c>FixedItemInfo.NumTicketBooths</c> (read signed by the loops, unsigned by the modulus) and the booths' cells and angles (<c>TicketBooth[i].PosX/PosY/Angle</c>).</summary>
	public int NumTicketBooths { get; init; }
	public IReadOnlyList<(int X, int Y, int Angle)> TicketBooths { get; init; } = [];
	/// <summary>The bus, heli and mono stops (<c>0x5D79F0</c>: kind 1 B, 2 C, any other A) and the two entrances (<c>0x5D7A70</c>).</summary>
	public (int X, int Y) BusStopA { get; init; }
	public (int X, int Y) BusStopB { get; init; }
	public (int X, int Y) BusStopC { get; init; }
	public (int X, int Y) EntranceA { get; init; }
	public (int X, int Y) EntranceB { get; init; }
	/// <summary><c>FirstHour.Minutes</c> (<c>0xA034D4</c>), the first hour of play state 5's tutorial tips count in.</summary>
	public int FirstHourMinutes { get; init; }

	/// <summary>The word at <paramref name="address"/> of the Standard object.</summary>
	public static int Word( ThemeParkIncSamObject standard, uint address ) => standard[(int)(address - ObjectAddress)];

	/// <summary>The float at <paramref name="address"/> of the Standard object (a float leaf's bits).</summary>
	public static float Float( ThemeParkIncSamObject standard, uint address ) => BitConverter.Int32BitsToSingle( Word( standard, address ) );

	/// <summary>The settings from a loaded Standard object, every word by its address.</summary>
	public static ThemeParkIncGuestSettings FromStandard( ThemeParkIncSamObject standard )
	{
		ArgumentNullException.ThrowIfNull( standard );
		int W( uint address ) => Word( standard, address );
		float F( uint address ) => Float( standard, address );
		ThemeParkIncWeatherRow[] Rows( uint address ) => Enumerable.Range( 0, Generations ).Select( generation =>
		{
			var row = address + (uint)(WeatherRowStride * generation);
			return new ThemeParkIncWeatherRow( F( row ), F( row + 4 ), F( row + 8 ), F( row + 12 ) );
		} ).ToArray();
		(int X, int Y) Cell( uint address ) => (W( address ), W( address + 4 ));
		uint Weight( int index ) => unchecked( (uint)W( DecisionVarDistWeightAddress + (uint)(4 * index) ) );
		return new ThemeParkIncGuestSettings
		{
			SmallHappinessChange = W( SmallHappinessChangeAddress ), MediumHappinessChange = W( MediumHappinessChangeAddress ), BigHappinessChange = W( BigHappinessChangeAddress ),
			ExcitementToCostDivisor = unchecked( (uint)W( ExcitementToCostDivisorAddress ) ), MinimumEntryFee = unchecked( (uint)W( MinimumEntryFeeAddress ) ),
			CheapPriceMultiplier = F( CheapPriceMultiplierAddress ), AveragePriceMultiplier = F( AveragePriceMultiplierAddress ), ExpensivePriceMultiplier = F( ExpensivePriceMultiplierAddress ),
			DecisionVariables = new( unchecked( (uint)W( DecisionVariable1Address ) ), unchecked( (uint)W( DecisionVariable2Address ) ), 0 ),
			DecisionWeights = new( Weight( 0 ), Weight( 1 ), Weight( 2 ), Weight( 3 ), Weight( 4 ), Weight( 5 ), Weight( 6 ) ),
			OpinionForMuchTooHigh = W( OpinionForMuchTooHighAddress ), OpinionForTooHigh = W( OpinionForTooHighAddress ), OpinionForTooLow = W( OpinionForTooLowAddress ),
			OpinionForAboutRight = W( OpinionForAboutRightAddress ),
			StinkbombLikelihood = W( StinkbombLikelihoodAddress ), PrankHappinessThreshold = W( PrankHappinessThresholdAddress ), PrankLikelihood = W( PrankLikelihoodAddress ),
			UmbrellaModifier = W( UmbrellaModifierAddress ), WeatherModifierFrequency = W( WeatherModifierFrequencyAddress ),
			SlowestWeatherChangeResponse = W( SlowestWeatherChangeResponseAddress ), DiscardBalloonPctModifier = W( DiscardBalloonPctModifierAddress ),
			ThoughtBubbleHungry = W( ThoughtBubbleHungryAddress ), ThoughtBubbleThirsty = W( ThoughtBubbleThirstyAddress ), ThoughtBubbleNeedTheLoo = W( ThoughtBubbleNeedTheLooAddress ),
			ThoughtBubbleSick = W( ThoughtBubbleSickAddress ), ThoughtBubbleMess = F( ThoughtBubbleMessAddress ), ThoughtBubbleHappy = W( ThoughtBubbleHappyAddress ),
			ThoughtBubbleUnhappy = W( ThoughtBubbleUnhappyAddress ),
			HappinessWeather = Rows( HappinessWeatherAddress ), ThirstWeather = Rows( ThirstWeatherAddress ), HungerWeather = Rows( HungerWeatherAddress ),
			NumTicketBooths = W( NumTicketBoothsAddress ),
			TicketBooths = Enumerable.Range( 0, TicketBoothCapacity ).Select( index =>
			{
				var booth = TicketBoothAddress + (uint)(TicketBoothStride * index);
				return (W( booth ), W( booth + 4 ), W( booth + 8 ));
			} ).ToArray(),
			BusStopA = Cell( BusStopAAddress ), BusStopB = Cell( BusStopBAddress ), BusStopC = Cell( BusStopCAddress ),
			EntranceA = Cell( EntranceAAddress ), EntranceB = Cell( EntranceBAddress ),
			FirstHourMinutes = W( FirstHourMinutesAddress )
		};
	}

	/// <summary>The settings of a level: the global Standard.sam with the level's over it (<see cref="ThemeParkIncStandard.Load"/>); null without the global file.</summary>
	public static ThemeParkIncGuestSettings? Load( Func<string, byte[]?> read, string level ) =>
		ThemeParkIncStandard.Load( read, level ) is { } standard ? FromStandard( standard ) : null;

	/// <summary>The cell word of booth <paramref name="index"/> (<c>0x5D79B0</c>: <c>(PosY &lt;&lt; 7) + PosX + 1</c>, 16 bits); an index past the array reads the words after it, which this port does not hold, so 0.</summary>
	// [BIN:TPI-EXE:0x5D79B0 booth cell] [0xA02614 + 12i] << 7 + [0xA02610 + 12i] + 1 stored as a word; nothing bounds i
	public ushort BoothCell( int index ) => (uint)index < (uint)TicketBooths.Count ? CellWord( TicketBooths[index].X, TicketBooths[index].Y ) : (ushort)0;

	/// <summary>The cell word of a cell (the original's <c>(y &lt;&lt; 7) + x + 1</c>, 16 bits).</summary>
	public static ushort CellWord( int x, int y ) => unchecked( (ushort)((y << 7) + x + 1) );

	/// <summary>The cell of a cell word: <c>((word − 1) &amp; 0x7F, (word − 1) &gt;&gt; 7)</c>.</summary>
	public static (int X, int Y) CellOf( ushort word ) => ((word - 1) & 0x7F, ((word - 1) & 0xFFFF) >> 7);

	/// <summary>The drop cell of a vehicle kind (<c>0x5D79F0</c>): 1 (the heli) BusStopB, 2 (the mono) BusStopC, any other BusStopA.</summary>
	// [BIN:TPI-EXE:0x5D79F0 stop cell] dec/je: kind 1 reads 0xA025C0/C4, kind 2 0xA025C8/CC, every other kind 0xA025B8/BC
	public (int X, int Y) StopCell( int kind ) => kind switch { 1 => BusStopB, 2 => BusStopC, _ => BusStopA };

	/// <summary>The entrance cell of a flag (<c>0x5D7A70</c>): 0 EntranceA, any other EntranceB.</summary>
	// [BIN:TPI-EXE:0x5D7A70 entrance cell] flag 0 reads 0xA025D0/D4, else 0xA025D8/DC
	public (int X, int Y) EntranceCell( int flag ) => flag == 0 ? EntranceA : EntranceB;
}
