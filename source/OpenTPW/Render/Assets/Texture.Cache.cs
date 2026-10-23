namespace OpenTPW;

partial class Texture
{
	private static bool TryGetCachedTexture( string path, out Texture? texture )
	{
		texture = null;

		if ( string.IsNullOrEmpty( path ) )
			return false;

		using var profile = LoadProfile.Measure( "texture-lookup" );
		var existingTexture = Asset.All.OfType<Texture>().ToList().FirstOrDefault( t => t.Path == path );
		if ( existingTexture != null )
		{
			texture = existingTexture;
			return true;
		}

		return false;
	}
}
