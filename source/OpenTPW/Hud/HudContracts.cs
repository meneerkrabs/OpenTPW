namespace OpenTPW.Hud;

/// <summary>
/// What the HUD shows and does with the park's money and time (<see cref="ParkDate"/> and
/// <see cref="GameSpeed"/> are the economy slice's). The game binds it to <see cref="Level.Park"/>
/// (<see cref="EconomyParkStatus"/>); <see cref="StubParkStatus"/> exists for tests.
/// </summary>
public interface IHudParkStatus
{
	/// <summary>False when the level runs no park economy (the generic sandbox): money and date are hidden.</summary>
	bool HasEconomy { get; }
	/// <summary>Bank balance (original tooltip "Your bank balance").</summary>
	long Money { get; }
	ParkDate Date { get; }
	GameSpeed Speed { get; set; }
	/// <summary>Multiplier for rides/guests on the fixed clock (0 when paused).</summary>
	float TimeScale { get; }
	/// <summary>Advances by real seconds (stubs only; the economy runs on the level's fixed clock).</summary>
	void Update( float realSeconds );
	/// <summary>Purchase price shown in the build menu, null when unknown.</summary>
	long? PriceOf( BuildItem item );
	/// <summary>Whether the item can be bought now (researched, in the catalogue).</summary>
	bool IsAvailable( BuildItem item );
}

/// <summary>Build-menu categories; the values are the original <c>Info.WhichUIType</c> (0 rides, 1 shops, 2 sideshows, 3 features).</summary>
public enum BuildCategory { Rides = 0, Shops = 1, Sideshows = 2, Features = 3 }

/// <summary>A buyable object in the build menu.</summary>
/// <param name="InfoId">Original <c>Info.Id</c> of the object (economy catalogue key).</param>
/// <param name="ObjectNameIndex">OBJECT_NAMES.str entry of the name.</param>
/// <param name="PreviewModel">Path of the original preview model (<c>P&lt;name&gt;.MD2</c>) drawn as the icon.</param>
/// <param name="TextureDirectories">Where the preview model's textures are looked up, in order.</param>
/// <param name="DefaultExcitement">Original <c>UsageInfo.ExcitementLevel</c> ("average excitement level by default"), if known.</param>
public sealed record BuildItem( string Id, int InfoId, BuildCategory Category, int ObjectNameIndex, long Cost, string? PreviewModel, IReadOnlyList<string> TextureDirectories, int? DefaultExcitement = null, ObjectCatalogEntry? Entry = null );

/// <summary>
/// What can be bought. The object-catalog slice implements this for every ride/shop/sideshow/feature;
/// <see cref="TotemBuildCatalog"/> offers the sandbox Totem only.
/// </summary>
public interface IBuildCatalog
{
	IReadOnlyList<BuildItem> GetItems( BuildCategory category );
}

/// <summary>One statistic of a selected object (label from UITEXT; null value = not simulated).</summary>
public readonly record struct ObjectStat( UIStrings Label, string? Value );

/// <summary>Info panel contents for a selected park object.</summary>
public sealed record ObjectInfo( int ObjectNameIndex, bool IsOpen, bool CanOpen, IReadOnlyList<ObjectStat> Stats, string? DisplayName = null );
