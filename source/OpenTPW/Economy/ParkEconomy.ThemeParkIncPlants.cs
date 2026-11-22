namespace OpenTPW;

/// <summary>
/// Theme Park Inc's plants (docs/reverse/TPI-STAFF.md, "Gardener and plants"): a thing with the flag 0x1000 (<c>UsageInfo.RequiresWatering</c>) runs <c>data\levels\vegetation.rse</c>
/// (<c>0x5DE61A</c>), whose timer withers it through the conditions 0 (healthy) to 3 (dead) unless a gardener waters it. The engine's side is the state-3 update <c>0x5E4CB0</c> (the weather's wither
/// rate into the script's <c>VAR_SCALE</c>, the condition into <c>+0x54</c> and the condition's RegionFX row, 10 to 13), the constructor's row 10 (<c>0x5DB675</c>), the destructor's row of the
/// condition (<c>0x5DE9D3</c>), the dry and on-fire tests the gardener's chooser reads (<c>0x5E3990</c>, <c>0x5E3B20</c>) and the watering's end (<c>0x5E31B0</c>).
/// </summary>
public sealed partial class ParkEconomy
{
	/// <summary>The plant script's variables by index (data\levels\vegetation.rse's <c>.var</c> list).</summary>
	public const int PlantCondition = 0, PlantWater = 1, PlantDead = 4, PlantOldState = 10, PlantOnFire = 12, PlantScale = 14;

	/// <summary>The RegionFX row of a plant in condition 0 (<c>RegionFX[10]</c>); conditions 1 to 3 take rows 11 to 13.</summary>
	public const int PlantHealthyRow = 10;

	/// <summary>
	/// The script variables of the object with an economy record (its script's array, read and written by index as Theme Park Inc's <c>0x6B0C90</c> and <c>0x6B0780</c> do), null for an object no
	/// script runs. The level supplies it; a headless economy without one reads 0 from every variable and drops every write, as the original does for a thing without a script.
	/// </summary>
	internal Func<int, int[]?>? ThemeParkIncScriptOf { get; set; }

	/// <summary>A script variable of the object (<c>0x6B0740</c>, <c>0x6B0C90</c>): 0 without a script or for an index outside its variables.</summary>
	// [BIN:TPI-EXE:0x6B0C90 script variable] nothing without an instance or for an index outside 0..+0x8C answers 0; else the instance's array (+0x1C) at the index
	internal int ReadThemeParkIncScript( ParkObjectState item, int index ) =>
		ThemeParkIncScriptOf?.Invoke( item.Id ) is { } variables && index >= 0 && index < variables.Length ? variables[index] : 0;

	/// <summary>Writes a script variable of the object (<c>0x6B0780</c>): dropped without a script or for an index outside its variables.</summary>
	// [BIN:TPI-EXE:0x6B0780 script variable setter] nothing without the subsystem, without an instance or for an index outside 0..+0x8C; else the instance's array (+0x1C) takes the value
	internal void WriteThemeParkIncScript( ParkObjectState item, int index, int value )
	{
		if ( ThemeParkIncScriptOf?.Invoke( item.Id ) is { } variables && index >= 0 && index < variables.Length )
			variables[index] = value;
	}

	/// <summary>Whether the object is a plant: its type's <c>UsageInfo.RequiresWatering</c> (the thing flag 0x1000).</summary>
	internal bool IsThemeParkIncPlant( ParkObjectState item ) => Rules.ThemeParkInc && Catalog.TryGet( item.InfoId, out var info ) && info.RequiresWatering;

	/// <summary>
	/// A plant's state-3 update (<c>0x5E4CB0</c>, the object's thing update after <c>0x5E4790</c>): the weather's wither rate goes into <c>VAR_SCALE</c> when the script's scale (in thousandths) differs
	/// from it; <c>+0x54</c> takes the condition's gauge; a changed condition is noted in <c>VAR_OLDSTATE</c> and the plant's RegionFX row follows it on its thing cell (the cell the level put its
	/// row on, <see cref="ParkObjectState.EffectCell"/>; a headless plant without one moves no row).
	/// </summary>
	// [BIN:TPI-EXE:0x5E4CB0 object state dispatch] the switch on the state word +0xA0: 0 the ride update 0x5E5960, 3 the plant block 0x5E4CDE, 1, 2 and 4 0x5E4F99; the plant block needs the flag
	// +0x43 bit 0x10 (else it returns). s = VAR_SCALE (variable 14) / 1000 (imul 0x10624DD3, sar 6, the sign bit added: toward zero) as a float; the weather thing (0x64EB80: the world word +0x1EAB58)
	// state +0x70 minus 1 (unsigned) below 4 picks the wither (the jump table 0x5E5034: 1 Vegetation.HeatwaveWither 0xA029A4, 2 RainWither 0xA029A8, 3 StormWither 0xA029AC, 4 SnowWither 0xA029B0),
	// else 1.0; a wither unequal to s (fcoms, C3 clear) writes VAR_SCALE = ftol(wither × 1000.0f) (0x5E4D82); then c = VAR_CONDITION (0), o = VAR_OLDSTATE (10), +0x54 = (float)((3 - c) × 33)
	// (0x5E4DB1..0x5E4DD4, every update); c unequal to o writes VAR_OLDSTATE = c, takes row 10 + o off the thing's cell for o in 0..3 (0x5D74D0, the table 0x5E5044) and puts row 10 + c on it for c in
	// 0..3 (0x5D74A0, the table 0x5E5054); c = 3 also clears the advisor's queued notices 0x2C..0x2F about the plant (0x64EA60 -> 0x636D10: [EXT:tpi-capability-gates], the advisor's other messages)
	// [EXT:hardware-d3d-path] the product wither × 1000.0f rounds to a single at the game thread's 24-bit x87 before the truncation
	internal void UpdateThemeParkIncPlant( ParkObjectState item )
	{
		if ( !IsThemeParkIncPlant( item ) || ObjectState( item ) != RideObjectStates.NotAnAttraction )
			return;
		var scale = (float)(ReadThemeParkIncScript( item, PlantScale ) / 1000);
		var wither = PlantWither( TpiWeatherState, Settings.ThemeParkIncWithers );
		if ( wither != scale )
			WriteThemeParkIncScript( item, PlantScale, PlantScaleWord( wither ) );
		var condition = ReadThemeParkIncScript( item, PlantCondition );
		var old = ReadThemeParkIncScript( item, PlantOldState );
		item.Repair = PlantGauge( condition );
		if ( condition == old )
			return;
		WriteThemeParkIncScript( item, PlantOldState, condition );
		if ( item.EffectCell is not { } cell )
			return;
		if ( old is >= 0 and <= 3 )
			Litter.Environment.Apply( PlantHealthyRow + old, cell.X, cell.Y, add: false );
		if ( condition is >= 0 and <= 3 )
			Litter.Environment.Apply( PlantHealthyRow + condition, cell.X, cell.Y, add: true );
	}

	/// <summary>The wither rate of the weather state (1 heatwave, 2 rain, 3 storm, 4 snow), 1.0 in any other.</summary>
	public static float PlantWither( int weatherState, ThemeParkIncWithers withers ) => weatherState switch
	{
		1 => withers.Heatwave,
		2 => withers.Rain,
		3 => withers.Storm,
		4 => withers.Snow,
		_ => 1.0f
	};

	/// <summary>The <c>VAR_SCALE</c> word of a wither rate: the single product with 1000 truncated (<c>0x86FAB0</c>).</summary>
	public static int PlantScaleWord( float wither )
	{
		var product = wither * 1000.0f;
		return Math.Abs( product ) < 2147483648f ? (int)product : int.MinValue;
	}

	/// <summary>A plant's <c>+0x54</c> for a condition: (3 − condition) × 33, as a single (99, 66, 33 and 0 for the conditions 0 to 3).</summary>
	public static float PlantGauge( int condition ) => unchecked( (3 - condition) * 33 );

	/// <summary>
	/// Whether the plant needs water (<c>0x5E3990</c>, the branch of the flag 0x1000): its condition is above 0 and it is alive or burning. A Theme Park Inc plant is neither a relief object nor a
	/// bin (ThemeParkIncPlantTests), so the routine's first two branches are not reached for one.
	/// </summary>
	// [BIN:TPI-EXE:0x5E3990 dry test] +0x42 bit 1 (relief): the repair byte below 25.0; bit 0x40 (a bin): variables 11, 4 and 5; bit 0x1000 (0x5E3A4E): d = VAR_DEAD (4), c = VAR_CONDITION (0),
	// f = VAR_ONFIRE (12); c at most 0 (signed) answers 0; d 0 answers 1; else f nonzero answers 1, zero answers 0; a thing with none of the three flags answers 0
	internal bool ThemeParkIncPlantDry( ParkObjectState item )
	{
		var dead = ReadThemeParkIncScript( item, PlantDead );
		var condition = ReadThemeParkIncScript( item, PlantCondition );
		var onFire = ReadThemeParkIncScript( item, PlantOnFire );
		if ( condition <= 0 )
			return false;
		return dead == 0 || onFire != 0;
	}

	/// <summary>The plant's <c>VAR_ONFIRE</c> (<c>0x5E3B20</c>, the branch of the flag 0x1000): a burning plant is no gardener's.</summary>
	// [BIN:TPI-EXE:0x5E3B20 on fire] +0x42 bit 0x40 (a bin) answers variable 5; else bit 0x1000 answers variable 12 (VAR_ONFIRE); else 0
	internal int ThemeParkIncPlantOnFire( ParkObjectState item ) => ReadThemeParkIncScript( item, PlantOnFire );

	/// <summary>
	/// A gardener's watering ends (<c>0x5E31B0</c> on a plant): <c>VAR_WATER</c> = 1 (the script restores the condition), <c>+0x54</c> = 100, and the claim and the request clear.
	/// </summary>
	// [BIN:TPI-EXE:0x5E31B0 maintenance finish] +0x42 bit 1 (relief): VAR_WORN = 0, rows and notices; bit 0x40 (a bin): variable 1 = 1, rows 9 and 8; bit 0x1000 (0x5E32FA): 0x6B0780(inst, 1, 1)
	// (VAR_WATER) and the advisor's queued notices 0x2C..0x2F about the plant cleared (0x64EA60 -> 0x636D10: [EXT:tpi-capability-gates], the advisor's other messages); then for every thing
	// +0x54 = 100.0 (0x42C80000), the claim word +0x72 = 0 and the request +0x78 = 0 (0x5E3364..0x5E3371). The claim turn +0x74 is not written; nothing reads it while the claim word is 0
	internal void FinishThemeParkIncWatering( ParkObjectState item )
	{
		if ( !IsThemeParkIncPlant( item ) )
			throw new InvalidOperationException( $"Theme Park Inc object {item.Id} is no plant; only a plant's maintenance finish is ported." );
		WriteThemeParkIncScript( item, PlantWater, 1 );
		item.Repair = RideRepair.FullGauge;
		item.MechanicId = 0;
		item.ClaimTurn = 0;
		item.MaintenanceRequested = false;
	}

	/// <summary>A newly built plant puts its row 10 on its thing cell (<c>0x5DB675</c>); its condition is 0 then.</summary>
	// [BIN:TPI-EXE:0x5DB63F plant row] the type word +0x1AC (UsageInfo.RequiresWatering) nonzero: +0x42 |= 0x1000 and 0x5D74A0(10, the thing's cell word (+7 << 7) + +5 + 1)
	internal static int PlantRow( int condition ) => PlantHealthyRow + condition;
}

/// <summary>The weather's wither rates of Theme Park Inc's plants (Standard.sam <c>Vegetation.*Wither</c>, <c>+0xF44</c>..<c>+0xF50</c> of the Standard object: <c>0xA029A4</c>..<c>0xA029B0</c>).</summary>
public readonly record struct ThemeParkIncWithers( float Heatwave, float Rain, float Storm, float Snow );
