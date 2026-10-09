using OpenTPW.Online;
using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>
/// [EXT:ONLINE-060] The player's online folder: exported parks, downloaded (visited) parks and the
/// postcard inbox/outbox/sent folders, plus the opt-in server settings. Default
/// <c>&lt;ApplicationData&gt;/OpenTPW/online</c>; <c>OPENTPW_ONLINE_DIR</c> or <c>--online-dir</c> override it.
/// It is never inside the original game folder.
/// </summary>
public sealed class OnlineFolders
{
	public const string EnvironmentVariable = "OPENTPW_ONLINE_DIR";

	public OnlineFolders( string root )
	{
		Root = Path.GetFullPath( root );
	}

	public static OnlineFolders FromEnvironment( string? overridePath = null )
	{
		var root = overridePath ?? Environment.GetEnvironmentVariable( EnvironmentVariable );
		if ( string.IsNullOrWhiteSpace( root ) )
			root = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create ), "OpenTPW", "online" );
		return new OnlineFolders( root );
	}

	public string Root { get; }
	public string Parks => Ensure( "parks" );
	public string Visited => Ensure( "visited" );
	public string Inbox => Ensure( Path.Combine( "postcards", "inbox" ) );
	public string Outbox => Ensure( Path.Combine( "postcards", "outbox" ) );
	public string Sent => Ensure( Path.Combine( "postcards", "sent" ) );
	public string SettingsFile => Path.Combine( Root, "online.json" );

	private string Ensure( string relative )
	{
		var path = Path.Combine( Root, relative );
		Directory.CreateDirectory( path );
		return path;
	}

	/// <summary>Stores a postcard under its id; returns the path. Existing cards with the same id are kept.</summary>
	public string Store( string folder, Postcard card )
	{
		var path = Path.Combine( folder, card.Id.ToString( "N" ) + Postcard.FileExtension );
		if ( File.Exists( path ) )
		{
			var existing = Postcard.Load( path );
			if ( !existing.ToBytes().SequenceEqual( card.ToBytes() ) )
				throw new InvalidDataException( "A different postcard already exists under this id; the incoming card was not stored." );
		}
		else
			card.Save( path );
		return path;
	}

	/// <summary>Valid cards in a folder, newest first; unreadable files are reported, not thrown.</summary>
	public IReadOnlyList<(string Path, Postcard? Card, string? Error)> List( string folder ) => Directory
		.EnumerateFiles( folder, "*" + Postcard.FileExtension )
		.Select( file =>
		{
			try { return (file, (Postcard?)Postcard.Load( file ), (string?)null); }
			catch ( Exception exception ) when ( exception is InvalidDataException or IOException ) { return (file, null, exception.Message); }
		} )
		.OrderByDescending( item => item.Item2?.Manifest.CreatedUtc ?? DateTimeOffset.MinValue )
		.ToList();

	public OnlineSettings LoadSettings()
	{
		if ( !File.Exists( SettingsFile ) )
			return new OnlineSettings( null, null );
		using var stream = File.OpenRead( SettingsFile );
		return StrictJson.Deserialize<OnlineSettings>( BoundedZip.ReadBounded( stream, 16 * 1024, "Online settings" ), "Online settings" );
	}

	public void SaveSettings( OnlineSettings settings ) => AtomicFile.Write( SettingsFile, StrictJson.Serialize( settings ) );
}

/// <summary>Opt-in online settings. No server is configured by default; passwords are never stored.</summary>
public sealed record OnlineSettings( string? ServerUrl, string? PlayerName );
