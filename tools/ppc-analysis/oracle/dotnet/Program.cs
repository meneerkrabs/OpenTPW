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

// RideVisitorBridge.TakeUnbounced (the UNBOUNCE scan) through reflection: it is private and static on main.
static JsonNode? Unbounce( JsonObject a )
{
	var method = typeof( RideVisitorBridge ).GetMethod( "TakeUnbounced", BindingFlags.NonPublic | BindingFlags.Static )
		?? throw new MissingMethodException( "RideVisitorBridge.TakeUnbounced" );
	var slots = a["slots"]!.AsArray().Select( slot => ((int)slot![0]!, (double)(int)slot[1]!, (double)(int)slot[2]!) ).ToList();
	var guest = (int)method.Invoke( null, new object[] { slots, (double)(int)a["now"]!, false } )!;
	return new JsonObject { ["guest"] = guest, ["remaining"] = slots.Count };
}
