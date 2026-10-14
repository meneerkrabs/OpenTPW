namespace OpenTPW;

/// <summary>
/// Balloons and costumes (tp.exe <c>0x004FE1E0</c>, the use routine's appearance step): a balloon shop gives the guest a balloon with a life from the shop's
/// quality of goods, a costume shop puts a costume on or takes it back.
/// </summary>
public static class GuestAppearance
{
	/// <summary><c>UsageInfo.AppearanceEffect</c> of a balloon shop.</summary>
	public const int Balloon = 1;
	/// <summary><c>UsageInfo.AppearanceEffect</c> of a costume shop.</summary>
	public const int Costume = 2;

	/// <summary>The shortest and the longest balloon life (tp.exe data <c>0x0075D0F0</c>, <c>0x0075D0F4</c>; nothing writes them).</summary>
	public const int MinimumBalloonLife = 25, MaximumBalloonLife = 255;

	/// <summary>The quality of goods of a shop (ride <c>+0x18C</c>, <c>mQualityOfGoods</c>) as the build sets it (tp.exe <c>0x004DB389</c>).</summary>
	public const int BuiltQualityOfGoods = 50;

	/// <summary>The life of a balloon from a shop of <paramref name="quality"/>: quality × 255 / 100 (unsigned), within 25..255. A shop at the built 50 gives 127.</summary>
	// [BIN:TPW-EXE:0x004FE737 balloon life] the shop's quality byte (0x004E2180) times the word at 0x0075D0F4 (255), divided by 100 as unsigned, then clamped to the words at 0x0075D0F0 (25) and 0x0075D0F4
	public static int BalloonLife( int quality )
	{
		var life = (int)((uint)((byte)quality * MaximumBalloonLife) / 100u);
		return Math.Clamp( life, MinimumBalloonLife, MaximumBalloonLife );
	}
}
