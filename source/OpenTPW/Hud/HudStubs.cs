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

	public BuildCharge ChargePlaced( BuildItem item, PrototypeRide ride )
	{
		if ( item.Cost > Money )
			return BuildCharge.NotEnoughMoney;
		Money -= item.Cost;
		return BuildCharge.Charged;
	}

	public long SellPlaced( PrototypeRide ride ) => 0;

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
	public BuildCharge ChargePlaced( BuildItem item, PrototypeRide ride ) => BuildCharge.Charged;
	public long SellPlaced( PrototypeRide ride ) => 0;
}

/// <summary>
/// HUD binding to the park economy of a level (<see cref="Level.Park"/>): balance, calendar and
/// speed from <see cref="ParkEconomy"/>, purchases through <see cref="ParkEconomy.TryBuild"/> and
/// sales through <see cref="ParkEconomy.Sell"/>. The runtime is looked up on every access, so a
/// loaded park save (which replaces the economy object) is followed.
/// </summary>
public sealed class EconomyParkStatus : IHudParkStatus
{
	private readonly Func<ParkEconomy?> economy;
	private readonly Func<GuestEconomyBridge?> guests;

	public EconomyParkStatus( Func<ParkEconomy?> economy, Func<GuestEconomyBridge?> guests )
	{
		this.economy = economy;
		this.guests = guests;
	}

	/// <summary>Binds to a level's runtime (followed on every access).</summary>
	public static EconomyParkStatus ForLevel( Level level ) => new( () => level.Park?.Economy, () => level.Park?.Guests );

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

	public void Update( float realSeconds ) { }

	public long? PriceOf( BuildItem item ) =>
		Economy is { } economy && economy.Catalog.TryGet( item.InfoId, out var info ) ? info.PurchaseCost : item.Cost;

	public bool IsAvailable( BuildItem item ) =>
		Economy is not { } park || park.Catalog.TryGet( item.InfoId, out _ ) && park.Research.IsAvailable( item.InfoId );

	public BuildCharge ChargePlaced( BuildItem item, PrototypeRide ride )
	{
		if ( Economy is not { } park )
			return BuildCharge.Charged;
		var result = park.TryBuild( item.InfoId, out var built );
		if ( result != ParkEconomy.PurchaseResult.Ok || built == null )
		{
			Log?.Trace( $"HUD purchase of {item.Id} refused by the park economy: {result}." );
			return result == ParkEconomy.PurchaseResult.NotEnoughMoney ? BuildCharge.NotEnoughMoney : BuildCharge.NotAvailable;
		}
		// The level linked the placement as an uncharged object; replace it with the purchased one.
		if ( guests() is { } bridge )
		{
			if ( bridge.TryGetInstance( ride.Visitors.AttractionId, out var placeholder ) && park.TryGetObject( placeholder, out _ ) )
				park.Remove( placeholder );
			bridge.Link( ride.Visitors.AttractionId, built.Id );
		}
		return BuildCharge.Charged;
	}

	public long SellPlaced( PrototypeRide ride )
	{
		if ( Economy is not { } park || guests() is not { } bridge || !bridge.TryGetInstance( ride.Visitors.AttractionId, out var instance ) || !park.TryGetObject( instance, out _ ) )
			return 0;
		bridge.Unlink( ride.Visitors.AttractionId );
		return park.Sell( instance );
	}
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
	// [APPROX:UI-033] price shown when Totem.sam cannot be read (the economy catalogue price is used when a park economy runs)
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
