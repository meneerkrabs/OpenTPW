using System.Buffers.Binary;

namespace OpenTPW;

/// <summary>
/// Thing models of the Theme Park Inc World thing list: the case index plus one of the loader's jump table 0x64d7e0 (<c>cmplwi</c>-style bound 23). 0, 2 and 18 have no
/// serializer; 18 shares the challenge case with 19 in the table, but only 19 is witnessed and written. 21, 22 and 23 are named after their stream tags
/// (<c>OBJH</c>, <c>CUTS</c>, <c>TRNR</c>); their classes are not traced.
/// </summary>
public enum ThemeParkIncThingModel
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
	ChallengeSystem = 19,
	Gardener = 20,
	Objh = 21,
	Cuts = 22,
	Trnr = 23
}

/// <summary>
/// Where a thing body's width comes from. Every model's width is now its own reader's: the attraction (flag byte and history), the fixed-width small things and bank, and the
/// traced layouts of the guests, staff, gardener, staff HQ, advisor controller, park analyser, research lab, weather and challenge system (<see cref="ThemeParkIncBodyLayout"/>).
/// </summary>
public enum ThemeParkIncWidthSource
{
	/// <summary>The thing's reader: a traced layout or a fixed record, never a constant taken from a save and never a search for the next header.</summary>
	Reader
}

/// <summary>
/// One thing of the World thing list: <c>u32 next used ID, u32 model, 'LDMT', model tag</c> (the 16-byte header at <see cref="Offset"/>), then <see cref="BodyLength"/> body
/// bytes from <see cref="BodyOffset"/>. <see cref="Id"/> is the previous record's next ID (the first is <c>Used Thing Head</c>). <see cref="Width"/> says how the body width was
/// obtained (always the model's reader); the bodies of models without a typed record are kept raw.
/// </summary>
public readonly record struct ThemeParkIncThing( int Id, ThemeParkIncThingModel Model, int NextId, int Offset, int BodyOffset, int BodyLength, ThemeParkIncWidthSource Width )
{
	public int End => BodyOffset + BodyLength;
}

/// <summary>
/// The Theme Park Inc World variables (writer 0x641060 / loader 0x64bc00), 70 bytes in wire order. Version 3 adds <see cref="Lobby"/>, <see cref="CurrentObstacle"/> and
/// <see cref="FirstGardener"/> to the Theme Park World variables (<see cref="SaveWorldVariables"/>). The 2-byte fields named after a subsystem are thing IDs; 0 means none.
/// </summary>
// [BIN:TPI-EXE:0x64bc00 World loader] version 4, three arrival vehicle sizes 2, bank, current arrival vehicle, tick 4, mechanic HQ, park analyser, closed 4, visitors 4, gates, lobby, traffic lights, current obstacle, seed 4, research lab, staff HQ, tag system, UI receiver, weather, world state 4, first handyman, mechanic, entertainer, guard, researcher, first object, first gardener
public sealed record ThemeParkIncWorldVariables(
	uint Version, ushort ArrivalVehicleSize1, ushort ArrivalVehicleSize2, ushort ArrivalVehicleSize3, ushort BankAccount,
	ushort CurrentArrivalVehicle, uint GameTick, ushort MechanicHq, ushort ParkAnalyser, uint ParkClosed,
	uint NumberOfVisitorsToDate, ushort ParkGates, ushort Lobby, ushort TrafficLights, ushort CurrentObstacle, uint RandomSeed,
	ushort ResearchLab, ushort StaffHq, ushort TagSystem, ushort UIMsgReceiver, ushort Weather, uint WorldState,
	ushort FirstHandyman, ushort FirstMechanic, ushort FirstEntertainer, ushort FirstGuard, ushort FirstResearcher, ushort FirstObject,
	ushort FirstGardener )
{
	public const int Bytes = 70;
	/// <summary>The only version witnessed (the prebuilt); the Theme Park World profile is version 2 with 64 bytes.</summary>
	public const uint SupportedVersion = 3;

	/// <summary>Each thing-ID variable with the model it must reference.</summary>
	public IEnumerable<(string Name, ushort Id, ThemeParkIncThingModel Model)> ThingReferences()
	{
		yield return (nameof( BankAccount ), BankAccount, ThemeParkIncThingModel.Bank);
		yield return (nameof( MechanicHq ), MechanicHq, ThemeParkIncThingModel.MechanicHq);
		yield return (nameof( ParkAnalyser ), ParkAnalyser, ThemeParkIncThingModel.ParkAnalyser);
		yield return (nameof( ParkGates ), ParkGates, ThemeParkIncThingModel.Attraction);
		yield return (nameof( Lobby ), Lobby, ThemeParkIncThingModel.Attraction);
		yield return (nameof( TrafficLights ), TrafficLights, ThemeParkIncThingModel.Attraction);
		yield return (nameof( CurrentObstacle ), CurrentObstacle, ThemeParkIncThingModel.Attraction);
		yield return (nameof( ResearchLab ), ResearchLab, ThemeParkIncThingModel.ResearchLab);
		yield return (nameof( StaffHq ), StaffHq, ThemeParkIncThingModel.StaffHq);
		yield return (nameof( TagSystem ), TagSystem, ThemeParkIncThingModel.TagSystem);
		yield return (nameof( UIMsgReceiver ), UIMsgReceiver, ThemeParkIncThingModel.UIMsgReceiver);
		yield return (nameof( Weather ), Weather, ThemeParkIncThingModel.Weather);
		yield return (nameof( FirstHandyman ), FirstHandyman, ThemeParkIncThingModel.Handyman);
		yield return (nameof( FirstMechanic ), FirstMechanic, ThemeParkIncThingModel.Mechanic);
		yield return (nameof( FirstEntertainer ), FirstEntertainer, ThemeParkIncThingModel.Entertainer);
		yield return (nameof( FirstGuard ), FirstGuard, ThemeParkIncThingModel.Guard);
		yield return (nameof( FirstResearcher ), FirstResearcher, ThemeParkIncThingModel.Researcher);
		yield return (nameof( FirstObject ), FirstObject, ThemeParkIncThingModel.Attraction);
		yield return (nameof( FirstGardener ), FirstGardener, ThemeParkIncThingModel.Gardener);
	}

	internal static ThemeParkIncWorldVariables Read( ref SaveFieldReader reader ) => new(
		reader.U32(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U32(), reader.U16(), reader.U16(),
		reader.U32(), reader.U32(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U32(), reader.U16(), reader.U16(),
		reader.U16(), reader.U16(), reader.U16(), reader.U32(), reader.U16(), reader.U16(), reader.U16(), reader.U16(), reader.U16(),
		reader.U16(), reader.U16() );
}

/// <summary>One object control (control manager 0x5cf9b0, 36 bytes): the words keep their offsets; the one added to the Theme Park World record (32 bytes) is taken to be the last.</summary>
// [APPROX:SAVE-941] the 36-byte control record is read as the Theme Park World one (id, cost, +8, +0xC, researched at +0x10, then +0x14, +0x18, +0x1C) plus a word at +0x20: the loader writes the record as one 0x24-byte block, so the new field's place follows the struct layout, which the save cannot show; the prebuilt's +0x1C reads -1 (unset) or a small index and +0x20 repeats +0x18 — evidence needed: the control struct's field list from the constructor or the research code
public readonly record struct ThemeParkIncObjectControl( int Index, int Id, uint Word4, uint Word8, uint WordC, bool Researched, uint Word14, uint Word18, uint Word1C, uint Word20 )
{
	public const int Bytes = 36;
}

/// <summary>The control manager's table (0x5cf9b0): 150 records of 36 bytes, <c>u32 mNumObjectControls</c> (the used records come first) and <c>u16 mPreviousSearchKey</c>.</summary>
public sealed record ThemeParkIncObjectControls( int Offset, IReadOnlyList<ThemeParkIncObjectControl> Controls, int PreviousSearchKey )
{
	public const int Slots = 150;
	public const int Bytes = Slots * ThemeParkIncObjectControl.Bytes + 4 + 2;

	public ThemeParkIncObjectControl? Find( int id ) => Controls.Where( control => control.Id == id ).Cast<ThemeParkIncObjectControl?>().FirstOrDefault();

	internal static ThemeParkIncObjectControls Read( ReadOnlySpan<byte> payload, int offset )
	{
		var reader = new SaveFieldReader( payload, offset + Slots * ThemeParkIncObjectControl.Bytes, "Theme Park Inc object controls" );
		var count = reader.U32();
		if ( count > Slots )
			throw new InvalidDataException( $"Save Theme Park Inc object-control count {count} exceeds the {Slots} slots." );
		var key = reader.U16();
		var controls = new List<ThemeParkIncObjectControl>( (int)count );
		for ( var index = 0; index < count; index++ )
		{
			var record = new SaveFieldReader( payload, offset + index * ThemeParkIncObjectControl.Bytes, "Theme Park Inc object control" );
			var (id, word4, word8, wordC, researched) = (record.U32(), record.U32(), record.U32(), record.U32(), record.U32());
			if ( id == 0 || id > ushort.MaxValue || researched > 1 )
				throw new InvalidDataException( $"Save Theme Park Inc object control {index} has id {id} and researched word {researched}." );
			controls.Add( new ThemeParkIncObjectControl( index, (int)id, word4, word8, wordC, researched != 0, record.U32(), record.U32(), record.U32(), record.U32() ) );
		}
		return new ThemeParkIncObjectControls( offset, controls.AsReadOnly(), key );
	}
}

/// <summary>One slot of the Theme Park Inc staff hiring pool (serializer 0x62f980, 31 bytes): the Theme Park World candidate without the grade, plus salary, agency and experience.</summary>
public readonly record struct ThemeParkIncStaffCandidate( uint Type, uint Name, uint Salary, uint Agency, byte SubType, bool Valid, bool OnPointer, uint TimeSig, uint Timeout, uint Experience )
{
	public const int Bytes = 31;
}

/// <summary>
/// The staff hiring pool (serializer 0x6308a0, 1,027 bytes): 32 candidates, six (people in category, stop producing) pairs, the pool's time stamp and the opening-pool flag.
/// Free slots and the people counts hold uninitialized (0xCD) memory in the prebuilt.
/// </summary>
// [BIN:TPI-EXE:0x62f980 staff candidate serializer] mType 4, mName 4, mSalary 4, mAgency 4, mSubType 1, mValid 1, mOnPointer 1, mTimeSig 4, mTimeoutTime 4, mExperience 4; the pool 0x6308a0 adds 6 x (mPeopleInCat[i] 4, mStopProducing[i] 1), mTimeSig 4 and mOpeningStaffPoolGenerated 1
public sealed record ThemeParkIncStaffPool( int Offset, IReadOnlyList<ThemeParkIncStaffCandidate> Candidates, IReadOnlyList<uint> PeopleInCategory, IReadOnlyList<bool> StopProducing,
	uint TimeSig, bool OpeningPoolGenerated )
{
	public const int Slots = 32;
	public const int Roles = 6;
	public const int Bytes = Slots * ThemeParkIncStaffCandidate.Bytes + Roles * 5 + 4 + 1;

	internal static ThemeParkIncStaffPool Read( ReadOnlySpan<byte> payload, int offset )
	{
		var reader = new SaveFieldReader( payload, offset, "Theme Park Inc staff pool" );
		var candidates = new ThemeParkIncStaffCandidate[Slots];
		for ( var slot = 0; slot < Slots; slot++ )
		{
			var candidate = new ThemeParkIncStaffCandidate( reader.U32(), reader.U32(), reader.U32(), reader.U32(), reader.U8(), reader.Flag( "mValid" ), reader.Flag( "mOnPointer" ),
				reader.U32(), reader.U32(), reader.U32() );
			if ( candidate.Valid && candidate.Type >= Roles )
				throw new InvalidDataException( $"Save Theme Park Inc staff candidate {slot} is valid but has role {candidate.Type}." );
			candidates[slot] = candidate;
		}
		var people = new uint[Roles];
		var stop = new bool[Roles];
		for ( var role = 0; role < Roles; role++ )
		{
			people[role] = reader.U32();
			stop[role] = reader.Flag( "mStopProducing" );
		}
		var timeSig = reader.U32();
		return new ThemeParkIncStaffPool( offset, candidates, people, stop, timeSig, reader.Flag( "mOpeningStaffPoolGenerated" ) );
	}
}

/// <summary>
/// The macro-AI calendar (32 bytes): the Theme Park World calendar (<see cref="SaveCalendar"/>) with <see cref="YearAtLastUpdate"/> between the session start and the month.
/// The time stamps count 100 ns since 1601-01-01; the month is zero-based.
/// </summary>
// [APPROX:SAVE-942] the calendar's field order is read from the prebuilt's bytes (two 8-byte stamps, then 2000, 0, 4, 15000), not from the serializer's field log
public readonly record struct ThemeParkIncCalendar( int Offset, long FunnyTimeStart, long SessionStart, int YearAtLastUpdate, int MonthAtLastUpdate, int DayAtLastUpdate, int FunnySecsPerRealSec )
{
	public const int Bytes = 32;
}

/// <summary>One special-ride record of the World's tail (0x6244b0): a u16 and 20 slots of 10 bytes, kept raw.</summary>
public sealed record ThemeParkIncSpecialRide( int Offset, ushort Id, byte[] Slots )
{
	public const int Bytes = 2 + 20 * 10;
}

/// <summary>A small fixed-width thing: its base and the bytes after it (mechanic HQ, tag system, UI receiver, OBJH, CUTS, TRNR).</summary>
public sealed record ThemeParkIncSmallThing( int Id, ThemeParkIncThingModel Model, SaveThingBase Base, byte[] Tail );

/// <summary>
/// The World block of a decoded Theme Park Inc payload (writer 0x641060, loader 0x64bc00). Wire order, verified to consume the prebuilt's World block exactly:
/// <list type="number">
/// <item>the 70 bytes of variables (<see cref="ThemeParkIncWorldVariables"/>, version 3);</item>
/// <item>the control manager (<see cref="ThemeParkIncObjectControls"/>, 5,406 bytes) and the macro AI: the staff pool (<see cref="ThemeParkIncStaffPool"/>, 1,027), the calendar
/// (<see cref="ThemeParkIncCalendar"/>, 32) and the arrivals (18, raw);</item>
/// <item>the map (loader 0x5d6d40): for each of 0x4000 cells a status byte, then the 56-byte map cell when bit 0 is set, the 31-byte track cell when bit 1 is set and the
/// 10-byte effects record when bit 2 is set (the Theme Park World rule with a 52-byte map cell; <see cref="SaveCellGrid.ParseThemeParkIncAt"/>);</item>
/// <item><c>Used Thing Head</c> and the marker <c>EATC</c>, then each thing as <c>u32 next, u32 model, 'LDMT', model tag, body</c>, ending with <c>ENOD</c>. Every body has a
/// traced width (<see cref="ThemeParkIncWidthSource.Reader"/>): the attraction (<see cref="ThemeParkIncAttraction"/>), the bank (<see cref="SaveBank"/>) and the small things by
/// their readers, and the guests, staff, gardener, staff HQ, advisor controller, park analyser, research lab, weather and challenge system by the layouts of
/// <see cref="ThemeParkIncBodyLayout"/> (history rings are sized by their saved counts). No width is searched for, so a thing header planted inside a body is data;</item>
/// <item>the special rides (new, 0x6244b0): <c>u32 count</c>, then count records of 202 bytes (<see cref="ThemeParkIncSpecialRide"/>).</item>
/// </list>
/// </summary>
// [BIN:TPI-EXE:0x64bc00 World loader] variables 0x64bc00, control manager 0x5cf9b0, macro AI (staff pool 0x6308a0, calendar, arrivals), map 0x5d6d40, "Used Thing Head" and the things through the jump table 0x64d7e0, special rides 0x6244b0
// [APPROX:SAVE-943] the special rides' count is taken as the saved word: the loader 0x6244b0 loops by a live global rather than the saved count, so a save whose count differs from the live global (the prebuilt's is 4) reads differently there — evidence needed: the global's value and writer at the loader's loop
// [APPROX:SAVE-944] the guests, staff and gardeners have layouts traced from their load routines (and emulated), but no Theme Park Inc save holds one: their widths and fields are unwitnessed, and a word is not typed beyond its width
public sealed class ThemeParkIncSaveWorld
{
	public const int MaximumThings = ushort.MaxValue;
	public const int ThingHeaderBytes = 16;
	public const int MaximumSpecialRides = 1024;
	public const int ArrivalBytes = 18;
	public const int Version = 3;
	/// <summary>The map loop of the loader runs a fixed 0x4000 times, so the map must be 128 × 128.</summary>
	public const int MapCells = SaveBlockWidths.WorldCells;
	/// <summary>The cell status byte and the records its bits add: the map cell (56 bytes), the track cell (31) and the effects record (10).</summary>
	public const int MapCellBytes = 56, TrackCellBytes = SaveBlockWidths.TrackCellBytes, EffectsCellBytes = SaveBlockWidths.EffectsCellBytes;

	private static ReadOnlySpan<byte> ThingMarker => "LDMT"u8;
	private static ReadOnlySpan<byte> ListMarker => "EATC"u8;
	private static ReadOnlySpan<byte> EndMarker => "ENOD"u8;

	/// <summary>The stream tag of each model's thing header (as stored: the loader's constants byte-reversed).</summary>
	// [BIN:TPI-EXE:0x64d7e0 thing loader jump table] each case compares the 4 bytes after 'LDMT' with its tag: models 1 SREP, 3 EJBO, 4 HCEM, 5 DNAH, 6 RTNE, 7 RAUG, 8 ESER, 9 FATS, 10 QHEM, 11 IVDA, 12 SGAT, 13 KRAP, 14 BALR, 15 TAEW, 16 KNAB, 17 SMIU, 19 QHHC, 20 DRAG, 21 HJBO, 22 STUC, 23 RNRT
	public static IReadOnlyDictionary<ThemeParkIncThingModel, string> Tags { get; } = new Dictionary<ThemeParkIncThingModel, string>
	{
		[ThemeParkIncThingModel.Guest] = "SREP", [ThemeParkIncThingModel.Attraction] = "EJBO", [ThemeParkIncThingModel.Mechanic] = "HCEM",
		[ThemeParkIncThingModel.Handyman] = "DNAH", [ThemeParkIncThingModel.Entertainer] = "RTNE", [ThemeParkIncThingModel.Guard] = "RAUG",
		[ThemeParkIncThingModel.Researcher] = "ESER", [ThemeParkIncThingModel.StaffHq] = "FATS", [ThemeParkIncThingModel.MechanicHq] = "QHEM",
		[ThemeParkIncThingModel.AdvisorController] = "IVDA", [ThemeParkIncThingModel.TagSystem] = "SGAT", [ThemeParkIncThingModel.ParkAnalyser] = "KRAP",
		[ThemeParkIncThingModel.ResearchLab] = "BALR", [ThemeParkIncThingModel.Weather] = "TAEW", [ThemeParkIncThingModel.Bank] = "KNAB",
		[ThemeParkIncThingModel.UIMsgReceiver] = "SMIU", [ThemeParkIncThingModel.ChallengeSystem] = "QHHC", [ThemeParkIncThingModel.Gardener] = "DRAG",
		[ThemeParkIncThingModel.Objh] = "HJBO", [ThemeParkIncThingModel.Cuts] = "STUC", [ThemeParkIncThingModel.Trnr] = "RNRT",
	};

	/// <summary>Body widths of the models whose reader width is exact (the attraction's depends on its history flag).</summary>
	// [BIN:TPI-EXE:0x64d7e0 thing loader] mechanic HQ 10 (base and mNextObject), UI receiver 8 (the thing base alone, load 0x63a1a0), OBJH 14 (base, mCutsceneObject, mCutsceneState; load 0x5f03c0), CUTS 10 (base and animThing; load 0x5d0160) and TRNR 8 (the thing base, 0x637230) bytes after their headers, each also run in an emulation of its load routine with single-member fuzzing (no read depends on stream data; the loops near those routines run after the reads), the bank 292 like Theme Park World's. The tag system has a counted list and is a layout (ThemeParkIncBodyLayout)
	public static IReadOnlyDictionary<ThemeParkIncThingModel, int> ExactBodyBytes { get; } = new Dictionary<ThemeParkIncThingModel, int>
	{
		[ThemeParkIncThingModel.MechanicHq] = 10, [ThemeParkIncThingModel.UIMsgReceiver] = 8,
		[ThemeParkIncThingModel.Objh] = 14, [ThemeParkIncThingModel.Cuts] = 10, [ThemeParkIncThingModel.Trnr] = 8, [ThemeParkIncThingModel.Bank] = SaveBank.Bytes,
	};

	private readonly ReadOnlyMemory<byte> payload;

	private ThemeParkIncSaveWorld( ReadOnlyMemory<byte> payload, SavePayloadBlock block, ThemeParkIncWorldVariables variables, ThemeParkIncObjectControls controls,
		ThemeParkIncStaffPool staffPool, ThemeParkIncCalendar calendar, int arrivalOffset, byte[] arrivals, SaveCellGrid cells, uint head, int thingListOffset,
		IReadOnlyList<ThemeParkIncThing> things, int specialRidesOffset, IReadOnlyList<ThemeParkIncSpecialRide> specialRides, IReadOnlyList<ThemeParkIncAttraction> attractions,
		SaveBank? bank, IReadOnlyList<ThemeParkIncSmallThing> smallThings, ThemeParkIncTracedThings traced )
	{
		this.payload = payload;
		Block = block;
		Variables = variables;
		ObjectControls = controls;
		StaffPool = staffPool;
		Calendar = calendar;
		ArrivalOffset = arrivalOffset;
		Arrivals = arrivals;
		Cells = cells;
		UsedThingHead = head;
		ThingListOffset = thingListOffset;
		Things = things;
		SpecialRidesOffset = specialRidesOffset;
		SpecialRides = specialRides;
		Attractions = attractions;
		Bank = bank;
		SmallThings = smallThings;
		People = traced.People;
		StaffHq = traced.StaffHq;
		AdvisorController = traced.AdvisorController;
		ParkAnalyser = traced.ParkAnalyser;
		ResearchLab = traced.ResearchLab;
		Weather = traced.Weather;
		ChallengeSystem = traced.ChallengeSystem;
		TagSystem = traced.TagSystem;
	}

	public SavePayloadBlock Block { get; }
	public ThemeParkIncWorldVariables Variables { get; }
	public int VariablesOffset => Block.Offset;
	public ThemeParkIncObjectControls ObjectControls { get; }
	public ThemeParkIncStaffPool StaffPool { get; }
	public ThemeParkIncCalendar Calendar { get; }
	public int ArrivalOffset { get; }
	/// <summary>The 18 raw arrival bytes at <see cref="ArrivalOffset"/> (field meanings not traced).</summary>
	public byte[] Arrivals { get; }
	public SaveCellGrid Cells { get; }
	public uint UsedThingHead { get; }
	/// <summary>Where <c>Used Thing Head</c> sits (the grid ends there).</summary>
	public int ThingListOffset { get; }
	/// <summary>Things in list order.</summary>
	public IReadOnlyList<ThemeParkIncThing> Things { get; }
	/// <summary>Where the special rides' count sits, right after the list's <c>ENOD</c>.</summary>
	public int SpecialRidesOffset { get; }
	public IReadOnlyList<ThemeParkIncSpecialRide> SpecialRides { get; }
	/// <summary>The model-3 things in list order: rides, shops, features, fixed items and mission obstacles.</summary>
	public IReadOnlyList<ThemeParkIncAttraction> Attractions { get; }
	/// <summary>The bank (model 16, decoded by <see cref="SaveBank"/>); null when the list has none.</summary>
	public SaveBank? Bank { get; }
	public IReadOnlyList<ThemeParkIncSmallThing> SmallThings { get; }
	/// <summary>The guests, staff and gardeners in list order (models 1, 4 to 8 and 20); none is in the prebuilt, so these are read through layouts that no save has witnessed.</summary>
	public IReadOnlyList<ThemeParkIncPerson> People { get; }
	public ThemeParkIncStaffHq? StaffHq { get; }
	public ThemeParkIncAdvisorController? AdvisorController { get; }
	public ThemeParkIncParkAnalyser? ParkAnalyser { get; }
	public ThemeParkIncResearchLab? ResearchLab { get; }
	public ThemeParkIncWeather? Weather { get; }
	public ThemeParkIncChallengeSystem? ChallengeSystem { get; }
	public ThemeParkIncTagSystem? TagSystem { get; }

	public IEnumerable<ThemeParkIncThing> OfModel( ThemeParkIncThingModel model ) => Things.Where( thing => thing.Model == model );

	public ThemeParkIncThing? Find( int id )
	{
		foreach ( var thing in Things )
			if ( thing.Id == id )
				return thing;
		return null;
	}

	/// <summary>The attraction thing with this id; null when there is none.</summary>
	public ThemeParkIncAttraction? FindAttraction( int id ) => Attractions.FirstOrDefault( attraction => attraction.Id == id );

	/// <summary>The body bytes of <paramref name="thing"/> (after its 16-byte header).</summary>
	public ReadOnlyMemory<byte> BodyOf( ThemeParkIncThing thing ) => payload.Slice( thing.BodyOffset, thing.BodyLength );

	/// <summary>
	/// Where the World block that starts at <paramref name="offset"/> ends, from the loader's own readers; false (nothing consumed) when the bytes do not read as a Theme Park Inc
	/// World: a wrong version, a cell grid, thing header, marker or special-rides tail that does not read, or a list that does not close with <c>ENOD</c> and a tail followed by the
	/// <c>WRLD</c> delimiter.
	/// </summary>
	public static bool TryMeasure( ReadOnlySpan<byte> payload, int offset, out int width )
	{
		width = 0;
		try
		{
			width = Walk( payload, offset ).End - offset;
			return true;
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or OverflowException or ArgumentOutOfRangeException )
		{
			return false;
		}
	}

	/// <summary>
	/// Reads the World block of <paramref name="layout"/> (a <see cref="SavePayloadProfile.ThemeParkInc"/> split) into typed records. Throws <see cref="InvalidDataException"/> when
	/// the block does not read exactly to its delimiter, a record is out of range or a variable names a thing of the wrong model.
	/// </summary>
	public static ThemeParkIncSaveWorld Parse( ReadOnlyMemory<byte> payload, SavePayloadLayout layout, int countX, int countY )
	{
		ArgumentNullException.ThrowIfNull( layout );
		if ( layout.PayloadLength != payload.Length )
			throw new ArgumentException( "Layout does not describe this payload.", nameof( layout ) );
		if ( (long)countX * countY != MapCells )
			throw new InvalidDataException( $"The Theme Park Inc World loader saves {MapCells} cells; a {countX}x{countY} map does not match." );
		var block = layout[SaveBlock.World];
		var span = payload.Span;
		var walked = Walk( span, block.Offset );
		if ( walked.End != block.End )
			throw new InvalidDataException( $"Save Theme Park Inc World block reads to offset {walked.End}, but its delimiter is at {block.End}." );
		var variablesReader = new SaveFieldReader( span[..block.End], block.Offset, "Theme Park Inc World variables" );
		var variables = ThemeParkIncWorldVariables.Read( ref variablesReader );
		var controls = ThemeParkIncObjectControls.Read( span[..block.End], walked.ControlsOffset );
		var staffPool = ThemeParkIncStaffPool.Read( span[..block.End], walked.StaffPoolOffset );
		var calendarReader = new SaveFieldReader( span[..block.End], walked.CalendarOffset, "Theme Park Inc World calendar" );
		var calendar = new ThemeParkIncCalendar( walked.CalendarOffset, calendarReader.I64(), calendarReader.I64(), calendarReader.I32(), calendarReader.I32(), calendarReader.I32(), calendarReader.I32() );
		if ( calendar.FunnySecsPerRealSec <= 0 || calendar.YearAtLastUpdate is < 1 or > 9999 || calendar.MonthAtLastUpdate is < 0 or > 11 || calendar.DayAtLastUpdate is < 1 or > 31 )
			throw new InvalidDataException( $"Save Theme Park Inc World calendar is not a calendar (rate {calendar.FunnySecsPerRealSec}, year {calendar.YearAtLastUpdate}, month {calendar.MonthAtLastUpdate}, day {calendar.DayAtLastUpdate})." );
		var cells = SaveCellGrid.ParseThemeParkIncAt( payload, walked.GridOffset, walked.GridEnd, countX, countY );
		var things = new List<ThemeParkIncThing>( walked.Things.Count );
		var attractions = new List<ThemeParkIncAttraction>();
		var smallThings = new List<ThemeParkIncSmallThing>();
		var traced = new ThemeParkIncTracedThings();
		SaveBank? bank = null;
		foreach ( var frame in walked.Things )
		{
			var thing = frame;
			things.Add( thing );
			var bounded = span[..thing.End];
			if ( thing.Model == ThemeParkIncThingModel.Attraction )
				attractions.Add( ThemeParkIncAttraction.Read( bounded, thing.BodyOffset, thing.Id ) );
			else if ( thing.Model == ThemeParkIncThingModel.Bank )
			{
				if ( bank != null )
					throw new InvalidDataException( "The save Theme Park Inc World holds more than one bank." );
				bank = SaveBank.Read( bounded, new SaveThing( thing.Id, SaveThingModel.Bank, thing.NextId, thing.Offset, thing.BodyOffset, thing.BodyLength ) );
			}
			else if ( ThemeParkIncBodyLayout.For( thing.Model ) != null )
				traced.Add( thing, new ThemeParkIncBody( thing.Model, payload.Slice( thing.BodyOffset, thing.BodyLength ) ) );
			else
			{
				var reader = new SaveFieldReader( bounded, thing.BodyOffset, $"{thing.Model} thing {thing.Id}" );
				var thingBase = reader.Base();
				smallThings.Add( new ThemeParkIncSmallThing( thing.Id, thing.Model, thingBase, reader.Bytes( thing.BodyLength - SaveThingBase.Bytes ) ) );
			}
		}
		var specials = new List<ThemeParkIncSpecialRide>( walked.SpecialCount );
		for ( var index = 0; index < walked.SpecialCount; index++ )
		{
			var recordOffset = walked.SpecialOffset + 4 + index * ThemeParkIncSpecialRide.Bytes;
			specials.Add( new ThemeParkIncSpecialRide( recordOffset, BinaryPrimitives.ReadUInt16LittleEndian( span[recordOffset..] ), span.Slice( recordOffset + 2, 200 ).ToArray() ) );
		}
		var world = new ThemeParkIncSaveWorld( payload, block, variables, controls, staffPool, calendar, walked.ArrivalOffset, span.Slice( walked.ArrivalOffset, ArrivalBytes ).ToArray(), cells,
			walked.Head, walked.ThingListOffset, things.AsReadOnly(), walked.SpecialOffset, specials.AsReadOnly(), attractions.AsReadOnly(), bank, smallThings.AsReadOnly(), traced );
		foreach ( var (name, id, model) in variables.ThingReferences() )
		{
			if ( id == 0 )
				continue;
			if ( world.Find( id ) is not { } target )
				throw new InvalidDataException( $"Save Theme Park Inc World variable {name} names thing {id}, which is not in the thing list." );
			if ( target.Model != model )
				throw new InvalidDataException( $"Save Theme Park Inc World variable {name} names thing {id}, a {target.Model}, not a {model}." );
		}
		return world;
	}

	private sealed record Walked( int ControlsOffset, int StaffPoolOffset, int CalendarOffset, int ArrivalOffset, int GridOffset, int GridEnd, int ThingListOffset, uint Head,
		List<ThemeParkIncThing> Things, int SpecialOffset, int SpecialCount, int End );

	/// <summary>
	/// The structural walk shared by <see cref="TryMeasure"/> and <see cref="Parse"/>: the fixed-width parts, the status-byte cells and the marker-checked thing chain. Throws
	/// <see cref="InvalidDataException"/> where the bytes stop reading as a World.
	/// </summary>
	private static Walked Walk( ReadOnlySpan<byte> payload, int offset )
	{
		if ( offset < 0 || payload.Length - offset < ThemeParkIncWorldVariables.Bytes )
			throw new InvalidDataException( "Save Theme Park Inc World block is truncated inside its variables." );
		var version = BinaryPrimitives.ReadUInt32LittleEndian( payload[offset..] );
		if ( version != ThemeParkIncWorldVariables.SupportedVersion )
			throw new InvalidDataException( $"Save Theme Park Inc World version {version} is not supported; only version {ThemeParkIncWorldVariables.SupportedVersion} is witnessed." );
		var controls = checked( offset + ThemeParkIncWorldVariables.Bytes );
		var staffPool = controls + ThemeParkIncObjectControls.Bytes;
		var calendar = staffPool + ThemeParkIncStaffPool.Bytes;
		var arrivals = calendar + ThemeParkIncCalendar.Bytes;
		var grid = arrivals + ArrivalBytes;
		var position = grid;
		for ( var cell = 0; cell < MapCells; cell++ )
		{
			if ( position >= payload.Length )
				throw new InvalidDataException( $"Save Theme Park Inc World map is truncated at cell {cell}." );
			position += SaveCellGrid.ThemeParkIncCellBytes( payload[position] );
		}
		if ( position > payload.Length )
			throw new InvalidDataException( "Save Theme Park Inc World map is truncated inside its last cell." );
		var thingList = position;
		var (head, things, afterThings) = WalkThings( payload, thingList );
		// the ENOD marker has just been read; the special rides follow it
		if ( payload.Length - afterThings < 4 )
			throw new InvalidDataException( "Save Theme Park Inc World is truncated before its special rides." );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( payload[afterThings..] );
		var end = TailEnd( payload, afterThings );
		if ( end < 0 )
			throw new InvalidDataException( $"Save Theme Park Inc World lists {count} special rides that do not fit before the end of the data." );
		return new Walked( controls, staffPool, calendar, arrivals, grid, position, thingList, head, things, afterThings, (int)count, end );
	}

	/// <summary>The end of the special-rides tail whose count word sits at <paramref name="offset"/>; -1 when it does not fit or exceeds the bound.</summary>
	private static int TailEnd( ReadOnlySpan<byte> payload, int offset )
	{
		var count = BinaryPrimitives.ReadUInt32LittleEndian( payload[offset..] );
		if ( count > MaximumSpecialRides )
			return -1;
		var end = offset + 4L + count * ThemeParkIncSpecialRide.Bytes;
		return end <= payload.Length ? (int)end : -1;
	}

	private static (uint Head, List<ThemeParkIncThing> Things, int AfterEnd) WalkThings( ReadOnlySpan<byte> payload, int start )
	{
		if ( payload.Length - start < 8 )
			throw new InvalidDataException( "Save Theme Park Inc thing list has no room for its head and marker." );
		var head = BinaryPrimitives.ReadUInt32LittleEndian( payload[start..] );
		if ( !payload.Slice( start + 4, 4 ).SequenceEqual( ListMarker ) )
			throw new InvalidDataException( $"Save Theme Park Inc thing list lacks its EATC marker at offset {start + 4}." );
		var things = new List<ThemeParkIncThing>();
		var seen = new HashSet<uint>();
		var current = head;
		var offset = start + 8;
		while ( true )
		{
			if ( current is 0 or > ushort.MaxValue || !seen.Add( current ) )
				throw new InvalidDataException( $"Save Theme Park Inc thing list has a {(current is 0 or > ushort.MaxValue ? "out-of-range" : "repeated")} thing ID {current} at offset {offset}." );
			if ( things.Count >= MaximumThings )
				throw new InvalidDataException( $"Save Theme Park Inc thing list exceeds {MaximumThings} things." );
			var (next, model) = ReadHeader( payload, offset, current );
			var body = offset + ThingHeaderBytes;
			int length;
			if ( model == ThemeParkIncThingModel.Attraction )
				length = ThemeParkIncAttraction.MeasureBody( payload, body, (int)current );
			else if ( ExactBodyBytes.TryGetValue( model, out var exact ) )
				length = exact;
			else if ( ThemeParkIncBodyLayout.For( model ) is { } layout )
			{
				try
				{
					length = layout.Measure( payload, body );
				}
				catch ( InvalidDataException exception )
				{
					throw new InvalidDataException( $"Save Theme Park Inc thing {current} ({model}): {exception.Message}", exception );
				}
			}
			else
				throw new InvalidDataException( $"Save Theme Park Inc thing {current} has model {model}, which has no body reader." );
			if ( payload.Length - body < length )
				throw new InvalidDataException( $"Save Theme Park Inc thing {current} ({model}) body of {length} bytes at offset {body} crosses the end of the data." );
			things.Add( new ThemeParkIncThing( (int)current, model, (int)next, offset, body, length, ThemeParkIncWidthSource.Reader ) );
			offset = body + length;
			if ( next == 0 )
				break;
			current = next;
		}
		if ( payload.Length - offset < 4 || !payload.Slice( offset, 4 ).SequenceEqual( EndMarker ) )
			throw new InvalidDataException( $"Save Theme Park Inc thing list does not close with ENOD at offset {offset}." );
		return (head, things, offset + 4);
	}

	/// <summary>Reads and checks the 16-byte thing header at <paramref name="offset"/>: marker, a model with a serializer and that model's tag.</summary>
	private static (uint Next, ThemeParkIncThingModel Model) ReadHeader( ReadOnlySpan<byte> payload, int offset, uint id )
	{
		if ( payload.Length - offset < ThingHeaderBytes )
			throw new InvalidDataException( $"Save Theme Park Inc thing {id} header at offset {offset} crosses the end of the data." );
		var next = BinaryPrimitives.ReadUInt32LittleEndian( payload[offset..] );
		var rawModel = BinaryPrimitives.ReadUInt32LittleEndian( payload[(offset + 4)..] );
		if ( !payload.Slice( offset + 8, 4 ).SequenceEqual( ThingMarker ) )
			throw new InvalidDataException( $"Save Theme Park Inc thing {id} at offset {offset} lacks its LDMT marker." );
		if ( rawModel > int.MaxValue || !Tags.TryGetValue( (ThemeParkIncThingModel)rawModel, out var tag ) )
			throw new InvalidDataException( $"Save Theme Park Inc thing {id} at offset {offset} has unsupported model {rawModel}." );
		if ( !payload.Slice( offset + 12, 4 ).SequenceEqual( System.Text.Encoding.ASCII.GetBytes( tag ) ) )
			throw new InvalidDataException( $"Save Theme Park Inc thing {id} at offset {offset} (model {rawModel}) lacks its {tag} tag." );
		return (next, (ThemeParkIncThingModel)rawModel);
	}
}
