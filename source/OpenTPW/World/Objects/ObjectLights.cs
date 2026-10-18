using System.Globalization;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

// [EXT:object-lights] The original's model format has a place for lights (a node attribute of class 0x20000 with the light number as value) and its RSE has four opcodes that switch and
// colour them (ENABLELIGHT, DISABLELIGHT, SETLIGHT and COLOURLIGHT, tp.exe 0x555C7A..0x555D9A), but the instance builder never creates the light behind the attribute (0x44A870) and no shipped model
// or script uses either. OpenTPW's own objects can: this is the object side of docs/LIGHTS.md, feeding the renderer's point lights (PointLights.cs). Presentation only.

/// <summary>
/// One light of a placed object: where its dummy hangs, its base colour and radius, and the state the opcodes change (on or off, the current colour).
/// </summary>
public sealed class ObjectLight
{
	/// <summary>The light number the opcodes address.</summary>
	public required int Number { get; init; }
	/// <summary>The model node of the light dummy: its world transform places the light, so it follows the object's animation.</summary>
	public required int NodeIndex { get; init; }
	/// <summary>The colour a level of 100 gives (SETLIGHT scales this, not the current colour; 0x4587E0).</summary>
	public required SVector3 BaseColour { get; init; }
	/// <summary>The radius in model units (default 100, the original's point-light radius).</summary>
	public required float RadiusModelUnits { get; init; }
	/// <summary>Whether the light shines (the original's off flag 0x10000000 is clear).</summary>
	public bool On { get; internal set; }
	/// <summary>The colour the light shines with now (linear 0..1 channels).</summary>
	public SVector3 Colour { get; internal set; }
}

/// <summary>The saved state of one light (park resume): presentation, so it is not part of any state hash.</summary>
public sealed record ObjectLightRecord
{
	public int Number { get; init; }
	public bool On { get; init; }
	public float Red { get; init; }
	public float Green { get; init; }
	public float Blue { get; init; }
}

/// <summary>
/// The lights of one placed object (docs/LIGHTS.md, "Lights on objects"): read from the model's light dummies (<see cref="ModelLights"/>) with the object's <c>.sam</c> keys over them, switched
/// and coloured by the four light opcodes. A script that names a light the model does not have changes nothing (and is logged once): the original would fault there, so this is deliberately safer.
/// </summary>
/// <remarks>
/// <b><c>.sam</c> keys</b> (all optional, per light number <c>n</c>): <c>Lights[n].Red</c>, <c>.Green</c>, <c>.Blue</c> the base colour in percent (0..100, the scale COLOURLIGHT uses; a key replaces
/// that channel of the model's colour, the default is white), <c>Lights[n].Radius</c> the radius in model units (a positive number; default 100; 0.2 engine units a model unit), and
/// <c>Lights[n].On</c> (1 or 0, default 1) whether the light starts on. The key values are single numbers, as in every <c>.sam</c>.
/// </remarks>
public sealed class ObjectLightBank
{
	/// <summary>Most lights one object carries: the bound of its saved state and of what one object may ask the renderer for.</summary>
	public const int MaximumLights = 64;

	private readonly List<ObjectLight> lights;
	private readonly string name;
	private readonly HashSet<string> missing = new( StringComparer.Ordinal );

	private ObjectLightBank( List<ObjectLight> lights, string name )
	{
		this.lights = lights;
		this.name = name;
	}

	public IReadOnlyList<ObjectLight> Lights => lights;

	/// <summary>The lights of a model with the settings' keys over them, or null when the model has no light dummy.</summary>
	public static ObjectLightBank? Create( ModelFile model, ObjectSettings settings, string name )
	{
		var dummies = ModelLights.Read( model );
		if ( dummies.Count == 0 )
			return null;
		var lights = new List<ObjectLight>();
		foreach ( var dummy in dummies.Take( MaximumLights ) )
		{
			var colour = dummy.Colour ?? SVector3.One;
			var prefix = $"Lights[{dummy.Number.ToString( CultureInfo.InvariantCulture )}]";
			colour = new SVector3( Percent( settings, prefix + ".Red", colour.X ), Percent( settings, prefix + ".Green", colour.Y ), Percent( settings, prefix + ".Blue", colour.Z ) );
			var radius = dummy.RadiusModelUnits ?? PointLightMath.OriginalRadiusModelUnits;
			var configured = settings.GetFloat( prefix + ".Radius", 0f );
			if ( float.IsFinite( configured ) && configured > 0f )
				radius = MathF.Min( configured, 65535f );
			var on = settings.GetBool( prefix + ".On", true );
			lights.Add( new ObjectLight
			{
				Number = dummy.Number,
				NodeIndex = dummy.NodeIndex,
				BaseColour = colour,
				RadiusModelUnits = radius,
				On = on,
				Colour = colour
			} );
		}
		return new ObjectLightBank( lights, name );
	}

	private static float Percent( ObjectSettings settings, string key, float fallback ) =>
		settings.Has( key ) ? Math.Clamp( settings.GetFloat( key, fallback * 100f ) * 0.01f, 0f, 1f ) : fallback;

	public ObjectLight? Find( int number )
	{
		foreach ( var light in lights )
			if ( light.Number == number )
				return light;
		return null;
	}

	/// <summary>
	/// Applies one of the four opcodes. ENABLELIGHT clears and DISABLELIGHT sets the off flag; SETLIGHT sets the colour to the base colour times <c>level x 0.01</c>; COLOURLIGHT sets it to
	/// the three operands times 0.01 (each channel of the result held to 0..1, as the packed ARGB word the original stores caps it). Returns false, changing nothing, when the light does not exist.
	/// </summary>
	// [BIN:TPW-EXE:0x555C7A ENABLELIGHT] the light dummy (class 0x20000, number `light`) is looked up with 0x44B220 and its record taken with 0x4587B0; the off flag 0x10000000 of the record's type word is cleared (ENABLELIGHT) or set (DISABLELIGHT 0x555CCD, 0x458940), then the model's lights are marked dirty
	// [BIN:TPW-EXE:0x555D20 SETLIGHT] colour = base colour x level x 0.01 for red, green and blue, alpha 1.0 (0x4587E0), stored with the packed ARGB word at +0x54; COLOURLIGHT 0x555D9A (0x458890): each operand x 0.01
	public bool Apply( Opcode opcode, int number, int a = 0, int b = 0, int c = 0 )
	{
		if ( Find( number ) is not { } light )
			return false;
		switch ( opcode )
		{
			case Opcode.ENABLELIGHT:
				light.On = true;
				break;
			case Opcode.DISABLELIGHT:
				light.On = false;
				break;
			case Opcode.SETLIGHT:
				light.Colour = Clamp( light.BaseColour * ( a * 0.01f ) );
				break;
			case Opcode.COLOURLIGHT:
				light.Colour = Clamp( new SVector3( a * 0.01f, b * 0.01f, c * 0.01f ) );
				break;
			default:
				throw new ArgumentOutOfRangeException( nameof( opcode ) );
		}
		return true;
	}

	/// <summary>
	/// Adds the lights that are on to <paramref name="into"/> in engine units: each at the world transform of its dummy node (<paramref name="nodeTransforms"/> are the animator's model-space node matrices,
	/// <paramref name="placement"/> is <see cref="ObjectPlacement.ModelToEngine"/>), the radius from model units at <see cref="ObjectPlacement.ModelScale"/>, the colour the opcodes last set.
	/// </summary>
	internal void AppendTo( List<PointLight> into, IReadOnlyList<System.Numerics.Matrix4x4> nodeTransforms, System.Numerics.Matrix4x4 placement )
	{
		foreach ( var light in lights )
		{
			if ( !light.On || light.NodeIndex >= nodeTransforms.Count )
				continue;
			var world = ObjectPlacement.SwapAxes( nodeTransforms[light.NodeIndex] ) * placement;
			into.Add( new PointLight( world.Translation, light.Colour, PointLightMath.ModelUnitsToEngine( light.RadiusModelUnits ) ) );
		}
	}

	private static SVector3 Clamp( SVector3 colour ) => SVector3.Clamp( colour, SVector3.Zero, SVector3.One );

	/// <summary>True the first time <paramref name="opcode"/> misses <paramref name="number"/> on this object, so the caller logs it once.</summary>
	public bool NoteMissing( Opcode opcode, int number ) => missing.Add( $"{opcode}:{number}" );

	/// <summary>The object's name, for the log.</summary>
	public string Name => name;

	/// <summary>The saved state of every light.</summary>
	public IReadOnlyList<ObjectLightRecord> Capture() =>
		lights.Select( light => new ObjectLightRecord { Number = light.Number, On = light.On, Red = light.Colour.X, Green = light.Colour.Y, Blue = light.Colour.Z } ).ToArray();

	/// <summary>Restores saved states; the numbers this object no longer has are returned (the model changed since the save), the others keep their settings state.</summary>
	public IReadOnlyList<int> Restore( IReadOnlyList<ObjectLightRecord> records )
	{
		var unknown = new List<int>();
		foreach ( var record in records )
		{
			if ( Find( record.Number ) is not { } light )
			{
				unknown.Add( record.Number );
				continue;
			}
			light.On = record.On;
			light.Colour = new SVector3( record.Red, record.Green, record.Blue );
		}
		return unknown;
	}

	/// <summary>Pure structural validation of saved light states; throws <see cref="InvalidDataException"/> on any violation.</summary>
	public static void ValidateRecords( IReadOnlyList<ObjectLightRecord> records )
	{
		if ( records.Count > MaximumLights )
			throw new InvalidDataException( $"runtime snapshot lists {records.Count} lights, above the bound of {MaximumLights}." );
		var seen = new HashSet<int>();
		foreach ( var record in records )
		{
			if ( record == null )
				throw new InvalidDataException( "runtime snapshot has a null light record." );
			if ( record.Number < 0 || record.Number > short.MaxValue || !seen.Add( record.Number ) )
				throw new InvalidDataException( $"runtime snapshot has a light number {record.Number} that is out of range or repeated." );
			foreach ( var channel in new[] { record.Red, record.Green, record.Blue } )
				if ( !float.IsFinite( channel ) || channel < 0f || channel > 1f )
					throw new InvalidDataException( $"runtime snapshot light {record.Number} has a colour channel outside 0..1." );
		}
	}
}
