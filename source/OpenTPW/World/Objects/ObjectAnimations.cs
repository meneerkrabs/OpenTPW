using System.Text.RegularExpressions;

namespace OpenTPW;

/// <summary>An animation member of an object archive: <c>&lt;model&gt;&lt;letter&gt;[number]</c>.MD2.</summary>
public sealed record ObjectAnimationFile( string Path, string BaseModel, char Letter, int? AnimationIndex, int Variant, bool Numbered )
{
	public string Name => System.IO.Path.GetFileNameWithoutExtension( Path );
}

/// <summary>
/// Maps the RSE animation numbers (<c>TRIGANIM a v</c>, <c>WAITANIM a v</c>, ...) to animation members.
/// The first operand is an <see cref="ScriptDefs.Animations"/> value; its first letter names the file
/// suffix (Create → c, Idle → i, Load → l, Start → s, Main → m, End → e, Unload → u, Break → b,
/// Repair → r, Other → o). The second operand selects the numbered variant: variant v plays
/// <c>&lt;model&gt;&lt;letter&gt;{v+1}</c>, or the unnumbered <c>&lt;model&gt;&lt;letter&gt;</c> for v = 0.
/// Evidence (docs/OBJECTS.md): all ten letters occur as suffixes and no other letter does except
/// <c>d</c> (2 files, never requested); the ScriptDefs names and letters agree one-to-one;
/// variant counts match exactly (Totem plays m1 with variant 0 and m2…m10 with TRIGANIM_CH variants
/// 1…9, Spider WAITANIM 5 0…3 ↔ spiderm1…m4, Monkey 5 0…6 ↔ monkeym1…m7, Bouncy LOOPANIM 5 0/1 ↔
/// bouncym1/m2, Spider LOOPANIM 9 1 ↔ spiderb2). The original executable is encrypted, so the
/// binding is derived from file names and scripts, not traced.
/// </summary>
public static class ObjectAnimations
{
	private static readonly Regex Suffix = new( @"^(?<letter>[cilsmeubrod])(?<number>\d*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant );

	public static char? GetLetter( int animation ) => animation switch
	{
		(int)ScriptDefs.Animations.ANIM_Create => 'c',
		(int)ScriptDefs.Animations.ANIM_Idle => 'i',
		(int)ScriptDefs.Animations.ANIM_Load => 'l',
		(int)ScriptDefs.Animations.ANIM_Start => 's',
		(int)ScriptDefs.Animations.ANIM_Main => 'm',
		(int)ScriptDefs.Animations.ANIM_End => 'e',
		(int)ScriptDefs.Animations.ANIM_Unload => 'u',
		(int)ScriptDefs.Animations.ANIM_Break => 'b',
		(int)ScriptDefs.Animations.ANIM_Repair => 'r',
		(int)ScriptDefs.Animations.ANIM_Other => 'o',
		_ => null
	};

	public static int? GetAnimation( char letter ) => char.ToLowerInvariant( letter ) switch
	{
		'c' => (int)ScriptDefs.Animations.ANIM_Create,
		'i' => (int)ScriptDefs.Animations.ANIM_Idle,
		'l' => (int)ScriptDefs.Animations.ANIM_Load,
		's' => (int)ScriptDefs.Animations.ANIM_Start,
		'm' => (int)ScriptDefs.Animations.ANIM_Main,
		'e' => (int)ScriptDefs.Animations.ANIM_End,
		'u' => (int)ScriptDefs.Animations.ANIM_Unload,
		'b' => (int)ScriptDefs.Animations.ANIM_Break,
		'r' => (int)ScriptDefs.Animations.ANIM_Repair,
		'o' => (int)ScriptDefs.Animations.ANIM_Other,
		_ => null
	};

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

	/// <summary>The member a script's (animation, variant) pair plays, or null when the archive has none.</summary>
	public static ObjectAnimationFile? Resolve( IReadOnlyList<ObjectAnimationFile> animations, int animation, int variant )
	{
		var letter = GetLetter( animation );
		if ( letter == null || variant < 0 )
			return null;
		return animations.FirstOrDefault( file => file.Letter == letter && file.Numbered && file.Variant == variant )
			?? (variant == 0 ? animations.FirstOrDefault( file => file.Letter == letter && !file.Numbered ) : null);
	}
}
