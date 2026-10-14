namespace OpenTPW;

/// <summary>One <c>RegionFX[i]</c> row of <c>levels/Standard.sam</c>: a square radius and the five coefficients a source spreads around its cell.</summary>
public readonly record struct RegionEffect( int Radius, short Happiness, short Illness, short Hunger, short Security, short Attraction )
{
	/// <summary>The rows the original indexes: entertainer, clean toilet, vomit, guard, camera, stinkbomb, dirty toilet, fireworks.</summary>
	public const int Count = 8;
	public const int Vomit = 2;
	public const int Stinkbomb = 5;

	/// <summary>The table from the balance settings (rows the file lacks spread nothing).</summary>
	// [DATA:levels/Standard.sam:RegionFX[0..7].Radius/Happiness/Illness/Hunger/Security/Attraction] the effect table the writer reads at cell manager +0x1D9104 + 12 × index (five words, the radius byte at +10; tp.exe 0x004D8480, the Mac 0xd73a0)
	public static RegionEffect[] Load( SamSettings settings )
	{
		var table = new RegionEffect[Count];
		for ( var index = 0; index < Count; index++ )
		{
			short Word( string key ) => unchecked( (short)settings.GetInt( $"RegionFX[{index}].{key}", 0, optional: true ) );
			table[index] = new RegionEffect( settings.GetInt( $"RegionFX[{index}].Radius", 0, optional: true ) & 0xFF, Word( "Happiness" ), Word( "Illness" ),
				Word( "Hunger" ), Word( "Security" ), Word( "Attraction" ) );
		}
		return table;
	}
}

/// <summary>
/// The park's environment grid (the cell manager's ten-byte records at <c>+0x1B1104</c>, docs/reverse/PPC-guests.md phase 2): per map cell five signed
/// words (happiness, illness, hunger, security, attraction) that region sources spread around them. A guest's needs phase adds its cell's first three
/// words to its meters (<see cref="NeedsUpdate"/>). Today vomit and stinkbombs are the sources (litter types 7 and 8); the entertainers, toilets, guards,
/// cameras and fireworks follow with their own items (GUEST-006).
/// </summary>
public sealed class ParkEnvironment
{
	public const int Components = 5;
	private readonly short[] words = new short[ParkLitter.MapSize * ParkLitter.MapSize * Components];

	/// <summary>The <c>RegionFX</c> table; all zero until the balance settings set it.</summary>
	public RegionEffect[] Effects { get; set; } = new RegionEffect[RegionEffect.Count];

	public (short Happiness, short Illness, short Hunger, short Security, short Attraction) At( int x, int y )
	{
		if ( (uint)x >= ParkLitter.MapSize || (uint)y >= ParkLitter.MapSize )
			return default;
		var index = (y * ParkLitter.MapSize + x) * Components;
		return (words[index], words[index + 1], words[index + 2], words[index + 3], words[index + 4]);
	}

	/// <summary>
	/// Adds (or removes) effect <paramref name="index"/> around cell (<paramref name="x"/>, <paramref name="y"/>): every cell of the square of the effect's
	/// radius, clipped to the map, gets each coefficient divided by the Manhattan distance + 1 (a signed division that truncates toward zero), with no
	/// saturation, so the words wrap like the original's halfwords and a removal cancels its add exactly.
	/// </summary>
	// [BIN:TPW-EXE:0x004D8480 region writer] the add/remove wrappers 0x004D8440 / 0x004D8460 pass 1 / 0; sign +1 or −1; x0 = max(0, x − r), x1 = min(127, x + r), likewise y; for each cell and each of the five words: word += sign × coefficient / (|dx| + |dy| + 1) through idiv (the Mac 0xd73a0, docs/reverse/PPC-guests.md)
	public void Apply( int index, int x, int y, bool add )
	{
		if ( (uint)index >= (uint)Effects.Length || (uint)x >= ParkLitter.MapSize || (uint)y >= ParkLitter.MapSize )
			return;
		var effect = Effects[index];
		var sign = add ? 1 : -1;
		Span<int> coefficients = [effect.Happiness, effect.Illness, effect.Hunger, effect.Security, effect.Attraction];
		var radius = effect.Radius;
		for ( var cx = Math.Max( 0, x - radius ); cx <= Math.Min( ParkLitter.MapSize - 1, x + radius ); cx++ )
		{
			for ( var cy = Math.Max( 0, y - radius ); cy <= Math.Min( ParkLitter.MapSize - 1, y + radius ); cy++ )
			{
				var divisor = Math.Abs( cx - x ) + Math.Abs( cy - y ) + 1;
				var cell = (cy * ParkLitter.MapSize + cx) * Components;
				for ( var component = 0; component < Components; component++ )
					words[cell + component] = unchecked( (short)(words[cell + component] + sign * coefficients[component] / divisor) );
			}
		}
	}

	/// <summary>Clears every word (a load rebuilds the grid from its sources).</summary>
	public void Clear() => Array.Clear( words );
}
