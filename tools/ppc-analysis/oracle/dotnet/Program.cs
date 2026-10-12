using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTPW;
using OpenTPW.Tests;

// JSON-lines bridge for the PowerPC differential oracle (docs/reverse/ORACLE.md).
// Input: one request per line, {"fn": name, ...arguments}. Output: one JSON result per line, same order.
// {"fn": "capabilities"} lists the functions this build of OpenTPW can evaluate; the Python side compares
// a target against C# only when its function is listed, and against the plan's reference formula otherwise.
// Hooks for later implementations (DET-I2 world RNG, WALK-I speed, COAST-I rating) are added here as cases.

var functions = new Dictionary<string, Func<JsonObject, JsonNode?>>
{
	["queue_limit"] = QueueLimit,
	["can_change_cell_type"] = CanChangeCellType,
	["set_cell_type"] = SetCellType,
	["unbounce"] = Unbounce,
	["bounce"] = Bounce,
	["guest_speed_limits"] = GuestSpeedLimits,
	["guest_speed_smoothing"] = GuestSpeedSmoothing,
	["guest_ingredient_boost"] = GuestIngredientBoost,
	["arrival_group"] = ArrivalGroup,
	["park_points"] = ParkPoints,
	["object_scrap_value"] = ObjectScrapValue,
	["loan_offers"] = LoanOffers,
	["loan_term"] = LoanTerm,
	["loan_take"] = LoanTake,
	["loan_payoff"] = LoanPayoff,
	["queue_failure"] = QueueFailure,
	["queue_join_difference"] = QueueJoinDifference,
	["queue_join_gate"] = QueueJoinGate,
	["shop_sale_litter"] = ShopSaleLitter,
	["staff_training"] = StaffTraining,
	["repair_timer"] = RepairTimer,
	["staff_vitals"] = StaffVitalsStep,
	["staff_rest_seek"] = StaffRestSeek,
	["staff_grievance"] = StaffGrievance,
	["staff_strike_month"] = StaffStrikeMonth,
	["profit_ticket"] = ProfitTicket,
	["local_tickets"] = LocalTickets,
	["global_tickets"] = GlobalTickets,
};

using var input = new StreamReader( Console.OpenStandardInput() );
using var output = new StreamWriter( Console.OpenStandardOutput() ) { AutoFlush = false };
string? line;
while ( (line = input.ReadLine()) != null )
{
	if ( string.IsNullOrWhiteSpace( line ) )
		continue;
	var request = JsonNode.Parse( line )!.AsObject();
	var name = (string)request["fn"]!;
	JsonNode? result;
	if ( name == "capabilities" )
		result = new JsonArray( functions.Keys.Select( key => (JsonNode)JsonValue.Create( key )! ).ToArray() );
	else if ( functions.TryGetValue( name, out var function ) )
	{
		// An exception is an outcome to compare (the original has no exceptions), not a bridge failure.
		try { result = function( request ); }
		catch ( Exception exception ) { result = new JsonObject { ["exception"] = (exception.InnerException ?? exception).GetType().Name }; }
	}
	else
		result = new JsonObject { ["error"] = $"unknown function {name}" };
	output.WriteLine( result?.ToJsonString() ?? "null" );
}
output.Flush();
return 0;

// RideVisitorBridge.ComputeQueueLimit; QWTC arrives as IEEE single bits so no decimal round trip intervenes.
static JsonNode? QueueLimit( JsonObject a )
{
	var qwtc = BitConverter.Int32BitsToSingle( (int)(uint)a["qwtc_bits"]! );
	var parameters = new QueueParameters( (bool)a["has_queue"]!, false, false, qwtc, (int)a["speed"]!, (int)a["init_speed"]!,
		(int)a["capacity"]!, (int)a["duration"]! );
	return JsonValue.Create( RideVisitorBridge.ComputeQueueLimit( parameters ) );
}

static JsonNode? CanChangeCellType( JsonObject a ) =>
	JsonValue.Create( ParkCellMap.CanChangeCellType( (byte)(int)a["old"]!, (byte)(int)a["type"]!, (bool)a["last_cell"]! ) );

// The order of ParkPathBuilder.Validate/Run for one written cell: CanChangeCellType, then the same nonzero type
// (placement bump, no charge), then the park economy's money test and spend (ParkEconomy.TrySpendCell).
static JsonNode? SetCellType( JsonObject a )
{
	var old = (byte)(int)a["old"]!;
	var type = (byte)(int)a["type"]!;
	if ( !ParkCellMap.CanChangeCellType( old, type, (bool)a["last_cell"]! ) )
		return Outcome( false, 0, false );
	if ( type != 0 && type == old )
		return Outcome( true, 0, true );
	CellPurchase? kind = type switch { 1 => CellPurchase.Path, 3 => CellPurchase.Queue, _ => null };
	if ( kind == null )
		return Outcome( true, 0, false );
	var text = EconomyTestData.StandardText( (int)a["balance"]! )
		.Replace( "Costs.QueueCell 75\nCosts.PathCell 20\n", $"Costs.QueueCell {(int)a["queue_cost"]!}\nCosts.PathCell {(int)a["path_cost"]!}\n" );
	var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
	var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
	var before = park.Balance;
	var result = park.TrySpendCell( kind.Value );
	return Outcome( result == ParkEconomy.PurchaseResult.Ok, before - park.Balance, false );
}

static JsonObject Outcome( bool ok, long charged, bool bumped ) => new() { ["ok"] = ok, ["charged"] = charged, ["bumped"] = bumped };

// RideVisitorBridge.TakeUnbounced (the UNBOUNCE scan) through reflection: it is internal and static. The slot
// table arrives whole, free slots (visitor 0) included; "remaining" counts the occupied slots (+108).
static JsonNode? Unbounce( JsonObject a )
{
	var method = typeof( RideVisitorBridge ).GetMethod( "TakeUnbounced", BindingFlags.NonPublic | BindingFlags.Static )
		?? throw new MissingMethodException( "RideVisitorBridge.TakeUnbounced" );
	var slots = a["slots"]!.AsArray().Select( slot => ((int)slot![0]!, (double)(int)slot[1]!, (double)(int)slot[2]!) ).ToList();
	var guest = (int)method.Invoke( null, new object[] { slots, (double)(int)a["now"]!, false } )!;
	return new JsonObject { ["guest"] = guest, ["remaining"] = slots.Count( slot => slot.Item1 != 0 ) };
}

// RideVisitorBridge.TakeBounceSlot (the BOUNCE slot search) through reflection on the whole table; the deadline is
// computed as the bridge's BOUNCE case does (now + dur × 1000 ms). The result lists the table padded to its size.
static JsonNode? Bounce( JsonObject a )
{
	var method = typeof( RideVisitorBridge ).GetMethod( "TakeBounceSlot", BindingFlags.NonPublic | BindingFlags.Static )
		?? throw new MissingMethodException( "RideVisitorBridge.TakeBounceSlot" );
	var slots = a["slots"]!.AsArray().Select( slot => ((int)slot![0]!, (double)(int)slot[1]!, (double)(int)slot[2]!) ).ToList();
	var capacity = slots.Count;
	var now = (double)(int)a["now"]!;
	var ok = (bool)method.Invoke( null, new object[] { slots, capacity, (int)a["visitor"]!, now + (int)a["dur"]! * 1000.0, now } )!;
	while ( slots.Count < capacity )
		slots.Add( default );
	var table = new JsonArray( slots.Select( slot => (JsonNode)new JsonArray( slot.Item1, (long)slot.Item2, (long)slot.Item3 ) ).ToArray() );
	return new JsonObject { ["ok"] = ok ? 1 : 0, ["count"] = slots.Count( slot => slot.Item1 != 0 ), ["slots"] = table };
}

// GuestSpeed.Caps (0xffe38): max speed +28 and max force +24 for a smoothed speed s, passed as IEEE single bits.
static JsonNode? GuestSpeedLimits( JsonObject a )
{
	var (speed, force) = GuestSpeed.Caps( BitConverter.Int32BitsToSingle( (int)(uint)a["s_bits"]! ) );
	return new JsonObject { ["force"] = force, ["speed"] = speed };
}

// GuestSpeed.Update (0xe6b28 with 0xffe38): the smoothed s (as bits), the decayed boost and the caps.
static JsonNode? GuestSpeedSmoothing( JsonObject a )
{
	var (s, boost, speed, force) = GuestSpeed.Update( BitConverter.Int32BitsToSingle( (int)(uint)a["s_bits"]! ), (int)a["base"]!,
		(int)a["bonus"]!, (ushort)(int)a["boost"]! );
	return new JsonObject { ["s_bits"] = (uint)BitConverter.SingleToInt32Bits( s ), ["boost"] = (int)boost, ["force"] = force, ["speed"] = speed };
}

// GuestSpeed.RaiseBoost (0xeb0cc..0xeb0f4 in the outcome routine 0xeaaf8): the boost after a use of an object with the given
// special ingredient (type +344) and amount byte (ride +404).
static JsonNode? GuestIngredientBoost( JsonObject a )
{
	var boost = GuestSpeed.RaiseBoost( (ushort)(int)a["boost"]!, (int)a["ingredient"]!, (int)a["amount"]! );
	return new JsonObject { ["boost"] = (int)boost };
}

// GuestSimulation.ArrivalGroupSize (0xcb4e8) on a fresh simulation. World mode 4 halts arrivals; the counted guests fill
// the guest list (a field) through reflection (only its count is read). The weather thing's +48 word (mCurrentDrops, game +0x1da724)
// is passed as the drops-falling flag (word > 0 selects the factor 0.8, F9). The GameType mode word 1 (limit_mode) is passed as OnlineVisit
// (the 500 cut; ARRIVAL-002 assumes a visit is GameType 1).
static JsonNode? ArrivalGroup( JsonObject a )
{
	var settings = new GuestSettings
	{
		ArrivalMinPeople = (int)a["min_people"]!,
		ArrivalNewParkBonus = (int)a["bonus"]!,
		ArrivalPointsPerVisitor = (int)a["per_visitor"]!,
	};
	var points = (int)a["points"]!;
	var halted = (int)a["world_mode"]! == 4;
	var dropsFalling = (int)a["obj48"]! > 0;
	var online = (int)a["limit_mode"]! == 1;
	var simulation = new GuestSimulation( new GuestPathGrid( 1, 1 ), settings, 1 ) { ParkPoints = () => points, ArrivalsHalted = () => halted, WeatherDropsFalling = () => dropsFalling, OnlineVisit = online };
	var field = typeof( GuestSimulation ).GetField( "guests", BindingFlags.NonPublic | BindingFlags.Instance )
		?? throw new MissingFieldException( "GuestSimulation.guests" );
	((List<Guest>)field.GetValue( simulation )!).AddRange( Enumerable.Repeat<Guest>( null!, (int)a["guests"]! ) );
	return JsonValue.Create( simulation.ArrivalGroupSize() );
}

// ParkEconomy.ParkPoints (0xc24c0) on an economy built from the input: one synthetic object type per input type (kind 0 -> Ride,
// 2 -> Sideshow, any other kind -> Shop, the C# reading ARRIVAL-003; AttractionValue, NewAttractionDecayTime, NewBonus and
// Bumper.WhichTrackType (type +0x9C) from the input), one RegisterExisting object per thing. The economy clock is a read-only
// property, so its setter is reached through reflection to put each object's BuiltTick on the first tick of its build turn and the
// economy on the first tick of turn "now". The type's first-purchase turn (park record +28) is then set to the input's value, as a
// loaded save restores its counters after its objects: it can stem from a sold copy or be unset (0) with later copies standing;
// ParkPointsTests covers how purchases set it. The input's open flag stands for the original's counts-test (0xdc834) and is passed as
// the object's open state (thing +0x64); the test's other conditions have no C# state (ARRIVAL-003, F10) and are not in the inputs.
static JsonNode? ParkPoints( JsonObject a )
{
	var infos = a["types"]!.AsArray().Select( type =>
	{
		var kind = (int)type!["kind"]! switch { 0 => ParkObjectKind.Ride, 2 => ParkObjectKind.Sideshow, _ => ParkObjectKind.Shop };
		return new EconomyObjectInfo( (int)type["id"]!, $"Type {(int)type["id"]!}", kind, ResearchCategory.Ride, 0,
			new[] { new UpgradeLevelInfo( 0, 0, 0, 0, 0, new[] { 0 } ) }, null, null, null, 0, 0, 0, 0, 0, (int)type["value"]!, "synthetic.wad",
			0, 0, (int)type["decay"]!, type["bonus"]!.AsArray().Select( bonus => (int)bonus! ).ToArray(), (int)type["track_type"]! );
	} ).ToList();
	var park = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( infos ), ParkGameMode.FullSimulation, 1 );
	var tick = typeof( ParkEconomy ).GetProperty( "Tick" )!.GetSetMethod( nonPublic: true )
		?? throw new MissingMethodException( "ParkEconomy.Tick setter" );
	foreach ( var thing in a["things"]!.AsArray() )
	{
		tick.Invoke( park, new object[] { ParkCalendar.TickOfTurn( (long)(int)thing!["built"]! ) } );
		park.RegisterExisting( (int)thing["type"]! ).IsOpen = (bool)thing["open"]!;
	}
	foreach ( var type in a["types"]!.AsArray() )
	{
		var key = ParkCounters.FirstPurchaseTurn( (int)type!["id"]! );
		park.Counters.Add( key, (long)(uint)(int)type["first"]! - park.Counters[key] );
	}
	tick.Invoke( park, new object[] { ParkCalendar.TickOfTurn( (long)(int)a["now"]! ) } );
	return JsonValue.Create( park.ParkPoints() );
}

// ParkEconomy.ScrapValue (the sale refund 0xe25e8, ORACLE F21) for one synthetic object type whose upgrade levels carry the
// input's costs (Upgrades[n].CostOfUpgrade) and percentages (Upgrades[n].ScrapValueYear1..4). The economy clock is a read-only
// property, so its setter is reached through reflection: the object is registered on the first tick of its build turn, then the
// economy is put on the first tick of turn "now". The input's use count (thing +412) is ParkObjectState.TimesUsed.
static JsonNode? ObjectScrapValue( JsonObject a )
{
	var costs = a["cost"]!.AsArray().Select( cost => (long)cost! ).ToArray();
	var levels = a["pct"]!.AsArray().Select( ( row, level ) => new UpgradeLevelInfo( level, costs[level], 0, 0, 0, row!.AsArray().Select( value => (int)value! ).ToArray() ) ).ToArray();
	var info = new EconomyObjectInfo( 0x101, "Type 257", ParkObjectKind.Ride, ResearchCategory.Ride, 0, levels, null, null, null, 0, 0, 0, 0, 0, 0, "synthetic.wad" );
	var park = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { info } ), ParkGameMode.FullSimulation, 1 );
	var tick = typeof( ParkEconomy ).GetProperty( "Tick" )!.GetSetMethod( nonPublic: true )
		?? throw new MissingMethodException( "ParkEconomy.Tick setter" );
	tick.Invoke( park, new object[] { ParkCalendar.TickOfTurn( (long)a["built"]! ) } );
	var item = park.RegisterExisting( 0x101 );
	item.Level = (int)a["level"]!;
	item.TimesUsed = (long)a["used"]!;
	tick.Invoke( park, new object[] { ParkCalendar.TickOfTurn( (long)a["now"]! ) } );
	return JsonValue.Create( park.ScrapValue( item ) );
}

// LoanMath.MonthlyRepayment (the monthly instalment at loan setup 0xcb7a8, ORACLE F12) for each [amount, APR, months] offer;
// null where C# throws (months below 1).
static JsonNode? LoanOffers( JsonObject a )
{
	var results = new JsonArray();
	foreach ( var offer in a["offers"]!.AsArray() )
	{
		try { results.Add( JsonValue.Create( LoanMath.MonthlyRepayment( (long)offer![0]!, (int)offer[1]!, (int)offer[2]! ) ) ); }
		catch ( ArgumentOutOfRangeException ) { results.Add( null ); }
	}
	return new JsonObject { ["monthly"] = results };
}

// The loan loop of ParkEconomy.EndMonth (the monthly step 0xcc21c) on the real ParkLedger and LoanMath: per month every
// outstanding loan, in index order, pays exactly M with LoanMath.Pay and posts it as LoanPayments, the instalment's interest
// share (LoanMath.InstalmentInterest) lowers the year's profit, and a loan with no months left ends. The loans arrive as
// opened with the given monthly instalment (the original's M, or any other). ParkEconomyTests run the same loop through
// EndMonth itself.
static JsonNode? LoanTerm( JsonObject a )
{
	var ledger = new ParkLedger( (long)a["balance"]! );
	var accounts = a["loans"]!.AsArray().Select( ( loan, index ) => new LoanAccount( index, 0, (long)loan!["amount"]!, (int)loan["apr"]!,
		(int)loan["months"]!, (long)loan["monthly"]!, 0 ) ).ToList();
	var months = new JsonArray();
	for ( var step = 0; step < (int)a["steps"]!; step++ )
	{
		for ( var index = 0; index < accounts.Count; )
		{
			var (account, paid) = LoanMath.Pay( accounts[index] );
			ledger.Post( LedgerCategory.LoanPayments, paid );
			ledger.ChargeLoanInterest( LoanMath.InstalmentInterest( account.MonthlyRepayment, account.Months, account.OriginalAmount ) );
			if ( account.MonthsRemaining <= 0 )
				accounts.RemoveAt( index );
			else
				accounts[index++] = account;
		}
		months.Add( new JsonObject { ["balance"] = ledger.Balance, ["profit"] = ledger.ProfitThisYear, ["active"] = accounts.Count } );
	}
	return new JsonObject { ["months"] = months };
}

// ParkEconomy.TakeLoan (0xcc904, ECON-005) on an economy whose only offer is the given amount: the changes in balance, year
// profit and the money-in total of the month, and whether the offer was taken. With taken_before the offer is taken first, so
// the second take is the refused one (the original's bought flag).
static JsonNode? LoanTake( JsonObject a )
{
	var amount = (long)a["amount"]!;
	var text = System.Text.RegularExpressions.Regex.Replace( EconomyTestData.StandardText( (int)a["cash"]! ), @"^LoanInfo\[\d+\]\..*\n", "",
		System.Text.RegularExpressions.RegexOptions.Multiline )
		+ $"LoanInfo[0].LoanAmount\t{amount}\nLoanInfo[0].APRInPercent\t20\nLoanInfo[0].RepaymentPeriodInMonths\t36\nLoanInfo[0].Lendername\t0\n";
	var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
	var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
	if ( (bool)a["taken_before"]! )
		park.TakeLoan( 0 );
	long MoneyIn() => park.Ledger.CurrentMonth( 0, 0 ).MoneyIn;
	var (balance, profit, moneyIn) = (park.Balance, park.Ledger.ProfitThisYear, MoneyIn());
	bool taken;
	try { park.TakeLoan( 0 ); taken = true; }
	catch ( InvalidOperationException ) { taken = false; }
	return new JsonObject { ["taken"] = taken, ["balance"] = park.Balance - balance, ["profit"] = park.Ledger.ProfitThisYear - profit,
		["money_in"] = MoneyIn() - moneyIn };
}

// ParkEconomy.TryRepayLoan (0xccc78, ORACLE F13) on a bank with the given balance, year profit and one bought loan (any
// instalment, so the original's M and the loan's months repaid are given): whether it was repaid, and the balance, year
// profit, money-out total, bought flag and months repaid afterwards. A loan that is not bought throws (no loan to repay).
static JsonNode? LoanPayoff( JsonObject a )
{
	var park = EconomyTestData.Park( initialCash: 0 );
	var accounts = (bool)a["bought"]!
		? new[] { new LoanAccount( 0, 0, (long)a["amount"]!, (int)a["apr"]!, (int)a["months"]!, (long)a["monthly"]!, (int)a["repaid"]! ) }
		: Array.Empty<LoanAccount>();
	typeof( ParkEconomy ).GetMethod( "ImportOriginalBank", BindingFlags.NonPublic | BindingFlags.Instance )!
		.Invoke( park, new object[] { (long)a["bal"]!, 20, (long)a["profit"]!, accounts } );
	bool taken;
	try { taken = park.TryRepayLoan( 0 ); }
	catch ( InvalidOperationException ) { taken = false; }
	var left = park.Loans.FirstOrDefault();
	return new JsonObject { ["taken"] = taken ? 1 : 0, ["balance"] = park.Balance, ["profit"] = park.Ledger.ProfitThisYear,
		["bought"] = left == null ? 0 : 1, ["repaid"] = left?.MonthsRepaid ?? 0, ["money_out"] = park.Ledger.CurrentMonth( 0, 0 )[LedgerCategory.LoanPayments] };
}

// A real RideVisitorBridge for the join excitement rules (ORACLE.md F15 and F16): type +1960 (Info.WhichUIType) 2 is a sideshow,
// anything else a ride or facility as OriginalObjectRuntime maps them; track type 3 gives the coaster source (0 until its controller
// exists, else trunc(0.5 E + 50)); the ride record's speed word, duration byte and design values come through QueueParameters; the
// sideshow's win byte is 100 minus the chance of losing; the wear gauge is the single of the original's float +64. The thing behind
// ride +36 has no C# counterpart and is not part of the request.
static RideVisitorBridge JoinBridge( JsonObject a )
{
	var kind = (int)a["kind"]! == 2 ? RideVisitorKind.Sideshow : RideVisitorKind.Ride;
	var bridge = new RideVisitorBridge( 1, "Oracle", kind, 4, (int)a["level"]!, 10, (bool)a["relief"]! ? GuestNeeds.Toilet : GuestNeeds.None )
	{
		Parameters = new QueueParameters( true, false, false, 130, (int)a["speed"]!, (int)a["init_speed"]!, 4, (int)a["dur"]!, (int)a["init_dur"]! ),
		Sideshow = new SideshowTerms( (int)a["goods"]!, (int)a["price"]!, 100 - (int)a["win"]! ),
		WearGauge = BitConverter.Int32BitsToSingle( (int)(uint)a["gauge_bits"]! )
	};
	if ( (int)a["track"]! == 3 )
	{
		var rating = BitConverter.Int32BitsToSingle( (int)(uint)a["e_bits"]! );
		var controller = (bool)a["ctrl"]!;
		bridge.ExcitementSource = () => controller ? CoasterRating.GuestExcitement( rating ) : 0;
	}
	return bridge;
}

// 0xdfe34: RideVisitorBridge.ReliefWornOut, the relief flag and the wear gauge.
static JsonNode? QueueFailure( JsonObject a )
{
	var bridge = new RideVisitorBridge( 1, "Oracle", RideVisitorKind.Facility, 1, 0, 0, (bool)a["relief"]! ? GuestNeeds.Toilet : GuestNeeds.None )
	{
		WearGauge = BitConverter.Int32BitsToSingle( (int)(uint)a["gauge_bits"]! )
	};
	return JsonValue.Create( bridge.ReliefWornOut ? 1 : 0 );
}

// 0xe9a84: RideVisitorBridge.JoinDifference for the guest's preferred excitement.
static JsonNode? QueueJoinDifference( JsonObject a ) => JsonValue.Create( JoinBridge( a ).JoinDifference( (int)a["pref"]! ) );

// The state-10 gate (0xeccb0): RideVisitorBridge.ExcitementRefusal with the difference it computed.
static JsonNode? QueueJoinGate( JsonObject a )
{
	var bridge = JoinBridge( a );
	var pref = (int)a["pref"]!;
	var outcome = bridge.ExcitementRefusal( pref ) switch { null => "pass", QueueJoinResult.NotExcitingEnough => "bored", QueueJoinResult.TooExciting => "scared", var other => other.ToString()! };
	return new JsonObject { ["outcome"] = outcome, ["difference"] = bridge.JoinDifference( pref ) };
}

// ParkEconomy.TryBuy (ECON-020, ORACLE F18): the change of the park's litter store (LitterScaled, 1/100 litter item) caused by one sale in
// a shop whose UsageInfo.LitterEffect is the input. The store has no per-guest meter and no floor or ceiling.
static JsonNode? ShopSaleLitter( JsonObject a )
{
	var shop = new EconomyObjectInfo( 9001, "Shop", ParkObjectKind.Shop, ResearchCategory.Ride, 0,
		new[] { new UpgradeLevelInfo( 0, 0, 0, 0, 0, new[] { 0 } ) }, 30, 20, null, 2, 0, 0, 0, (int)a["effect"]!, 0, "synthetic.wad" );
	var park = new ParkEconomy( EconomyTestData.Settings(), new EconomyObjectCatalog( new[] { shop } ), ParkGameMode.FullSimulation, 1 );
	var item = park.RegisterExisting( 9001 );
	var before = park.LitterScaled;
	if ( !park.TryBuy( item.Id, 1000, out _ ) )
		throw new InvalidOperationException( "the sale was refused" );
	return JsonValue.Create( park.LitterScaled - before );
}

// ParkObjectives.CheckGoldenTickets (the local ticket check 0xd31d0, ORACLE F23) on a park whose bank holds the given year profit
// (ImportOriginalBank, like a loaded original save) and whose Standard.sam has the given GoldenTicketLocal.ProfitYear: whether the
// yearly profit ticket is newly won. The other tickets are not reached on an empty park and are not looked at.
static JsonNode? ProfitTicket( JsonObject a )
{
	var text = System.Text.RegularExpressions.Regex.Replace( EconomyTestData.StandardText(), @"^GoldenTicketLocal\.ProfitYear .*$",
		$"GoldenTicketLocal.ProfitYear {(int)a["threshold"]!}", System.Text.RegularExpressions.RegexOptions.Multiline );
	var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
	var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
	typeof( ParkEconomy ).GetMethod( "ImportOriginalBank", BindingFlags.NonPublic | BindingFlags.Instance )!
		.Invoke( park, new object[] { 0L, 20, (long)(int)a["profit"]!, Array.Empty<LoanAccount>() } );
	var won = park.Objectives.CheckGoldenTickets( park.Counters, park.GuestStatistics, park.Research, park.Ledger.ProfitThisYear, Array.Empty<long>() );
	return JsonValue.Create( won.Contains( GoldenTicketKind.ProfitYear ) );
}

// ParkObjectives.CheckGoldenTickets for the six local tickets (0xd31d0, ORACLE F23): the Standard.sam keys are replaced, the guest
// statistics come from a stub, the profit from the imported bank and the monthly admissions are `age` closed months whose last
// RecentVisitorMonths sum to `recent`. Returns the won ticket indices (the enum order is the original's ticket order).
static JsonNode? LocalTickets( JsonObject a )
{
	var text = EconomyTestData.StandardText();
	foreach ( var (key, value) in new[] { ("Visitors", "visitors_limit"), ("PeopleInPark", "people_limit"), ("Happiness", "happiness_limit"),
		("AtLeastThisManyHappyPeople", "happy_people_limit"), ("ProfitYear", "threshold"), ("RecentVisitors", "recent_limit"), ("RecentVisitorMonths", "months_limit") } )
		text = System.Text.RegularExpressions.Regex.Replace( text, $@"^GoldenTicketLocal\.{key} .*$", $"GoldenTicketLocal.{key} {(int)a[value]!}",
			System.Text.RegularExpressions.RegexOptions.Multiline );
	var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
	var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
	typeof( ParkEconomy ).GetMethod( "ImportOriginalBank", BindingFlags.NonPublic | BindingFlags.Instance )!
		.Invoke( park, new object[] { 0L, 20, (long)(int)a["profit"]!, Array.Empty<LoanAccount>() } );
	park.Counters.Add( ParkCounters.Admissions, (int)a["visitors"]! );
	var months = new long[Math.Max( 0, (int)a["age"]! )];
	if ( months.Length > 0 )
		months[^1] = (int)a["recent"]!;
	var average = BitConverter.Int32BitsToSingle( (int)(uint)a["avg_bits"]! );
	var won = park.Objectives.CheckGoldenTickets( park.Counters, new FixedGuestStatistics( (int)a["people"]!, average ), park.Research, park.Ledger.ProfitThisYear, months );
	return new JsonArray( won.Where( kind => (int)kind < 6 ).Select( kind => (JsonNode)JsonValue.Create( (int)kind )! ).ToArray() );
}

// The four global tickets (0xd34c0, ORACLE F23): the statistic is the reported record, the limits are the Standard.sam keys (big park
// reads MinCellsOwned in C# and [1916] = MinCellsCovered in the original, so both keys get the limit). Returns the won kinds (6..9).
static JsonNode? GlobalTickets( JsonObject a )
{
	var text = EconomyTestData.StandardText();
	var keys = new[] { "CoasterHeight", "GokartExcitement", "WaterLength", "MinCellsOwned" };
	for ( var i = 0; i < 4; i++ )
		text = System.Text.RegularExpressions.Regex.Replace( text, $@"^GoldenTicketGlobal\.{keys[i]} .*$", $"GoldenTicketGlobal.{keys[i]} {(int)a["limits"]![i]!}",
			System.Text.RegularExpressions.RegexOptions.Multiline );
	var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
	var park = new ParkEconomy( settings, EconomyTestData.Catalog(), ParkGameMode.FullSimulation, 1 );
	var kinds = new[] { ParkRecordKind.CoasterHeight, ParkRecordKind.GokartExcitement, ParkRecordKind.WaterLength, ParkRecordKind.CellsOwned };
	for ( var i = 0; i < 4; i++ )
		park.Counters.Max( ParkCounters.Record( kinds[i], 0 ), (int)a["stats"]![i]! );
	var won = park.Objectives.CheckGoldenTickets( park.Counters, NoGuestStatistics.Instance, park.Research, 0, Array.Empty<long>() );
	return new JsonArray( won.Where( kind => (int)kind >= 6 && (int)kind <= 9 ).Select( kind => (JsonNode)JsonValue.Create( (int)kind )! ).ToArray() );
}

// ParkStaff.Train (0xfc1c4, ORACLE F17) on staff added the way an original save adds them (AddImported is internal: reflection), with
// the five roles' PoundsPerTrainingPoint columns replaced in the synthetic Standard.sam. Returns the members in input order and the
// amount Train reports (ParkEconomy.EndMonth books it as one staff cost).
static JsonNode? StaffTraining( JsonObject a )
{
	var singulars = new[] { "Handyman", "Mechanic", "Entertainer", "Guard", "Researcher" };
	var text = EconomyTestData.StandardText();
	for ( var role = 0; role < 5; role++ )
	{
		for ( var grade = 0; grade < 5; grade++ )
			text = System.Text.RegularExpressions.Regex.Replace( text, $@"^{singulars[role]}ConstsPerGrade\[{grade}\]\.PoundsPerTrainingPoint .*$",
				$"{singulars[role]}ConstsPerGrade[{grade}].PoundsPerTrainingPoint {(int)a["prices"]![role]![grade]!}", System.Text.RegularExpressions.RegexOptions.Multiline );
	}
	var settings = new BalanceSettings( new SamSettings( new[] { SamDocument.Parse( text, "Standard.sam" ) } ), null, null, "jungle", false );
	var staff = new ParkStaff( settings );
	var add = typeof( ParkStaff ).GetMethod( "AddImported", BindingFlags.NonPublic | BindingFlags.Instance )
		?? throw new MissingMethodException( "ParkStaff.AddImported" );
	var members = a["staff"]!.AsArray().Select( member => (StaffMember)add.Invoke( staff, new object[]
		{ (StaffType)(int)member!["role"]!, (int)member["grade"]!, (int)member["points"]!, 100f, StaffState.Idle, 0L, 100f } )! ).ToList();
	for ( var role = 0; role < 5; role++ )
		staff.SetTrainingBudget( (StaffType)role, (long)(int)a["budgets"]![role]! );
	var spent = staff.Train();
	return new JsonObject { ["spent"] = spent,
		["staff"] = new JsonArray( members.Select( member => (JsonNode)new JsonArray( member.Grade, member.TrainingPoints ) ).ToArray() ) };
}

// 0xd9c0c: ParkEconomy.RepairTimer. The upgrade duration is sent only for a ride in state 2, as DispatchMechanics sends it for a pending upgrade.
static JsonNode? RepairTimer( JsonObject a )
{
	var gauge = BitConverter.Int32BitsToSingle( (int)(uint)a["gauge_bits"]! );
	int? upgrade = a["upgrade"] is null ? null : (int)a["upgrade"]!;
	return JsonValue.Create( ParkEconomy.RepairTimer( (int)a["work"]!, gauge, upgrade ) );
}

static StaffMember StaffMemberOf( int grade, double energy, double happiness ) =>
	new() { Id = 1, Type = StaffType.Mechanic, NameIndex = 0, Grade = grade, Energy = (float)energy, Happiness = (float)happiness };

static uint SingleBits( float value ) => (uint)BitConverter.SingleToInt32Bits( value );

// StaffVitals.Walk / Work / Rest / IsBelowRestLevel (0xf44c0, 0xf459c, 0xf3d8c, 0xf448c) on a staff member; the rates come from the settings
// block in the original, so they are passed as the settings' singles.
static JsonNode? StaffVitalsStep( JsonObject a )
{
	var member = StaffMemberOf( (int)a["grade"]!, (double)a["energy"]!, (double)a["happiness"]! );
	var finished = false;
	var below = false;
	switch ( (string)a["op"]! )
	{
		case "walk": StaffVitals.Walk( member ); break;
		case "work": StaffVitals.Work( member ); break;
		case "rest": finished = StaffVitals.Rest( member, (float)(double)a["rate_energy"]!, (float)(double)a["rate_happiness"]! ); break;
		case "below": below = StaffVitals.IsBelowRestLevel( member.Energy, (int)a["level"]! ); break;
	}
	return new JsonObject { ["energy_bits"] = SingleBits( member.Energy ), ["happiness_bits"] = SingleBits( member.Happiness ), ["finished"] = finished, ["below"] = below };
}

// StaffVitals.SeekRest (the tail of 0xf4900): the world draw is passed in (OpenTPW's economy draws from its own stream).
static JsonNode? StaffRestSeek( JsonObject a )
{
	var member = StaffMemberOf( 0, (double)a["energy"]!, (double)a["happiness"]! );
	var sets = StaffVitals.SeekRest( member, (int)a["level"]!, (int)a["hit"]!, (bool)a["found"]!, () => (uint)(long)a["draw"]! );
	return new JsonObject { ["ret"] = sets ? 1 : 0, ["happiness_bits"] = SingleBits( member.Happiness ) };
}

static List<StaffVital>[] StaffRoster( JsonNode? staff ) =>
	staff!.AsArray().Select( role => role!.AsArray().Select( member => new StaffVital( (float)(double)member![0]!, (float)(double)member[1]! ) ).ToList() ).ToArray();

// StaffStrikeBoard.Grievance (0xf8410) for one role of the lists.
static JsonNode? StaffGrievance( JsonObject a )
{
	var roster = StaffRoster( a["staff"] );
	var role = (StaffType)(int)a["role"]!;
	var strike = StaffStrikeBoard.Grievance( role, roster[(int)role], (long)a["forced"]! != 0, new LitterStatistic( (int)a["litter_den"]!, (int)a["litter_num"]! ) );
	return new JsonObject { ["strike"] = strike ? 1 : 0 };
}

// StaffStrikeBoard.ConsiderMonth (0xf7e18 with 0xf7fe0): the saved levels are restored, then one call per month.
static JsonNode? StaffStrikeMonth( JsonObject a )
{
	var roster = StaffRoster( a["staff"] );
	var board = new StaffStrikeBoard();
	board.Restore( a["levels"]!.AsArray().Select( value => (int)value! ).ToList(), a["flags"]!.AsArray().Select( value => (int)value! != 0 ).ToList(),
		a["ticks"]!.AsArray().Select( value => (uint)(long)value! ).ToList() );
	var roles = Enum.GetValues<StaffType>();
	var levels = new JsonArray();
	var flags = new JsonArray();
	var ticks = new JsonArray();
	var messages = new JsonArray();
	foreach ( var month in a["months"]!.AsArray() )
	{
		var sent = board.ConsiderMonth( (long)month!["turn"]!, (long)a["forced"]! != 0, !(bool)month["closed"]!, (int)month["guests"]!, (long)month["stamp"]!,
			role => roster[(int)role], new LitterStatistic( (int)a["litter_den"]!, (int)a["litter_num"]! ) );
		levels.Add( new JsonArray( roles.Select( role => (JsonNode)JsonValue.Create( board.Level( role ) )! ).ToArray() ) );
		flags.Add( new JsonArray( roles.Select( role => (JsonNode)JsonValue.Create( board.IsActive( role ) ? 1 : 0 )! ).ToArray() ) );
		ticks.Add( new JsonArray( roles.Select( role => (JsonNode)JsonValue.Create( board.LastConsidered( role ) )! ).ToArray() ) );
		messages.Add( new JsonArray( sent.Select( message => (JsonNode)new JsonArray( (int)message.Role, (int)message.Kind ) ).ToArray() ) );
	}
	return new JsonObject { ["levels"] = levels, ["flags"] = flags, ["ticks"] = ticks, ["messages"] = messages };
}

sealed class FixedGuestStatistics( int people, float average ) : IParkGuestStatistics
{
	public int PeopleInPark => people;
	public float AverageHappiness => average;
	public int CountHappierThan( int happiness ) => 0;
	public int KidsWithBalloonsPercent => 0;
	public int KidsWithCostumesPercent => 0;
}
