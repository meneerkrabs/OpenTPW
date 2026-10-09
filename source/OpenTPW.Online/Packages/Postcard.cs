namespace OpenTPW.Online.Packages;

/// <summary>Park a postcard points to: the package id and, when published, the server park id.</summary>
public sealed record PostcardParkReference( Guid PackageId, string ParkName, string Level, string? ServerParkId );

/// <summary><c>card.json</c> of a <c>.tpwcard</c>.</summary>
public sealed record PostcardManifest(
	string Format,
	int FormatVersion,
	Guid CardId,
	DateTimeOffset CreatedUtc,
	string From,
	IReadOnlyList<string> To,
	string Title,
	string Text,
	string Language,
	PostcardParkReference? Park,
	ImageInfo? Image );

/// <summary>
/// [EXT:ONLINE-020] The <c>.tpwcard</c> postcard: a bounded ZIP with <c>card.json</c> and an optional
/// <c>image.png</c>. The original sent postcards as e-mail (UITEXT "Send Postcard", "Message title",
/// "Message text", "Email address(es)"; weamailr.dll SMTP-style DoFrom/DoTo/DoData); OpenTPW keeps the
/// title/text/addressee shape but addresses players by name and sends files or server mail instead.
/// </summary>
public sealed record Postcard( PostcardManifest Manifest, byte[]? Image )
{
	public const string FileExtension = ".tpwcard";
	public const string FormatName = "opentpw.postcard";
	public const int CurrentFormatVersion = 1;
	public const string CardEntry = "card.json";
	public const string ImageEntry = "image.png";

	// [EXT:ONLINE-021] Limits chosen for OpenTPW; the original field lengths are unknown.
	public const int MaximumCardBytes = 2 * 1024 * 1024;
	public const int MaximumJsonBytes = 32 * 1024;
	public const int MaximumImageBytes = 1024 * 1024;
	public const int MaximumImageDimension = 640;
	public const int MaximumTitleLength = 64;
	public const int MaximumTextLength = 1024;
	public const int MaximumRecipients = 10;

	private static readonly ZipEntryRule[] Rules =
	{
		new( CardEntry, MaximumJsonBytes, true ),
		new( ImageEntry, MaximumImageBytes, false ),
	};

	public static Postcard Create( string from, IEnumerable<string> to, string title, string text, string language, PostcardParkReference? park = null, byte[]? image = null, Guid? cardId = null, DateTimeOffset? createdUtc = null )
	{
		ImageInfo? info = null;
		if ( image != null )
		{
			var (width, height) = PngImage.Validate( image, MaximumImageDimension );
			info = new ImageInfo( image.Length, ParkPackage.Hash( image ), width, height );
		}
		var card = new Postcard( new PostcardManifest( FormatName, CurrentFormatVersion, cardId ?? Guid.NewGuid(), createdUtc ?? DateTimeOffset.UtcNow,
			from, to.ToArray(), title, text.Replace( "\r\n", "\n" ), language, park, info ), image );
		card.Validate();
		return card;
	}

	public Guid Id => Manifest.CardId;

	public byte[] ToBytes()
	{
		Validate();
		var entries = new List<KeyValuePair<string, byte[]>> { new( CardEntry, StrictJson.Serialize( Manifest ) ) };
		if ( Image != null )
			entries.Add( new( ImageEntry, Image ) );
		return BoundedZip.Write( entries );
	}

	public void Save( string path ) => AtomicFile.Write( path, ToBytes() );

	public static Postcard Load( string path )
	{
		using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.Read );
		return Read( stream );
	}

	public static Postcard Read( Stream input ) => Read( BoundedZip.ReadBounded( input, MaximumCardBytes, "Postcard" ) );

	public static Postcard Read( byte[] container )
	{
		if ( container.Length > MaximumCardBytes )
			throw new InvalidDataException( "Postcard exceeds the size limit." );
		var entries = BoundedZip.Read( container, Rules );
		var manifest = StrictJson.Deserialize<PostcardManifest>( entries[CardEntry], "Postcard" );
		entries.TryGetValue( ImageEntry, out var image );
		var card = new Postcard( manifest, image );
		card.Validate();
		return card;
	}

	public void Validate()
	{
		var m = Manifest ?? throw new InvalidDataException( "Postcard data is missing." );
		if ( m.Format != FormatName )
			throw new InvalidDataException( "File is not an OpenTPW postcard." );
		if ( m.FormatVersion != CurrentFormatVersion )
			throw new InvalidDataException( $"Postcard version {m.FormatVersion} is not supported." );
		if ( m.CardId == Guid.Empty )
			throw new InvalidDataException( "Postcard id is missing." );
		OnlineText.RequirePlayerName( m.From, "Postcard sender" );
		if ( m.To == null || m.To.Count is < 1 or > MaximumRecipients )
			throw new InvalidDataException( $"A postcard needs 1-{MaximumRecipients} recipients." );
		var recipients = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var name in m.To )
		{
			OnlineText.RequirePlayerName( name, "Postcard recipient" );
			if ( !recipients.Add( OnlineText.NormalizeName( name ) ) )
				throw new InvalidDataException( "Postcard lists a recipient twice." );
		}
		OnlineText.RequireText( m.Title, MaximumTitleLength, false, false, "Postcard title" );
		OnlineText.RequireText( m.Text, MaximumTextLength, true, true, "Postcard text" );
		if ( m.Language == null || m.Language.Length is < 1 or > 32 || !m.Language.All( char.IsAsciiLetter ) )
			throw new InvalidDataException( "Postcard language is invalid." );
		if ( m.Park != null )
		{
			if ( m.Park.PackageId == Guid.Empty || m.Park.Level == null || !ParkPackage.LevelPattern.IsMatch( m.Park.Level ) )
				throw new InvalidDataException( "Postcard park reference is invalid." );
			OnlineText.RequireText( m.Park.ParkName, ParkPackage.MaximumParkNameLength, false, false, "Postcard park name" );
			if ( m.Park.ServerParkId != null && (m.Park.ServerParkId.Length is < 1 or > 64 || !m.Park.ServerParkId.All( char.IsAsciiLetterOrDigit )) )
				throw new InvalidDataException( "Postcard server park id is invalid." );
		}
		if ( (m.Image == null) != (Image == null) )
			throw new InvalidDataException( "Postcard image and data disagree." );
		if ( m.Image != null && Image != null )
		{
			if ( m.Image.Length != Image.Length || m.Image.Sha256 != ParkPackage.Hash( Image ) )
				throw new InvalidDataException( "Postcard image does not match its hash." );
			var (width, height) = PngImage.Validate( Image, MaximumImageDimension );
			if ( width != m.Image.Width || height != m.Image.Height )
				throw new InvalidDataException( "Postcard image size does not match its data." );
		}
	}
}
