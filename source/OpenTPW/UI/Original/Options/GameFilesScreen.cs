namespace OpenTPW.UI.Original;

/// <summary>
/// Game files (docs/SETUP.md): the game folder, the optional CD for music and movies, and the optional
/// official bonus content (a folder or a .zip, imported into the configuration directory). Changed with the
/// platform's folder dialog (or a typed path) and stored in setup.json. The game reads its data at start-up,
/// so a change applies after a restart. Reached from the options screen; the first-run setup
/// window only asks for the game folder, because before that no original art or font exists.
/// </summary>
// [EXT:SETUP] OpenTPW setting; the original installer chose one folder and never changed it in game
public static class GameFilesScreen
{
	public static UiScreen Create( UiScreenStack stack, UiStringTable strings, string? settingsPath = null ) =>
		Create( stack, strings, settingsPath, (path, cancellation) => InstallationDiscovery.InspectAsync( path, cancellation: cancellation ) );

	internal static UiScreen Create( UiScreenStack stack, UiStringTable strings, string? settingsPath,
		Func<string, CancellationToken, Task<InstallationDiscoveryResult>> inspect,
		Func<string, GameEdition, CancellationToken, Task<InstallationDiscoveryResult>>? editionInspect = null )
	{
		editionInspect ??= ( path, edition, cancellation ) => InstallationDiscovery.InspectAsync( path, edition, cancellation );
		settingsPath ??= SetupSettings.GetDefaultPath();
		var saved = SetupSettings.Load( settingsPath );
		var gamePath = saved.GamePath ?? LegacySettings.GamePath ?? "";
		var cdPath = saved.CdPath;
		// The bonus folder shown is the one the game would use: the saved choice, else the imported copy in the configuration directory.
		var configDirectory = Path.GetDirectoryName( Path.GetFullPath( settingsPath ) )!;
		string? bonusShown = null;
		void RefreshBonus() => bonusShown = BonusContent.ResolveRoot( null, null, saved.BonusPath, configDirectory )?.Path;
		RefreshBonus();
		var status = "";
		Task<InstallationDiscoveryResult>? picker = null;
		Task<InstallationDiscoveryResult>? inspection = null;
		Action<InstallationReport>? inspected = null;
		var lifetime = new CancellationTokenSource();
		Action<string>? pickerTarget = null;

		var screen = new UiScreen( "gameFiles" );
		screen.Removed = () => { lifetime.Cancel(); lifetime.Dispose(); };
		var window = UiDialogs.CenteredWindow( 1500, 1420 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings.Extra( OpenTpwText.GameFiles ) );

		void Store( SetupSettings settings )
		{
			// a new game, CD or bonus folder applies at the next start; until then no archive keeps a file of the old one open, so it can be moved or removed
			WadArchive.CloseFileHandles();
			try
			{
				settings.Save( settingsPath );
				saved = settings;
				RefreshBonus();
				status = strings.Extra( OpenTpwText.RestartToApply );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
			{
				status = exception.Message;
			}
		}

		void Inspect( string path, Action<InstallationReport> apply )
		{
			if ( inspection != null ) return;
			inspected = apply;
			inspection = inspect( path, lifetime.Token );
		}

		void SetGame( string path ) => Inspect( path, report =>
		{
			if ( !report.IsUsable ) { status = strings.Extra( OpenTpwText.FolderNotUsable ); return; }
			gamePath = report.Path;
			Store( saved with { GamePath = report.Path } );
		} );

		void SetCd( string path ) => Inspect( path, report =>
		{
			if ( report.DataDirectory == null ) { status = strings.Extra( OpenTpwText.FolderNotUsable ); return; }
			cdPath = report.Path;
			Store( saved with { CdPath = report.Path } );
		} );

		void SetBonus( string path )
		{
			var chosen = Path.GetFullPath( path );
			var imported = Path.Combine( configDirectory, BonusContent.FolderName );
			try
			{
				// A folder already inside the configuration bonus folder is used as it is; a zip or any other folder is imported first.
				var inside = chosen == imported || chosen.StartsWith( imported + Path.DirectorySeparatorChar, StringComparison.Ordinal );
				var count = inside ? BonusContent.Validate( imported ) : BonusContent.Import( chosen, configDirectory );
				if ( count == 0 )
				{
					status = strings.Extra( OpenTpwText.BonusNotFound );
					return;
				}
				Store( saved with { BonusPath = imported } );
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException or InvalidDataException )
			{
				status = exception.Message;
			}
		}

		void Browse( Action<string> target, string? initial )
		{
			if ( picker != null )
				return;
			// Without a platform dialog (Linux without zenity or kdialog) the path is typed instead.
			if ( !FolderPicker.IsAvailable )
			{
				stack.Push( PathEntry( stack, strings, initial, target ) );
				return;
			}
			pickerTarget = target;
			picker = InstallationDiscovery.PickAsync( strings.Extra( OpenTpwText.GameFiles ), initial, lifetime.Token );
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
		var typeGame = screen.Add( new UiButton { Id = "gameType", Text = () => strings.Extra( OpenTpwText.EnterFolderPath ),
			Clicked = () => stack.Push( PathEntry( stack, strings, gamePath, SetGame ) ),
			Bounds = new UiRect( x + 460, window.Y + 356, 420, 104 ), Anchor = UiAnchor.Center } );
		var changeCd = AddFolder( "cd", OpenTpwText.CdFolder, () => cdPath ?? strings.Extra( OpenTpwText.NoFolder ), window.Y + 500, () => Browse( SetCd, cdPath ) );
		var typeCd = screen.Add( new UiButton { Id = "cdType", Text = () => strings.Extra( OpenTpwText.EnterFolderPath ),
			Clicked = () => stack.Push( PathEntry( stack, strings, cdPath, SetCd ) ),
			Bounds = new UiRect( x + 920, window.Y + 696, 240, 104 ), Anchor = UiAnchor.Center } );
		var removeCd = screen.Add( new UiButton { Id = "cdRemove", Text = () => strings.Extra( OpenTpwText.RemoveFolder ),
			Clicked = () => { cdPath = null; Store( saved with { CdPath = null } ); },
			Bounds = new UiRect( x + 460, window.Y + 696, 420, 104 ), Anchor = UiAnchor.Center } );
		var changeBonus = AddFolder( "bonus", OpenTpwText.BonusFolder, () => bonusShown ?? strings.Extra( OpenTpwText.NoFolder ), window.Y + 840, () => Browse( SetBonus, bonusShown ) );
		var typeBonus = screen.Add( new UiButton { Id = "bonusType", Text = () => strings.Extra( OpenTpwText.EnterFolderPath ),
			Clicked = () => stack.Push( PathEntry( stack, strings, bonusShown, SetBonus ) ),
			Bounds = new UiRect( x + 920, window.Y + 1036, 240, 104 ), Anchor = UiAnchor.Center } );
		// Remove stores an empty path, so the imported copy is not used either; the files stay where they are.
		var removeBonus = screen.Add( new UiButton { Id = "bonusRemove", Text = () => strings.Extra( OpenTpwText.RemoveFolder ),
			Clicked = () => Store( saved with { BonusPath = "" } ),
			Bounds = new UiRect( x + 460, window.Y + 1036, 420, 104 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "status", Text = () => status, Font = fonts => fonts.Small, Wrap = true, Bounds = new UiRect( x, window.Bottom - 270, width, 100 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "back", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = stack.Pop,
			Bounds = new UiRect( x, window.Bottom - 150, 420, 104 ), Anchor = UiAnchor.Center } );
		// [EXT:tpi-viewer] choosing the experimental Theme Park Inc edition; the browser has no Game files screen at all.
		var editionButton = screen.Add( new UiButton { Id = "edition", Text = () => strings.Extra( OpenTpwText.Edition ),
			Clicked = () => stack.Push( CreateEdition( stack, strings, settingsPath, editionInspect, () => { saved = SetupSettings.Load( settingsPath ); status = strings.Extra( OpenTpwText.EditionRestart ); } ) ),
			Bounds = new UiRect( x + 460, window.Bottom - 150, 560, 104 ), Anchor = UiAnchor.Center } );
		screen.Back = stack.Pop;
		screen.Updating += _ =>
		{
			if ( picker is { IsCompleted: true } )
			{
				var chosen = picker.Status == TaskStatus.RanToCompletion ? picker.Result.Reports.FirstOrDefault()?.Path : null;
				if ( chosen != null )
					pickerTarget?.Invoke( chosen );
				picker = null;
				pickerTarget = null;
			}
			if ( inspection is { IsCompleted: true } )
			{
				if ( inspection.IsCompletedSuccessfully && inspection.Result.Reports.FirstOrDefault() is { } report )
					inspected?.Invoke( report );
				else status = strings.Extra( inspection.IsCompletedSuccessfully && inspection.Result.TimedOut ? OpenTpwText.InspectionTimedOut : OpenTpwText.FolderNotUsable );
				inspection = null;
				inspected = null;
			}
			editionButton.Enabled = picker == null && inspection == null;
			changeGame.Enabled = changeCd.Enabled = changeBonus.Enabled = picker == null && inspection == null;
			typeGame.Enabled = typeCd.Enabled = typeBonus.Enabled = inspection == null;
			removeCd.Enabled = cdPath != null && picker == null && inspection == null;
			removeBonus.Enabled = bonusShown != null && picker == null && inspection == null;
		};
		screen.Focus( changeGame );
		return screen;
	}

	/// <summary>
	/// The edition choice (docs/SETUP.md): Theme Park World, or the experimental Theme Park Inc folder and world. Both go
	/// through the edition-aware inspection of the setup window, and the choice is stored in setup.json for the next start.
	/// </summary>
	// [EXT:tpi-viewer] experimental Theme Park Inc slice; the original game has one edition per install
	internal static UiScreen CreateEdition( UiScreenStack stack, UiStringTable strings, string settingsPath,
		Func<string, GameEdition, CancellationToken, Task<InstallationDiscoveryResult>> inspect, Action changed )
	{
		var saved = SetupSettings.Load( settingsPath );
		var status = "";
		Task<InstallationDiscoveryResult>? picker = null;
		Task<InstallationDiscoveryResult>? inspection = null;
		Action<InstallationReport>? inspected = null;
		Action<string>? pickerTarget = null;
		var lifetime = new CancellationTokenSource();
		var screen = new UiScreen( "edition" );
		screen.Removed = () => { lifetime.Cancel(); lifetime.Dispose(); };
		var window = UiDialogs.CenteredWindow( 1500, 1000 );
		UiDialogs.AddWindow( screen, window, "w_med", () => strings.Extra( OpenTpwText.Edition ) );

		void Store( SetupSettings settings )
		{
			try
			{
				settings.Save( settingsPath );
				saved = settings;
				status = strings.Extra( OpenTpwText.EditionRestart );
				changed();
			}
			catch ( Exception exception ) when ( exception is IOException or UnauthorizedAccessException )
			{
				status = exception.Message;
			}
		}

		void Inspect( string path, GameEdition edition, Action<InstallationReport> apply )
		{
			if ( inspection != null ) return;
			inspected = apply;
			inspection = inspect( path, edition, lifetime.Token );
		}

		string? PreferredWorld( InstallationReport report ) =>
			report.Worlds.FirstOrDefault( world => string.Equals( world, saved.ThemeParkIncWorld, StringComparison.OrdinalIgnoreCase ) ) ?? report.Worlds.FirstOrDefault();

		void SelectTpw()
		{
			var path = saved.GamePath ?? LegacySettings.GamePath;
			if ( string.IsNullOrWhiteSpace( path ) ) { status = strings.Extra( OpenTpwText.FolderNotUsable ); return; }
			Inspect( path, GameEdition.ThemeParkWorld, report =>
			{
				if ( !report.IsUsable ) { status = strings.Extra( OpenTpwText.FolderNotUsable ); return; }
				Store( saved with { SelectedEdition = GameEdition.ThemeParkWorld, GamePath = report.Path } );
			} );
		}

		void SelectTpiFolder( string path ) => Inspect( path, GameEdition.ThemeParkInc, report =>
		{
			if ( !report.IsUsable || PreferredWorld( report ) is not { } world || !GameEditions.IsViewableLevel( world ) ) { status = strings.Extra( OpenTpwText.ExperimentalNotUsable ); return; }
			Store( saved with { SelectedEdition = GameEdition.ThemeParkInc, ThemeParkIncPath = report.Path, ThemeParkIncWorld = world.ToLowerInvariant() } );
		} );

		void Browse( Action<string> target, string? initial )
		{
			if ( picker != null ) return;
			if ( !FolderPicker.IsAvailable ) { stack.Push( PathEntry( stack, strings, initial, target ) ); return; }
			pickerTarget = target;
			picker = InstallationDiscovery.PickAsync( strings.Extra( OpenTpwText.ExperimentalFolder ), initial, lifetime.Token );
		}

		void SelectTpi()
		{
			if ( string.IsNullOrWhiteSpace( saved.ThemeParkIncPath ) ) { Browse( SelectTpiFolder, null ); return; }
			SelectTpiFolder( saved.ThemeParkIncPath );
		}

		void NextWorld()
		{
			if ( string.IsNullOrWhiteSpace( saved.ThemeParkIncPath ) ) { status = strings.Extra( OpenTpwText.ExperimentalNotUsable ); return; }
			Inspect( saved.ThemeParkIncPath, GameEdition.ThemeParkInc, report =>
			{
				if ( !report.IsUsable || report.Worlds.Count == 0 ) { status = strings.Extra( OpenTpwText.ExperimentalNotUsable ); return; }
				var current = report.Worlds.ToList().FindIndex( world => string.Equals( world, saved.ThemeParkIncWorld, StringComparison.OrdinalIgnoreCase ) );
				Store( saved with { ThemeParkIncWorld = report.Worlds[(current + 1) % report.Worlds.Count].ToLowerInvariant() } );
			} );
		}

		var x = window.X + 120;
		var width = window.Width - 340;
		string EditionName( GameEdition edition ) => strings.Extra( edition == GameEdition.ThemeParkInc ? OpenTpwText.EditionThemeParkInc : OpenTpwText.EditionThemeParkWorld );
		screen.Add( new UiLabel { Id = "current", Text = () => EditionName( saved.SelectedEdition ), Bounds = new UiRect( x, window.Y + 160, width, 70 ), Anchor = UiAnchor.Center } );
		var tpw = screen.Add( new UiButton { Id = "editionTpw", Text = () => EditionName( GameEdition.ThemeParkWorld ), Clicked = SelectTpw,
			Bounds = new UiRect( x, window.Y + 260, 640, 104 ), Anchor = UiAnchor.Center } );
		var tpi = screen.Add( new UiButton { Id = "editionTpi", Text = () => EditionName( GameEdition.ThemeParkInc ), Clicked = SelectTpi,
			Bounds = new UiRect( x + 680, window.Y + 260, 640, 104 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "tpiFolderLabel", Text = () => strings.Extra( OpenTpwText.ExperimentalFolder ), Bounds = new UiRect( x, window.Y + 420, width, 70 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "tpiFolder", Text = () => saved.ThemeParkIncPath ?? strings.Extra( OpenTpwText.NoFolder ), Font = fonts => fonts.Small, Color = UiColors.Value, Wrap = true,
			Bounds = new UiRect( x, window.Y + 494, width, 110 ), Anchor = UiAnchor.Center } );
		var tpiChange = screen.Add( new UiButton { Id = "tpiFolderChange", Text = () => strings.Extra( OpenTpwText.ChangeFolder ), Clicked = () => Browse( SelectTpiFolder, saved.ThemeParkIncPath ),
			Bounds = new UiRect( x, window.Y + 616, 420, 104 ), Anchor = UiAnchor.Center } );
		var tpiType = screen.Add( new UiButton { Id = "tpiFolderType", Text = () => strings.Extra( OpenTpwText.EnterFolderPath ),
			Clicked = () => stack.Push( PathEntry( stack, strings, saved.ThemeParkIncPath, SelectTpiFolder ) ),
			Bounds = new UiRect( x + 460, window.Y + 616, 420, 104 ), Anchor = UiAnchor.Center } );
		var world = screen.Add( new UiButton { Id = "tpiWorld", Text = () => strings.Extra( OpenTpwText.ExperimentalWorld ) + " " + (saved.ThemeParkIncWorld ?? strings.Extra( OpenTpwText.NoFolder )), Clicked = NextWorld,
			Bounds = new UiRect( x + 920, window.Y + 616, 400, 104 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiLabel { Id = "status", Text = () => status, Font = fonts => fonts.Small, Wrap = true, Bounds = new UiRect( x, window.Bottom - 270, width, 100 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "back", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = stack.Pop,
			Bounds = new UiRect( x, window.Bottom - 150, 420, 104 ), Anchor = UiAnchor.Center } );
		screen.Back = stack.Pop;
		screen.Updating += _ =>
		{
			if ( picker is { IsCompleted: true } )
			{
				var chosen = picker.Status == TaskStatus.RanToCompletion ? picker.Result.Reports.FirstOrDefault()?.Path : null;
				if ( chosen != null ) pickerTarget?.Invoke( chosen );
				picker = null;
				pickerTarget = null;
			}
			if ( inspection is { IsCompleted: true } )
			{
				if ( inspection.IsCompletedSuccessfully && inspection.Result.Reports.FirstOrDefault() is { } report )
					inspected?.Invoke( report );
				else status = strings.Extra( inspection.IsCompletedSuccessfully && inspection.Result.TimedOut ? OpenTpwText.InspectionTimedOut : OpenTpwText.ExperimentalNotUsable );
				inspection = null;
				inspected = null;
			}
			tpw.Enabled = tpi.Enabled = tpiChange.Enabled = world.Enabled = picker == null && inspection == null;
			tpiType.Enabled = inspection == null;
		};
		screen.Focus( tpw );
		return screen;
	}

	/// <summary>A typed folder path, for platforms without a folder dialog.</summary>
	internal static UiScreen PathEntry( UiScreenStack stack, UiStringTable strings, string? initial, Action<string> chosen )
	{
		var screen = new UiScreen( "folderEntry" );
		var window = UiDialogs.CenteredWindow( 1400, 640 );
		UiDialogs.AddWindow( screen, window, "w_dialog", () => strings.Extra( OpenTpwText.ChangeFolder ) );
		var field = screen.Add( new UiTextField { Id = "path", Text = initial ?? "", MaximumLength = 1024, Model = "f_text1",
			Bounds = new UiRect( window.X + 100, window.Y + 190, window.Width - 300, 84 ), Anchor = UiAnchor.Center } );
		void Accept()
		{
			stack.Pop();
			if ( field.Text.Trim().Length > 0 )
				chosen( field.Text.Trim() );
		}
		field.Submitted = Accept;
		screen.Add( new UiButton { Id = "ok", Model = "b_okay", Clicked = Accept, Bounds = new UiRect( window.Right - 330, window.Bottom - 230, 120, 120 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiButton { Id = "back", Text = () => strings.Extra( OpenTpwText.Back ), Clicked = stack.Pop,
			Bounds = new UiRect( window.X + 100, window.Bottom - 220, 420, 104 ), Anchor = UiAnchor.Center } );
		screen.Back = stack.Pop;
		screen.Focus( field );
		return screen;
	}
}
