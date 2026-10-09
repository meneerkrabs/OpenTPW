namespace OpenTPW.UI.Original;

/// <summary>
/// Game files (docs/SETUP.md): the game folder and the optional CD for music and movies, changed
/// with the platform's folder dialog and stored in setup.json. The game reads its data at start-up,
/// so a change applies after a restart. Reached from the options screen; the first-run setup
/// window only asks for the game folder, because before that no original art or font exists.
/// </summary>
// [EXT:SETUP] OpenTPW setting; the original installer chose one folder and never changed it in game
public static class GameFilesScreen
{
	public static UiScreen Create( UiScreenStack stack, UiStringTable strings, string? settingsPath = null )
	{
		settingsPath ??= SetupSettings.GetDefaultPath();
		var saved = SetupSettings.Load( settingsPath );
		var gamePath = saved.GamePath ?? Settings.Default.GamePath;
		var cdPath = saved.CdPath;
		var status = "";
		Task<string?>? picker = null;
		Action<string>? pickerTarget = null;

		var screen = new UiScreen( "gameFiles" );
		var window = UiDialogs.CenteredWindow( 1500, 1060 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings.Extra( OpenTpwText.GameFiles ) );

		void Store( SetupSettings settings )
		{
			try
			{
				settings.Save( settingsPath );
				saved = settings;
				status = strings.Extra( OpenTpwText.RestartToApply );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
			{
				status = exception.Message;
			}
		}

		void SetGame( string path )
		{
			var report = GameInstallation.Inspect( path );
			if ( !report.IsUsable )
			{
				status = strings.Extra( OpenTpwText.FolderNotUsable );
				return;
			}
			gamePath = report.Path;
			Store( saved with { GamePath = report.Path } );
		}

		void SetCd( string path )
		{
			var report = GameInstallation.Inspect( path );
			if ( report.DataDirectory == null )
			{
				status = strings.Extra( OpenTpwText.FolderNotUsable );
				return;
			}
			cdPath = report.Path;
			Store( saved with { CdPath = report.Path } );
		}

		void Browse( Action<string> target, string? initial )
		{
			if ( picker != null || !FolderPicker.IsAvailable )
				return;
			pickerTarget = target;
			var start = initial != null && Directory.Exists( initial ) ? initial : null;
			picker = Task.Run( () => FolderPicker.Pick( strings.Extra( OpenTpwText.GameFiles ), start ) );
		}

		var x = window.X + 120;
		var width = window.Width - 340;
		UiButton AddFolder( string id, OpenTpwText label, Func<string> value, float y, Action change )
		{
			screen.Add( new UiLabel { Id = id + "Label", Text = () => strings.Extra( label ), Bounds = new UiRect( x, y, width, 70 ), Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = id, Text = value, Font = fonts => fonts.Small, Color = UiColors.Value, Wrap = true, Bounds = new UiRect( x, y + 74, width, 110 ), Anchor = UiAnchor.Center } );
			return screen.Add( new UiButton { Id = id + "Change", Text = () => strings.Extra( OpenTpwText.ChangeFolder ), Clicked = change,
				Bounds = new UiRect( x, y + 196, 420, 104 ), Anchor = UiAnchor.Center } );
		}

		var changeGame = AddFolder( "game", OpenTpwText.GameFolder, () => gamePath, window.Y + 160, () => Browse( SetGame, gamePath ) );
		var changeCd = AddFolder( "cd", OpenTpwText.CdFolder, () => cdPath ?? strings.Extra( OpenTpwText.NoFolder ), window.Y + 500, () => Browse( SetCd, cdPath ) );
		var removeCd = screen.Add( new UiButton { Id = "cdRemove", Text = () => strings.Extra( OpenTpwText.RemoveFolder ),
			Clicked = () => { cdPath = null; Store( saved with { CdPath = null } ); },
			Bounds = new UiRect( x + 460, window.Y + 696, 420, 104 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "status", Text = () => status, Font = fonts => fonts.Small, Wrap = true, Bounds = new UiRect( x, window.Bottom - 290, width, 100 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "back", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = stack.Pop,
			Bounds = new UiRect( x, window.Bottom - 180, 420, 104 ), Anchor = UiAnchor.Center } );
		screen.Back = stack.Pop;
		screen.Updating += _ =>
		{
			if ( picker is { IsCompleted: true } )
			{
				var chosen = picker.Status == TaskStatus.RanToCompletion ? picker.Result : null;
				if ( chosen != null )
					pickerTarget?.Invoke( chosen );
				picker = null;
				pickerTarget = null;
			}
			changeGame.Enabled = changeCd.Enabled = picker == null && FolderPicker.IsAvailable;
			removeCd.Enabled = cdPath != null && picker == null;
		};
		screen.Focus( changeGame );
		return screen;
	}
}
