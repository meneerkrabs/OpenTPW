using StbImageSharp;

namespace OpenTPW.UI.Original;

/// <summary>
/// Local art that replaces an original UI element, e.g. a sharper Theme Park World logo: PNG files named
/// <c>&lt;config&gt;/art/&lt;name&gt;.png</c> (docs/UI.md). The player supplies them; OpenTPW ships none, and
/// without a file the original art is drawn. Looked up once per name.
/// </summary>
// [EXT:art-override] optional player-supplied replacement art; the original only has its own textures
public static class UiArtOverrides
{
	public sealed record Art( string Path, int Width, int Height );

	private static readonly Dictionary<string, Art?> found = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The override directory, <c>art</c> next to the display settings.</summary>
	public static string Directory => System.IO.Path.Combine( System.IO.Path.GetDirectoryName( DisplaySettings.GetDefaultPath() )!, "art" );

	/// <summary>The override for <paramref name="name"/>, or null when there is none or it is not a readable image.</summary>
	public static Art? Find( string name )
	{
		if ( found.TryGetValue( name, out var art ) )
			return art;
		var path = System.IO.Path.Combine( Directory, name + ".png" );
		art = null;
		if ( File.Exists( path ) )
		{
			try
			{
				using var stream = File.OpenRead( path );
				var info = ImageInfo.FromStream( stream );
				if ( info is { Width: > 0, Height: > 0 } size )
					art = new Art( path, size.Width, size.Height );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidOperationException )
			{
				Log?.Warning( $"Art override {path} could not be read: {exception.Message}" );
			}
			if ( art != null )
				Log?.Trace( $"Art override for {name}: {path} ({art.Width}x{art.Height})." );
		}
		found[name] = art;
		return art;
	}
}
