namespace OpenTPW.Hud;

/// <summary>Park calendar date as shown on the HUD date display (original tooltip: "Current date").</summary>
public readonly record struct ParkDate( int Year, int Month, int Day );

/// <summary>Game speed selected on the HUD. Paused/normal/fast exist as an OpenTPW control; no original speed values are evidenced.</summary>
public enum GameSpeed { Paused, Normal, Fast, Fastest }

/// <summary>
/// What the HUD shows about the park's money and time. The economy/simulation slice implements
/// this; <see cref="StubParkStatus"/> is a stand-in until then.
/// </summary>
public interface IHudParkStatus
{
	/// <summary>Bank balance (original tooltip "Your bank balance").</summary>
	long Money { get; }
	ParkDate Date { get; }
	GameSpeed Speed { get; set; }
	/// <summary>Simulation time multiplier for <see cref="Speed"/> (0 when paused).</summary>
	float TimeScale { get; }
	/// <summary>Advances by real seconds (the stub's calendar; a real economy may ignore it).</summary>
	void Update( float realSeconds );
	/// <summary>Charges a purchase; false when there is not enough money.</summary>
	bool TrySpend( long amount );
	void Refund( long amount );
	/// <summary>Income (guest admissions and ride/shop payments).</summary>
	void Earn( long amount );
}

/// <summary>Build-menu categories; the values are the original <c>Info.WhichUIType</c> (0 rides, 1 shops, 2 sideshows, 3 features).</summary>
public enum BuildCategory { Rides = 0, Shops = 1, Sideshows = 2, Features = 3 }

/// <summary>A buyable object in the build menu.</summary>
/// <param name="ObjectNameIndex">OBJECT_NAMES.str entry of the name.</param>
/// <param name="PreviewModel">Path of the original preview model (<c>P&lt;name&gt;.MD2</c>) drawn as the icon.</param>
/// <param name="TextureDirectories">Where the preview model's textures are looked up, in order.</param>
/// <param name="DefaultExcitement">Original <c>UsageInfo.ExcitementLevel</c> ("average excitement level by default"), if known.</param>
public sealed record BuildItem( string Id, BuildCategory Category, int ObjectNameIndex, long Cost, string? PreviewModel, IReadOnlyList<string> TextureDirectories, int? DefaultExcitement = null );

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
public sealed record ObjectInfo( int ObjectNameIndex, bool IsOpen, bool CanOpen, IReadOnlyList<ObjectStat> Stats );
