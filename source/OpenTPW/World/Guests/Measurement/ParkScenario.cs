using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTPW;

/// <summary>One attraction of a <see cref="ParkScenario"/>: its visitor bridge and the script that serves guests.</summary>
public sealed class ScenarioAttraction
{
	internal ScenarioAttraction( RideVisitorBridge bridge, RideVM script, string role )
	{
		Bridge = bridge;
		Script = script;
		Role = role;
	}

	public RideVisitorBridge Bridge { get; }
	public RideVM Script { get; }
	/// <summary>What the attraction is for in the scenario (twin ride, thrill ride, cheap shop, ...).</summary>
	public string Role { get; }

	internal void Step( double seconds )
	{
		Bridge.HostStep();
		Script.Advance( seconds );
	}
}

/// <summary>
/// A reusable headless park for guest measurement (docs/SMARTER-GUESTS.md "Measurement"), with no game data and no economy (its park turns are swept the way the game's are): one street of 40 path cells from the gate, three
/// branches off it, and on them two identical rides, a thrill ride and a calm ride, a cheap and a dear shop, and a toilet at the far end. Every attraction takes one guest at a time through
/// the corpus shop protocol (<see cref="ScenarioScripts"/>), so the rides differ in excitement and ride time, the shops in price, and the paths in length: the rides and shops stand
/// within a few cells of the gate, the toilet at the end of a long walk. Guests arrive in groups
/// through the settings' lanes and pay a flat entrance fee; the same seed always gives the same park.
/// </summary>
// [EXT:park-scenario] a measurement fixture of OpenTPW's own (no original counterpart): the layout, excitement levels, prices and hold times are chosen so each of the guest rules in docs/SMARTER-GUESTS.md has something to act on
public sealed class ParkScenario
{
	private const double Tick = 1d / FixedStepClock.TicksPerSecond;
	/// <summary>The street's row and its length in cells.</summary>
	public const int StreetRow = 5, StreetLength = 40;
	/// <summary>The street columns of the bin variant's bins.</summary>
	public static readonly int[] BinColumns = { 5, 20, 24, 34 };
	/// <summary>The toilet stands on the street far from the gate: the long walk of the scenario.</summary>
	public const int ToiletColumn = 28;
	/// <summary>The column of the food shop's branch (<see cref="ParkScenarioOptions.FoodShop"/>), beside the other shops'.</summary>
	public const int FoodColumn = 16;

	private readonly List<ScenarioAttraction> attractions = new();

	public ParkScenario( WorldSeed seed, ParkScenarioOptions? options = null, IGuestDecisions? decisions = null )
	{
		options ??= new ParkScenarioOptions();
		Seed = seed;
		Options = options;
		Grid = BuildGrid( options );
		Settings = CreateSettings( options );
		Guests = new GuestSimulation( Grid, Settings, seed.GuestStream, decisions );
		if ( options.ParkPoints != 0 )
			Guests.ParkPoints = () => options.ParkPoints;
		// the park's one thing pool as Level builds it: the world's ten singletons, then the gate and the lights (things 1 to 12), so the first guest is thing 13
		Things = new ThingPool();
		Things.AllocateWorldSetup();
		Things.Allocate( SaveThingModel.Attraction );
		Things.Allocate( SaveThingModel.Attraction );
		Guests.AttachThings( Things );
		// [EXT:park-scenario] the litter variant: the guests drop and vomit litter on the paths, and a handyman proxy sweeps it (see ParkScenarioOptions.Litter)
		if ( options.Litter )
			Guests.Litter = new ParkLitter { Cells = Grid.Cells };
		// [EXT:park-scenario] the bin variant: litter bins on the street's upper side (see ParkScenarioOptions.Bins)
		if ( options.Bins )
		{
			var bins = options.BinsAtShops
				? new[] { new LitterBin( 1000, (15, 2), (14, 2) ), new LitterBin( 1002, (15, 8), (14, 8) ) }.Concat( options.FoodShop ? [new LitterBin( 1001, (17, 2), (16, 2) )] : [] ).ToArray()
				: BinColumns.Select( ( column, index ) => new LitterBin( 1000 + index, (column, StreetRow - 1), (column, StreetRow) ) ).ToArray();
			Guests.Bins = () => bins;
		}
		Scripts = new RideScriptWorld( seed.ScriptStream );
		AddAttraction( "twin-a", RideVisitorKind.Ride, (10, 1), 60, options.TwinAHoldMilliseconds, GuestNeeds.None, 0 );
		if ( !options.SingleRide )
		{
			AddAttraction( "twin-b", RideVisitorKind.Ride, (10, 9), 60, 6000, GuestNeeds.None, 0 );
			AddAttraction( "thrill", RideVisitorKind.Ride, (12, 1), 95, 9000, GuestNeeds.None, 0 );
			AddAttraction( "calm", RideVisitorKind.Ride, (12, 9), 20, 3000, GuestNeeds.None, 0 );
		}
		AddAttraction( "shop-cheap", RideVisitorKind.Shop, (14, 1), 60, 1000, GuestNeeds.Thirst, options.CheapPrice );
		AddAttraction( "shop-dear", RideVisitorKind.Shop, (14, 9), 60, 1000, GuestNeeds.Thirst, options.DearPrice );
		AddAttraction( "toilet", RideVisitorKind.Shop, (ToiletColumn, StreetRow), 0, 1500, GuestNeeds.Toilet, 0 );
		// the food shop is the last attraction, so the others keep their ids and the default scenario's guests are untouched
		if ( options.FoodShop )
			AddAttraction( "shop-food", RideVisitorKind.Shop, (FoodColumn, 1), 0, 1500, GuestNeeds.Hunger, options.CheapPrice );
	}

	public WorldSeed Seed { get; }
	public ParkScenarioOptions Options { get; }
	public GuestPathGrid Grid { get; }
	public GuestSettings Settings { get; }
	public GuestSimulation Guests { get; }
	/// <summary>The park's thing pool, which the guests take their ids from and the park turn's sweep walks.</summary>
	public ThingPool Things { get; }
	public RideScriptWorld Scripts { get; }
	public IReadOnlyList<ScenarioAttraction> Attractions => attractions;
	/// <summary>Fixed ticks run so far.</summary>
	public long Ticks => Guests.TickCount;

	/// <summary>
	/// A hold-time offset of -200 to +199 ms that differs for every seed (97 is coprime with 400) and is the same for every attraction of the park. The guests' generator is reseeded with
	/// each new guest's id (tp.exe 0x004FB19E), so the streams of two seeds merge into one after a while and their runs become identical; parks that differ in their ride times cannot merge,
	/// and the load stays the same on average.
	/// </summary>
	public static int HoldJitterMilliseconds( WorldSeed seed ) => (int)(seed.Value * 97 % 400) - 200;

	/// <summary>The street and the three branches, each an arm of 4 cells above and below the street; the attractions stand on the arm ends.</summary>
	private static GuestPathGrid BuildGrid( ParkScenarioOptions options )
	{
		var grid = new GuestPathGrid( StreetLength, 12 );
		for ( var x = 0; x < StreetLength; x++ )
			grid.SetPath( x, StreetRow, true );
		foreach ( var column in options.FoodShop ? new[] { 10, 12, 14, FoodColumn } : new[] { 10, 12, 14 } )
			for ( var y = 1; y <= 9; y++ )
				grid.SetPath( column, y, true );
		return grid;
	}

	/// <summary>The scenario's guest settings: the test settings' two guest types and gate lanes with the options' arrival rate and fee.</summary>
	public static GuestSettings CreateSettings( ParkScenarioOptions options ) => new()
	{
		Types = new() { new GuestType( 70, 300, 30 ), new GuestType( 40, 600, 40 ) },
		// both lanes lie in the street's upper left quarter, clear of its path cells, each ending on the street, as in the synthetic park of the determinism tests
		ArrivalLaneA = new[] { (1, 2), (3, 2), (3, 3), (3, 4), (3, 5) },
		ArrivalLaneB = new[] { (8, 2), (6, 2), (6, 3), (6, 4), (6, 5) },
		ArrivalTimeBetween = options.ArrivalTimeBetween,
		AdmissionFee = options.AdmissionFee
	};

	private void AddAttraction( string role, RideVisitorKind kind, (int X, int Y) cell, int excitement, int holdMilliseconds, GuestNeeds satisfies, int price )
	{
		var bridge = new RideVisitorBridge( Scripts.AllocateAttractionId(), role, kind, 1, excitement, 30, satisfies, price ) { EntranceCell = cell, ExitCell = cell };
		if ( kind == RideVisitorKind.Shop )
		{
			// the default scenario's shops add no litter (its guests and pins are the baseline's); the litter variant gives them the shipped value
			bridge.LitterEffect = Options.Litter ? Options.LitterEffect : 0;
			// the price test's terms (QueueJoinRules.WillingnessToPay): a shop gives back what a guest lacks
			bridge.InitCostOfGoods = 100;
			bridge.ThirstEffect = satisfies.HasFlag( GuestNeeds.Thirst ) ? 50 : 0;
			// a food shop is the shipped Burger Shop's: 25 off hunger, 15 on illness (the thirst shops keep their effect of 50 and none on illness)
			bridge.HungerEffect = satisfies.HasFlag( GuestNeeds.Hunger ) ? 25 : 0;
			bridge.VomitEffect = satisfies.HasFlag( GuestNeeds.Hunger ) ? 15 : 0;
		}
		var script = ScenarioScripts.VisitorScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Scripts, holdMilliseconds + HoldJitterMilliseconds( Seed ), 3 + attractions.Count );
		bridge.Attach( script, () => true );
		Guests.Register( bridge );
		// the scenario registers its attractions without a placement, so it lays the cell a called guest's search walks into (state 13) as a build lays an entrance
		var (standX, standY) = GuestSimulation.StandCellOf( bridge );
		ObjectAccessCells.LayStandCell( Grid.Cells, standX, standY );
		attractions.Add( new ScenarioAttraction( bridge, script, role ) );
	}

	/// <summary>
	/// Runs <paramref name="ticks"/> fixed ticks in the order of <c>Level.AdvanceSimulation</c> (and <c>DeterminismTests.SyntheticPark.Run</c>): the tick that starts a park turn
	/// runs the substep's ride scripts, then the turn's thing sweep over the pool newest first (the guests, the attractions' admission), the turn's destroys and the arrival update;
	/// the ticks between turns update the guests first and free nothing. The scenario has no economy, so the turns are the guests' own, as in a level whose economy is aligned to them.
	/// </summary>
	public void Run( long ticks )
	{
		for ( long tick = 0; tick < ticks; tick++ )
		{
			var swept = Guests.NextTickStartsTurn;
			if ( swept && Options.Litter && Guests.ParkTurn % Math.Max( 1, Options.HandymanTurns ) == 0 )
				SweepOneLitter();
			if ( swept )
				Guests.BeginSweepTick( (float)Tick );
			else
				Guests.Tick( (float)Tick );
			foreach ( var attraction in attractions )
			{
				attraction.Bridge.DefersAdmission = swept;
				attraction.Step( Tick );
			}
			if ( !swept )
				continue;
			foreach ( var slot in Things.Capture() )
				if ( !Things.IsQueued( slot.Id ) )
					Guests.SweepGuest( slot.Id );
			Guests.FinishSweep();
			foreach ( var attraction in attractions )
				attraction.Bridge.UpdateAdmission();
			Things.DestroyQueued();
			Guests.UpdateTurnEndArrivals();
		}
	}

	/// <summary>Clock ticks (of the clock <see cref="RunTimed"/> was given) the guests' own update took in <see cref="RunTimed"/> (the guest tick or the swept turn's tick, the sweep over the thing pool, its end and the arrival update); the ride scripts and the admission are left out.</summary>
	public long GuestWatchTicks { get; private set; }
	/// <summary>The guests in the park summed over the park turns <see cref="RunTimed"/> ran: the denominator of the cost per guest-turn.</summary>
	public long GuestTurns { get; private set; }
	/// <summary>The park turns <see cref="RunTimed"/> ran.</summary>
	public long TimedTurns { get; private set; }

	/// <summary>
	/// <see cref="Run"/> that times the guests' update apart from the rest (docs/SMARTER-GUESTS.md, "Cost"): the same calls in the same order, so the run is the same, with a timestamp pair around the guests' part of each tick.
	/// Counts the guests in the park at the start of every park turn into <see cref="GuestTurns"/>. The clock is the caller's (the simulation folders read none, <c>Client/GuestCost.cs</c> passes a stopwatch's).
	/// </summary>
	public void RunTimed( long ticks, Func<long> timestamp )
	{
		long guestTicks = 0;
		for ( long tick = 0; tick < ticks; tick++ )
		{
			var swept = Guests.NextTickStartsTurn;
			if ( swept && Options.Litter && Guests.ParkTurn % Math.Max( 1, Options.HandymanTurns ) == 0 )
				SweepOneLitter();
			var mark = timestamp();
			if ( swept )
			{
				TimedTurns++;
				GuestTurns += Guests.Guests.Count;
				Guests.BeginSweepTick( (float)Tick );
			}
			else
			{
				Guests.Tick( (float)Tick );
			}
			guestTicks += timestamp() - mark;
			foreach ( var attraction in attractions )
			{
				attraction.Bridge.DefersAdmission = swept;
				attraction.Step( Tick );
			}
			if ( !swept )
				continue;
			mark = timestamp();
			foreach ( var slot in Things.Capture() )
				if ( !Things.IsQueued( slot.Id ) )
					Guests.SweepGuest( slot.Id );
			Guests.FinishSweep();
			guestTicks += timestamp() - mark;
			foreach ( var attraction in attractions )
				attraction.Bridge.UpdateAdmission();
			Things.DestroyQueued();
			mark = timestamp();
			Guests.UpdateTurnEndArrivals();
			guestTicks += timestamp() - mark;
		}
		GuestWatchTicks += guestTicks;
	}

	/// <summary>The cell the handyman proxy swept last, as <c>x * 128 + y</c> in the original's search order; the next sweep takes the first littered cell after it, so the sweeping goes round the whole park.</summary>
	private int sweepCursor = -1;

	/// <summary>
	/// The handyman proxy of the litter variant: removes one littered cell, the first after the one it swept last in the original's search order (<see cref="ParkLitter.Littered"/>), wrapping round. A real handyman walks to the
	/// litter; this fixture only sets how fast the park is cleaned (<see cref="ParkScenarioOptions.HandymanTurns"/>), which is what the path rules are measured against.
	/// </summary>
	private void SweepOneLitter()
	{
		var litter = Guests.Litter;
		if ( litter == null || litter.Count == 0 )
			return;
		(int X, int Y)? first = null, next = null;
		foreach ( var cell in litter.Littered() )
		{
			first ??= cell;
			if ( cell.X * ParkLitter.MapSize + cell.Y > sweepCursor )
			{
				next = cell;
				break;
			}
		}
		var target = next ?? first!.Value;
		sweepCursor = target.X * ParkLitter.MapSize + target.Y;
		litter.Remove( target.X, target.Y );
	}

	/// <summary>The canonical hash of the scenario's guests (docs/DETERMINISM.md).</summary>
	public ulong Hash => Guests.ComputeStateHash();
}

/// <summary>The knobs of a <see cref="ParkScenario"/>.</summary>
public sealed record ParkScenarioOptions
{
	/// <summary>Timer units (four park turns each) between arriving groups; 20 is a group about every four days.</summary>
	public int ArrivalTimeBetween { get; init; } = 20;
	public int AdmissionFee { get; init; } = 20;
	/// <summary>The price of the cheap and the dear shop, as the price test's words.</summary>
	public int CheapPrice { get; init; } = 30;
	public int DearPrice { get; init; } = 220;
	/// <summary>The park's attraction points (its rating) for the booth and the arrivals; 0, the default, is a park with no rating (the report's scenario). A variant with a rating exercises the smarter booth.</summary>
	public int ParkPoints { get; init; }
	/// <summary>The hold time of the ride "twin-a", in milliseconds (6,000 by default; the wait opcode takes 16 bits, so 32,767 at most); a long one makes a queue that waits far longer than its twin's (tests of the queue patience rules).</summary>
	public int TwinAHoldMilliseconds { get; init; } = 6000;
	/// <summary>A park with one ride only ("twin-a", no other ride), like the level parks: nothing for a guest to go to instead (tests of the queue patience rules). Off by default.</summary>
	public bool SingleRide { get; init; }
	/// <summary>
	/// A fourth branch with a food shop (the shipped Burger Shop's effects: 25 off hunger, 15 on illness, at the cheap price): the scenario's guests can then eat, which the planning rules of docs/SMARTER-GUESTS.md (phase 5) need to act on. Off by default,
	/// so the default scenario, its report and its pins are the ones of the baseline.
	/// </summary>
	public bool FoodShop { get; init; }
	/// <summary>
	/// The litter variant (docs/SMARTER-GUESTS.md, phase 6): the guests' litter is dropped on the path cells (<see cref="GuestSimulation.Litter"/>; a vomit, a dropped piece when the litter meter is full) and a handyman proxy
	/// sweeps one littered cell every <see cref="HandymanTurns"/> park turns. Off by default, so the default scenario, its report and its pins are the ones of the baseline.
	/// </summary>
	public bool Litter { get; init; }
	/// <summary>The bin variant (phase 6b): four litter bins stand beside the street; a guest with a full litter meter walks to one within the original's three cells (or the policy's radius) instead of dropping it.</summary>
	public bool Bins { get; init; }
	/// <summary>With <see cref="Bins"/>: bins beside the shops' branches instead of four along the street (a player's usual place for them): one beside the cheap shop's, one beside the dear shop's and, only with <see cref="FoodShop"/> (it needs that branch's path), one beside the food shop's.</summary>
	public bool BinsAtShops { get; init; }
	/// <summary>The litter a use of a shop adds to the guest's litter meter in the litter variant; 5 is the shipped shops' value (<c>UsageInfo.LitterEffect</c> in every theme's Shops.sam).</summary>
	public int LitterEffect { get; init; } = 5;
	/// <summary>Park turns between two sweeps of the litter variant's handyman proxy; the fewer the handymen, the more turns.</summary>
	public int HandymanTurns { get; init; } = 40;
}

/// <summary>The visitor script every scenario attraction runs: take a guest (VAR_LETMEON), hold it for the ride time, hand it back (VAR_LETMEOFF), the corpus shop protocol.</summary>
internal static class ScenarioScripts
{
	private static readonly string[] Variables = Enum.GetNames<RideVariables>().Append( "VAR_PEEPID" ).ToArray();
	private const int PeepId = 12;
	private static uint V( int index ) => 0x4000_0000 | (uint)index;

	internal static RideVM VisitorScript( IRideScriptEffects effects, RideScriptWorld world, int holdMilliseconds, int seed )
	{
		var on = V( (int)RideVariables.VAR_LETMEON );
		var off = V( (int)RideVariables.VAR_LETMEOFF );
		var onRide = V( (int)RideVariables.VAR_ONRIDE );
		var words = new List<uint>();
		var labels = new Dictionary<string, int>();
		var fixups = new List<(int Word, string Label)>();
		void Label( string name ) => labels[name] = words.Count;
		void I( Opcode opcode, params object[] operands )
		{
			words.Add( 0x8000_0000 | (uint)opcode );
			foreach ( var operand in operands )
			{
				switch ( operand )
				{
					case int literal: words.Add( (ushort)(short)literal ); break;
					case uint word: words.Add( word ); break;
					case string label: fixups.Add( (words.Count, label) ); words.Add( 0 ); break;
					default: throw new ArgumentException( operand?.ToString() );
				}
			}
		}
		Label( "loop" );
		I( Opcode.CRIT_LOCK );
		I( Opcode.TEST, on );
		I( Opcode.BRANCH_Z, "idle" );
		I( Opcode.COPY, V( PeepId ), on );
		I( Opcode.COPY, on, 0 );
		I( Opcode.ADD, onRide, 1 );
		I( Opcode.CRIT_UNLOCK );
		I( Opcode.WAIT, holdMilliseconds );
		Label( "wait" );
		I( Opcode.TEST, off );
		I( Opcode.BRANCH_NZ, "wait" );
		I( Opcode.COPY, off, V( PeepId ) );
		I( Opcode.ADD, onRide, 0xFFFF );
		I( Opcode.BRANCH, "loop" );
		Label( "idle" );
		I( Opcode.CRIT_UNLOCK );
		I( Opcode.ENDSLICE );
		I( Opcode.BRANCH, "loop" );
		foreach ( var (word, label) in fixups )
			words[word] = 0x2000_0000 | (uint)labels[label];
		return new RideVM( new RideScriptFile( new MemoryStream( Encode( words.ToArray() ) ) ), new RideVMOptions { Effects = effects, World = world, Seed = seed } );
	}

	/// <summary>The RSSEQ container of the script format (docs/RSE.md): header, code words, string blob (empty) and variable names.</summary>
	private static byte[] Encode( uint[] words )
	{
		var output = new MemoryStream();
		var writer = new BinaryWriter( output );
		writer.Write( new byte[] { (byte)'R', (byte)'S', (byte)'S', (byte)'E', (byte)'Q', 0x0F, 0x01, 0x00 } );
		writer.Write( Variables.Length );
		writer.Write( 4 );   // stack
		writer.Write( 50 );  // time slice
		writer.Write( 2 );   // limbo
		writer.Write( 3 );   // bounce
		writer.Write( 5 );   // walk
		writer.Write( "Pad Pad Pad Pad "u8.ToArray() );
		writer.Write( (uint)words.Length );
		foreach ( var word in words )
			writer.Write( word );
		writer.Write( 0 );
		foreach ( var name in Variables )
		{
			writer.Write( name.Length + 1 );
			writer.Write( Encoding.ASCII.GetBytes( name + "\0" ) );
		}
		return output.ToArray();
	}
}
