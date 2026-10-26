using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>Developer seam for interface captures (<c>--debug-hud</c>, Debug builds): opens windows, posts messages and forces a help line.</summary>
// [EXT:debug-hud] developer/screenshot helper, not original behaviour
public sealed partial class ParkHud
{
	private string[] debugModels = Array.Empty<string>();
	private string? debugHelp;
	/// <summary>The cell a capture holds the patrol tool's pointer on instead of the real pointer (<see cref="DebugPatrolArea"/> with "hover").</summary>
	private (int X, int Y)? debugPatrolHover;

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

	/// <summary>
	/// The patrol area flow as a player does it, for a capture: opens the employee window of the first staff member of <paramref name="role"/>, presses its patrol button (the tool starts and the
	/// window closes), then clicks the first corner and, unless <paramref name="hoverOnly"/>, the second one, which sets the area; with <paramref name="hoverOnly"/> the tool stays armed with
	/// the pointer on the second cell. Returns false when there is no such member or a step was refused.
	/// </summary>
	internal bool DebugPatrolArea( StaffType role, (int X, int Y) first, (int X, int Y) second, bool hoverOnly )
	{
		var member = StaffMembers.FirstOrDefault( item => item.Type == role );
		if ( member == null || level.PatrolTool is not { } tool )
			return Refused( "there is no such member or no patrol tool" );
		if ( !OpenEmployeeWindow( member ) )
			return Refused( "the employee window did not open" );
		// a saved park's staff may have an area of their own already: the button, a toggle, first clears it
		if ( member.HasPatrolArea && !PressEmployeePatrol() )
			return Refused( "the patrol button did not clear the member's area" );
		if ( !PressEmployeePatrol() )
			return Refused( $"the patrol button did nothing (visit {level.IsReadOnlyVisit}, area {member.HasPatrolArea})" );
		if ( !tool.Click( first.X, first.Y ) )
			return Refused( $"the first click was not taken (tool active {tool.IsActive})" );
		if ( hoverOnly )
			tool.Hover( (debugPatrolHover = second).Value.X, second.Y );
		else if ( !tool.Click( second.X, second.Y ) )
			return Refused( "the second click was not taken" );
		Log.Trace( $"--debug-hud patrol: member {member.Id} ({role}) is at ({member.CellX:0.#}, {member.CellY:0.#}) and has the area {member.PatrolArea}, the tool is {(tool.IsActive ? "armed" : "done")}." );
		return true;

		static bool Refused( string why )
		{
			Log.Warning( $"--debug-hud patrol: {why}." );
			return false;
		}
	}

	/// <summary>Locates the first staff member of <paramref name="role"/> with the staff locator (Ctrl+S, its radio), so the camera follows it and its patrol area is marked.</summary>
	internal bool DebugLocateStaff( StaffType role )
	{
		var member = StaffMembers.FirstOrDefault( item => item.Type == role );
		if ( member == null )
			return false;
		OpenLocator( LocatorMode.Staff, LocatorModel.RoleOf( role ), member.Id );
		return true;
	}

	/// <summary>Starts the end-of-park finale now, whatever the research and the player's records say (a capture of the winning screen); the screen opens on the next update.</summary>
	internal void DebugFinale()
	{
		if ( !level.DebugStartFinale() )
			Log.Warning( "--debug-hud finale: this park has no End to build." );
	}

	/// <summary>Shows the Instant Action completion message box (a capture of it in any game type).</summary>
	internal void DebugWinningMessage() => Stack.Push( WinningMessage() );

	/// <summary>The buy preview's fixed turn in radians for a capture (<see cref="DebugBuyItem"/>), or null for the clock's turn.</summary>
	internal float? DebugBuyYaw { get; private set; }

	/// <summary>Holds the buy preview at a turn in degrees for a capture (null: the clock's turn).</summary>
	internal void DebugHoldBuyYaw( float? yawDegrees ) => DebugBuyYaw = yawDegrees * MathF.PI / 180;

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
