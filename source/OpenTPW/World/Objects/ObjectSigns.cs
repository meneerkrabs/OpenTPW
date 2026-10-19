namespace OpenTPW;

/// <summary>The two sign textures of one object type and name: RGBA halves, plus the GPU textures once requested.</summary>
public sealed class ObjectSignImages
{
	internal ObjectSignImages( string line1, string line2, byte[] left, byte[] right, int textPixels, IReadOnlyList<string> diagnostics )
	{
		Line1 = line1;
		Line2 = line2;
		Left = left;
		Right = right;
		TextPixelCount = textPixels;
		Diagnostics = diagnostics;
	}

	public string Line1 { get; private set; }
	public string Line2 { get; private set; }
	/// <summary>RGBA of the <c>sign1</c> texture (<see cref="SignSurfaceComposer.HalfWidth"/> x <see cref="SignSurfaceComposer.Height"/>).</summary>
	public byte[] Left { get; private set; }
	/// <summary>RGBA of the <c>sign2</c> texture.</summary>
	public byte[] Right { get; private set; }
	/// <summary>Nonzero text-mask pixels, independent of the board image.</summary>
	public int TextPixelCount { get; private set; }
	public IReadOnlyList<string> Diagnostics { get; private set; }

	private Texture? leftTexture;
	private Texture? rightTexture;

	/// <summary>
	/// New text for images owned by one entity (the park gate's sign after a rename): the halves are replaced and GPU textures
	/// made so far take the new pixels in place, so materials that bind them follow.
	/// </summary>
	internal void Replace( string line1, string line2, byte[] left, byte[] right, int textPixels, IReadOnlyList<string> diagnostics )
	{
		if ( left.Length != Left.Length || right.Length != Right.Length )
			throw new ArgumentException( "Sign halves must keep their size." );
		Line1 = line1;
		Line2 = line2;
		Left = left;
		Right = right;
		TextPixelCount = textPixels;
		Diagnostics = diagnostics;
		leftTexture?.Replace( left, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height );
		rightTexture?.Replace( right, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height );
	}

	/// <summary>Releases the GPU textures made so far, for images owned by one entity (the park gate's own sign); type signs are shared and stay.</summary>
	internal void ReleaseTextures()
	{
		leftTexture?.ReleaseOwned();
		rightTexture?.ReleaseOwned();
		leftTexture = null;
		rightTexture = null;
	}

	/// <summary>GPU texture of the <c>sign1</c> half (created on first use, shared by every object of the type).</summary>
	public Texture LeftTexture => leftTexture ??= new Texture( Left, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height );
	/// <summary>GPU texture of the <c>sign2</c> half.</summary>
	public Texture RightTexture => rightTexture ??= new Texture( Right, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height );

	/// <summary>The texture for a material slot name (<c>sign1</c>/<c>sign2</c>, with or without extension), or null.</summary>
	public Texture? TextureFor( string textureName ) => ObjectSigns.SlotOf( textureName ) switch { 0 => LeftTexture, 1 => RightTexture, _ => null };
}

/// <summary>
/// The text on ride, shop and sideshow signs. Object models that carry the material slots <c>sign1</c>/<c>sign2</c>
/// get their textures from here instead of the magenta "SIGN1"/"SIGN2" placeholders: the object's name is split into
/// two lines and drawn with the fonts and effects of the object's <c>.sgn</c> (docs/COMPATIBILITY.md).
/// </summary>
public static class ObjectSigns
{
	private static readonly Dictionary<string, ObjectSignImages?> cache = new( StringComparer.Ordinal );

	/// <summary>0 for <c>sign1</c>, 1 for <c>sign2</c> (extension and case ignored), else -1.</summary>
	public static int SlotOf( string textureName ) => Path.GetFileNameWithoutExtension( textureName.TrimEnd( '\0' ) ).ToLowerInvariant() switch { "sign1" => 0, "sign2" => 1, _ => -1 };

	/// <summary>True for a material name that the game draws at run time.</summary>
	public static bool IsSignSlot( string textureName ) => SlotOf( textureName ) >= 0;

	/// <summary>
	/// The two lines of an object's sign. The name is the object's info-record name (<see cref="ObjectCatalogEntry.SignName"/>:
	/// the OBJECT_NAMES entries joined by a space, as the build list shows it, or empty for an id outside the original's
	/// table), else its display name. Bonus objects whose language file gives <c>SIGNA</c>/<c>SIGNB</c> use those two lines.
	/// </summary>
	// [BIN:STP-PPC:0x1018E254 sign text builder] one name string is split at its middle space (SignCanvas.SplitAtMiddleSpace); a caller may instead pass the two lines (text slot 0 and 1) explicitly
	// [BIN:STP-PPC:0x1011A08C object name builder] the name string (info record +0x70C) is the first OBJECT_NAMES text, a space, then the second ("%ls%ls%ls"); an entry ending in '-' is joined to the next without space or dash
	// [BIN:STP-PPC:0x101598C4 object sign call] for objects built from the catalogue the sign text is that name string of the object's info record (no explicit text passed)
	// [BIN:TPW-EXE:0x004629D0 object builder] without explicit text the sign text is the info record's name at +0x410 (0x412F50), filled at 0x414126 by 0x412FB0 from the object text file's NAME or else 0x4146B0 over the table 0x7488D8, which gives an empty name for an id outside the table
	// [BIN:TPW-EXE:0x00467A80 sign generator] a non-null name is split at the space nearest its middle (searched outward, alternating sides); an empty name has no space, so line 1 is the empty name and line 2 empty, and the sign is still generated (0x5ECB40) with both glyph masks cleared by 0x5EAB70
	public static (string Line1, string Line2) Lines( ObjectCatalogEntry entry )
	{
		if ( entry.IsBonus && BonusNames.ReadSignLines( entry ) is { } explicitLines )
			return explicitLines;
		return SignCanvas.SplitAtMiddleSpace( (entry.SignName ?? entry.DisplayName).Trim() );
	}

	/// <summary>Failed reads of one sign (an I/O or access error) before it is given up for the session, as for coaster models.</summary>
	internal const int ReadAttempts = 3;
	private static readonly Dictionary<string, int> failedReads = new( StringComparer.Ordinal );

	/// <summary>
	/// Whether a result stays cached: a sign is, and so is a failure that repeats (unreadable data, a missing key or a bad argument in the sign data), or an archive that is there without a sign.
	/// A read that may succeed later (an I/O or access error) is tried again until it has failed <see cref="ReadAttempts"/> times; an archive with no files yet
	/// (the browser copies a theme's files when a park starts) is always tried again.
	/// </summary>
	internal static bool CanCache( Exception? failure, ObjectSignImages? images, bool archiveWithoutSign, int failedReadCount = 1 ) =>
		images != null || (failure == null ? archiveWithoutSign : failure is not (IOException or UnauthorizedAccessException) || failedReadCount >= ReadAttempts);

	/// <summary>
	/// The CPU-composed sign of an object (cached per archive and text), or null when the object has no <c>.sgn</c>. A sign
	/// with two empty lines is composed too (its board alone). Drawing problems (missing glyphs or fonts) are logged once, not thrown.
	/// </summary>
	public static ObjectSignImages? Get( ObjectCatalogEntry entry )
	{
		// Two empty lines still compose: the original draws the board and the paint and effect layers of empty masks (0x467A80 only skips a null name).
		var (line1, line2) = Lines( entry );
		var key = $"{entry.FileSystem.GetAbsolutePath( entry.ArchivePath )}\0{line1}\0{line2}";
		lock ( cache )
		{
			if ( cache.TryGetValue( key, out var cached ) )
				return cached;
			ObjectSignImages? images = null;
			var archiveWithoutSign = false;
			Exception? failure = null;
			try
			{
				var sign = SignTextRenderer.LoadSignFile( entry.ArchivePath, entry.FileSystem );
				archiveWithoutSign = sign == null && entry.FileSystem.GetFiles( entry.ArchivePath ).Any();
				if ( sign != null )
				{
					var composed = SignTextRenderer.Shared.ComposeSign( sign, new[] { line1, line2 }, default );
					foreach ( var diagnostic in composed.Diagnostics.Where( diagnostic => !diagnostic.StartsWith( "COMPAT-003", StringComparison.Ordinal ) ).Distinct() )
						Log?.Warning( $"Sign of {entry.ArchiveName}: {diagnostic}" );
					images = new ObjectSignImages( line1, line2, composed.Left, composed.Right, composed.TextPixelCount, composed.Diagnostics );
				}
			}
			// KeyNotFoundException: a font or glyph table the sign names but the data lacks; ArgumentException: values the composer refuses. Both repeat, so they are cached like bad data.
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or ArgumentException or KeyNotFoundException or UnauthorizedAccessException )
			{
				failure = exception;
			}
			var failures = 0;
			if ( failure != null )
			{
				failedReads.TryGetValue( key, out failures );
				failedReads[key] = ++failures;
			}
			// A missing result stays uncached unless the archive is there without a sign: a failure that may pass, or an archive whose files have not arrived yet (the browser copies a theme's files when a park starts), must not leave the placeholder for the rest of the session
			var final = CanCache( failure, images, archiveWithoutSign, failures );
			// logged once per sign (the park HUD asks for a sign on every preview draw), and once more when it is given up
			if ( failure != null && (failures == 1 || final) )
				Log?.Warning( $"Sign of {entry.ArchiveName} unavailable: {failure.Message}{(final ? "; it is not drawn" : "; it is tried again")}" );
			if ( final )
			{
				cache[key] = images;
				failedReads.Remove( key );
			}
			return images;
		}
	}
}

/// <summary>
/// Which texture an object's <c>sign1</c>/<c>sign2</c> material slots show: the park name for the gate, the object's own sign for any other object
/// that has one, and for a fixed item whose sign cannot be drawn (the gate above all) a plain brown board instead of the shipped magenta "SIGN1 SIGN2" placeholder.
/// </summary>
// [EXT:fixed-sign-fallback] a fixed item (the park gate above all) that has no runtime sign shows a plain brown board instead of the shipped placeholder; an OpenTPW rendering choice, the original always draws the sign
internal readonly record struct SignSlotPlan( ObjectSignImages? Images, bool UsesFallback )
{
	public static readonly SignSlotPlan None = new( null, false );

	/// <summary>The plain board of a sign slot that has no sign: one texture for every part, park and lobby, never released (<see cref="Texture.SignBoard"/>).</summary>
	public static Texture FallbackBoard => Texture.SignBoard;

	/// <param name="infoId">The object's <see cref="ObjectCatalogEntry.InfoId"/>.</param>
	/// <param name="entryArchivePath">The object's archive path (<see cref="ObjectCatalogEntry.ArchivePath"/>), used only when the level names no gate.</param>
	/// <param name="gateInfoId">The level's gate (<c>global.sam</c> <c>ParkName.GateObjectId</c>), or null when it names none.</param>
	/// <param name="gateImages">The park name's sign of the object's own level, or null when it failed.</param>
	/// <param name="typeSign">The object type's own sign, asked for every object but the gate.</param>
	public static SignSlotPlan For( bool isFixedItem, int infoId, string entryArchivePath, int? gateInfoId, ObjectSignImages? gateImages, Func<ObjectSignImages?> typeSign )
	{
		// [DATA:levels/<level>/global.sam:ParkName.GateObjectId] the level names its gate, whether or not its sign could be drawn; without that line the fixed item named "gates" stands in
		var isGate = gateInfoId is { } gate
			? infoId == gate
			: isFixedItem && string.Equals( Path.GetFileName( entryArchivePath.TrimEnd( '/' ) ), "gates", StringComparison.OrdinalIgnoreCase );
		// the gate never draws its type sign: that would be its default lines, not the park's name
		if ( isGate )
			return gateImages != null ? new( gateImages, false ) : new( null, true );
		// [BIN:TPW-EXE:0x004629D0 object builder] every model the builder makes, fixed items included, reaches the sign generator 0x467A80 once its mesh has loaded (0x461F10 returns 1 at 0x462C18; the 0x400 flag test at 0x462DCC and its fall-through both reach 0x462E94, and 0x468250 fails only without the fonts directory), with the info record's name when no lines are passed (0x412F50 at 0x462ECE): a fixed item with its own .sgn draws its own sign as any catalogue object does
		if ( typeSign() is { } own )
			return new( own, false );
		return isFixedItem ? new( null, true ) : None;
	}

	/// <summary>
	/// The plan of a front-end scene's model (the lobby islands and gates, the globe): its runtime sign once it is composed, else the plain board; a front-end model
	/// never shows the shipped placeholder.
	/// </summary>
	// [EXT:fixed-sign-fallback] the lobby's sign models show the board until (or unless) their sign is composed
	public static SignSlotPlan ForScene( ObjectSignImages? images ) => images != null ? new( images, false ) : new( null, true );

	/// <summary>Where one material slot's texture comes from under this plan.</summary>
	public SignSlotSource SourceOf( string textureName ) =>
		!ObjectSigns.IsSignSlot( textureName ) ? SignSlotSource.Shipped
		: Images != null ? SignSlotSource.RuntimeSign
		: UsesFallback ? SignSlotSource.FallbackBoard : SignSlotSource.Shipped;
}

internal enum SignSlotSource { Shipped, RuntimeSign, FallbackBoard }
