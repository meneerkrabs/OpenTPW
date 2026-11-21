using System.Globalization;

namespace OpenTPW;

internal readonly record struct ThemeParkIncGuestType( int PreferredExcitement, int StartingCash, int BoredomThreshold, int Generation, float SpeedMultiplier = 1f );

/// <summary>The shipped offline profile, independent of the Theme Park World guest settings.</summary>
internal sealed record ThemeParkIncGuestStartSettings( IReadOnlyList<ThemeParkIncGuestType> Types, int ExitLevel, int ExitVariation, int CashVariation,
	int Kids, int Parents, int Grandparents, int PrankLikelihood )
{
	internal const int TypeCount = 24;
	internal const string TypeFields = "PreferredExcitement.StartingCash.BoredomThreshold.Generation";

	// [BIN:TPI-EXE:0x419CD0 guest settings loader] The isolated SAM loader verifies the scalar bindings and the four profile words
	// at 0xA01C24 plus 20 bytes per type. The speed multiplier follows at +16 (the constructor reads 0xA01C34).
	internal static ThemeParkIncGuestStartSettings Parse( IReadOnlyDictionary<string, string[]> values )
	{
		string[] Tokens( string key, int length ) => values.TryGetValue( key, out var tokens ) && tokens.Length == length
			? tokens : throw new InvalidDataException( $"Theme Park Inc guest settings require {length} value(s) for '{key}'." );
		static int Integer( string token, string key ) => int.TryParse( token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value )
			? value : throw new InvalidDataException( $"Theme Park Inc guest setting '{key}' is not an integer." );
		int Peep( string name )
		{
			var key = $"PeepInfo.{name}";
			return Integer( Tokens( key, 1 )[0], key );
		}

		var types = new ThemeParkIncGuestType[TypeCount];
		for ( var index = 0; index < types.Length; index++ )
		{
			var key = $"PeepTypes[{index}].{TypeFields}";
			var row = Tokens( key, 4 );
			var speedKey = $"PeepTypes[{index}].SpeedMultiplier";
			if ( !float.TryParse( Tokens( speedKey, 1 )[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed ) || !float.IsFinite( speed ) || speed <= 0 )
				throw new InvalidDataException( $"Theme Park Inc guest setting '{speedKey}' must be positive and finite." );
			var generation = Integer( row[3], key );
			if ( generation != index / (TypeCount / 3) )
				throw new InvalidDataException( $"Theme Park Inc guest type {index} is outside its generation's block." );
			types[index] = new( Integer( row[0], key ), Integer( row[1], key ), Integer( row[2], key ), generation, speed );
		}
		var exitVariation = Peep( "ExitLevelVar" );
		var cashVariation = Peep( "StartingCashVarPc" );
		var kids = Peep( "PercentageOfKids" );
		var parents = Peep( "PercentageOfParents" );
		var grandparents = Peep( "PercentageOfGrandparents" );
		if ( exitVariation is <= 0 or > int.MaxValue / 2 || cashVariation is < 0 or > 100
			|| kids is < 0 or > 100 || parents is < 0 or > 100 || grandparents is < 0 or > 100 || kids + parents + grandparents != 100 )
			throw new InvalidDataException( "Invalid Theme Park Inc guest variation or generation percentages." );
		return new( types, Peep( "ExitLevel" ), exitVariation, cashVariation, kids, parents, grandparents, Peep( "PrankeryLikelihood" ) );
	}

	internal ThemeParkIncGuestProfile ReadProfile( WorldRng world ) => ThemeParkIncGuestStart.Read( Types, ExitLevel, ExitVariation, CashVariation,
		Kids, Parents, Grandparents, world.NextWord );
}
internal readonly record struct ThemeParkIncGuestProfile( int Type, int ExitLevel, int Money, int Generation, int PreferredExcitement, int BoredomThreshold );
internal readonly record struct ThemeParkIncStartingMeters( int Happiness, int Thirst, int Hunger, int Toilet, int Prank );

/// <summary>Profile and meter fragments of the Theme Park Inc guest constructor.</summary>
internal static class ThemeParkIncGuestStart
{
	// [BIN:TPI-EXE:0x60C58F guest profile] The fragment reads four successive world draws: exit variance, variant within a third of the table,
	// generation roll (even when the generation is forced), and starting cash variance. The forced generation comes from 0xA029C4/0xA029C8.
	internal static ThemeParkIncGuestProfile Read( IReadOnlyList<ThemeParkIncGuestType> types, int exitLevel, int exitVariation, int cashVariation,
		int kids, int parents, int grandparents, Func<uint> draw, int? forcedGeneration = null )
	{
		var exit = unchecked(exitLevel + (int)draw() % (2 * exitVariation) - exitVariation);
		var groupSize = types.Count / 3;
		var variant = (int)draw() % groupSize;
		var roll = draw() % 100;
		var group = forcedGeneration ?? (roll < unchecked((uint)kids) ? 0
			: roll < unchecked((uint)(kids + parents)) ? 1
			: roll < unchecked((uint)(kids + parents + grandparents)) ? 2 : variant);
		// The original multiplies and adds bytes before the table lookup. A valid settings table must contain that row.
		var index = unchecked((byte)(groupSize * group + variant));
		var type = types[index];
		var delta = unchecked((int)draw() % (2 * cashVariation + 1) - cashVariation);
		var money = Math.Max( 0, unchecked(type.StartingCash * (100 + delta)) / 100 );
		return new( index, exit, money, type.Generation, type.PreferredExcitement, type.BoredomThreshold );
	}

	// [BIN:TPI-EXE:0x60C734 guest meters] Three needs draws, one discarded draw, then the prank chance and generation-specific table.
	internal static ThemeParkIncStartingMeters ReadMeters( int generation, int prankLikelihood, ushort thingId, Func<uint> draw )
	{
		var thirst = (int)(draw() % 50);
		var hunger = (int)(draw() % 50);
		var toilet = (int)(draw() % 30);
		draw();
		var prank = draw() % 100 < unchecked((uint)prankLikelihood) ? Pranks[generation * 10 + thingId % 10] : 0;
		return new( 50, thirst, hunger, toilet, prank );
	}

	// [BIN:TPI-EXE:0x60C88A guest reseed] The constructor reseeds even when the sprite set is forced.
	// [BIN:TPI-EXE:0x6902F0 guest sprite] The ordinary draw selects within the generation's loaded sprite banks.
	internal static (int Sprite, int AppearanceIndex) SelectAppearance( ushort thingId, int generation, int bankCount, WorldRng world, int? forcedSprite = null )
	{
		world.Reseed( thingId );
		var sprite = forcedSprite ?? (int)((world.NextWord() >> 2) % (uint)bankCount);
		return (sprite, unchecked(sprite + 4 * generation));
	}

	// [BIN:TPI-EXE:0x60C712 guest base speed] Multiply the base word by the profile's binary32
	// coefficient in x87 precision, truncate toward zero, then store the low word. With a retail
	// base word, binary64 represents the full product at either 53-bit or extended precision.
	internal static ushort ScaleBaseSpeed( ushort speed, float multiplier, bool singlePrecision ) =>
		unchecked( (ushort)(int)(singlePrecision ? speed * multiplier : speed * (double)multiplier) );

	internal static bool ValidPrankCode( int code ) => code == 0 || Pranks.Contains( code );

	// [BIN:TPI-EXE:0x60C81B guest prank table] Three rows of ten signed words at 0x9D0624.
	private static ReadOnlySpan<int> Pranks => [1, 1, 2, 2, 3, 3, 4, 4, 6, 6, 8, 8, 8, 8, 7, 7, 7, 7, 6, 6, 8, 8, 8, 8, 7, 7, 7, 7, 6, 6];
}
