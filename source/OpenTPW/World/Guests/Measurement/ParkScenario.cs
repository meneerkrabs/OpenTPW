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
	/// <summary>The toilet stands on the street far from the gate: the long walk of the scenario.</summary>
	public const int ToiletColumn = 28;

	private readonly List<ScenarioAttraction> attractions = new();

	public ParkScenario( WorldSeed seed, ParkScenarioOptions? options = null )
	{
		options ??= new ParkScenarioOptions();
		Seed = seed;
		Options = options;
		Grid = BuildGrid();
		Settings = CreateSettings( options );
		Guests = new GuestSimulation( Grid, Settings, seed.GuestStream );
		// the park's one thing pool as Level builds it: the world's ten singletons, then the gate and the lights (things 1 to 12), so the first guest is thing 13
		Things = new ThingPool();
		Things.AllocateWorldSetup();
		Things.Allocate( SaveThingModel.Attraction );
		Things.Allocate( SaveThingModel.Attraction );
		Guests.AttachThings( Things );
		Scripts = new RideScriptWorld( seed.ScriptStream );
		AddAttraction( "twin-a", RideVisitorKind.Ride, (10, 1), 60, 6000, GuestNeeds.None, 0 );
		AddAttraction( "twin-b", RideVisitorKind.Ride, (10, 9), 60, 6000, GuestNeeds.None, 0 );
		AddAttraction( "thrill", RideVisitorKind.Ride, (12, 1), 95, 9000, GuestNeeds.None, 0 );
		AddAttraction( "calm", RideVisitorKind.Ride, (12, 9), 20, 3000, GuestNeeds.None, 0 );
		AddAttraction( "shop-cheap", RideVisitorKind.Shop, (14, 1), 60, 1000, GuestNeeds.Thirst, options.CheapPrice );
		AddAttraction( "shop-dear", RideVisitorKind.Shop, (14, 9), 60, 1000, GuestNeeds.Thirst, options.DearPrice );
		AddAttraction( "toilet", RideVisitorKind.Shop, (ToiletColumn, StreetRow), 0, 1500, GuestNeeds.Toilet, 0 );
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

	/// <summary>The street and the three branches, each an arm of 4 cells above and below the street; the attractions stand on the arm ends.</summary>
	private static GuestPathGrid BuildGrid()
	{
		var grid = new GuestPathGrid( StreetLength, 12 );
		for ( var x = 0; x < StreetLength; x++ )
			grid.SetPath( x, StreetRow, true );
		foreach ( var column in new[] { 10, 12, 14 } )
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
			// the price test's terms (QueueJoinRules.WillingnessToPay): a shop gives back what a guest lacks
			bridge.InitCostOfGoods = 100;
			bridge.ThirstEffect = satisfies.HasFlag( GuestNeeds.Thirst ) ? 50 : 0;
			bridge.HungerEffect = satisfies.HasFlag( GuestNeeds.Hunger ) ? 50 : 0;
		}
		var script = ScenarioScripts.VisitorScript( new VisitorRideScriptEffects( bridge, UnimplementedRideScriptEffects.Instance ), Scripts, holdMilliseconds, 3 + attractions.Count );
		bridge.Attach( script, () => true );
		Guests.Register( bridge );
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
