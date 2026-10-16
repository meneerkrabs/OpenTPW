using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>An animation member of an object archive: <c>&lt;model&gt;&lt;letter&gt;[number]</c>.MD2.</summary>
public sealed record ObjectAnimationFile( string Path, string BaseModel, char Letter, int? AnimationIndex, int Variant, bool Numbered )
{
	public string Name => System.IO.Path.GetFileNameWithoutExtension( Path );
}

/// <summary>
/// Maps the RSE animation numbers (<c>TRIGANIM a v</c>, <c>WAITANIM a v</c>, ...) to animation members.
/// The first operand is the AnimID indexing the original's letter table C D I L S M E U W B R O (0..11;
/// <see cref="ScriptDefs.Animations"/> names ten of them). The second operand selects the numbered variant: variant v plays
/// <c>&lt;model&gt;&lt;letter&gt;{v+1}</c>; the unnumbered <c>&lt;model&gt;&lt;letter&gt;</c> is only loaded when
/// <c>&lt;letter&gt;1</c> is missing, and numbered members load only up to the first gap (RIDES-008 retired against
/// the Windows loader 0x461f10).
/// </summary>
public static class ObjectAnimations
{
	private static readonly Regex Suffix = new( @"^(?<letter>[cdilsmeuwbro])(?<number>\d*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	// [BIN:TPW-EXE:0x6fe6bc animation letter table] The 12-entry table is indexed by AnimID: C D I L S M E U W B R O (AnimIDs 1 and 8 name 'd' and 'w', which have no ScriptDefs constant). The loader 0x461f10 formats %s%s%c%d.md2 (0x74d2b4) for <letter>1, <letter>2, … and stops at the first missing file, and %s%s%c.md2 (0x74d2a8) only when <letter>1 is missing.
	private const string Letters = "cdilsmeuwbro";

	public static char? GetLetter( int animation ) => animation >= 0 && animation < Letters.Length ? Letters[animation] : null;

	public static int? GetAnimation( char letter )
	{
		var index = Letters.IndexOf( char.ToLowerInvariant( letter ) );
		return index < 0 ? null : index;
	}

	/// <summary>
	/// Classifies <paramref name="memberPaths"/> as animations of <paramref name="baseModel"/> (name = base + letter +
	/// optional number). Members that are themselves geometry models with that prefix are excluded by the caller.
	/// </summary>
	public static IReadOnlyList<ObjectAnimationFile> Find( string baseModel, IEnumerable<string> memberPaths )
	{
		var result = new List<ObjectAnimationFile>();
		foreach ( var path in memberPaths )
		{
			var name = System.IO.Path.GetFileNameWithoutExtension( path );
			if ( name.Length <= baseModel.Length || !name.StartsWith( baseModel, StringComparison.OrdinalIgnoreCase ) )
				continue;
			var suffix = name[baseModel.Length..];
			var match = Suffix.Match( suffix );
			if ( !match.Success )
				continue;
			var letter = char.ToLowerInvariant( match.Groups["letter"].Value[0] );
			var numbered = match.Groups["number"].Length > 0;
			var variant = numbered ? int.Parse( match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture ) - 1 : 0;
			if ( variant < 0 )
				continue;
			result.Add( new ObjectAnimationFile( path, baseModel, letter, GetAnimation( letter ), variant, numbered ) );
		}
		return result.OrderBy( file => file.Letter ).ThenBy( file => file.Variant ).ToList().AsReadOnly();
	}

	/// <summary>
	/// True when the object has no clip in AnimIDs 1 to 11, only (or no) AnimID 0 (letter C) clips: the original sets the instance flag
	/// 0x10 then, and a clip that ends with nothing deferred holds its last frame instead of looping or restarting as AnimID 14.
	/// </summary>
	// [BIN:TPW-EXE:0x473F2B instance flag 0x10] 0x473e30 (called by the creator 0x463060 at 0x4631d6 with (instance, 1, 0, 0)) walks the twelve animation groups of the instance (count word at +0x1c + 12 × group, the groups being the letter table 0x6fe6bc in order) and ORs 0x10 into the instance flag word (+4) at 0x473f32 when no group other than group 0 has any clip; the flag has no other writer or clearer
	public static bool HasOnlyBaseClips( IReadOnlyList<ObjectAnimationFile> animations )
	{
		ArgumentNullException.ThrowIfNull( animations );
		for ( var animation = 1; animation < Letters.Length; animation++ )
			if ( Resolve( animations, animation, 0 ) != null )
				return false;
		return true;
	}

	/// <summary>The member a script's (animation, variant) pair plays, or null when the archive has none.</summary>
	public static ObjectAnimationFile? Resolve( IReadOnlyList<ObjectAnimationFile> animations, int animation, int variant )
	{
		var letter = GetLetter( animation );
		if ( letter == null || variant < 0 )
			return null;
		var numbered = animations.Where( file => file.Letter == letter && file.Numbered ).Select( file => file.Variant ).ToHashSet();
		if ( numbered.Contains( 0 ) )
		{
			// The loader reads <letter>1, <letter>2, … until the first gap; the unnumbered member is read only without <letter>1.
			for ( var index = 0; index <= variant; index++ )
				if ( !numbered.Contains( index ) )
					return null;
			return animations.First( file => file.Letter == letter && file.Numbered && file.Variant == variant );
		}
		return variant == 0 ? animations.FirstOrDefault( file => file.Letter == letter && !file.Numbered ) : null;
	}
}
