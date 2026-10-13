using OpenTPW.Online.Packages;

namespace OpenTPW;

/// <summary>Exports one explicitly selected local FSH image using the existing CPU decoder and PNG codec.</summary>
// [EXT:fsh-export] application-owned conversion of current decoded pixels, not original rendering parity
public static class FshPngExport
{
	public static void Run(string inputPath, int imageIndex, string outputPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
		ArgumentOutOfRangeException.ThrowIfNegative(imageIndex);
		if (!Path.GetExtension(inputPath).Equals(".fsh", StringComparison.OrdinalIgnoreCase) ||
			!Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("FSH export requires a local .fsh input and .png output.");
		var destination = Path.GetFullPath(outputPath);
		if (File.Exists(destination)) throw new IOException("FSH export will not overwrite an existing output file.");
		FshFile fsh;
		using (var input = File.OpenRead(inputPath)) fsh = new FshFile(input);
		if (imageIndex >= fsh.Images.Count) throw new ArgumentOutOfRangeException(nameof(imageIndex), "The selected FSH image does not exist.");
		var image = fsh.Images[imageIndex];
		var png = PngImage.EncodeRgba(image.Width, image.Height, image.Rgba);
		// Decode/selection/encoding finish before creating any output. Move without overwrite also protects a racing file.
		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
		var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
		var ownsTemporary = false;
		try
		{
			using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				ownsTemporary = true;
				stream.Write(png);
				stream.Flush(true);
			}
			File.Move(temporary, destination, overwrite: false);
		}
		finally { if (ownsTemporary) File.Delete(temporary); }
		Console.WriteLine($"Exported FSH image {imageIndex} '{image.Tag}': {image.Width}x{image.Height} decoded RGBA to {destination}.");
	}
}
