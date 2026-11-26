namespace OpenTPW;

/// <summary>
/// An attraction thing as Theme Park Inc's idle guest reads it (docs/reverse/TPI-GUESTS.md, phase P2): its thing id, its thing cell (<c>+5</c>, <c>+7</c>), its entrance cell word (<c>+0x46</c>), its angle
/// (<c>+0x18</c>: 0, 90, 180 or 270), its type's <c>UsageInfo.EntryCellStandPosX/Y</c> (type <c>+0x184</c>, <c>+0x188</c>) and its script's variables (the live array <c>0x6B0C90</c> and <c>0x6B0780</c>
/// read and write; null for a thing no script runs, whose variables read 0 and drop every write).
/// </summary>
public readonly record struct ThemeParkIncThingView( ushort ThingId, (int X, int Y) Cell, ushort EntranceWord, int Angle, float StandX, float StandY, int[]? Variables )
{
	/// <summary>A script variable (<c>0x6B0740</c>, <c>0x6B0C90</c>): 0 without a script or for an index outside its variables.</summary>
	// [BIN:TPI-EXE:0x6B0C90 script variable] no subsystem, no instance or an index outside 0..+0x8C answers 0; else the instance's array (+0x1C) at the index
	public int Variable( int index ) => Variables is { } variables && (uint)index < (uint)variables.Length ? variables[index] : 0;

	/// <summary>Writes a script variable (<c>0x6B0780</c>): dropped without a script or for an index outside its variables.</summary>
	// [BIN:TPI-EXE:0x6B0780 script variable setter] nothing without the subsystem, without an instance or for an index outside 0..+0x8C; else the instance's array (+0x1C) takes the value
	public void SetVariable( int index, int value )
	{
		if ( Variables is { } variables && (uint)index < (uint)variables.Length )
			variables[index] = value;
	}
}

/// <summary>A placed Theme Park Inc entertainer as the guests' finder <c>0x619100</c> reads him: his thing id, his cell and whether his state word <c>+0x1B4</c> is 0x17 (performing).</summary>
public readonly record struct ThemeParkIncEntertainerView( ushort ThingId, (int X, int Y) Cell, bool Performing );

/// <summary>
/// An attraction thing as Theme Park Inc's ride choice and queues read it (phase P3a): its thing id, its thing cell (<c>+5</c>, <c>+7</c>, the cell whose thing chain the minor decision walks), the
/// ride bridge that holds its queue list, its open word and its admission (<see cref="IRideVisitorBridge"/>) and its type's score words. Whether it is a special ride awaiting its activation
/// (<c>0x6242F0</c>) is the economy's live state (<see cref="IGuestPayments.AwaitsActivation"/>), read when it is scored.
/// </summary>
public readonly record struct ThemeParkIncAttractionView( ushort ThingId, (int X, int Y) Cell, RideVisitorBridge Ride, ThemeParkIncScoreTerms Terms )
{
	/// <summary>The type's words the visit reads (phase P3b, <see cref="ThemeParkIncUseTerms"/>); all 0 by default.</summary>
	public ThemeParkIncUseTerms Use { get; init; }
}

/// <summary>
/// The words of an attraction's type a native guest's visit reads (phase P3b: the states 16 to 20, the use routine <c>0x6118D0</c> and its outcome <c>0x612740</c>), by the type record's offsets: the info
/// words <c>Info.WhichUIType</c> (<c>+0x600</c>'s kind: 0 a ride, 1 a shop, 2 a sideshow), <c>Info.RideTypeStringIndex</c> (<c>+0xC0</c>); the usage words <c>FatigueEffect</c> (<c>+0x198</c>),
/// <c>RequiresTeleport</c> (<c>+0x1C0</c>), <c>RideHandlesSprite</c> (<c>+0x1C4</c>, the thing flag <c>+0x42</c> bit 0x20), <c>ThirstEffect</c> to <c>ToiletEffect</c> (<c>+0x200</c>..<c>+0x214</c>),
/// <c>SpecialIngredient</c> (<c>+0x218</c>) and <c>SaltEffect</c>, <c>FatEffect</c>, <c>IceEffect</c>, <c>SugarEffect</c> (<c>+0x228</c>..<c>+0x234</c>). A key the type's .sam leaves out reads 0.
/// </summary>
// [BIN:TPI-EXE:0x9A4520 usage key table] four bytes a key from GoldenTicketCost +0x174: FatigueEffect +0x198, RequiresTeleport +0x1C0, RideHandlesSprite +0x1C4, ThirstEffect +0x200, HungerEffect +0x204,
// VomitEffect +0x208, HappinessEffect +0x20C, LitterEffect +0x210, ToiletEffect +0x214, SpecialIngredient +0x218, AppearanceEffect +0x21C, InitPrizeValue +0x220, ShopType +0x224, SaltEffect +0x228,
// FatEffect +0x22C, IceEffect +0x230, SugarEffect +0x234; the info keys from GenderSpecific +0x78 run to TurnOffScrollingTextures +0xBC, RideTypeStringIndex +0xC0 and IsPhotoBooth +0xCC
public readonly record struct ThemeParkIncUseTerms( int WhichUIType, int RideTypeStringIndex, int FatigueEffect, bool RequiresTeleport, bool RideHandlesSprite, int ThirstEffect,
	int HungerEffect, int VomitEffect, int HappinessEffect, int LitterEffect, int ToiletEffect, int SpecialIngredient, int SaltEffect, int FatEffect, int IceEffect, int SugarEffect );

/// <summary>
/// What Theme Park Inc's native guests read of the park outside the guest simulation (phase P2 of the guest states, docs/reverse/TPI-GUESTS.md): the object list's litter bins and shows, a thing by
/// its id, the entertainers' list and the customers of a bin. The level supplies it; a headless simulation without one finds no bin, no show and no entertainer.
/// </summary>
public interface IThemeParkIncGuestWorld
{
	/// <summary>The object list (world <c>+0x1EAB88</c>, newest first) filtered to the things with the flag <c>+0x42</c> bit 0x40 (<c>UsageInfo.HoldsLitter</c>): the litter bins.</summary>
	IReadOnlyList<ThemeParkIncThingView> Bins();

	/// <summary>The object list filtered to the things with the flag <c>+0x42</c> bit 0x80 (<c>UsageInfo.IsFireworks</c>, type <c>+0x1CC</c>): the shows.</summary>
	IReadOnlyList<ThemeParkIncThingView> Shows();

	/// <summary>The attraction thing with the id <paramref name="thingId"/>, or null when no attraction holds it (a bin state 9 walks to that has gone).</summary>
	ThemeParkIncThingView? Thing( ushort thingId );

	/// <summary>The placed entertainers in the entertainers' list order (world <c>+0x1EAB82</c> through <c>+0x5D0</c>, newest first).</summary>
	IReadOnlyList<ThemeParkIncEntertainerView> Entertainers();

	/// <summary>A guest used the thing (<c>0x5E5DA0</c>): its daily and monthly customers rise.</summary>
	void RecordCustomer( ushort thingId );

	/// <summary>
	/// The object list's choosable attractions (the thing flag <c>+0x42</c> bit 4, <c>Info.IsChoosable</c>) whose ride bridge the guests host, newest first, as the major decision <c>0x610080</c> walks them
	/// (phase P3a). None by default: a world without rides offers no choice.
	/// </summary>
	IReadOnlyList<ThemeParkIncAttractionView> Attractions() => [];

	/// <summary>
	/// The type of any live thing in the thing slot <paramref name="thingId"/> (the table <c>0xAA18B0</c>, the type word <c>+0x16</c>), or null for an empty slot: the score's same-type gate reads it for
	/// the last used thing, whatever kind of thing that is. By default only the choosable attractions are known.
	/// </summary>
	int? ThingTypeId( ushort thingId )
	{
		var list = Attractions();
		for ( var index = 0; index < list.Count; index++ )
			if ( list[index].ThingId == thingId )
				return list[index].Ride.TypeId;
		return null;
	}
}
