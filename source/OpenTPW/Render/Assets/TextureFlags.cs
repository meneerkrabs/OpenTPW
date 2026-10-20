namespace OpenTPW;

[Flags]
public enum TextureFlags
{
	None = 0,

	/// <summary>
	/// If this is set, then the pixel value (255, 0, 255) will be treated as a transparent pixel.
	/// </summary>
	PinkChromaKey = 1,

	/// <summary>
	/// Force point filtering for this texture's sampler
	/// </summary>
	PointFilter = 2,

	/// <summary>
	/// Force wrap for this texture's sampler
	/// </summary>
	Wrap = 8,

	Repeat = 4
}
