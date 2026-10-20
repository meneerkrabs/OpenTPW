using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.UI.Original;

/// <summary>A vertical gauge whose image contains upright filled (left) and empty (right) halves.</summary>
internal static class UiVerticalGauge
{
	// [BIN:TPW-EXE:0x477f4e vertical gauge] message 8 sets value / 1024; 0x477cc0 and 0x477d20 move the geometry and UV boundary together.
	// After UiModel's V flip, each half is cropped at 1-q, never stretched into its variable-height region. The image owns colour and alpha.
	internal static void Draw( UiBatch batch, UiRect bounds, int value, string texturePath )
	{
		var q = Math.Clamp( value, 0, UiGauge.FullValue ) / (float)UiGauge.FullValue;
		var emptyHeight = bounds.Height * (1 - q);
		var texture = UiTexture.Image( texturePath );
		batch.AddQuad( texture, new UiRect( bounds.X, bounds.Y, bounds.Width, emptyHeight ),
			new NVector2( 0.5f, 0 ), new NVector2( 1, 1 - q ), RgbaByte.White );
		batch.AddQuad( texture, new UiRect( bounds.X, bounds.Y + emptyHeight, bounds.Width, bounds.Height - emptyHeight ),
			new NVector2( 0, 1 - q ), new NVector2( 0.5f, 1 ), RgbaByte.White );
	}
}
