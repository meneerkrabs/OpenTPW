using System.Globalization;

namespace OpenTPW.Hud;

/// <summary>
/// Stand-in for the economy slice. Starting cash is original data (<c>BankAccountInfo.InitialCash</c>
/// from the level's <c>Easy_Standard.sam</c>, 100,000 for the jungle); the calendar (one day per two
/// real seconds at normal speed, 30-day months, 12 months a year) and the speed multipliers are
/// OpenTPW placeholders, not original values.
/// </summary>
public sealed class StubParkStatus : IHudParkStatus
{
	public const float SecondsPerDay = 2f;
	public const long DefaultInitialCash = 100000;
	private double days;

	public StubParkStatus( long initialCash ) => Money = initialCash;

	public static StubParkStatus ForLevel( string levelName )
	{
		var cash = DefaultInitialCash;
		try
		{
			var settings = new SettingsFile( $"/levels/{levelName}/Easy_Standard.sam" );
			if ( long.TryParse( settings["BankAccountInfo.InitialCash"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) )
				cash = value;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or ArgumentException )
		{
			Log.Warning( $"Starting cash for {levelName} unavailable ({exception.Message}); using {DefaultInitialCash}." );
		}
		return new StubParkStatus( cash );
	}

	public long Money { get; private set; }
	public GameSpeed Speed { get; set; } = GameSpeed.Normal;
	public float TimeScale => Speed switch { GameSpeed.Paused => 0, GameSpeed.Fast => 2, GameSpeed.Fastest => 4, _ => 1 };

	public ParkDate Date
	{
		get
		{
			var day = (int)Math.Floor( days );
			return new ParkDate( day / 360 + 1, day / 30 % 12 + 1, day % 30 + 1 );
		}
	}

	public void Update( float realSeconds ) => days += Math.Max( 0, realSeconds ) * TimeScale / SecondsPerDay;

	public bool TrySpend( long amount )
	{
		if ( amount < 0 || amount > Money )
			return false;
		Money -= amount;
		return true;
	}

	public void Refund( long amount ) => Money += Math.Max( 0, amount );
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
	public const long FallbackCost = 3250;
	private readonly BuildItem? totem;

	public TotemBuildCatalog( string levelName = "jungle" )
	{
		var archive = $"/levels/{levelName}/rides/totem";
		var cost = FallbackCost;
		var category = BuildCategory.Rides;
		try
		{
			var settings = new SettingsFile( $"{archive}/Totem.sam" );
			if ( long.TryParse( settings["Upgrades[0].CostOfUpgrade"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value ) )
				cost = value;
			var rides = new SettingsFile( $"/levels/{levelName}/rides/Rides.sam" );
			if ( int.TryParse( rides["Info.WhichUIType"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var type ) && Enum.IsDefined( (BuildCategory)type ) )
				category = (BuildCategory)type;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or ArgumentException )
		{
			Log.Warning( $"Totem catalog data unavailable: {exception.Message}" );
		}
		totem = new BuildItem( ItemId, category, ObjectNameIndex, cost, $"{archive}/Ptotem.MD2",
			new[] { $"{archive}/textures", $"/levels/{levelName}/sharetex", $"/levels/{levelName}/ssharete", $"{archive}/stexture" } );
	}

	public IReadOnlyList<BuildItem> GetItems( BuildCategory category ) =>
		totem != null && totem.Category == category ? new[] { totem } : Array.Empty<BuildItem>();
}
