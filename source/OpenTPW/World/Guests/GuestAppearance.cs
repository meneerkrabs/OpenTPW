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

	/// <summary>
	/// The kid sprite set a guest is drawn with (the original's sprite bank <c>+0x20</c>): the world generator reseeded with the guest's thing id, its first draw shifted right by 2 and taken modulo
	/// the number of loaded kid sets. A pure function of the id, so a guest who returns a costume (reseed, same draw) gets its own set back.
	/// </summary>
	// [BIN:TPW-EXE:0x004FB19E guest sprite variant] the guest constructor reseeds with the thing id (0x00516370) and draws 0x00541F70(0): (draw >> 2) mod the category's loaded bank count (the table 0x00763F88 entry +8); returning a costume does the same (0x004FE6A1), so the value repeats
	public static int KidSet( int guestId, int sets )
	{
		if ( sets <= 0 )
			return 0;
		return (int)(RawKidDraw( guestId ) % (uint)sets);
	}

	/// <summary>
	/// The draw a guest's kid set comes from, before the modulo: the world generator reseeded with the guest's thing id (zero-extended u16), its first draw shifted right by 2 (a 30-bit value).
	/// The reseed makes it a function of the id alone, which is also the draw the constructor takes at the guest's creation (<see cref="Guest.KidSetDraw"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x00541F70 kid set draw] the function returns (word >> 2) modulo the category's loaded count (div esi; mov eax, edx: the entry +8 that 0x00541E7D fills with 0x00541310's result); the guest stores that remainder (0x004FB1B5) and a load reduces it again (0x004F93AA), so the value kept is (raw mod count at creation) and the bank drawn is that mod the count now
	public static uint RawKidDraw( int guestId ) => new WorldRng( (ushort)guestId ).NextWord() >> 2;

	/// <summary>The kid set a guest is drawn with among <paramref name="sets"/> loaded sets: its kept value (<see cref="Guest.KidSetDraw"/>, already reduced by the count of its creation when that was known) modulo the count loaded now, as the person serializer reduces a loaded value (0x004F93AA).</summary>
	// [BIN:TPW-EXE:0x004F93AA saved sprite reduction] the person serializer reduces a loaded sprite id by the category's loaded count (0x00541F60: the entry +8), for every person, so a guest's bank is its kept value modulo the count loaded now
	public static int KidSet( Guest guest, int sets ) => sets <= 0 ? 0 : (int)(guest.KidSetDraw % (uint)sets);

	/// <summary>
	/// The colour (the animation slot) of a guest's balloon: the world generator reseeded with the guest's thing id, a first draw for the sprite set (one set, so always the first) and a second,
	/// shifted right by 2, modulo the set's animation slots (0x00541FD0: the word at <c>+0x21E</c>, the count of slots with frames). The same whenever the balloon appears.
	/// </summary>
	// [BIN:TPW-EXE:0x004FE6FC balloon colour] the shop's gift reseeds (0x00516370) and calls 0x00541F70(10) then 0x00541FD0(10, set); the give-back after a ride does the same (0x00502053..0x00502079); the colour is the slot of the sprite (0x004758F0 +0xB4)
	public static int BalloonColour( int guestId, int colours )
	{
		if ( colours <= 0 )
			return 0;
		var generator = new WorldRng( (ushort)guestId );
		generator.NextWord();
		return (int)((generator.NextWord() >> 2) % (uint)colours);
	}
}
