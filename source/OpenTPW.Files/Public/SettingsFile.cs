using System.Text;

namespace OpenTPW;

public record struct SettingsPair( string Key, string Value );

public sealed class SettingsFile : BaseFormat
{
	public List<SettingsPair> Entries { get; set; } = new();

	public SettingsFile( string path )
	{
		ReadFromFile( path );
	}

	public SettingsFile( Stream stream )
	{
		ReadFromStream( stream );
	}

	public string this[string val] => Entries.FirstOrDefault( x => x.Key.Equals( val, StringComparison.OrdinalIgnoreCase ) ).Value ?? null!;

	protected override void ReadFromStream( Stream stream )
	{
		// Read to the end instead of trusting Length, so non-seekable and partly read streams work;
		// the stream is still closed afterwards, as before.
		using var buffer = new MemoryStream();
		using ( stream )
			stream.CopyTo( buffer );
		var text = Encoding.ASCII.GetString( buffer.GetBuffer(), 0, (int)buffer.Length );

		var parser = new SAMParser( text );
		Entries = parser.Parse();
	}
}
