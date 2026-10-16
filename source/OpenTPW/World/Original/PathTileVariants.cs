namespace OpenTPW;

/// <summary>
/// The tile chooser's texture roll (tp.exe <c>0x535DD0</c>): a straight or edge path tile takes its second texture when the byte
/// <c>0x820AC0</c> is set, and every chooser call stores a new <c>rand() &amp; 1</c> there, so a cell's version is the roll of the chooser
/// call before its own last recomputation. <see cref="Refresh"/> is that recomputation (the tile refresh <c>0x5365D0</c> of a path or
/// queue cell). Presentation only: the roll comes from its own generator, never from the world's streams.
/// </summary>
// [BIN:TPW-EXE:0x00535FCC path texture roll] while 0x820AC0 is set a class-1 result (path table 0x763138) of texture 2 becomes 19 and of texture 10 becomes 20; then CRT rand 0x67B5C0 (state × 214013 + 2531011, bits 16..30) & 1 is stored there (0x53602C) by every call that gets that far, i.e. every call for a cell that is not of the types 2, 4, 7, 9, 10, 0x15 or 0x1E; the tile refresh 0x5365D0 calls the chooser for cells of type 1 or more other than 5
// Presentation stub: tp.exe seeds the CRT generator with GetTickCount (0x54DDDC) and shares it with the whole game, so no run's rolls can be reproduced; this generator is the CRT's own formula with the CRT's default seed 1, drawn only by the chooser
public sealed class PathTileVariants
{
	private readonly Dictionary<(int X, int Y), bool> second = new();
	private uint state;
	private bool roll;

	public PathTileVariants( uint seed = 1 ) => state = seed;

	/// <summary>The chooser ran for the path or queue cell (<paramref name="x"/>, <paramref name="y"/>): it keeps the current roll, then rolls again.</summary>
	public void Refresh( int x, int y )
	{
		second[(x, y)] = roll;
		roll = (Rand() & 1) != 0;
		Version++;
	}

	/// <summary>Whether the cell's straight or edge tile is the second texture (19 or 20): the roll its last recomputation took; false before any.</summary>
	public bool IsSecond( int x, int y ) => second.TryGetValue( (x, y), out var value ) && value;

	/// <summary>Raised by every <see cref="Refresh"/>, so the path layer can be redrawn.</summary>
	public int Version { get; private set; }

	/// <summary>The MSVC CRT <c>rand</c>: the state times 214013 plus 2531011, bits 16..30 returned.</summary>
	private int Rand()
	{
		unchecked
		{
			state = state * 214013 + 2531011;
		}
		return (int)((state >> 16) & 0x7FFF);
	}
}
