using System.Globalization;

namespace OpenTPW;

/// <summary>Per-type values from <c>PeepTypes[n].PreferredExcitement.StartingCash.BoredomThreshold</c>.</summary>
public readonly record struct GuestType( int PreferredExcitement, int StartingCash, int BoredomThreshold );

/// <summary>
/// Guest balance values. Fields marked "sam" are read from the original balance files
/// (<c>levels/Standard.sam</c>, overridden by <c>levels/&lt;theme&gt;/Standard.sam</c>); fields marked
/// "approximation" have no original source and are OpenTPW choices documented in docs/GUESTS.md.
/// </summary>
public sealed class GuestSettings
{
	// sam: PeepInfo
	public int ExitLevelSeconds { get; set; } = 120;
	public int ExitLevelVarSeconds { get; set; } = 60;
	public int StartingCashVarPercent { get; set; } = 15;
	public int SmallHappinessChange { get; set; } = 5;
	public int MediumHappinessChange { get; set; } = 15;
	/// <summary><c>PeepInfo.ExcitementToCostDivisor</c>: the booth's park value is the attraction points over this plus a draw of up to half of it (tp.exe <c>0x004FF5B0</c>, word <c>0x0078507C</c>).</summary>
	public int ExcitementToCostDivisor { get; set; } = 4;
	/// <summary><c>PeepInfo.MinimumEntryFee</c>: added to the park value; a fee and value both at or below it are always fair (word <c>0x00785080</c>).</summary>
	public int MinimumEntryFee { get; set; } = 20;
	/// <summary><c>PeepInfo.CheapPriceMultiplier</c>: a fee up to this × the park value is cheap (float <c>0x00785084</c>).</summary>
	public float CheapPriceMultiplier { get; set; } = 0.75f;
	/// <summary><c>PeepInfo.AveragePriceMultiplier</c>: from this × the park value the fee is on the expensive side (float <c>0x00785088</c>).</summary>
	public float AveragePriceMultiplier { get; set; } = 1.25f;
	/// <summary><c>PeepInfo.ExpensivePriceMultiplier</c>: from this × the park value the fee is far too expensive (float <c>0x0078508C</c>).</summary>
	public float ExpensivePriceMultiplier { get; set; } = 2.0f;
	/// <summary>
	/// <c>PeepInfo.OpinionForMuchTooHigh</c>, <c>OpinionForTooHigh</c>, <c>OpinionForTooLow</c> and <c>OpinionForAboutRight</c> (the bytes <c>0x007850CC</c>-<c>0x007850D8</c>): what the booth records in the park's perception ring 0
	/// for a verdict of far too expensive, expensive, cheap and fair (<see cref="BoothVerdict.Opinion"/>); null while the settings have none, which records nothing.
	/// </summary>
	public int? OpinionForMuchTooHigh { get; set; }
	public int? OpinionForTooHigh { get; set; }
	public int? OpinionForTooLow { get; set; }
	public int? OpinionForAboutRight { get; set; }
	public int BigHappinessChange { get; set; } = 25;
	public int RideVomitDivisor { get; set; } = 10;
	public int ToiletDesperate { get; set; } = 100;
	public int VomitCapacity { get; set; } = 100;
	/// <summary>Guests per 100 that are pranksters (the constructor's eighth draw; tp.exe settings word <c>0x007850DC</c>). 0 here: none.</summary>
	public int PrankeryLikelihood { get; set; }
	/// <summary>The percent of a stinkbomb prankster's pranks that drop a stinkbomb (tp.exe <c>0x007850E0</c>). 0 here: none.</summary>
	public int StinkbombLikelihood { get; set; }
	public int DecisionDistanceWeight { get; set; } = 1;
	public int DecisionQueueWeight { get; set; } = 1;
	public int DecisionExcitementWeight { get; set; } = 1;
	public int DecisionThirstWeight { get; set; } = 2;
	public int DecisionHungerWeight { get; set; } = 2;
	public int DecisionToiletWeight { get; set; } = 2;
	public int DecisionIllnessWeight { get; set; } = 2;
	/// <summary><c>PeepInfo.DecisionVariable1</c>: days below which a ride is new (settings word <c>+80</c>).</summary>
	public int DecisionNewRideDays { get; set; } = 7;
	/// <summary><c>PeepInfo.DecisionVariable2</c>: the score multiplier of a new ride (<c>+84</c>).</summary>
	public int DecisionNewRideMultiplier { get; set; } = 5;
	/// <summary><c>PeepInfo.DecisionVariable3</c>: the score multiplier of an indoor ride while it rains (<c>+88</c>).</summary>
	public int DecisionIndoorRainMultiplier { get; set; } = 5;
	public int PerfectRide { get; set; } = 25;
	public int GoodRide { get; set; } = 15;
	public int OkRide { get; set; } = 5;
	// sam: PeepTypes (8 types ship; up to 20 allowed by the file comment)
	public List<GuestType> Types { get; set; } = new() { new( 50, 500, 40 ) };
	// sam: Arrival / BankAccountInfo. The arrival code (0x100CB2AC, 0x100CB4E8) reads MinPeople, TimeBetweenArrivals,
	// NewParkBonus and PointsPerVisitor; it does not read Arrival.FixedRate.
	public int ArrivalMinPeople { get; set; } = 1;
	/// <summary>Arrival.TimeBetweenArrivals in timer units of four park turns (about one second).</summary>
	public int ArrivalTimeBetween { get; set; } = 150;
	public int ArrivalNewParkBonus { get; set; } = 20;
	public int ArrivalPointsPerVisitor { get; set; } = 6;
	/// <summary>
	/// Theme Park Inc only: the group size's percentages for rain, storm, snow and heatwave (<c>Arrival.RainPercentageModifier</c>, <c>StormPercentageModifier</c>,
	/// <c>SnowPercentageModifier</c>, <c>HeatwavePercentageModifier</c>). Null when the balance file has not all four, as every Theme Park World file; a settings
	/// object that holds them follows Theme Park Inc's group rule (<see cref="GuestSimulation.ArrivalGroupSize"/>).
	/// </summary>
	public int[]? ArrivalWeatherPercentages { get; set; }
	public int AdmissionFee { get; set; } = 20;
	// sam: FixedItemInfo (cells); null when the balance file has none
	public (int X, int Y)[]? ArrivalLaneA { get; set; }
	public (int X, int Y)[]? ArrivalLaneB { get; set; }
	/// <summary>The park entrance's two cells (FixedItemInfo.EntranceA/B), null when the balance file lacks one; the park fence starts beside them.</summary>
	public (int X, int Y)? EntranceA { get; set; }
	public (int X, int Y)? EntranceB { get; set; }

	// approximation: no original source for the cap (the walk speed is per guest and traced: GuestSpeed, docs/reverse/WALK-plan.md; the needs, starting values and
	// the exit counter are traced: NeedsUpdate, GuestStart, docs/reverse/ORACLE.md F25)
	public int MaximumGuests { get; set; } = 1000;

	/// <summary>The seven decision weights in the order of the original's settings words.</summary>
	public DecisionWeights DecisionWeights => new( (uint)DecisionDistanceWeight, (uint)DecisionQueueWeight, (uint)DecisionExcitementWeight,
		(uint)DecisionThirstWeight, (uint)DecisionHungerWeight, (uint)DecisionToiletWeight, (uint)DecisionIllnessWeight );

	/// <summary>The three decision variables (new-ride days, new-ride multiplier, indoor-in-rain multiplier).</summary>
	public DecisionVariables DecisionVariables => new( (uint)DecisionNewRideDays, (uint)DecisionNewRideMultiplier, (uint)DecisionIndoorRainMultiplier );

	/// <summary>Loads the theme balance: <c>levels/Standard.sam</c> overridden by <c>levels/&lt;theme&gt;/Standard.sam</c>.</summary>
	public static GuestSettings Load( string levelName )
	{
		var values = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in new[] { "/levels/Standard.sam", $"/levels/{levelName}/Standard.sam" } )
		{
			if ( FileSystem.FileExists( path ) )
				ParseSam( FileSystem.ReadAllText( path ), values );
		}
		return FromValues( values );
	}

	/// <summary>Key → whitespace-separated value tokens of one SAM line (comments after the values are dropped).</summary>
	public static void ParseSam( string text, Dictionary<string, string[]> values )
	{
		foreach ( var rawLine in text.Split( '\n' ) )
		{
			var line = rawLine.Trim();
			if ( line.Length == 0 || line[0] == '#' )
				continue;
			var tokens = line.Split( (char[]?)null, StringSplitOptions.RemoveEmptyEntries );
			var numbers = tokens.Skip( 1 ).TakeWhile( token => double.TryParse( token, NumberStyles.Float, CultureInfo.InvariantCulture, out _ ) ).ToArray();
			if ( numbers.Length > 0 )
				values[tokens[0]] = numbers;
		}
	}

	public static GuestSettings FromValues( IReadOnlyDictionary<string, string[]> values )
	{
		var settings = new GuestSettings();
		int Get( string key, int fallback ) => values.TryGetValue( key, out var v ) ? (int)Math.Round( double.Parse( v[0], CultureInfo.InvariantCulture ) ) : fallback;
		settings.ExitLevelSeconds = Get( "PeepInfo.ExitLevel", settings.ExitLevelSeconds );
		settings.ExitLevelVarSeconds = Get( "PeepInfo.ExitLevelVar", settings.ExitLevelVarSeconds );
		settings.StartingCashVarPercent = Get( "PeepInfo.StartingCashVarPc", settings.StartingCashVarPercent );
		settings.SmallHappinessChange = Get( "PeepInfo.SmallHappinessChange", settings.SmallHappinessChange );
		settings.MediumHappinessChange = Get( "PeepInfo.MediumHappinessChange", settings.MediumHappinessChange );
		settings.BigHappinessChange = Get( "PeepInfo.BigHappinessChange", settings.BigHappinessChange );
		// [DATA:levels/Standard.sam:PeepInfo.ExcitementToCostDivisor/MinimumEntryFee/CheapPriceMultiplier/AveragePriceMultiplier/ExpensivePriceMultiplier] the key table's consecutive records 0x0073FF80..0x00740070 fill the words 0x0078507C..0x0078508C read by the booth verdict 0x004FF5B0
		float GetFloat( string key, float fallback ) => values.TryGetValue( key, out var v ) ? float.Parse( v[0], CultureInfo.InvariantCulture ) : fallback;
		settings.ExcitementToCostDivisor = Get( "PeepInfo.ExcitementToCostDivisor", settings.ExcitementToCostDivisor );
		settings.MinimumEntryFee = Get( "PeepInfo.MinimumEntryFee", settings.MinimumEntryFee );
		settings.CheapPriceMultiplier = GetFloat( "PeepInfo.CheapPriceMultiplier", settings.CheapPriceMultiplier );
		settings.AveragePriceMultiplier = GetFloat( "PeepInfo.AveragePriceMultiplier", settings.AveragePriceMultiplier );
		settings.ExpensivePriceMultiplier = GetFloat( "PeepInfo.ExpensivePriceMultiplier", settings.ExpensivePriceMultiplier );
		// [DATA:levels/Standard.sam:PeepInfo.OpinionFor*] the key table's records 0x00740430..0x007404E4 (schema indices 32 to 35) fill the bytes 0x007850CC..0x007850D8 the booth verdict records in perception ring 0
		int? Opinion( string key ) => values.TryGetValue( key, out var v ) ? (int)Math.Round( double.Parse( v[0], CultureInfo.InvariantCulture ) ) : null;
		settings.OpinionForMuchTooHigh = Opinion( "PeepInfo.OpinionForMuchTooHigh" );
		settings.OpinionForTooHigh = Opinion( "PeepInfo.OpinionForTooHigh" );
		settings.OpinionForTooLow = Opinion( "PeepInfo.OpinionForTooLow" );
		settings.OpinionForAboutRight = Opinion( "PeepInfo.OpinionForAboutRight" );
		settings.RideVomitDivisor = Math.Max( 1, Get( "PeepInfo.RideVomitDivisor", settings.RideVomitDivisor ) );
		settings.ToiletDesperate = Get( "PeepInfo.ToiletDesparate", settings.ToiletDesperate );
		settings.VomitCapacity = Get( "PeepInfo.VomitCapacity", settings.VomitCapacity );
		// [DATA:levels/Standard.sam:PeepInfo.PrankeryLikelihood/StinkbombLikelihood] the key table's words 0x007850DC and 0x007850E0 (schema indices 36 and 37); the file's PrankHappinessThreshold and PrankLikelihood have no key in tp.exe (the code uses 15 and the flag against R % 101)
		settings.PrankeryLikelihood = Get( "PeepInfo.PrankeryLikelihood", settings.PrankeryLikelihood );
		settings.StinkbombLikelihood = Get( "PeepInfo.StinkbombLikelihood", settings.StinkbombLikelihood );
		settings.DecisionDistanceWeight = Get( "PeepInfo.DecisionVarDistWeight", settings.DecisionDistanceWeight );
		settings.DecisionQueueWeight = Get( "PeepInfo.DecisionVarQueueWeight", settings.DecisionQueueWeight );
		settings.DecisionExcitementWeight = Get( "PeepInfo.DecisionVarExcitementWeight", settings.DecisionExcitementWeight );
		settings.DecisionThirstWeight = Get( "PeepInfo.DecisionVarThirstWeight", settings.DecisionThirstWeight );
		settings.DecisionHungerWeight = Get( "PeepInfo.DecisionVarHungerWeight", settings.DecisionHungerWeight );
		settings.DecisionToiletWeight = Get( "PeepInfo.DecisionVarToiletWeight", settings.DecisionToiletWeight );
		settings.DecisionIllnessWeight = Get( "PeepInfo.DecisionVarIllnessWeight", settings.DecisionIllnessWeight );
		settings.DecisionNewRideDays = Get( "PeepInfo.DecisionVariable1", settings.DecisionNewRideDays );
		settings.DecisionNewRideMultiplier = Get( "PeepInfo.DecisionVariable2", settings.DecisionNewRideMultiplier );
		settings.DecisionIndoorRainMultiplier = Get( "PeepInfo.DecisionVariable3", settings.DecisionIndoorRainMultiplier );
		settings.PerfectRide = Get( "PeepInfo.PerfectRide", settings.PerfectRide );
		settings.GoodRide = Get( "PeepInfo.GoodRide", settings.GoodRide );
		settings.OkRide = Get( "PeepInfo.OKRide", settings.OkRide );
		settings.ArrivalMinPeople = Get( "Arrival.MinPeople", settings.ArrivalMinPeople );
		settings.ArrivalTimeBetween = Math.Max( 1, Get( "Arrival.TimeBetweenArrivals", settings.ArrivalTimeBetween ) );
		settings.ArrivalNewParkBonus = Get( "Arrival.NewParkBonus", settings.ArrivalNewParkBonus );
		settings.ArrivalPointsPerVisitor = Get( "Arrival.PointsPerVisitor", settings.ArrivalPointsPerVisitor );
		// [BIN:TPI-EXE:0x005BCDEE group size] Theme Park Inc's group size scales by one of four percentage words (0x00A02244..0x00A02250: Arrival.Rain/Storm/Snow/HeatwavePercentageModifier) chosen by the weather thing's state; Theme Park World's files carry none
		var weatherKeys = new[] { "Arrival.RainPercentageModifier", "Arrival.StormPercentageModifier", "Arrival.SnowPercentageModifier", "Arrival.HeatwavePercentageModifier" };
		if ( weatherKeys.All( values.ContainsKey ) )
			settings.ArrivalWeatherPercentages = weatherKeys.Select( key => Get( key, 100 ) ).ToArray();
		settings.AdmissionFee = Get( "BankAccountInfo.InitialAdmissionFee", settings.AdmissionFee );

		var types = new List<GuestType>();
		for ( var index = 0; index < 20; index++ )
		{
			if ( !values.TryGetValue( $"PeepTypes[{index}].PreferredExcitement.StartingCash.BoredomThreshold", out var v ) || v.Length < 3 )
				break;
			types.Add( new GuestType( int.Parse( v[0], CultureInfo.InvariantCulture ), int.Parse( v[1], CultureInfo.InvariantCulture ), int.Parse( v[2], CultureInfo.InvariantCulture ) ) );
		}
		if ( types.Count > 0 )
			settings.Types = types;

		(int X, int Y)? Cell( string name ) => values.ContainsKey( $"FixedItemInfo.{name}PosX" ) && values.ContainsKey( $"FixedItemInfo.{name}PosY" )
			? (Get( $"FixedItemInfo.{name}PosX", 0 ), Get( $"FixedItemInfo.{name}PosY", 0 )) : null;
		(int X, int Y)[]? Lane( string side )
		{
			var cells = new[] { Cell( $"BusStop{side}" ), Cell( $"CrossingBSSide{side}" ), Cell( $"CrossingParkSide{side}" ), Cell( $"TicketBooth{side}" ), Cell( $"Entrance{side}" ) };
			return cells.All( cell => cell != null ) ? cells.Select( cell => cell!.Value ).ToArray() : null;
		}
		settings.ArrivalLaneA = Lane( "A" );
		settings.ArrivalLaneB = Lane( "B" );
		settings.EntranceA = Cell( "EntranceA" );
		settings.EntranceB = Cell( "EntranceB" );
		return settings;
	}
}
