using OpenTPW.Online.Api;

namespace OpenTPW.Server;

/// <summary>
/// The server's Game News and System News (the original online hub's two news columns, docs/ONLINE.md):
/// plain-text files <c>news/game.txt</c> and <c>news/system.txt</c> in the data folder, written by the
/// server operator. Read again only when a file changes, so frequent requests stay cheap.
/// </summary>
// [EXT:ONLINE-056] the original fetched news from EA's news server; an OpenTPW server serves the operator's text files
public sealed class NewsFeed
{
	/// <summary>Longest news text served per column; longer files are cut.</summary>
	public const int MaximumCharacters = 4000;

	private readonly string directory;
	private readonly object gate = new();
	private (DateTime Game, DateTime System) stamps;
	private NewsInfo current = new( "", "", null );

	public NewsFeed( string dataDirectory ) => directory = Path.Combine( dataDirectory, "news" );

	public string Directory => directory;

	public NewsInfo Read()
	{
		var gamePath = Path.Combine( directory, "game.txt" );
		var systemPath = Path.Combine( directory, "system.txt" );
		var next = (Stamp( gamePath ), Stamp( systemPath ));
		lock ( gate )
		{
			if ( next != stamps )
			{
				var updated = new[] { next.Item1, next.Item2 }.Where( stamp => stamp != DateTime.MinValue ).DefaultIfEmpty( DateTime.MinValue ).Max();
				current = new NewsInfo( Text( gamePath ), Text( systemPath ), updated == DateTime.MinValue ? null : new DateTimeOffset( updated, TimeSpan.Zero ) );
				stamps = next;
			}
			return current;
		}
	}

	private static DateTime Stamp( string path ) => File.Exists( path ) ? File.GetLastWriteTimeUtc( path ) : DateTime.MinValue;

	private static string Text( string path )
	{
		try
		{
			if ( !File.Exists( path ) )
				return "";
			var text = File.ReadAllText( path ).Replace( "\r\n", "\n" ).Trim();
			return text.Length <= MaximumCharacters ? text : text[..MaximumCharacters];
		}
		catch ( IOException )
		{
			return "";
		}
	}
}
