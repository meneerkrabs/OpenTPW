using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OpenTPW;

/// <summary>A 1x clean-up run on a decoded texture before it is upscaled (e.g. removing compression artifacts).</summary>
public interface IImagePrepass
{
	/// <summary>Model identifier recorded in <c>pack.json</c> (the ONNX file name).</summary>
	string Model { get; }

	/// <summary>
	/// Returns a cleaned copy of an RGBA image of the same size; alpha is kept as is.
	/// <paramref name="wrap"/>: the image tiles (borders see the opposite edge), otherwise its edge pixels are repeated.
	/// </summary>
	byte[] Process( byte[] rgba, int width, int height, bool wrap );
}

/// <summary>A model with a fixed square input that maps an RGB tile to an RGB tile of the same size.</summary>
public interface IImageTileModel
{
	int TileSize { get; }
	/// <summary>Planar NCHW float RGB in 0..1, <c>3 * TileSize * TileSize</c> values, in and out.</summary>
	float[] Run( float[] tile );
}

/// <summary>
/// Applies a fixed-size tile model to an image of any size. A dimension that fits with at least <c>Margin</c> texels of context on both sides is centred in one tile
/// and padded by wrapping (or edge repetition); a larger one is cut into overlapping tiles that advance by
/// <c>TileSize - 2 * Margin</c>, each contributing only its centre. Tiles read across the image border by wrapping, so a
/// tiling texture comes out seamless.
/// </summary>
// [EXT:texture-pack] Optional 1x de-artifact pre-pass for the local texture pack.
public sealed class TiledPrepass : IImagePrepass
{
	private readonly IImageTileModel model;
	private readonly string name;
	private readonly int margin;

	/// <param name="margin">Context texels around each tile's centre; reduced to a quarter of the tile for small tiles so the tiles always advance.</param>
	public TiledPrepass( IImageTileModel model, string name, int margin = 32 )
	{
		if ( model.TileSize < 8 )
			throw new ArgumentException( $"A pre-pass tile of {model.TileSize} texels is too small." );
		this.model = model;
		this.name = name;
		this.margin = Math.Clamp( margin, 0, model.TileSize / 4 );
	}

	public string Model => name;

	/// <summary>For each tile along one axis: where the tile starts in image coordinates (may be negative) and which output range it fills.</summary>
	public static IReadOnlyList<(int Origin, int From, int To)> Plan( int length, int tile, int margin )
	{
		if ( length <= tile - 2 * margin )
			return new[] { (-(tile - length) / 2, 0, length) };
		var stride = tile - 2 * margin;
		if ( stride <= 0 )
			throw new ArgumentException( $"A tile of {tile} texels cannot have a margin of {margin}." );
		var plan = new List<(int, int, int)>();
		for ( var from = 0; from < length; from += stride )
			plan.Add( (from - margin, from, Math.Min( length, from + stride )) );
		return plan;
	}

	public byte[] Process( byte[] rgba, int width, int height, bool wrap )
	{
		var tile = model.TileSize;
		var plane = tile * tile;
		var result = (byte[])rgba.Clone();
		var columns = Plan( width, tile, margin );
		var rows = Plan( height, tile, margin );
		var input = new float[3 * plane];
		foreach ( var (originY, fromY, toY) in rows )
			foreach ( var (originX, fromX, toX) in columns )
			{
				for ( var y = 0; y < tile; y++ )
				{
					var sourceY = Source( originY + y, height, wrap );
					for ( var x = 0; x < tile; x++ )
					{
						var source = (sourceY * width + Source( originX + x, width, wrap )) * 4;
						var index = y * tile + x;
						input[index] = rgba[source] / 255f;
						input[plane + index] = rgba[source + 1] / 255f;
						input[2 * plane + index] = rgba[source + 2] / 255f;
					}
				}
				var output = model.Run( input );
				if ( output.Length != input.Length )
					throw new InvalidOperationException( $"The pre-pass model returned {output.Length} values for a tile of {input.Length}." );
				for ( var y = fromY; y < toY; y++ )
					for ( var x = fromX; x < toX; x++ )
					{
						var index = (y - originY) * tile + (x - originX);
						var target = (y * width + x) * 4;
						result[target] = ToByte( output[index] );
						result[target + 1] = ToByte( output[plane + index] );
						result[target + 2] = ToByte( output[2 * plane + index] );
					}
			}
		return result;
	}

	private static int Source( int position, int length, bool wrap ) =>
		wrap ? ((position % length) + length) % length : Math.Clamp( position, 0, length - 1 );

	private static byte ToByte( float value ) => (byte)Math.Clamp( (int)MathF.Round( value * 255f ), 0, 255 );
}

/// <summary>
/// A player-supplied ONNX de-artifact model (e.g. 1xDeJPG by Helaman/Phhofm, CC-BY-4.0) run through ONNX Runtime:
/// CoreML when the platform has it, CPU otherwise. OpenTPW neither ships nor downloads the model.
/// </summary>
public sealed class OnnxTileModel : IImageTileModel, IDisposable
{
	private readonly InferenceSession session;
	private readonly string inputName;

	public int TileSize { get; }
	/// <summary>The execution provider in use: <c>CoreML</c> or <c>CPU</c>.</summary>
	public string Provider { get; }

	public OnnxTileModel( string path )
	{
		if ( !File.Exists( path ) )
			throw new FileNotFoundException( $"Pre-pass model not found: {path}", path );
		InferenceSession? created = null;
		var provider = "CPU";
		// OPENTPW_PREPASS_PROVIDER=cpu skips CoreML (e.g. when it is slower or misbehaves for a model).
		if ( OperatingSystem.IsMacOS() && !string.Equals( Environment.GetEnvironmentVariable( "OPENTPW_PREPASS_PROVIDER" ), "cpu", StringComparison.OrdinalIgnoreCase ) )
		{
			try
			{
				using var options = new SessionOptions();
				options.AppendExecutionProvider_CoreML();
				created = new InferenceSession( path, options );
				provider = "CoreML";
			}
			catch ( Exception exception ) when ( exception is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException )
			{
				created = null;
			}
		}
		session = created ?? new InferenceSession( path );
		Provider = provider;
		var input = session.InputMetadata.Single();
		inputName = input.Key;
		var dimensions = input.Value.Dimensions;
		if ( dimensions.Length != 4 || dimensions[0] is not (1 or -1) || dimensions[1] != 3 || dimensions[2] != dimensions[3] || dimensions[2] <= 0 )
			throw new InvalidDataException( $"The pre-pass model must take a 1x3xSxS float tensor, not [{string.Join( ", ", dimensions )}]." );
		TileSize = dimensions[2];
	}

	public float[] Run( float[] tile )
	{
		var tensor = new DenseTensor<float>( tile, new[] { 1, 3, TileSize, TileSize } );
		using var results = session.Run( new[] { NamedOnnxValue.CreateFromTensor( inputName, tensor ) } );
		return results.First().AsEnumerable<float>().ToArray();
	}

	public void Dispose() => session.Dispose();
}
