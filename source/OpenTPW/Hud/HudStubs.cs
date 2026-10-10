using System.Globalization;

namespace OpenTPW.Hud;

/// <summary>
/// Test stand-in for <see cref="IHudParkStatus"/> (the game binds the HUD to the park economy).
/// Starting cash is whatever the test passes; the calendar is a fixed test schedule.
/// </summary>
// [APPROX:UI-023] test-only calendar (2 s/day, 30-day months) — not used by the game
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
	// [APPROX:UI-022] without an economy only pause stops the rides; faster speeds run at normal speed
	public float TimeScale => Speed == GameSpeed.Paused ? 0 : 1;
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

	public EconomyParkStatus( Func<ParkEconomy?> economy )
	{
		this.economy = economy;
	}

	/// <summary>Binds to a level's runtime (followed on every access).</summary>
	public static EconomyParkStatus ForLevel( Level level ) => new( () => level.Park?.Economy );

	private ParkEconomy? Economy => economy();

	public bool HasEconomy => Economy != null;
	public long Money => Economy?.Balance ?? 0;
	public ParkDate Date => Economy?.Date ?? default;

	public GameSpeed Speed
	{
		get => Economy?.Speed ?? GameSpeed.Normal;
		set
		{
			if ( Economy is { } economy )
				economy.Speed = value;
		}
	}

	// The economy multiplies its own clock by Speed each fixed tick; rides and guests keep normal speed.
	// [APPROX:UI-022] faster speeds only speed up the park clock/economy, not rides or guests — evidence needed: original speed controls
	public float TimeScale => Speed == GameSpeed.Paused ? 0 : 1;

	public int GoldenTickets => Math.Max( 0, Economy?.GoldenTicketsAvailable ?? 0 );
	public object? EconomySource => Economy;

	public void Update( float realSeconds ) { }

	public long? PriceOf( BuildItem item ) =>
		Economy is { } economy && economy.Catalog.TryGet( item.InfoId, out var info ) ? info.PurchaseCost : item.Cost;

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
		categories = catalog.Buildable.OrderBy( entry => entry.InfoId ).Select( entry => new BuildItem(
			$"{entry.Theme}/{entry.InfoId}", entry.InfoId, (BuildCategory)entry.WhichUIType, entry.ObjectNameIndex ?? -1,
			entry.BuildCost, entry.IsBonus ? null : entry.PreviewModelPath ?? entry.ModelPath,
			ObjectAssets.TextureDirectories( entry ).Where( location => location.FileSystem == FileSystem ).Select( location => location.Directory ).ToArray(),
			entry.Settings.Has( "UsageInfo.ExcitementLevel" ) ? entry.Settings.GetInt( "UsageInfo.ExcitementLevel" ) : null, entry ) )
			.GroupBy( item => item.Category ).ToDictionary( group => group.Key, group => group.ToArray() );
	}

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
