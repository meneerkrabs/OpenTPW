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
	public int BigHappinessChange { get; set; } = 25;
	public int RideVomitDivisor { get; set; } = 10;
	public int ToiletDesperate { get; set; } = 100;
	public int VomitCapacity { get; set; } = 100;
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
	public int AdmissionFee { get; set; } = 20;
	// sam: FixedItemInfo (cells); null when the balance file has none
	public (int X, int Y)[]? ArrivalLaneA { get; set; }
	public (int X, int Y)[]? ArrivalLaneB { get; set; }

	// approximation: no original source (the walk speed is per guest and traced: GuestSpeed, docs/reverse/WALK-plan.md; the needs, starting values and
	// the exit counter are traced: NeedsUpdate, GuestStart, docs/reverse/ORACLE.md F25)
	public float EnergyDrainPerSecond { get; set; } = 0.15f;
	public float EnergyRecoveryPerSecond { get; set; } = 1.0f;
	public float NeedThoughtLevel { get; set; } = 70;
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
		settings.RideVomitDivisor = Math.Max( 1, Get( "PeepInfo.RideVomitDivisor", settings.RideVomitDivisor ) );
		settings.ToiletDesperate = Get( "PeepInfo.ToiletDesparate", settings.ToiletDesperate );
		settings.VomitCapacity = Get( "PeepInfo.VomitCapacity", settings.VomitCapacity );
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
		return settings;
	}
}
