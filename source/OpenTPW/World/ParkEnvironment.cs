namespace OpenTPW;

/// <summary>One <c>RegionFX[i]</c> row of <c>levels/Standard.sam</c>: a square radius and the five coefficients a source spreads around its cell.</summary>
public readonly record struct RegionEffect( int Radius, short Happiness, short Illness, short Hunger, short Security, short Attraction )
{
	/// <summary>The rows the original indexes: entertainer, clean toilet, vomit, guard, camera, stinkbomb, dirty toilet, fireworks.</summary>
	public const int Count = 8;
	public const int Entertainer = 0;
	public const int CleanToilet = 1;
	public const int Vomit = 2;
	public const int Guard = 3;
	public const int Camera = 4;
	public const int Stinkbomb = 5;
	public const int DirtyToilet = 6;
	public const int Fireworks = 7;

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
/// words to its meters (<see cref="NeedsUpdate"/>). The sources are all the original's: vomit and stinkbombs (litter types 7 and 8), the entertainers and
/// guards where they stand, the toilets (clean or dirty), the security cameras and the fireworks (<see cref="ParkEconomy.PlaceObjectEffects"/>).
/// </summary>
public sealed class ParkEnvironment
{
	public const int Components = 5;
	private readonly short[] words = new short[ParkLitter.MapSize * ParkLitter.MapSize * Components];

	/// <summary>The <c>RegionFX</c> table; all zero until the balance settings set it.</summary>
	public RegionEffect[] Effects { get; set; } = new RegionEffect[RegionEffect.Count];

	/// <summary>What an original save's grid holds beyond the sources OpenTPW spread again (<see cref="ImportSaved"/>); null for a park that was not loaded from an original save or whose grid is its sources'.</summary>
	private short[]? residue;

	public (short Happiness, short Illness, short Hunger, short Security, short Attraction) At( int x, int y )
	{
		if ( (uint)x >= ParkLitter.MapSize || (uint)y >= ParkLitter.MapSize )
			return default;
		var index = (y * ParkLitter.MapSize + x) * Components;
		if ( residue is { } extra )
			return (unchecked( (short)(words[index] + extra[index]) ), unchecked( (short)(words[index + 1] + extra[index + 1]) ), unchecked( (short)(words[index + 2] + extra[index + 2]) ),
				unchecked( (short)(words[index + 3] + extra[index + 3]) ), unchecked( (short)(words[index + 4] + extra[index + 4]) ));
		return (words[index], words[index + 1], words[index + 2], words[index + 3], words[index + 4]);
	}

	/// <summary>
	/// Takes the grid an original save holds: <paramref name="saved"/> gives each cell's five words (null for a cell the save keeps no record for, the default zeros). The sources have been spread again
	/// by then (litter, entertainers, guards, toilets, cameras and fireworks are put back where the save has them), so what the saved grid holds beyond them is kept apart as a residue that reads
	/// with the words and is never touched by a source's later add or remove; returns the number of cells with a residue.
	/// </summary>
	// [BIN:TPW-EXE:0x4D7EA0 cell manager serializer] the original saves and loads its region grid as it is: the loader constructs no object (0x4DB090 is not run for a loaded thing, so no row is added again) and keeps the ten-byte records of the cells whose status byte has bit 4, so the grid after a load is the grid the save held, whichever sources made it
	public int ImportSaved( Func<int, int, short[]?> saved )
	{
		ArgumentNullException.ThrowIfNull( saved );
		var extra = new short[words.Length];
		var cells = 0;
		for ( var y = 0; y < ParkLitter.MapSize; y++ )
			for ( var x = 0; x < ParkLitter.MapSize; x++ )
			{
				var record = saved( x, y );
				var index = (y * ParkLitter.MapSize + x) * Components;
				var any = false;
				for ( var component = 0; component < Components; component++ )
				{
					var delta = unchecked( (short)((record != null && component < record.Length ? record[component] : 0) - words[index + component]) );
					extra[index + component] = delta;
					any |= delta != 0;
				}
				if ( any )
					cells++;
			}
		residue = cells == 0 ? null : extra;
		return cells;
	}

	/// <summary>The cells that hold a residue, row by row: x, y and the five words (saves).</summary>
	public IEnumerable<int[]> ResidueCells()
	{
		if ( residue is not { } extra )
			yield break;
		for ( var cell = 0; cell < ParkLitter.MapSize * ParkLitter.MapSize; cell++ )
		{
			var index = cell * Components;
			if ( extra.AsSpan( index, Components ).ContainsAnyExcept( (short)0 ) )
				yield return [cell % ParkLitter.MapSize, cell / ParkLitter.MapSize, extra[index], extra[index + 1], extra[index + 2], extra[index + 3], extra[index + 4]];
		}
	}

	/// <summary>Puts a residue back (<see cref="ResidueCells"/>); every entry must be a cell of the map with five words.</summary>
	public void RestoreResidue( IEnumerable<int[]> cells )
	{
		ArgumentNullException.ThrowIfNull( cells );
		var extra = new short[words.Length];
		var any = false;
		foreach ( var cell in cells )
		{
			if ( cell is not { Length: 2 + Components } || (uint)cell[0] >= ParkLitter.MapSize || (uint)cell[1] >= ParkLitter.MapSize )
				throw new InvalidDataException( "An environment residue entry is not a map cell with five words." );
			var index = (cell[1] * ParkLitter.MapSize + cell[0]) * Components;
			for ( var component = 0; component < Components; component++ )
			{
				if ( cell[2 + component] is < short.MinValue or > short.MaxValue )
					throw new InvalidDataException( "An environment residue word does not fit 16 bits." );
				extra[index + component] = (short)cell[2 + component];
				any |= extra[index + component] != 0;
			}
		}
		residue = any ? extra : null;
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
	public void Clear()
	{
		Array.Clear( words );
		residue = null;
	}
}
