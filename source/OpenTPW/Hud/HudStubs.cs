using System.Globalization;

namespace OpenTPW.Hud;

/// <summary>
/// Test stand-in for <see cref="IHudParkStatus"/> (the game binds the HUD to the park economy).
/// Starting cash is whatever the test passes; the calendar is a fixed test schedule.
/// </summary>
// test fixture only (2 s/day, 30-day months); the game binds the HUD to the park economy's clock
public sealed class StubParkStatus : IHudParkStatus
{
	public const float SecondsPerDay = 2f;
	private double days;

	public StubParkStatus( long initialCash ) => Money = initialCash;

	public bool HasEconomy => true;
	public long Money { get; private set; }
	public GameSpeed Speed { get; set; } = GameSpeed.Normal;
	public float TimeScale => (int)Speed;

	public ParkDate Date
	{
		get
		{
			var day = (int)Math.Floor( days );
			return new ParkDate( day / 360 + 1, day / 30 % 12 + 1, day % 30 + 1, 0 );
		}
	}

	public void Update( float realSeconds ) => days += Math.Max( 0, realSeconds ) * TimeScale / SecondsPerDay;

	public long? PriceOf( BuildItem item ) => item.Cost;

	public bool IsAvailable( BuildItem item ) => true;

	public void Earn( long amount ) => Money += Math.Max( 0, amount );
}

/// <summary>Status for levels without a park economy (the generic sandbox): no money or date, builds are free.</summary>
public sealed class NoEconomyStatus : IHudParkStatus
{
	public bool HasEconomy => false;
	public long Money => 0;
	public ParkDate Date => default;
	public GameSpeed Speed { get; set; } = GameSpeed.Normal;
	public float TimeScale => GameSpeedScale.Of( Speed );
	public void Update( float realSeconds ) { }
	public long? PriceOf( BuildItem item ) => null;
	public bool IsAvailable( BuildItem item ) => true;
}

/// <summary>
/// HUD binding to the park economy of a level (<see cref="Level.Park"/>): balance, calendar and
/// speed from <see cref="ParkEconomy"/>. The level owns purchases and sales. The runtime is
/// looked up on every access, so a loaded park save (which replaces the economy object) is followed.
/// </summary>
public sealed class EconomyParkStatus : IHudParkStatus
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<int?>? playerKeys;
	private Level? level;

	/// <param name="playerKeys">The player's golden keys from the application-owned ticket ledger, null while no ledger is attached.</param>
	public EconomyParkStatus( Func<ParkEconomy?> economy, Func<int?>? playerKeys = null )
	{
		this.economy = economy;
		this.playerKeys = playerKeys;
	}

	/// <summary>Binds to a level's runtime (followed on every access); a speed set through the status goes through the level's commands.</summary>
	public static EconomyParkStatus ForLevel( Level level, Func<int?>? playerKeys = null ) => new( () => level.Park?.Economy, playerKeys ) { level = level };

	private ParkEconomy? Economy => economy();

	public bool HasEconomy => Economy != null;
	public long Money => Economy?.Balance ?? 0;
	public ParkDate Date => Economy?.Date ?? default;

	/// <summary>The park's speed; setting it issues a <see cref="SetGameSpeedCommand"/> through the bound level (<see cref="ForLevel"/>). A status bound to no level cannot set it.</summary>
	public GameSpeed Speed
	{
		get => Economy?.Speed ?? GameSpeed.Normal;
		set => (level ?? throw new InvalidOperationException( "An economy status without its level cannot set the speed: bind it with ForLevel." ))
			.Commands.Issue( new SetGameSpeedCommand( value ) );
	}

	// The level scales the elapsed time of its whole fixed clock (guests, rides, scripts, economy) by this factor; the economy itself runs one tick per fixed tick.
	// [EXT:speed-buttons] the four speed buttons (pause, ×1, ×2, ×4) are an OpenTPW addition scaling the simulation clock by 0, 1, 2 and 4; the original's time keys are numpad −/+ (×0.8, ×1.25, clamped 0.25-2.0) and * (= 1.0) (0x40c3b0/0x40c3c0/0x40c3d0), and "Speed :" is a ride-panel statistic, not a game-speed control
	public float TimeScale => GameSpeedScale.Of( Speed );

	public int GoldenTickets => Math.Max( 0, Economy?.GoldenTicketsAvailable ?? 0 );
	// [BIN:TPW-EXE:0x004a1017 HUD key counter] the HUD's message 0x1e handler (0x4a0fb0) skips the key row in game type 2 (Instant Action) and otherwise shows the player's
	// keys (0x409410 → 0x5af680): the earned global, per-theme and secret tickets / 3 + the extra keys, which is PlayerTicketLedger.Keys; spent tickets keep their key
	public int GoldenKeys => Economy is not { } park || park.Mode == ParkGameMode.InstantAction ? 0
		: Math.Max( 0, playerKeys?.Invoke() ?? park.Objectives.GoldenTickets.Count / PlayerProgress.TicketsPerKey );
	public object? EconomySource => Economy;

	public void Update( float realSeconds ) { }

	public long? PriceOf( BuildItem item ) =>
		Economy is { } economy && economy.Catalog.TryGet( item.InfoId, out var info ) ? info.PurchaseCost : item.Cost;

	public int? TicketCostOf( BuildItem item ) => Economy?.ListedTicketCost( item.InfoId );

	public bool IsAvailable( BuildItem item ) =>
		Economy is not { } park || park.Catalog.TryGet( item.InfoId, out _ ) && park.Research.IsAvailable( item.InfoId );

}

/// <summary>The level's original, footprint-bearing catalogue, grouped by its authored UI category.</summary>
public sealed class OriginalBuildCatalog : IBuildCatalog
{
	private readonly IReadOnlyDictionary<BuildCategory, BuildItem[]> categories;

	public OriginalBuildCatalog( ObjectCatalog catalog )
	{
		// [BIN:STP-PPC:0x10059F00 mesh instance flag 0x400] the buy-window preview (0x10162584 -> 0x10139084 -> 0x1005C35C) instances the p<name> preview mesh when it loaded and the object's main mesh otherwise, so objects without a P model show their main model
		categories = catalog.Buildable.OrderBy( entry => entry.InfoId ).Select( Item )
			.GroupBy( item => item.Category ).ToDictionary( group => group.Key, group => group.ToArray() );
		MysteryPreview = catalog.Find( MysteryRideInfoId ) is { } mystery ? Item( mystery ) : null;
	}

	/// <summary>Info.Id of the Mystery ride tool (<c>rides/mystery</c>), whose model the buy window shows for an unbought golden-ticket row.</summary>
	// [BIN:TPW-EXE:0x4ab1b0 buy preview] a row whose type has GoldenTicketCost (+0xc4) above 0 and is not in the bought-with-tickets set (0x4d4b70) takes the branch of the negative row ids: the preview object (0x486410) is the record of type 0x64 = 100 (0x412e90), as the land rows -1 and -2 take types 101 and 102
	public const int MysteryRideInfoId = 100;

	public BuildItem? MysteryPreview { get; }

	private static BuildItem Item( ObjectCatalogEntry entry ) => new(
		$"{entry.Theme}/{entry.InfoId}", entry.InfoId, (BuildCategory)entry.WhichUIType, entry.ObjectNameIndex ?? -1,
		entry.BuildCost, entry.PreviewModelPath ?? entry.ModelPath,
		ObjectAssets.TextureDirectories( entry ).Where( location => location.FileSystem == FileSystem ).Select( location => location.Directory ).ToArray(),
		entry.Settings.Has( "UsageInfo.ExcitementLevel" ) ? entry.Settings.GetInt( "UsageInfo.ExcitementLevel" ) : null, entry );

	public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) =>
		categories.TryGetValue( category, out var items ) ? items : Array.Empty<BuildItem>();
}

/// <summary>
/// Build catalog stand-in with the one object the sandbox can place: the jungle Inca Totem. Category
/// (<c>Info.WhichUIType</c> in Rides.sam), price (<c>Upgrades[0].CostOfUpgrade</c>, "cash cost when
/// buying this item", in Totem.sam) and preview model (<c>Ptotem.MD2</c>) are original data.
/// </summary>
public sealed class TotemBuildCatalog : IBuildCatalog
{
	public const string ItemId = "totem";
	public const int ObjectNameIndex = 29;
	// [EXT:test-stub] price shown when Totem.sam cannot be read (the economy catalogue price is used when a park economy runs)
	public const long FallbackCost = 3250;
	private readonly BuildItem? totem;

	public TotemBuildCatalog( string levelName = "jungle" )
	{
		var archive = $"/levels/{levelName}/rides/totem";
		var cost = FallbackCost;
		var category = BuildCategory.Rides;
		int? excitement = null;
		try
		{
			var settings = new SettingsFile( $"{archive}/Totem.sam" );
			// [DATA:Totem.sam:Upgrades[0].CostOfUpgrade] (FallbackCost only when the file is missing)
			if ( long.TryParse( settings["Upgrades[0].CostOfUpgrade"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) )
				cost = value;
			var rides = new SettingsFile( $"/levels/{levelName}/rides/Rides.sam" );
			// [DATA:Totem.sam:UsageInfo.ExcitementLevel] overrides [DATA:Rides.sam:UsageInfo.ExcitementLevel]
			if ( int.TryParse( settings["UsageInfo.ExcitementLevel"] ?? rides["UsageInfo.ExcitementLevel"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level ) )
				excitement = level;
			// [DATA:Rides.sam:Info.WhichUIType]
			if ( int.TryParse( rides["Info.WhichUIType"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var type ) && Enum.IsDefined( (BuildCategory)type ) )
				category = (BuildCategory)type;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or ArgumentException )
		{
			Log?.Warning( $"Totem catalog data unavailable: {exception.Message}" );
		}
		totem = new BuildItem( ItemId, PrototypeRide.InfoId, category, ObjectNameIndex, cost, $"{archive}/Ptotem.MD2",
			new[] { $"{archive}/textures", $"/levels/{levelName}/sharetex", $"/levels/{levelName}/ssharete", $"{archive}/stexture" }, excitement );
	}

	public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) =>
		totem != null && totem.Category == category ? new[] { totem } : Array.Empty<BuildItem>();
}
