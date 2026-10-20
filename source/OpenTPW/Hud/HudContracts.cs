namespace OpenTPW.Hud;

/// <summary>
/// The online players of a visited park, as the visit's HUD uses them (<see cref="ParkPresence"/>): a click on the park's ground walks the player's
/// figure, and the original chat overlay shows the park's room (<see cref="Chat"/>).
/// </summary>
public interface IVisitPresence
{
	/// <summary>A click on the park's ground at (<paramref name="cellX"/>, <paramref name="cellY"/>): the figure walks there, or teleports on a double click.</summary>
	void Click( float cellX, float cellY );
	/// <summary>Whether a chat line can be said now (connected and in the park's room).</summary>
	bool CanChat { get; }
	/// <summary>Whether the figures are shown and a ground click walks the player's (the server relays movement in park rooms).</summary>
	bool Walks { get; }
	/// <summary>The server refused the park's room (the park was unpublished, or its chat satellite is gone): the visit has no other players.</summary>
	bool Refused { get; }
	/// <summary>The players' names and where their figures stand, drawn by the HUD under its windows.</summary>
	IEnumerable<VisitPresenceName> Names { get; }
	/// <summary>The park room's chat for the chat overlay; it sends only while the visit is in the room (<see cref="CanChat"/>).</summary>
	IChatPanelSource Chat { get; }
	/// <summary>Takes the player's figure to another player in the park (a name popup's goto).</summary>
	void GoTo( string name );
}

/// <summary>A player's name over their figure in a visited park: the player's own name or another's, and the figure's feet (engine position).</summary>
public readonly record struct VisitPresenceName( string Name, bool Own, System.Numerics.Vector3 Feet );

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
	/// <summary>Factor on the elapsed time of the whole simulation (guests, rides, scripts, economy; 0 when paused), see <see cref="GameSpeedScale"/>.</summary>
	float TimeScale { get; }
	/// <summary>Advances by real seconds (stubs only; the economy runs on the level's fixed clock).</summary>
	void Update( float realSeconds );
	/// <summary>Golden tickets the park has earned and not spent (the HUD counter, Mac main HUD table control 55); 0 without an economy.</summary>
	int GoldenTickets => 0;
	/// <summary>Golden keys (control 54): the player's keys, one per three golden tickets earned plus any extra keys; 0 without an economy and in Instant Action.</summary>
	int GoldenKeys => 0;
	/// <summary>The economy the balance comes from; it changes when a save replaces it, so a balance jump is not shown as income or spending.</summary>
	object? EconomySource => null;
	/// <summary>Purchase price shown in the build menu, null when unknown.</summary>
	long? PriceOf( BuildItem item );
	/// <summary>The golden tickets the item's next copy costs while the buy list shows it as a ticket row (the original's mystery-ride rows), null for a cash row.</summary>
	int? TicketCostOf( BuildItem item ) => null;
	/// <summary>Whether the item can be bought now (researched, in the catalogue).</summary>
	bool IsAvailable( BuildItem item );
}

/// <summary>Build-menu categories; the values are the original <c>Info.WhichUIType</c> (0 rides, 1 shops, 2 sideshows, 3 features).</summary>
public enum BuildCategory { Rides = 0, Shops = 1, Sideshows = 2, Features = 3 }

/// <summary>A buyable object in the build menu.</summary>
/// <param name="InfoId">Original <c>Info.Id</c> of the object (economy catalogue key).</param>
/// <param name="ObjectNameIndex">OBJECT_NAMES.str entry of the name.</param>
/// <param name="PreviewModel">Model drawn as the icon: the original preview model (<c>P&lt;name&gt;.MD2</c>), or the object's
/// main model when it has none, as the original buy window does (docs/reverse/BUILD-PREVIEW.md).</param>
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

	/// <summary>The Mystery ride tool (Info.Id 100) as a preview item: the model the buy window turns for a golden-ticket row the player has not bought yet; null when there is none.</summary>
	BuildItem? MysteryPreview => null;
}

/// <summary>One statistic of a selected object (label from UITEXT; null value = not simulated). A gauge row (<paramref name="IsGauge"/>) is a red-to-green bar of <paramref name="Gauge"/> (0..1; null draws the empty track) instead of text.</summary>
public readonly record struct ObjectStat( UIStrings Label, string? Value, float? Gauge = null, bool IsGauge = false, bool IsHappiness = false );

/// <summary>Info panel contents for a selected park object.</summary>
public sealed record ObjectInfo( int ObjectNameIndex, bool IsOpen, bool CanOpen, IReadOnlyList<ObjectStat> Stats, string? DisplayName = null );
