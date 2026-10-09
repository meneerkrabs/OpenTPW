namespace OpenTPW.Online.Packages;

/// <summary>Content a park needs on the visitor's machine. Referenced by id/hash only; never embedded.</summary>
/// <param name="Kind">One of <see cref="ContentKinds"/>.</param>
/// <param name="Id">Level name, data-relative archive path or object Info.Id.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the local file, when the kind identifies a file.</param>
public sealed record RequiredContent( string Kind, string Id, string? Sha256 );

public static class ContentKinds
{
	/// <summary>An original level directory (<c>levels/&lt;id&gt;</c>); hash of its <c>terrain/base.map</c>.</summary>
	public const string Level = "level";
	/// <summary>An original object by Info.Id (<c>.sam</c> <c>Info.Id</c>).</summary>
	public const string Object = "object";
	/// <summary>A downloadable bonus object archive (<c>levels/&lt;level&gt;/&lt;category&gt;/_name_N.wad</c>).</summary>
	public const string BonusArchive = "bonus-archive";

	public static bool IsKnown( string kind ) => kind is Level or Object or BonusArchive;
}

/// <summary>
/// An opaque, versioned park state produced by whichever slice owns the park save format
/// (economy/simulation). The package format only stores and hashes it.
/// </summary>
/// <param name="PayloadFormat">Payload format id, e.g. <see cref="MinimalParkPayload.Format"/>.</param>
/// <param name="PayloadVersion">Version within that format.</param>
/// <param name="Level">Original level (theme) directory name, e.g. <c>jungle</c>.</param>
/// <param name="Payload">The serialized park state.</param>
/// <param name="RequiredContent">Content that must exist locally to show the park.</param>
/// <param name="Flags">Compatibility flags (see <see cref="CompatibilityFlags"/>).</param>
public sealed record ParkSnapshot(
	string PayloadFormat,
	int PayloadVersion,
	string Level,
	byte[] Payload,
	IReadOnlyList<RequiredContent> RequiredContent,
	IReadOnlyList<string> Flags );

/// <summary>Implemented by whatever owns the live park (the level, or the economy slice's save model).</summary>
public interface IParkSnapshotSource
{
	ParkSnapshot CaptureSnapshot();
}

/// <summary>Loads a snapshot for a read-only visit. Implementations must not write economy state or saves.</summary>
public interface IParkSnapshotSink
{
	bool CanLoad( string payloadFormat, int payloadVersion );
	void LoadReadOnly( ParkSnapshot snapshot );
}

/// <summary>[EXT:ONLINE-004] Compatibility flags written by OpenTPW.</summary>
public static class CompatibilityFlags
{
	/// <summary>Package is meant for read-only visits (always set for version 1).</summary>
	public const string ReadOnlyVisit = "read-only-visit";
	/// <summary>Payload has no economy/simulation state (money, guests, staff, research).</summary>
	public const string NoEconomy = "no-economy";
	/// <summary>Park is the OpenTPW generic sandbox rather than an imported original level.</summary>
	public const string Sandbox = "sandbox";

	public static readonly IReadOnlySet<string> Known = new HashSet<string>( StringComparer.Ordinal ) { ReadOnlyVisit, NoEconomy, Sandbox };
}
