using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// One light dummy of a model: the number the light opcodes address, the node it hangs on (its world transform places the light), the base colour and radius the model carries (when it
/// carries any) and nothing else; the runtime layers the object's <c>.sam</c> keys over these (docs/LIGHTS.md).
/// </summary>
/// <param name="Number">The light number (the attribute value).</param>
/// <param name="NodeIndex">The model node the attribute record belongs to (<c>Unknown46 + record index</c>).</param>
/// <param name="Colour">The base colour of the data block (linear 0..1 channels), or null when the record has no data block.</param>
/// <param name="RadiusModelUnits">The radius in model units from the data block, or null for the default (the data word is 0, or there is no block).</param>
public sealed record ModelLightDummy( int Number, int NodeIndex, NVector3? Colour, int? RadiusModelUnits );

/// <summary>
/// [EXT:object-lights] Light dummies in a model. The original's engine addresses a model's lights as node attributes of class 0x20000 with the light number as the value
/// (tp.exe 0x44B220 as ENABLELIGHT, DISABLELIGHT, SETLIGHT and COLOURLIGHT call it, 0x555C7A..0x555D9A) but never fills the light behind the attribute (0x44A870), and no
/// shipped model has one. OpenTPW's own: a record of type <see cref="ModelBuilder.LightType"/> (0x20040: the class and the data-block flag), the light number as value, and a
/// 16-byte data block whose three floats are the base colour (linear red, green, blue from 0 to 1) and whose word is the radius in model units (0: the default of 100, the
/// original's point-light radius). A record of the class without a data block (an attribute type 0x20000 written by another tool) is a light with the default colour and radius.
/// </summary>
public static class ModelLights
{
	/// <summary>The lights of <paramref name="model"/> in record order; a number that appears twice keeps its first record (the original's lookup takes the first dummy).</summary>
	public static IReadOnlyList<ModelLightDummy> Read( ModelFile model )
	{
		ArgumentNullException.ThrowIfNull( model );
		var lights = new List<ModelLightDummy>();
		var first = model.Counts.Unknown46;
		if ( first == 0xFFFF )
			return lights;
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var attribute = model.DummyAttributes[index];
			if ( (attribute.Type & ModelDummyAttribute.LightFlag) == 0 || attribute.Value > short.MaxValue )
				continue;
			var node = first + index;
			if ( node >= model.Nodes.Count || lights.Any( light => light.Number == (int)attribute.Value ) )
				continue;
			NVector3? colour = null;
			int? radius = null;
			if ( attribute.Data is { } data )
			{
				if ( float.IsFinite( data.X ) && float.IsFinite( data.Y ) && float.IsFinite( data.Z ) )
					colour = new NVector3( Math.Clamp( data.X, 0f, 1f ), Math.Clamp( data.Y, 0f, 1f ), Math.Clamp( data.Z, 0f, 1f ) );
				var word = data.Word & 0xFFFF;
				if ( word != 0 )
					radius = (int)word;
			}
			lights.Add( new ModelLightDummy( (int)attribute.Value, node, colour, radius ) );
		}
		return lights;
	}
}
