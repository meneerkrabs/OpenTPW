namespace OpenTPW;

/// <summary>Object kind by original level directory (<c>rides</c>, <c>shops</c>, <c>sideshow</c>, <c>features</c>, <c>upgrades</c>).</summary>
public enum ParkObjectKind
{
	Ride,
	Shop,
	Sideshow,
	Feature,
	Upgrade,
	/// <summary>Gates, bus, lights, ferry and similar items placed by the level, not bought.</summary>
	FixedItem,
	/// <summary>Info.Id 101/102 "Buy Land"/"Clear Land" (UITEXT 134/135); priced per cell by <c>Costs.MapCell</c>.</summary>
	LandTool
}

/// <summary><c>Upgrades[n]</c> of an object: level 0 is the base purchase, levels 1 and 2 the ride upgrades.</summary>
/// <param name="RedLineSpeed"><c>Upgrades[n].RedLineSpeed</c>: at or above this speed a ride wears faster.</param>
/// <param name="RedLineCapacity"><c>Upgrades[n].RedLineCapacity</c>, compared with riders / 10 in the wear step.</param>
public sealed record UpgradeLevelInfo( int Level, long CostOfUpgrade, int CostOfResearch, int WearRate, int DurationOfUpgrade, IReadOnlyList<int> ScrapValuePercentByYear,
	int RedLineSpeed = 0, int RedLineCapacity = 0 );

/// <summary>
/// The economy-relevant fields of one original object, merged from the category <c>.sam</c>, the
/// object's own <c>.sam</c> and, in easy mode, its <c>Easy_</c> override. The rides/objects slice may
/// build these from its own catalog through <see cref="IEconomyObjectCatalog"/>.
/// </summary>
public sealed record EconomyObjectInfo(
	int InfoId,
	string Name,
	ParkObjectKind Kind,
	ResearchCategory ResearchCategory,
	int ResearchGroup,
	IReadOnlyList<UpgradeLevelInfo> Upgrades,
	int? PricePerUse,
	int? CostOfGoods,
	int? ChanceOfLosingPercent,
	int ShopType,
	int SpecialIngredient,
	int GoldenTicketCost,
	int AddOnTargetId,
	int LitterEffect,
	int AttractionValue,
	string ArchivePath,
	int MaxSpeed = 0,
	int MaxCapacity = 0 )
{
	public long PurchaseCost => Upgrades.Count > 0 ? Upgrades[0].CostOfUpgrade : 0;
	public bool IsBuyable => Kind is ParkObjectKind.Ride or ParkObjectKind.Shop or ParkObjectKind.Sideshow or ParkObjectKind.Feature or ParkObjectKind.Upgrade;
}

/// <summary>Source of object prices/costs for the economy. Implemented here from the original files; other slices may substitute their own.</summary>
public interface IEconomyObjectCatalog
{
	IReadOnlyCollection<EconomyObjectInfo> Objects { get; }
	bool TryGet( int infoId, out EconomyObjectInfo info );
}

/// <summary>Reads <see cref="EconomyObjectInfo"/> for every object archive of an original theme.</summary>
public sealed class EconomyObjectCatalog : IEconomyObjectCatalog
{
	private static readonly (string Directory, string CategoryFile, ParkObjectKind Kind)[] Categories =
	{
		("rides", "Rides.sam", ParkObjectKind.Ride),
		("shops", "Shops.sam", ParkObjectKind.Shop),
		("sideshow", "SideShow.sam", ParkObjectKind.Sideshow),
		("features", "Features.sam", ParkObjectKind.Feature),
		("upgrades", "Upgrades.sam", ParkObjectKind.Upgrade)
	};

	private readonly Dictionary<int, EconomyObjectInfo> objects;

	public EconomyObjectCatalog( IEnumerable<EconomyObjectInfo> objects )
	{
		this.objects = new Dictionary<int, EconomyObjectInfo>();
		foreach ( var item in objects )
		{
			if ( !this.objects.TryAdd( item.InfoId, item ) )
				throw new InvalidDataException( $"Two objects declare Info.Id {item.InfoId}." );
		}
	}

	public IReadOnlyCollection<EconomyObjectInfo> Objects => objects.Values;

	public bool TryGet( int infoId, out EconomyObjectInfo info ) => objects.TryGetValue( infoId, out info! );

	public EconomyObjectInfo this[int infoId] => objects.TryGetValue( infoId, out var info ) ? info : throw new KeyNotFoundException( $"No object with Info.Id {infoId}." );

	/// <summary>Merges one object's layers into an <see cref="EconomyObjectInfo"/>.</summary>
	public static EconomyObjectInfo FromSettings( SamSettings settings, ParkObjectKind directoryKind, string archivePath )
	{
		var id = settings.GetInt( "Info.Id" );
		var researchCategory = settings.GetInt( "Research.Category", (int)ResearchCategory.Feature, optional: true );
		var kind = directoryKind;
		if ( id is 101 or 102 )
			kind = ParkObjectKind.LandTool;
		else if ( directoryKind == ParkObjectKind.Feature && researchCategory != (int)ResearchCategory.Feature )
			// [APPROX:ECON-041] features-directory objects with Research.Category != 3 are fixed (non-buyable) items — evidence needed: buy-menu capture
			kind = ParkObjectKind.FixedItem; // gates, bus, lights, ferry, seaplane, end: Research.Category 0 in the features directory
		var upgrades = new List<UpgradeLevelInfo>();
		for ( var level = 0; level < 3; level++ )
		{
			if ( !settings.Contains( $"Upgrades[{level}].CostOfUpgrade" ) && level > 0 )
				break;
			var scrap = Enumerable.Range( 1, 4 ).Select( year => settings.GetInt( $"Upgrades[{level}].ScrapValueYear{year}", 0, optional: true ) ).ToArray();
			upgrades.Add( new UpgradeLevelInfo( level, settings.GetLong( $"Upgrades[{level}].CostOfUpgrade", 0L ), settings.GetInt( $"Upgrades[{level}].CostOfResearch", 0, optional: true ),
				settings.GetInt( $"Upgrades[{level}].WearRate", 0, optional: true ), settings.GetInt( $"Upgrades[{level}].DurationOfUpgrade", 0, optional: true ), scrap,
				settings.GetInt( $"Upgrades[{level}].RedLineSpeed", 0, optional: true ), settings.GetInt( $"Upgrades[{level}].RedLineCapacity", 0, optional: true ) ) );
		}
		// Shops and features list levels 1 and 2 with zero cost ("no upgrades possible"); keep only real upgrade levels.
		while ( upgrades.Count > 1 && upgrades[^1].CostOfUpgrade == 0 && upgrades[^1].CostOfResearch == 0 )
			upgrades.RemoveAt( upgrades.Count - 1 );
		int? Optional( string key ) => settings.Contains( key ) ? settings.GetInt( key ) : null;
		var hasPrices = kind is ParkObjectKind.Shop or ParkObjectKind.Sideshow;
		return new EconomyObjectInfo( id, settings.Contains( "Info.Name" ) ? settings.GetString( "Info.Name" ).Trim() : $"Info.Id {id}", kind,
			(ResearchCategory)Math.Clamp( researchCategory, 0, 4 ), settings.GetInt( "Research.Group", 0, optional: true ), upgrades,
			// [BIN:STP-PPC:0x100EAAF8 sideshow win] each sideshow win charges the object's cost of goods (+0x184, copied from catalogue word 0x50 = UsageInfo.InitCostOfGoods at +0x140) to its costs and the bank
			hasPrices ? Optional( "UsageInfo.InitPricePerUse" ) : null, hasPrices ? Optional( "UsageInfo.InitCostOfGoods" ) : null,
			kind == ParkObjectKind.Sideshow ? Optional( "UsageInfo.InitChanceOfLoosing" ) : null,
			settings.GetInt( "UsageInfo.ShopType", 0, optional: true ), settings.GetInt( "UsageInfo.SpecialIngredient", 0, optional: true ),
			settings.GetInt( "UsageInfo.GoldenTicketCost", 0, optional: true ), settings.GetInt( "AddOn.UpgradesId", 0, optional: true ),
			settings.GetInt( "UsageInfo.LitterEffect", 0, optional: true ), settings.GetInt( "Info.AttractionValue", 0, optional: true ), archivePath,
			settings.GetInt( "UsageInfo.MaxSpeed", 0, optional: true ), settings.GetInt( "UsageInfo.MaxCapacity", 0, optional: true ) );
	}

	/// <summary>
	/// Loads every object of <paramref name="theme"/>. Per archive the object file is the <c>.sam</c>
	/// declaring <c>Info.Id</c>; it is layered over the category file (<c>Rides.sam</c> …) and, in easy
	/// mode, under <c>Easy_&lt;object file&gt;</c>. <c>Online_</c> files are ignored (offline game).
	/// </summary>
	public static EconomyObjectCatalog Load( string theme, bool easy )
	{
		BalanceSettings.GetStandardLayers( theme, false );
		var result = new List<EconomyObjectInfo>();
		foreach ( var (directory, categoryFile, kind) in Categories )
		{
			var categoryPath = $"/levels/{theme}/{directory}";
			if ( !FileSystem.DirectoryExists( categoryPath ) )
				continue;
			var categoryLayer = FileSystem.FileExists( $"{categoryPath}/{categoryFile}" )
				? SamDocument.Parse( FileSystem.ReadAllText( $"{categoryPath}/{categoryFile}" ), $"{categoryPath}/{categoryFile}" )
				: new SamDocument( categoryFile, Array.Empty<SamEntry>() );
			foreach ( var archive in FileSystem.GetDirectories( categoryPath ).OrderBy( item => item, StringComparer.OrdinalIgnoreCase ) )
			{
				var archivePath = FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( archive ) );
				var files = FileSystem.GetFiles( archivePath )
					.Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) )
					.Select( file => FileSystem.GetRelativePath( FileSystem.GetAbsolutePath( file ) ) )
					.ToDictionary( file => Path.GetFileName( file ), StringComparer.OrdinalIgnoreCase );
				foreach ( var (name, path) in files.OrderBy( item => item.Key, StringComparer.OrdinalIgnoreCase ) )
				{
					if ( name.StartsWith( "Easy_", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "Online_", StringComparison.OrdinalIgnoreCase ) )
						continue;
					var objectLayer = SamDocument.Parse( FileSystem.ReadAllText( path ), path );
					if ( !objectLayer.Entries.Any( entry => entry.Key.Equals( "Info.Id", StringComparison.OrdinalIgnoreCase ) ) )
						continue;
					var layers = new List<SamDocument> { categoryLayer, objectLayer };
					if ( easy && files.TryGetValue( "Easy_" + name, out var easyPath ) )
						layers.Add( SamDocument.Parse( FileSystem.ReadAllText( easyPath ), easyPath ) );
					result.Add( FromSettings( new SamSettings( layers ), kind, archivePath ) );
				}
			}
		}
		return new EconomyObjectCatalog( result );
	}
}
