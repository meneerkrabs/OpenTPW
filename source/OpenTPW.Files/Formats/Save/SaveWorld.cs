using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Thing model numbers of the World thing list: the case index of the writer/reader jump tables
/// (data 0x44924 / 0x44974, bound <c>cmplwi 19</c>). 0, 2 and 18 have no serializer: the original
/// reports "Unknown thing model number loaded" (0, 2) or "Should not be able to save online persons" (18).
/// </summary>
public enum SaveThingModel
{
	Guest = 1,
	Attraction = 3,
	Mechanic = 4,
	Handyman = 5,
	Entertainer = 6,
	Guard = 7,
	Researcher = 8,
	StaffHq = 9,
	MechanicHq = 10,
	AdvisorController = 11,
	TagSystem = 12,
	ParkAnalyser = 13,
	ResearchLab = 14,
	Weather = 15,
	Bank = 16,
	UIMsgReceiver = 17,
	ChallengeSystem = 19
}

/// <summary>
/// One used thing of the World thing list: <c>u32 next used ID, u32 model</c> at <see cref="Offset"/>, then
/// <see cref="BodyLength"/> body bytes from <see cref="BodyOffset"/>. <see cref="Id"/> is the previous record's
/// next ID (the first is <c>Used Thing Head</c>).
/// </summary>
public readonly record struct SaveThing( int Id, SaveThingModel Model, int NextId, int Offset, int BodyOffset, int BodyLength )
{
	public int End => BodyOffset + BodyLength;
}

/// <summary>
/// The labelled World variables (writer 0x105d3c, reader 0x106708; docs/reverse/PPC-formats.md), in wire order.
/// The 2-byte fields named after a subsystem are thing IDs; 0 means none.
/// </summary>
public sealed record SaveWorldVariables(
	uint Version, ushort ArrivalVehicleSize1, ushort ArrivalVehicleSize2, ushort ArrivalVehicleSize3, ushort BankAccount,
	ushort CurrentArrivalVehicle, uint GameTick, ushort MechanicHq, ushort ParkAnalyser, uint ParkClosed,
	uint NumberOfVisitorsToDate, ushort ParkGates, ushort TrafficLights, uint RandomSeed, ushort ResearchLab, ushort StaffHq,
	ushort TagSystem, ushort UIMsgReceiver, ushort Weather, uint WorldState, ushort FirstHandyman, ushort FirstMechanic,
	ushort FirstEntertainer, ushort FirstGuard, ushort FirstResearcher, ushort FirstObject )
{
	public const int Bytes = 64;
	/// <summary>The only World version witnessed (jungle Easymode.TPWI).</summary>
	public const uint SupportedVersion = 2;

	/// <summary>Each thing-ID variable with the model it must reference (SAVE-plan §3, "World thing models").</summary>
	public IEnumerable<(string Name, ushort Id, SaveThingModel Model)> ThingReferences()
	{
		yield return (nameof( BankAccount ), BankAccount, SaveThingModel.Bank);
		yield return (nameof( MechanicHq ), MechanicHq, SaveThingModel.MechanicHq);
		yield return (nameof( ParkAnalyser ), ParkAnalyser, SaveThingModel.ParkAnalyser);
		yield return (nameof( ParkGates ), ParkGates, SaveThingModel.Attraction);
		yield return (nameof( TrafficLights ), TrafficLights, SaveThingModel.Attraction);
		yield return (nameof( ResearchLab ), ResearchLab, SaveThingModel.ResearchLab);
		yield return (nameof( StaffHq ), StaffHq, SaveThingModel.StaffHq);
		yield return (nameof( TagSystem ), TagSystem, SaveThingModel.TagSystem);
		yield return (nameof( UIMsgReceiver ), UIMsgReceiver, SaveThingModel.UIMsgReceiver);
		yield return (nameof( Weather ), Weather, SaveThingModel.Weather);
		yield return (nameof( FirstHandyman ), FirstHandyman, SaveThingModel.Handyman);
		yield return (nameof( FirstMechanic ), FirstMechanic, SaveThingModel.Mechanic);
		yield return (nameof( FirstEntertainer ), FirstEntertainer, SaveThingModel.Entertainer);
		yield return (nameof( FirstGuard ), FirstGuard, SaveThingModel.Guard);
		yield return (nameof( FirstResearcher ), FirstResearcher, SaveThingModel.Researcher);
		yield return (nameof( FirstObject ), FirstObject, SaveThingModel.Attraction);
	}

	internal static SaveWorldVariables Read( ref SaveFieldReader reader ) => new(
		reader.U32(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U32(), reader.U16(), reader.U16(),
		reader.U32(), reader.U32(), reader.U16(), reader.U16(), reader.U32(), reader.U16(), reader.U16(), reader.U16(), reader.U16(),
		reader.U16(), reader.U32(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16() );
}

/// <summary>One slot of the saved staff hiring pool (tp.exe serializer <c>0x507850</c>, 20 bytes).</summary>
/// <param name="Type">The role in handyman, mechanic, entertainer, guard, researcher order.</param>
/// <param name="Name">The index into the role's name table.</param>
/// <param name="SubType">The role sub-type byte (not traced).</param>
/// <param name="OnPointer">Set while the candidate hangs on the mouse pointer to be placed.</param>
/// <param name="TimeSig">The park turn the candidate was made.</param>
/// <param name="Timeout">Its life in units of four park turns.</param>
public readonly record struct SaveStaffCandidate( uint Type, uint Name, byte Grade, byte SubType, bool Valid, bool OnPointer, uint TimeSig, uint Timeout )
{
	public const int Bytes = 20;
}

/// <summary>
/// The staff hiring pool, the first part of the macro AI (tp.exe serializer <c>0x507850</c>, 670 bytes before the calendar): 32 candidate slots,
/// five (people in category, stop producing) pairs, the pool's time stamp (the park turn of the last refill) and the opening-pool flag.
/// </summary>
// [BIN:TPW-EXE:0x507850 staff pool serializer] 32 × 20-byte candidates (+0 type, +4 name, +8 grade, +9 sub-type, +0xa valid, +0xb on pointer, +0xc time stamp, +0x10 timeout), 5 × (u32 mPeopleInCat, u8 mStopProducing), u32 mTimeSig, u8 mOpeningStaffPoolGenerated; the macro AI 0x4d7a70 writes it just before the calendar 0x4f7f30
public sealed record SaveStaffPool( int Offset, IReadOnlyList<SaveStaffCandidate> Candidates, IReadOnlyList<uint> PeopleInCategory, IReadOnlyList<bool> StopProducing,
	uint TimeSig, bool OpeningPoolGenerated )
{
	public const int Slots = 32;
	public const int Roles = 5;
	public const int Bytes = Slots * SaveStaffCandidate.Bytes + Roles * 5 + 5;

	/// <summary>Reads the pool ending at <paramref name="end"/>; null when fewer bytes precede it or a flag or valid role is out of range.</summary>
	internal static SaveStaffPool? TryRead( ReadOnlySpan<byte> payload, int start, int end )
	{
		var offset = end - Bytes;
		if ( offset < start )
			return null;
		var reader = new SaveFieldReader( payload[..end], offset, "staff pool" );
		var candidates = new SaveStaffCandidate[Slots];
		for ( var slot = 0; slot < Slots; slot++ )
		{
			var candidate = new SaveStaffCandidate( reader.U32(), reader.U32(), reader.U8(), reader.U8(), reader.U8() != 0, reader.U8() != 0, reader.U32(), reader.U32() );
			if ( payload[offset + slot * SaveStaffCandidate.Bytes + 10] > 1 || payload[offset + slot * SaveStaffCandidate.Bytes + 11] > 1
				|| candidate.Valid && candidate.Type >= Roles )
				return null;
			candidates[slot] = candidate;
		}
		var people = new uint[Roles];
		var stop = new bool[Roles];
		for ( var role = 0; role < Roles; role++ )
		{
			people[role] = reader.U32();
			var flag = reader.U8();
			if ( flag > 1 )
				return null;
			stop[role] = flag != 0;
		}
		var timeSig = reader.U32();
		var generated = reader.U8();
		return generated > 1 ? null : new SaveStaffPool( offset, candidates, people, stop, timeSig, generated != 0 );
	}
}

/// <summary>
/// The macro-AI calendar (serializer 0xe3d30, widths 8, 8, 4, 4, 4). The time stamps count 100 ns since
/// 1601-01-01; <see cref="MonthAtLastUpdate"/> is zero-based. The year cache is not saved.
/// </summary>
public readonly record struct SaveCalendar( int Offset, long FunnyTimeStart, long SessionStart, int MonthAtLastUpdate, int DayAtLastUpdate, int FunnySecsPerRealSec )
{
	public const int Bytes = 28;
	/// <summary>The rate (<c>mFunnySecsPerRealSec</c>) of every calendar, played or not.</summary>
	public const int InitialRate = 15_000;
	/// <summary><c>mFunnyTimeStart</c> of a new calendar: 2000-01-01 00:00:00 as 100 ns ticks since 1601-01-01.</summary>
	public const long InitialFunnyTimeStart = 125_911_584_000_000_000;
	/// <summary>The day cache (<c>mDayAtLastUpdate</c>) of a calendar that has not updated yet.</summary>
	public const int InitialDay = -1;

	/// <summary>
	/// True for the calendar the macro-AI constructor builds and nothing has updated yet: the start of the park (2000-01-01), month cache 0, day cache -1, rate 15000.
	/// The session start is the wall clock of the moment the park was made, so any value is allowed.
	/// </summary>
	// [BIN:TPW-EXE:0x4F7E80 calendar constructor] +0 and +8 time stamps 0 then set from 2000-01-01 (0x5FC5C0) and the local time; +0x10 month 0, +0x14 day 0xFFFFFFFF, +0x18 0, +0x1C rate 15000; the loader 0x4F7F30 takes the fields back unchecked
	public bool IsInitial => FunnyTimeStart == InitialFunnyTimeStart && MonthAtLastUpdate == 0 && DayAtLastUpdate == InitialDay && FunnySecsPerRealSec == InitialRate;

	/// <summary>True for a calendar a park has updated: a positive rate, a month 0–11 and a day 1–31 (the caches the updates write).</summary>
	public bool IsUpdated => FunnySecsPerRealSec > 0 && MonthAtLastUpdate is >= 0 and <= 11 && DayAtLastUpdate is >= 1 and <= 31;
}

/// <summary>
/// The World block of a decoded payload: action-recording header, the 64 bytes of labelled variables,
/// control manager and macro AI, the cell map and the thing list (docs/reverse/SAVE-plan.md §8.1).
/// <list type="bullet">
/// <item>The control manager (4806 bytes, <c>0x4D3AA0</c>) and the macro AI (<c>0x4D7A70</c>) have fixed widths, so the map starts 5522 bytes after the variables; their bytes
/// stay opaque (<see cref="OpaqueMacroAiOffset"/>/<see cref="OpaqueMacroAiLength"/>, which still include the staff pool). A World that does not read that way has its map
/// located by the structural cell-grid search inside the World block.</item>
/// <item>The staff hiring pool (<see cref="SaveStaffPool"/>, 670 bytes) is the macro AI's first part, just before the calendar.</item>
/// <item>The calendar (28 bytes) and arrivals (18 bytes, 0xcb050: three 4-byte numbers, a 4-byte timer and two
/// bytes) are the fixed-width macro-AI suffix immediately before the map.</item>
/// <item>The thing list walks from the map end and must close with next ID 0 exactly at the World delimiter.</item>
/// </list>
/// </summary>
public sealed class SaveWorld
{
	/// <summary>Arrival serializer 0xcb050 bytes between the calendar and the map.</summary>
	public const int ArrivalBytes = 18;
	/// <summary>Bound on the number of used things (IDs are 16-bit).</summary>
	public const int MaximumThings = ushort.MaxValue;
	public const int ThingHeaderBytes = 8;

	private SaveWorld( SavePayloadBlock block, SaveWorldVariables variables, int variablesOffset, SaveCalendar calendar, int arrivalOffset,
		byte[] arrivals, SaveCellGrid cells, uint usedThingHead, IReadOnlyList<SaveThing> things, SaveStaffPool? staffPool, SaveObjectControls? objectControls )
	{
		ObjectControls = objectControls;
		StaffPool = staffPool;
		Block = block;
		Variables = variables;
		VariablesOffset = variablesOffset;
		Calendar = calendar;
		ArrivalOffset = arrivalOffset;
		Arrivals = arrivals;
		Cells = cells;
		UsedThingHead = usedThingHead;
		Things = things;
	}

	public SavePayloadBlock Block { get; }
	public SaveWorldVariables Variables { get; }
	public int VariablesOffset { get; }
	public int OpaqueMacroAiOffset => VariablesOffset + SaveWorldVariables.Bytes;
	public int OpaqueMacroAiLength => Calendar.Offset - OpaqueMacroAiOffset;
	public SaveCalendar Calendar { get; }
	/// <summary>The control manager's object-control table right after the variables (tp.exe 0x4D3AA0): each object's researched flag among others; null when it does not read as one.</summary>
	public SaveObjectControls? ObjectControls { get; }
	/// <summary>The staff hiring pool before the calendar; null when the bytes there are too few or do not read as one.</summary>
	public SaveStaffPool? StaffPool { get; }
	public int ArrivalOffset { get; }
	/// <summary>The <see cref="ArrivalBytes"/> raw arrival bytes at <see cref="ArrivalOffset"/>.</summary>
	public byte[] Arrivals { get; }
	/// <summary>The decoded arrival scheduler; its vehicle handles are in <see cref="Variables"/>.</summary>
	public SaveArrivals ArrivalState => SaveArrivals.Parse( Arrivals );
	public SaveCellGrid Cells { get; }
	public uint UsedThingHead { get; }
	/// <summary>Used things in list order.</summary>
	public IReadOnlyList<SaveThing> Things { get; }

	public IEnumerable<SaveThing> OfModel( SaveThingModel model ) => Things.Where( thing => thing.Model == model );

	public SaveThing? Find( int id )
	{
		foreach ( var thing in Things )
			if ( thing.Id == id )
				return thing;
		return null;
	}

	/// <summary>The one thing of a singleton model (bank, advisor controller, …); throws unless exactly one exists.</summary>
	public SaveThing Single( SaveThingModel model )
	{
		SaveThing? found = null;
		foreach ( var thing in OfModel( model ) )
		{
			if ( found != null )
				throw new InvalidDataException( $"The save World holds more than one {model} thing." );
			found = thing;
		}
		return found ?? throw new InvalidDataException( $"The save World holds no {model} thing." );
	}

	public static SaveWorld Parse( ReadOnlyMemory<byte> payload, SavePayloadLayout layout, int countX, int countY )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		var block = layout[SaveBlock.World];
		var span = payload.Span;
		var reader = new SaveFieldReader( span[..block.End], block.Offset, "World variables" );
		var variables = SaveWorldVariables.Read( ref reader );
		if ( variables.Version != SaveWorldVariables.SupportedVersion )
			throw new InvalidDataException( $"Save World version {variables.Version} is not supported; only version {SaveWorldVariables.SupportedVersion} is witnessed." );
		// [BIN:TPW-EXE:0x5179C0 World loader] the control manager (4806 bytes) and the macro AI (the hiring pool 670, the calendar 28, the arrivals 18) have fixed widths, so the map starts there
		var gridStart = reader.Position + SaveObjectControls.Bytes + SaveStaffPool.Bytes + SaveCalendar.Bytes + ArrivalBytes;
		SaveCellGrid cells;
		try
		{
			cells = SaveCellGrid.ParseAt( payload, gridStart, block.End, countX, countY );
		}
		catch ( InvalidDataException )
		{
			// a World that does not read as the original's (a synthetic payload): the grid is located structurally
			cells = SaveCellGrid.Parse( payload, reader.Position, block.End, countX, countY );
		}
		var arrival = cells.StartOffset - ArrivalBytes;
		var calendarOffset = arrival - SaveCalendar.Bytes;
		if ( calendarOffset < reader.Position )
			throw new InvalidDataException( "Save World map starts before the macro-AI calendar and arrivals could end." );
		var calendarReader = new SaveFieldReader( span[..arrival], calendarOffset, "World calendar" );
		var calendar = new SaveCalendar( calendarOffset, calendarReader.I64(), calendarReader.I64(), calendarReader.I32(), calendarReader.I32(), calendarReader.I32() );
		// an unplayed park (the restart snapshot, restart.INTS) keeps the constructor's calendar; every other calendar has been updated
		if ( !calendar.IsUpdated && !calendar.IsInitial )
			throw new InvalidDataException( $"Save World calendar before the map is not a calendar (rate {calendar.FunnySecsPerRealSec}, month {calendar.MonthAtLastUpdate}, day {calendar.DayAtLastUpdate})." );
		var (head, things) = SaveThingChain.Walk( span, cells.EndOffset, block.End );
		var world = new SaveWorld( block, variables, block.Offset, calendar, arrival, span.Slice( arrival, ArrivalBytes ).ToArray(), cells, head, things,
			SaveStaffPool.TryRead( span, reader.Position, calendarOffset ), SaveObjectControls.TryRead( span, reader.Position, calendarOffset ) );
		foreach ( var (name, id, model) in variables.ThingReferences() )
		{
			if ( id == 0 )
				continue;
			if ( world.Find( id ) is not { } target )
				throw new InvalidDataException( $"Save World variable {name} names thing {id}, which is not in the thing list." );
			if ( target.Model != model )
				throw new InvalidDataException( $"Save World variable {name} names thing {id}, a {target.Model}, not a {model}." );
		}
		return world;
	}
}

/// <summary>
/// The World thing-chain walk (headers 0xcd1b0/0x10a5b8, dispatch 0x105d3c/0x106708). Every model has one
/// fixed body width except attractions, whose six history buffers carry their own counts.
/// </summary>
public static class SaveThingChain
{
	/// <summary>Person base shared by guests and staff (0xe4a54): base 8, sprite 12, scalars 49, navigation 177, thoughts 144.</summary>
	public const int PersonBytes = 390;
	/// <summary>Staff suffix (0xf2c28).</summary>
	public const int StaffBytes = 105;

	/// <summary>Saved body bytes after the 8-byte header for every fixed-width model (SAVE-plan §3).</summary>
	public static IReadOnlyDictionary<SaveThingModel, int> FixedBodyBytes { get; } = new Dictionary<SaveThingModel, int>
	{
		[SaveThingModel.Guest] = PersonBytes + 135,
		[SaveThingModel.Mechanic] = PersonBytes + StaffBytes + 4 + 2 + 2,
		[SaveThingModel.Handyman] = PersonBytes + StaffBytes + 2 + 4 + 2 + 2,
		[SaveThingModel.Entertainer] = PersonBytes + StaffBytes + 4 + 2,
		[SaveThingModel.Guard] = PersonBytes + StaffBytes + 2 + 4 + 2,
		[SaveThingModel.Researcher] = PersonBytes + StaffBytes + 4 + 2,
		[SaveThingModel.StaffHq] = 8 + 4 + 2 + 5 * 12 + 5 * 4 + 1,
		[SaveThingModel.MechanicHq] = 8 + 2,
		[SaveThingModel.AdvisorController] = SaveAdvisorController.Bytes,
		[SaveThingModel.TagSystem] = 8,
		// [BIN:TPW-EXE:0x4C5D70 analyser serializer] grid, short rings, trackers, monthly/stay/byte rings and totals; SaveParkStatistics derives the same fixed body width.
		[SaveThingModel.ParkAnalyser] = 73_536,
		[SaveThingModel.ResearchLab] = SaveResearchLab.Bytes,
		[SaveThingModel.Weather] = 8 + 4 + 8 + 8 + 13 + 4 + 4 + 1 + 4 + 4 + 4 + 12 + 4 + 4 + 4 + 4 + 1,
		[SaveThingModel.Bank] = SaveBank.Bytes,
		[SaveThingModel.UIMsgReceiver] = 8,
		[SaveThingModel.ChallengeSystem] = SaveChallengeSystem.Bytes,
	};

	/// <summary>
	/// Walks <c>Used Thing Head</c> at <paramref name="start"/>, then <c>(next ID, model, body)</c> records until next ID 0,
	/// which must end exactly at <paramref name="end"/>. Rejects unknown models, repeated or out-of-range IDs, and bodies
	/// that cross <paramref name="end"/>.
	/// </summary>
	public static (uint Head, IReadOnlyList<SaveThing> Things) Walk( ReadOnlySpan<byte> payload, int start, int end )
	{
		var (head, things, offset) = WalkCore( payload, start, end );
		if ( offset != end )
			throw new InvalidDataException( $"Save thing list ends at offset {offset}, but the World delimiter is at {end}; the World block does not close." );
		return (head, things);
	}

	/// <summary>
	/// Where the thing list that starts at <paramref name="start"/> ends: the original's loader reads the head and then one thing after the other until a thing's next id
	/// is 0, so the list closes itself and its end needs no delimiter. Bodies may extend to the end of the payload.
	/// </summary>
	// [BIN:TPW-EXE:0x5179C0 World loader] after the three serializers the loader reads "Used Thing Head" and then one thing at a time through its model's serializer until the chain's next id is 0
	public static int End( ReadOnlySpan<byte> payload, int start )
	{
		if ( start < 0 || payload.Length - start < 4 )
			throw new InvalidDataException( "Save thing list has no room for its Used Thing Head." );
		return WalkCore( payload, start, payload.Length ).End;
	}

	private static (uint Head, IReadOnlyList<SaveThing> Things, int End) WalkCore( ReadOnlySpan<byte> payload, int start, int end )
	{
		if ( start < 0 || end > payload.Length || end - start < 4 )
			throw new InvalidDataException( "Save thing list has no room for its Used Thing Head before the World delimiter." );
		var data = payload[..end];
		var head = BinaryPrimitives.ReadUInt32LittleEndian( data[start..] );
		var current = head;
		var offset = start + 4;
		var things = new List<SaveThing>();
		var seen = new HashSet<uint>();
		while ( true )
		{
			if ( current is 0 or > ushort.MaxValue || !seen.Add( current ) )
				throw new InvalidDataException( $"Save thing list has a {(current is 0 or > ushort.MaxValue ? "out-of-range" : "repeated")} thing ID {current} at offset {offset}." );
			if ( things.Count >= SaveWorld.MaximumThings )
				throw new InvalidDataException( $"Save thing list exceeds {SaveWorld.MaximumThings} things." );
			if ( end - offset < SaveWorld.ThingHeaderBytes )
				throw new InvalidDataException( $"Save thing {current} header at offset {offset} crosses the World delimiter at {end}." );
			var next = BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] );
			var rawModel = BinaryPrimitives.ReadUInt32LittleEndian( data[(offset + 4)..] );
			var body = offset + SaveWorld.ThingHeaderBytes;
			var model = (SaveThingModel)rawModel;
			int length;
			if ( model == SaveThingModel.Attraction )
				length = SaveAttraction.Read( data, body, (int)current ).Length;
			else if ( rawModel <= int.MaxValue && FixedBodyBytes.TryGetValue( model, out var fixedLength ) )
				length = fixedLength;
			else
				throw new InvalidDataException( $"Save thing {current} at offset {offset} has unsupported model {rawModel}." );
			if ( end - body < length )
				throw new InvalidDataException( $"Save thing {current} ({model}) body of {length} bytes at offset {body} crosses the World delimiter at {end}." );
			things.Add( new SaveThing( (int)current, model, (int)next, offset, body, length ) );
			offset = body + length;
			if ( next == 0 )
				break;
			current = next;
		}
		return (head, things.AsReadOnly(), offset);
	}
}

/// <summary>Bounds-checked little-endian field reader that throws <see cref="InvalidDataException"/> on truncation.</summary>
internal ref struct SaveFieldReader
{
	private readonly ReadOnlySpan<byte> data;
	private readonly string what;

	public SaveFieldReader( ReadOnlySpan<byte> data, int position, string what )
	{
		this.data = data;
		this.what = what;
		if ( position < 0 || position > data.Length )
			throw new InvalidDataException( $"Save {what} starts outside its block." );
		Position = position;
	}

	public int Position { get; private set; }
	public string What => what;

	private ReadOnlySpan<byte> Take( int count )
	{
		if ( data.Length - Position < count )
			throw new InvalidDataException( $"Save {what} is truncated at offset {Position} (needs {count} more bytes, {data.Length - Position} remain)." );
		var result = data.Slice( Position, count );
		Position += count;
		return result;
	}

	public void Skip( int count ) => Take( count );
	public byte[] Bytes( int count ) => Take( count ).ToArray();
	public byte U8() => Take( 1 )[0];
	public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian( Take( 2 ) );
	public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian( Take( 4 ) );
	public int I32() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ) );
	public long I64() => BinaryPrimitives.ReadInt64LittleEndian( Take( 8 ) );
	public float F32() => BinaryPrimitives.ReadSingleLittleEndian( Take( 4 ) );

	public bool Flag( string field )
	{
		var value = U8();
		return value <= 1 ? value != 0 : throw new InvalidDataException( $"Save {what} field {field} at offset {Position - 1} is {value}, not a 0/1 flag." );
	}

	public string Utf16( int units )
	{
		var characters = new char[units];
		for ( var index = 0; index < units; index++ )
			characters[index] = (char)U16();
		var end = Array.IndexOf( characters, '\0' );
		return new string( characters, 0, end < 0 ? units : end );
	}

	public SaveThingBase Base() => new( U16(), U16(), U16(), U16() );
}
