namespace OpenTPW;

/// <summary>
/// CPU path tiles used by the procedural compositor. Like the independent GPU copies, these must be decoded again after a completed texture-pack switch.
/// A generation identifies their pixels even when a replacement has the same dimensions, texture key and cell word.
/// Unreadable/malformed replacements retain the previous art, like the GPU switch; a decoder's null result explicitly removes it. Only an out-of-memory failure propagates.
/// </summary>
internal sealed class PathCellArtCache : IDisposable
{
	private readonly Func<int, (byte[] Rgba, int Width, int Height)?> decode;
	private readonly Action changed;
	private sealed record Tile( (byte[] Rgba, int Width, int Height)? Art, ulong AttemptedGeneration, ulong ArtGeneration );
	private readonly Dictionary<int, Tile> tiles = new();
	private ulong generation;
	private bool disposed;

	public PathCellArtCache( Func<int, (byte[] Rgba, int Width, int Height)?> decode, Action changed )
	{
		this.decode = decode;
		this.changed = changed;
		TexturePackSwitch.PackChanged += PackChanged;
	}

	public ProceduralCellArt? Get( int texture, uint word )
	{
		tiles.TryGetValue( texture, out var cached );
		if ( cached == null || cached.AttemptedGeneration != generation )
		{
			var art = cached?.Art;
			var artGeneration = cached?.ArtGeneration ?? generation;
			try
			{
				art = decode( texture );
				artGeneration = generation;
			}
			catch ( Exception exception ) when ( exception is not OutOfMemoryException )
			{
				// Any decoder failure (malformed image, IO, access or security) degrades this tile only, as TexturePackSwitch.Pump does; it must not break the level load,
				// path edits or later PackChanged subscribers. Only OutOfMemoryException stays observable. A failed tile is attempted once in this generation.
				Log?.Warning( $"Path texture {texture} could not be decoded ({exception.GetType().Name}: {exception.Message}); {(art != null ? "keeping its previous pixels" : "using procedural ground")}." );
			}
			tiles[texture] = cached = new Tile( art, generation, artGeneration );
		}
		return cached.Art is { } tile ? new ProceduralCellArt( texture, tile.Rgba, tile.Width, tile.Height, word, cached.ArtGeneration ) : null;
	}

	private void PackChanged()
	{
		if ( disposed )
			return;
		// The switch raises this only on completion, on the render thread. Cancelled/replaced switches cannot publish stale art.
		generation++;
		// One entry per texture, not per generation: preserve the prior pixels only as a fallback for a failed decode.
		// Synchronize all path cells, not just the visible list: offscreen cache hits must also be dirty on their return.
		changed();
	}

	public void Dispose()
	{
		disposed = true;
		TexturePackSwitch.PackChanged -= PackChanged;
		tiles.Clear();
	}
}
