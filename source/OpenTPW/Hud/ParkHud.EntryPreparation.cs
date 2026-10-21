using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// [EXT:park-entry-preparation] What the HUD's first draw would make in the frame that enters the park: a park built over frames prepares
/// it in build steps instead (GameFlow.PrepareEntry), so that frame does not decode and upload it. Only interface textures are named here;
/// the HUD itself is not drawn or updated, so its state is what its constructor made.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>
	/// The interface textures the main screen's first draw would make: the images of every frame of the models its visible elements show
	/// (a button's states included), the purple bar art of its text buttons, the happiness meter, the fonts of its labels and buttons
	/// at the canvas's text scale, and the pointer art the first frame can show. Each texture once, in element order; a missing model or image is left out (its draw reports it as before).
	/// </summary>
	internal IEnumerable<UiTexture> EntryTextures( UiContext context )
	{
		var seen = new HashSet<UiTexture>();
		IEnumerable<UiTexture> ModelTextures( string? name )
		{
			if ( string.IsNullOrEmpty( name ) || context.Models.Get( name ) is not { } model )
				yield break;
			foreach ( var frame in model.Frames )
				foreach ( var part in frame.Parts )
					yield return context.TextureFor( part );
		}
		IEnumerable<UiTexture> ElementTextures( UiElement element )
		{
			switch ( element )
			{
				case UiModelImage image:
					foreach ( var texture in ModelTextures( image.Model ) )
						yield return texture;
					break;
				case UiButton button:
					foreach ( var texture in ModelTextures( button.Model ) )
						yield return texture;
					if ( button.Model == null && context.ResolveTexture( "purple_button" ) is { } art )
						yield return UiTexture.Image( art );
					if ( button.Text != null && context.HasFonts )
						yield return UiTexture.Font( button.Font( context.Fonts ), context.Canvas.TextScale );
					break;
				case UiLabel label when context.HasFonts:
					yield return UiTexture.Font( label.Font( context.Fonts ), context.Canvas.TextScale );
					break;
				case HappinessGaugeFill:
					yield return UiTexture.Image( HappinessGaugeFill.ArtPath );
					break;
			}
		}
		foreach ( var element in hud.Elements )
		{
			if ( !element.Visible )
				continue;
			foreach ( var texture in ElementTextures( element ) )
			{
				// a part without an image draws untextured: nothing to prepare
				if ( (texture.ImagePath != null || texture.Atlas != null) && seen.Add( texture ) )
					yield return texture;
			}
		}
		// the pointer of the first frame: the normal one over the HUD, the park's own (no tool, ChooseCursor) over the park
		foreach ( var type in new[] { Input.CursorTypes.Normal, OriginalPointers.TypeOf( 2 ) } )
			if ( CursorArt( type ) is { } cursor && seen.Add( UiTexture.Image( cursor ) ) )
				yield return UiTexture.Image( cursor );
	}
}
