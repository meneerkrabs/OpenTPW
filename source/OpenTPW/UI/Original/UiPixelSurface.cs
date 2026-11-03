namespace OpenTPW.UI.Original;

/// <summary>
/// A CPU pixel surface its owner writes (straight RGBA, row 0 on top) and the renderer shows as a linear filtered texture: the map window's surface, which the original composes on the CPU and
/// uploads. The owner calls <see cref="Touch"/> after writing; the renderer uploads the pixels again only when the version changed, and drops its GPU copy after the surface goes unused for a while.
/// </summary>
public sealed class UiPixelSurface
{
	public UiPixelSurface( int width, int height )
	{
		Width = width;
		Height = height;
		Rgba = new byte[width * height * 4];
		Texture = new UiTexture( null, null, Surface: this );
	}

	public int Width { get; }
	public int Height { get; }
	public byte[] Rgba { get; }
	/// <summary>Changes whenever the pixels do.</summary>
	public int Version { get; private set; }
	/// <summary>The UI texture that draws this surface, made once so a frame allocates none.</summary>
	public UiTexture Texture { get; }

	public void Touch() => Version++;
}
