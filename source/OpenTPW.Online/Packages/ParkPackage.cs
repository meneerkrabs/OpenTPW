using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OpenTPW.Online.Packages;

public sealed record ParkInfo( string Name, string Description, string Author, string Level );

public sealed record GameInfo( string Edition, string Language );

public sealed record BlobInfo( string Format, int Version, int Length, string Sha256 );

public sealed record ImageInfo( int Length, string Sha256, int Width, int Height );

/// <summary><c>manifest.json</c> of a <c>.tpwpark</c> package (see docs/ONLINE.md).</summary>
public sealed record ParkManifest(
	string Format,
	int FormatVersion,
	Guid PackageId,
	DateTimeOffset CreatedUtc,
	ParkInfo Park,
	GameInfo Game,
	IReadOnlyList<RequiredContent> RequiredContent,
	IReadOnlyList<string> Requires,
	IReadOnlyList<string> Flags,
	BlobInfo Payload,
	ImageInfo? Thumbnail );

/// <summary>
/// [EXT:ONLINE-005] The <c>.tpwpark</c> park package: a bounded ZIP with <c>manifest.json</c>,
/// <c>payload.bin</c> (opaque park state, hashed) and an optional <c>thumbnail.png</c>. Original
/// game assets are never stored in a package; content is referenced by id and SHA-256.
/// </summary>
public sealed record ParkPackage( ParkManifest Manifest, byte[] Payload, byte[]? Thumbnail )
{
	public const string FileExtension = ".tpwpark";
	public const string FormatName = "opentpw.park";
	public const int CurrentFormatVersion = 1;
	public const string ManifestEntry = "manifest.json";
	public const string PayloadEntry = "payload.bin";
	public const string ThumbnailEntry = "thumbnail.png";

	// [EXT:ONLINE-006] Size limits chosen for OpenTPW; the original upload limit ("File is too large",
	// ERRORMSG.str 85) has an unknown value.
	public const int MaximumPackageBytes = 24 * 1024 * 1024;
	public const int MaximumManifestBytes = 256 * 1024;
	public const int MaximumPayloadBytes = 16 * 1024 * 1024;
	public const int MaximumThumbnailBytes = 1024 * 1024;
	public const int MaximumThumbnailDimension = 512;
	public const int MaximumParkNameLength = 32;
	public const int MaximumDescriptionLength = 512;
	public const int MaximumRequiredContent = 1024;
	public const int MaximumFlags = 16;

	/// <summary>Features a reader of this version understands; anything else in <c>requires</c> is rejected.</summary>
	public static readonly IReadOnlySet<string> SupportedRequirements = new HashSet<string>( StringComparer.Ordinal );

	private static readonly ZipEntryRule[] Rules =
	{
		new( ManifestEntry, MaximumManifestBytes, true ),
		new( PayloadEntry, MaximumPayloadBytes, true ),
		new( ThumbnailEntry, MaximumThumbnailBytes, false ),
	};

	internal static readonly Regex LevelPattern = new( "^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant );
	private static readonly Regex FormatPattern = new( "^[a-z0-9][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant );
	private static readonly Regex FlagPattern = new( "^[a-z0-9][a-z0-9-]{0,47}$", RegexOptions.CultureInvariant );
	private static readonly Regex HashPattern = new( "^[0-9a-f]{64}$", RegexOptions.CultureInvariant );
	private static readonly Regex ContentIdPattern = new( "^[A-Za-z0-9_][A-Za-z0-9_./-]{0,127}$", RegexOptions.CultureInvariant );
	private static readonly Regex LanguagePattern = new( "^[A-Za-z]{1,32}$", RegexOptions.CultureInvariant );

	public static ParkPackage Create( ParkSnapshot snapshot, ParkInfo park, GameInfo game, byte[]? thumbnail, Guid? packageId = null, DateTimeOffset? createdUtc = null )
	{
		ArgumentNullException.ThrowIfNull( snapshot );
		ImageInfo? image = null;
		if ( thumbnail != null )
		{
			var (width, height) = PngImage.Validate( thumbnail, MaximumThumbnailDimension );
			image = new ImageInfo( thumbnail.Length, Hash( thumbnail ), width, height );
		}
		var flags = snapshot.Flags.Contains( CompatibilityFlags.ReadOnlyVisit ) ? snapshot.Flags : snapshot.Flags.Append( CompatibilityFlags.ReadOnlyVisit ).ToArray();
		var manifest = new ParkManifest( FormatName, CurrentFormatVersion, packageId ?? Guid.NewGuid(),
			createdUtc ?? DateTimeOffset.UtcNow, park with { Level = snapshot.Level }, game, snapshot.RequiredContent, Array.Empty<string>(), flags,
			new BlobInfo( snapshot.PayloadFormat, snapshot.PayloadVersion, snapshot.Payload.Length, Hash( snapshot.Payload ) ), image );
		var package = new ParkPackage( manifest, snapshot.Payload, thumbnail );
		package.Validate();
		return package;
	}

	public ParkSnapshot ToSnapshot() => new( Manifest.Payload.Format, Manifest.Payload.Version, Manifest.Park.Level, Payload, Manifest.RequiredContent, Manifest.Flags );

	public byte[] ToBytes()
	{
		Validate();
		var entries = new List<KeyValuePair<string, byte[]>>
		{
			new( ManifestEntry, StrictJson.Serialize( Manifest ) ),
			new( PayloadEntry, Payload ),
		};
		if ( Thumbnail != null )
			entries.Add( new( ThumbnailEntry, Thumbnail ) );
		var bytes = BoundedZip.Write( entries );
		if ( bytes.Length > MaximumPackageBytes )
			throw new InvalidDataException( "Park package exceeds the size limit." );
		return bytes;
	}

	public void Save( string path )
	{
		var bytes = ToBytes();
		AtomicFile.Write( path, bytes );
	}

	public static ParkPackage Read( Stream input ) => Read( BoundedZip.ReadBounded( input, MaximumPackageBytes, "Park package" ) );

	public static ParkPackage Load( string path )
	{
		using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read );
		return Read( stream );
	}

	public static ParkPackage Read( byte[] container )
	{
		if ( container.Length > MaximumPackageBytes )
			throw new InvalidDataException( "Park package exceeds the size limit." );
		var entries = BoundedZip.Read( container, Rules );
		var manifest = StrictJson.Deserialize<ParkManifest>( entries[ManifestEntry], "Park manifest" );
		entries.TryGetValue( ThumbnailEntry, out var thumbnail );
		var package = new ParkPackage( manifest, entries[PayloadEntry], thumbnail );
		package.Validate();
		return package;
	}

	/// <summary>Checks every manifest field and the payload/thumbnail hashes.</summary>
	public void Validate()
	{
		var m = Manifest ?? throw new InvalidDataException( "Park manifest is missing." );
		if ( m.Format != FormatName )
			throw new InvalidDataException( "File is not an OpenTPW park package." );
		if ( m.FormatVersion != CurrentFormatVersion )
			throw new InvalidDataException( $"Park package version {m.FormatVersion} is not supported (this build reads version {CurrentFormatVersion})." );
		if ( m.PackageId == Guid.Empty )
			throw new InvalidDataException( "Park package id is missing." );
		if ( m.Park == null || m.Game == null || m.Payload == null || m.RequiredContent == null || m.Requires == null || m.Flags == null )
			throw new InvalidDataException( "Park manifest has missing fields." );
		OnlineText.RequireText( m.Park.Name, MaximumParkNameLength, false, false, "Park name" );
		OnlineText.RequireText( m.Park.Description, MaximumDescriptionLength, true, true, "Park description" );
		OnlineText.RequireText( m.Park.Author, OnlineText.MaximumNameLength, false, true, "Park author" );
		if ( m.Park.Level == null || !LevelPattern.IsMatch( m.Park.Level ) )
			throw new InvalidDataException( "Park level must be a plain level directory name." );
		OnlineText.RequireText( m.Game.Edition, 64, false, false, "Game edition" );
		if ( m.Game.Language == null || !LanguagePattern.IsMatch( m.Game.Language ) )
			throw new InvalidDataException( "Game language must be a language folder name." );
		if ( m.RequiredContent.Count > MaximumRequiredContent )
			throw new InvalidDataException( "Park manifest lists too much required content." );
		foreach ( var content in m.RequiredContent )
		{
			if ( content == null || content.Kind == null || !ContentKinds.IsKnown( content.Kind ) )
				throw new InvalidDataException( "Park manifest has an unknown required content kind." );
			if ( content.Id == null || !ContentIdPattern.IsMatch( content.Id ) || content.Id.Contains( "..", StringComparison.Ordinal ) )
				throw new InvalidDataException( "Park manifest has an invalid content id." );
			if ( content.Sha256 != null && !HashPattern.IsMatch( content.Sha256 ) )
				throw new InvalidDataException( "Park manifest has an invalid content hash." );
		}
		if ( m.Requires.Count > MaximumFlags || m.Flags.Count > MaximumFlags )
			throw new InvalidDataException( "Park manifest has too many flags." );
		foreach ( var flag in m.Requires.Concat( m.Flags ) )
		{
			if ( flag == null || !FlagPattern.IsMatch( flag ) )
				throw new InvalidDataException( "Park manifest has an invalid flag." );
		}
		foreach ( var requirement in m.Requires )
		{
			if ( !SupportedRequirements.Contains( requirement ) )
				throw new InvalidDataException( $"Park needs the feature '{requirement}', which this OpenTPW version does not support." );
		}
		if ( !m.Flags.Contains( CompatibilityFlags.ReadOnlyVisit ) )
			throw new InvalidDataException( "Version 1 park packages must be marked for read-only visits." );
		if ( m.Payload.Format == null || !FormatPattern.IsMatch( m.Payload.Format ) || m.Payload.Version < 1 )
			throw new InvalidDataException( "Park payload format is invalid." );
		if ( Payload == null || m.Payload.Length != Payload.Length || m.Payload.Sha256 != Hash( Payload ) )
			throw new InvalidDataException( "Park payload does not match its manifest hash." );
		if ( (m.Thumbnail == null) != (Thumbnail == null) )
			throw new InvalidDataException( "Park thumbnail and manifest disagree." );
		if ( m.Thumbnail != null && Thumbnail != null )
		{
			if ( m.Thumbnail.Length != Thumbnail.Length || m.Thumbnail.Sha256 != Hash( Thumbnail ) )
				throw new InvalidDataException( "Park thumbnail does not match its manifest hash." );
			var (width, height) = PngImage.Validate( Thumbnail, MaximumThumbnailDimension );
			if ( width != m.Thumbnail.Width || height != m.Thumbnail.Height )
				throw new InvalidDataException( "Park thumbnail size does not match its manifest." );
		}
	}

	public static string Hash( ReadOnlySpan<byte> data ) => Convert.ToHexString( SHA256.HashData( data ) ).ToLowerInvariant();
}
