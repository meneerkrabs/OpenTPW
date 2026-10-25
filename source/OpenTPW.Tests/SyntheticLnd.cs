using System;
using System.IO;

namespace OpenTPW.Tests;

/// <summary>Builds small synthetic <c>base.lnd</c> files for the format and texturer tests (no original bytes).</summary>
internal static class SyntheticLnd
{
	/// <param name="cellsX">Cells across; the weight grid has 4 * cellsX + 1 samples per row.</param>
	/// <param name="cellsY">Cells down.</param>
	/// <param name="parameters">The ten parameter bytes.</param>
	/// <param name="grid">Grid byte for (sample x, sample y, channel 0 valid, 1..3 weights).</param>
	/// <param name="imageSize">Optional (width, height) of an image for (texture size, image index), called once per image before its pixels.</param>
	/// <param name="pixel">Image byte for (texture size, image index, x, y, channel); colour images (0..5) have channels 0..3, byte maps channel 0.</param>
	public static byte[] Build( int cellsX, int cellsY, byte[] parameters, Func<int, int, int, byte> grid, Func<int, int, int, int, int, byte> pixel,
		int imageExtraWidth = 8, int imageExtraHeight = 6, int trailingBytes = 0, Func<int, int, (int Width, int Height)>? imageSize = null )
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter( stream );
		writer.Write( (byte)3 );
		writer.Write( parameters );
		var width = 4 * cellsX + 1;
		var height = 4 * cellsY + 1;
		writer.Write( width );
		writer.Write( height );
		writer.Write( 4 );
		for ( var y = 0; y < height; y++ )
			for ( var x = 0; x < width; x++ )
				for ( var channel = 0; channel < 4; channel++ )
					writer.Write( grid( x, y, channel ) );
		for ( var index = 0; index < 3; index++ )
		{
			var size = 64 >> index;
			writer.Write( size );
			writer.Write( (byte)index );
			for ( var image = 0; image < 13; image++ )
			{
				var bytes = image < 6 ? 4 : 1;
				var (imageWidth, imageHeight) = imageSize?.Invoke( size, image ) ?? (size + imageExtraWidth, size + imageExtraHeight);
				writer.Write( imageWidth );
				writer.Write( imageHeight );
				writer.Write( bytes );
				for ( var y = 0; y < imageHeight; y++ )
					for ( var x = 0; x < imageWidth; x++ )
						for ( var channel = 0; channel < bytes; channel++ )
							writer.Write( pixel( size, image, x, y, channel ) );
			}
		}
		for ( var index = 0; index < trailingBytes; index++ )
			writer.Write( (byte)0 );
		writer.Flush();
		return stream.ToArray();
	}
}
