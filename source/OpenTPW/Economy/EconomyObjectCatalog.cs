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
/// <param name="RunningCost">Theme Park Inc only: <c>Upgrades[n].RunningCost</c> (type record +0x2B4 + 0x44 · n), the object's monthly running cost at this level; 0 in Theme Park World, whose files lack the key.</param>
/// <param name="CashPerBrainPoint">Theme Park Inc only: <c>Upgrades[n].CashPerBrainPoint</c> (+0x274 + 0x44 · n); read, not used by the money slice (meaning not traced).</param>
public sealed record UpgradeLevelInfo( int Level, long CostOfUpgrade, int CostOfResearch, int WearRate, int DurationOfUpgrade, IReadOnlyList<int> ScrapValuePercentByYear,
	int RedLineSpeed = 0, int RedLineCapacity = 0, int RunningCost = 0, int CashPerBrainPoint = 0 );

/// <summary>
/// The economy-relevant fields of one original object, merged from the category <c>.sam</c>, the
/// object's own <c>.sam</c> and, in easy mode, its <c>Easy_</c> override. The rides/objects slice may
/// build these from its own catalog through <see cref="IEconomyObjectCatalog"/>.
/// </summary>
/// <param name="ExpectedPrice">Theme Park Inc only: <c>Info.ExpectedPrice</c> (type record +0x88): the base of the guest's willingness to pay for every kind but a sideshow (Game.exe <c>0x6120FC</c>); read, not used by OpenTPW's price test (ECON-062); 0 in Theme Park World.</param>
/// <param name="RidePricePerUse">Theme Park Inc only: a ride's <c>UsageInfo.InitPricePerUse</c>, the fee each use takes (ECON-062); 0 in Theme Park World, whose rides are free.</param>
/// <param name="NotResearched">[EXT:mods] True for an object of an <c>objects.json</c> that declares <c>"research": false</c> (the pools extension's): it is never a research project, so it is in no research table, never in the saved completed set, does not count in the group statistics and is available from the start; the golden ticket's "everything built" does not ask for it.</param>
/// <param name="WorkingLife">Theme Park Inc only: <c>Info.WorkingLife</c> (+0x8C); read, not used by the money slice (meaning not traced); 0 in Theme Park World.</param>
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
	int MaxCapacity = 0,
	int NewAttractionDecayTime = 0,
	IReadOnlyList<int>? NewBonus = null,
	int TrackType = 0,
	bool IsChoosable = true,
	bool ChillsYouOut = false,
	int WhichUIType = -1,
	bool ProvidesRelief = false,
	bool ProvidesSecurity = false,
	bool IsFireworks = false,
	int ExpectedPrice = 0,
	int WorkingLife = 0,
	bool NotResearched = false,
	bool RequiresWatering = false,
	int RidePricePerUse = 0 )
{
	public long PurchaseCost => Upgrades.Count > 0 ? Upgrades[0].CostOfUpgrade : 0;
	public bool IsBuyable => Kind is ParkObjectKind.Ride or ParkObjectKind.Shop or ParkObjectKind.Sideshow or ParkObjectKind.Feature or ParkObjectKind.Upgrade;

	/// <summary>
	/// The type record's <c>Info.WhichUIType</c> (<c>+1960</c>) the park rating counts objects by (<c>0xc7b24</c>, ORACLE.md F29): 0 ride, 1 shop, 2 sideshow, 3 feature, 4 not
	/// shown. The shipped add-ons (the "upgrades" directory: the karts' jumps and tunnels, the racers' teleport) declare 0, so they count as rides. An object without the
	/// key (a synthetic one) takes the value of its directory.
	/// </summary>
	public int RatingType => WhichUIType >= 0 ? WhichUIType : Kind switch
	{
		ParkObjectKind.Ride or ParkObjectKind.Upgrade => 0,
		ParkObjectKind.Shop => 1,
		ParkObjectKind.Sideshow => 2,
		ParkObjectKind.Feature => 3,
		_ => 4
	};
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
		// [BIN:TPW-EXE:0x4aaf70 buy-menu filler] a definition is listed only in the tab (0-3) equal to its Info.WhichUIType (+0x4ac, copied from +0x4c by 0x413410),
		// so a WhichUIType 4 object is never offered; Research.Category is not read. In the features directory those are the six fixed items of each theme
		// (bus, gates, seaplane, lights, ferry, end); the mystery ride (rides directory, also 4) is bought with golden tickets, not here
		else if ( directoryKind == ParkObjectKind.Feature && settings.GetInt( "Info.WhichUIType", -1, optional: true ) == 4 )
			kind = ParkObjectKind.FixedItem;
		var upgrades = new List<UpgradeLevelInfo>();
		for ( var level = 0; level < 3; level++ )
		{
			if ( !settings.Contains( $"Upgrades[{level}].CostOfUpgrade" ) && level > 0 )
				break;
			var scrap = Enumerable.Range( 1, 4 ).Select( year => settings.GetInt( $"Upgrades[{level}].ScrapValueYear{year}", 0, optional: true ) ).ToArray();
			upgrades.Add( new UpgradeLevelInfo( level, settings.GetLong( $"Upgrades[{level}].CostOfUpgrade", 0L ), settings.GetInt( $"Upgrades[{level}].CostOfResearch", 0, optional: true ),
				settings.GetInt( $"Upgrades[{level}].WearRate", 0, optional: true ), settings.GetInt( $"Upgrades[{level}].DurationOfUpgrade", 0, optional: true ), scrap,
				settings.GetInt( $"Upgrades[{level}].RedLineSpeed", 0, optional: true ), settings.GetInt( $"Upgrades[{level}].RedLineCapacity", 0, optional: true ),
				// [BIN:TPI-EXE:0x005DEFFF monthly object cost] Theme Park Inc's type record holds Upgrades[n].RunningCost (+0x2B4, stride 0x44) and CashPerBrainPoint (+0x274); Theme Park World's files carry neither, so they read 0 there
				settings.GetInt( $"Upgrades[{level}].RunningCost", 0, optional: true ), settings.GetInt( $"Upgrades[{level}].CashPerBrainPoint", 0, optional: true ) ) );
		}
		// Shops and features list levels 1 and 2 with zero cost ("no upgrades possible"); keep only real upgrade levels.
		while ( upgrades.Count > 1 && upgrades[^1].CostOfUpgrade == 0 && upgrades[^1].CostOfResearch == 0 )
			upgrades.RemoveAt( upgrades.Count - 1 );
		int? Optional( string key ) => settings.Contains( key ) ? settings.GetInt( key ) : null;
		// [BIN:TPW-EXE:0x004FE1A0 price taken] only shops and sideshows carry a price per use in Theme Park World; Theme Park Inc's rides read theirs through RidePricePerUse
		// [APPROX:ECON-062] a Theme Park Inc ride's fee is charged and a guest without the cash is refused (0x6125E7, 0x61243A), but the willingness test of Game.exe's price test 0x6120C0 (Info.ExpectedPrice as its base, the attraction's rating byte 0x5E3DF0 against the guest's expected byte) is not ported, so no ride is refused for its price
		var hasPrices = kind is ParkObjectKind.Shop or ParkObjectKind.Sideshow;
		return new EconomyObjectInfo( id, settings.Contains( "Info.Name" ) ? settings.GetString( "Info.Name" ).Trim() : $"Info.Id {id}", kind,
			(ResearchCategory)Math.Clamp( researchCategory, 0, 4 ), settings.GetInt( "Research.Group", 0, optional: true ), upgrades,
			// [BIN:STP-PPC:0x100EAAF8 sideshow win] each sideshow win charges the object's cost of goods (+0x184, copied from catalogue word 0x50 = UsageInfo.InitCostOfGoods at +0x140) to its costs and the bank
			hasPrices ? Optional( "UsageInfo.InitPricePerUse" ) : null, hasPrices ? Optional( "UsageInfo.InitCostOfGoods" ) : null,
			kind == ParkObjectKind.Sideshow ? Optional( "UsageInfo.InitChanceOfLoosing" ) : null,
			settings.GetInt( "UsageInfo.ShopType", 0, optional: true ), settings.GetInt( "UsageInfo.SpecialIngredient", 0, optional: true ),
			settings.GetInt( "UsageInfo.GoldenTicketCost", 0, optional: true ), settings.GetInt( "AddOn.UpgradesId", 0, optional: true ),
			settings.GetInt( "UsageInfo.LitterEffect", 0, optional: true ), settings.GetInt( "Info.AttractionValue", 0, optional: true ), archivePath,
			settings.GetInt( "UsageInfo.MaxSpeed", 0, optional: true ), settings.GetInt( "UsageInfo.MaxCapacity", 0, optional: true ),
			settings.GetInt( "Info.NewAttractionDecayTime", 0, optional: true ),
			Enumerable.Range( 0, 3 ).Select( index => settings.GetInt( $"Attraction[{index}].NewBonus", 0, optional: true ) ).ToArray(),
			// [DATA:<object>.sam:Bumper.WhichTrackType] type record +0x9C (0xe2fe4 tests it against 3, the coaster value): 0 none, 1 car, 2 water track
			settings.GetInt( "Bumper.WhichTrackType", 0, optional: true ),
			// [BIN:STP-PPC:0x100DABDC object constructor] type record +0x3C (Info.IsChoosable) nonzero sets thing +46 bit 0x4 (0x100DABDC..0x100DABF0), the first test of the park points counts-test 0x100DC834; the category files (Rides.sam, SideShow.sam, Shops.sam) declare it 1
			// An object file that declares neither the key nor a category default is read as choosable (the shipped category files always declare it).
			settings.GetInt( "Info.IsChoosable", 1, optional: true ) != 0,
			// [BIN:STP-PPC:0x100DABD0 object constructor] type record +0xFC (UsageInfo.ChillsYouOut) nonzero sets thing +46 bit 0x2, the only test of the rest-area search 0x100F47A0 (ORACLE.md F22): any such object is a rest area for staff
			settings.GetInt( "UsageInfo.ChillsYouOut", 0, optional: true ) != 0,
			// [DATA:<object>.sam:Info.WhichUIType] type record +1960, which 0xc7b24 counts objects by
			settings.GetInt( "Info.WhichUIType", -1, optional: true ),
			// [DATA:<object>.sam:UsageInfo.ProvidesRelief] the relief flag (thing +0x32 bit 0 on the PC): the toilet's gauge wears with every use and handymen clean it (tp.exe 0x004E2440, 0x004D7880)
			settings.GetInt( "UsageInfo.ProvidesRelief", 0, optional: true ) != 0,
			// [BIN:TPW-EXE:0x004DB090 object constructor] UsageInfo.ProvidesSecurity (type +0xF0, thing +0x32 bit 0x10) adds RegionFX row 4 at the object's cell: the security cameras of all four themes
			settings.GetInt( "UsageInfo.ProvidesSecurity", 0, optional: true ) != 0,
			// [BIN:TPW-EXE:0x004DB090 object constructor] UsageInfo.IsFireworks (type +0x110, thing +0x32 bit 0x80) adds RegionFX row 7: Hallow's Firework Show and Space's Laser Show
			settings.GetInt( "UsageInfo.IsFireworks", 0, optional: true ) != 0,
			// [BIN:TPI-EXE:0x005DEFFF type record] Theme Park Inc only: Info.ExpectedPrice (+0x88) and Info.WorkingLife (+0x8C); neither is used yet
			settings.GetInt( "Info.ExpectedPrice", 0, optional: true ), settings.GetInt( "Info.WorkingLife", 0, optional: true ),
			// [BIN:TPI-EXE:0x5DB654 thing flag +0x43 bit 0x10] a nonzero type word +0x1AC (UsageInfo.RequiresWatering, between HoldsLitter 0x1A8 and ChillsYouOut 0x1B0 in the key order) makes the thing builder set the flag (or word [thing+0x42], 0x1000) that selects the scrap percentage's first branch (0x5E75F9)
			RequiresWatering: settings.GetInt( "UsageInfo.RequiresWatering", 0, optional: true ) != 0,
			// [BIN:TPI-EXE:0x5DB58D thing price] the thing's price per use (+0x94) starts as the type's UsageInfo.InitPricePerUse (+0x194) for every kind, a ride's included (20 in Rides.sam); Theme Park World's rides carry no such key
			RidePricePerUse: kind == ParkObjectKind.Ride ? settings.GetInt( "UsageInfo.InitPricePerUse", 0, optional: true ) : 0 );
	}

	/// <summary>
	/// Loads every object of <paramref name="theme"/>. Per archive the object file is the <c>.sam</c>
	/// declaring <c>Info.Id</c>; it is layered over the category file (<c>Rides.sam</c> …) and, in easy
	/// mode, under <c>Easy_&lt;object file&gt;</c>. <c>Online_</c> files are ignored (offline game).
	/// </summary>
	public static EconomyObjectCatalog Load( string theme, bool easy ) => LoadSteps( theme, easy ).Last()!;

	/// <summary>
	/// <see cref="Load"/> in steps, for a park built over frames (docs/BROWSER-PERFORMANCE.md, "Build steps"): null after each object
	/// archive and after each archive of the object catalogue the mods' objects are read from, then the catalogue. The work and its order are those of <see cref="Load"/>.
	/// </summary>
	internal static IEnumerable<EconomyObjectCatalog?> LoadSteps( string theme, bool easy )
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
				yield return null;
			}
		}
		// the mods' objects come from the object catalogue (AddModObjects), which is loaded here in its own steps; the objects step finds it cached
		if ( ObjectCatalog.ContentRoots.Count != 0 )
			foreach ( var loaded in ObjectCatalog.LoadSteps( theme, easy ) )
				if ( loaded == null )
					yield return null;
		AddModObjects( theme, easy, result );
		yield return new EconomyObjectCatalog( result );
	}

	/// <summary>
	/// [EXT:mods] The objects the enabled mods add to <paramref name="theme"/> (docs/MODS.md), after the game's: each catalogue entry of a mod root gets the same
	/// layers as a game object (the theme's category file from the game data, the object's .sam, <c>Easy_</c> in easy mode) plus the Info.Id its
	/// <c>objects.json</c> assigns, so a bought mod object is priced, researched and listed like any other. With no mod objects nothing is added or read.
	/// </summary>
	private static void AddModObjects( string theme, bool easy, List<EconomyObjectInfo> result )
	{
		if ( ObjectCatalog.ContentRoots.Count == 0 )
			return;
		foreach ( var entry in ObjectCatalog.Load( theme, easy ).Entries.Where( entry => entry.ModDefinition != null ) )
		{
			var (directory, categoryFile, kind) = Categories.First( category => category.Directory == OpenTPW.Mods.ModObjectsFile.CategoryName( entry.Category ) );
			var categoryPath = $"/levels/{theme}/{directory}";
			var categoryLayer = FileSystem.FileExists( $"{categoryPath}/{categoryFile}" )
				? SamDocument.Parse( FileSystem.ReadAllText( $"{categoryPath}/{categoryFile}" ), $"{categoryPath}/{categoryFile}" )
				: new SamDocument( categoryFile, Array.Empty<SamEntry>() );
			var files = entry.FileSystem.GetFiles( entry.ArchivePath )
				.Where( file => file.EndsWith( ".sam", StringComparison.OrdinalIgnoreCase ) )
				.Select( file => entry.FileSystem.GetRelativePath( entry.FileSystem.GetAbsolutePath( file ) ) )
				.ToDictionary( file => Path.GetFileName( file ), StringComparer.OrdinalIgnoreCase );
			SamDocument? objectLayer = null;
			string? objectFile = null;
			foreach ( var (name, path) in files.OrderBy( item => item.Key, StringComparer.OrdinalIgnoreCase ) )
			{
				if ( name.StartsWith( "Easy_", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "Online_", StringComparison.OrdinalIgnoreCase ) )
					continue;
				var layer = SamDocument.Parse( entry.FileSystem.ReadAllText( path ), path );
				if ( !layer.Entries.Any( item => item.Key.Equals( "Info.Id", StringComparison.OrdinalIgnoreCase ) ) )
					continue;
				(objectLayer, objectFile) = (layer, name);
				break;
			}
			if ( objectLayer == null )
				continue;
			var layers = new List<SamDocument> { categoryLayer, objectLayer };
			if ( easy && files.TryGetValue( "Easy_" + objectFile, out var easyPath ) )
				layers.Add( SamDocument.Parse( entry.FileSystem.ReadAllText( easyPath ), easyPath ) );
			layers.Add( SamDocument.Parse( ObjectCatalog.ModLayerText( entry.ModDefinition! ), "objects.json" ) );
			var modInfo = FromSettings( new SamSettings( layers ), kind, entry.ArchivePath );
			result.Add( entry.ModDefinition.Research ? modInfo : modInfo with { NotResearched = true } );
		}
	}
}
