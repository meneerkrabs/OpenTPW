using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>Developer seam for interface captures (<c>--debug-hud</c>, Debug builds): opens windows, posts messages and forces a help line.</summary>
// [EXT:debug-hud] developer/screenshot helper, not original behaviour
public sealed partial class ParkHud
{
	private string[] debugModels = Array.Empty<string>();
	private string? debugHelp;

	/// <summary>Draws the named ui.wad models in a row along the top of the screen (a capture to identify art).</summary>
	internal void DebugShowModels( IEnumerable<string> names ) => debugModels = names.ToArray();

	/// <summary>Boards a rideable ride: the first one, or the first whose name contains <paramref name="name"/>, or "coaster"; "look:x,y" holds the look pointer at that offset instead.</summary>
	internal void DebugRide( string? name )
	{
		if ( name != null && name.StartsWith( "look:", StringComparison.Ordinal ) )
		{
			var parts = name[5..].Split( ',' );
			RideCameraMode.PointerOverride = parts.Length == 2 && float.TryParse( parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x )
				&& float.TryParse( parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y ) ? new System.Numerics.Vector2( x, y ) : null;
			return;
		}
		RideCameraMode.PointerOverride ??= System.Numerics.Vector2.Zero;
		var candidates = level.Objects.Objects.Where( item => !item.IsDeleted && RideCameraMode.CanRide( item ) );
		var match = string.IsNullOrEmpty( name ) ? candidates.FirstOrDefault( item => item.Runtime.Coaster == null )
			: name == "coaster" ? candidates.FirstOrDefault( item => item.Runtime.Coaster != null )
			: candidates.FirstOrDefault( item => (item.Entry.DisplayName ?? "").Contains( name, StringComparison.OrdinalIgnoreCase ) );
		if ( match == null || !level.EnterRideView( match ) )
			Log.Warning( $"--debug-hud ride: no rideable ride matches '{name}'." );
		else
			Log.Trace( $"--debug-hud ride: riding {match.Entry.DisplayName}." );
	}

	/// <summary>The buy preview's fixed turn in radians for a capture (<see cref="DebugBuyItem"/>), or null for the clock's turn.</summary>
	internal float? DebugBuyYaw { get; private set; }

	/// <summary>
	/// Opens the buy window on the row of the object type <paramref name="infoId"/> (its category, then its row) and, when <paramref name="yawDegrees"/> is given,
	/// holds the preview at that turn, so a capture shows one object's preview at a known angle. A type still in the lab is made available first, as the
	/// research cheat does. False when no category lists the type.
	/// </summary>
	internal bool DebugBuyItem( int infoId, float? yawDegrees )
	{
		OpenBuyWindow();
		DebugBuyYaw = yawDegrees * MathF.PI / 180;
		// a type still in the lab is made available as the research cheat does, so every preview can be captured
		for ( var attempt = 0; attempt < 2; attempt++ )
		{
			foreach ( var category in Enum.GetValues<BuildCategory>() )
			{
				SelectCategory( category );
				var index = BuyItems.ToList().FindIndex( item => item.InfoId == infoId );
				if ( index < 0 )
					continue;
				SelectBuyItem( index );
				return true;
			}
			if ( Status.EconomySource is not ParkEconomy economy || economy.Research.MakeEveryItemAvailable() == 0 )
				break;
		}
		return false;
	}

	/// <summary>Shows <paramref name="text"/> as if an element under the pointer had this help.</summary>
	internal void DebugHelp( string? text ) => debugHelp = text;

	private void DrawDebugModels( UiContext context )
	{
		var x = 40f;
		foreach ( var name in debugModels )
		{
			var model = context.Models.Get( name );
			if ( model == null )
			{
				x += 120;
				continue;
			}
			var frame = model.GetFrame( 0 );
			var scale = Math.Min( 260f / Math.Max( 1f, frame.Width ), 260f / Math.Max( 1f, frame.Height ) );
			var rect = context.Canvas.Map( new UiRect( x, 40, frame.Width * scale, frame.Height * scale ), UiAnchor.TopLeft );
			context.Batch.AddRectangle( rect, new Veldrid.RgbaByte( 70, 70, 70, 255 ) );
			context.DrawModel( name, 0, rect );
			context.DrawText( context.Fonts.Small, name, new UiRect( rect.X, rect.Bottom, 400, 40 ), UiColors.Text );
			x += frame.Width * scale + 30;
		}
	}
}
