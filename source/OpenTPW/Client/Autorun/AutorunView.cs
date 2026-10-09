using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>What a click or key press on the launcher asks for.</summary>
internal enum AutorunAction { None, Play, Readme, Exit }

/// <summary>One button of the launcher: its art (a 1:1 copy of the bitmap at <see cref="X"/>, <see cref="Y"/>) and whether it is available.</summary>
internal sealed record AutorunButton( AutorunButtonId Id, int X, int Y, AutorunBitmap Art, bool Enabled )
{
	public int Width => Art.Width;
	public int Height => Art.Height;
	public bool Contains( int x, int y ) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// The 640x480 launcher window of the original CD (docs/AUTORUN.md) without any GPU: backdrop, buttons, keyboard focus
/// and clicks. [EXT:autorun] OpenTPW reproduces the CD's autorun dialog as an optional start screen.
/// </summary>
/// <remarks>
/// The original has no hover or pressed look. A button is either drawn from its own bitmap (bright yellow label, the
/// button is available and clickable) or absent, so the dim label baked into the backdrop shows (unavailable). The
/// owner-draw code copies the bitmap with SRCCOPY, draws an edge only when the script asks for a border (it does
/// not) and a dotted focus rectangle inflated by 2 pixels on the button that has the keyboard focus.
/// </remarks>
internal sealed class AutorunView
{
	public const int Width = 640;
	public const int Height = 480;

	private readonly AutorunAssets assets;
	private readonly byte[] frame = new byte[Width * Height * 4];
	private AutorunButton? pressed;
	private bool dirty = true;

	/// <param name="readmeAvailable">Whether the language's read-me file exists (the script then shows its button).</param>
	public AutorunView( AutorunAssets assets, bool readmeAvailable )
	{
		this.assets = assets;
		var rows = assets.Rows;
		var shortLayout = AutorunAssets.UsesShortLayout( assets.Language );
		var buttons = new List<AutorunButton>();
		void Add( AutorunButtonId id, int y, bool enabled )
		{
			if ( assets.Art.TryGetValue( id, out var art ) )
				buttons.Add( new AutorunButton( id, 0, y, art, enabled ) );
		}
		// The script moves Read-me to the Tech Support row and Exit to the Read-me row for the "short" languages, which have no Tech Support row.
		Add( AutorunButtonId.Play, rows["nvPlayY"], true );
		Add( AutorunButtonId.Install, rows["nvInstallY"], false );
		Add( AutorunButtonId.Uninstall, rows["nvUninstallY"], false );
		Add( AutorunButtonId.Reinstall, rows["nvReinstallY"], false );
		if ( !shortLayout )
			Add( AutorunButtonId.TechSupport, rows["nvTechbuttonY"], false );
		Add( AutorunButtonId.Readme, shortLayout ? rows["nvTechbuttonY"] : rows["nvReadmeY"], readmeAvailable );
		Add( AutorunButtonId.Exit, shortLayout ? rows["nvReadmeY"] : rows["nvQuitY"], true );
		Buttons = buttons;
	}

	public IReadOnlyList<AutorunButton> Buttons { get; }
	public AutorunBitmap Background => assets.Background;
	/// <summary>The button with the keyboard focus (null until Tab, an arrow key or a click gave it to one).</summary>
	public AutorunButton? Focus { get; private set; }

	/// <summary>True when <see cref="Compose"/> would differ from its last result.</summary>
	public bool Dirty => dirty;

	public AutorunButton? HitTest( int x, int y ) => Buttons.LastOrDefault( button => button.Enabled && button.Contains( x, y ) );

	/// <summary>
	/// Applies one frame of input. <paramref name="client"/> is the mouse in launcher pixels (null when it is outside the 640x480 image).
	/// A click needs press and release on the same button, like a push button; a release without a press (injected clicks) counts.
	/// </summary>
	public AutorunAction Handle( NVector2? client, bool leftPressed, bool leftReleased, UiKeys keys )
	{
		var hit = client is { } point ? HitTest( (int)Math.Floor( point.X ), (int)Math.Floor( point.Y ) ) : null;
		if ( leftPressed )
		{
			pressed = hit;
			if ( hit != null )
				SetFocus( hit );
		}
		var action = AutorunAction.None;
		if ( leftReleased )
		{
			if ( hit != null && (pressed == null || pressed == hit) )
			{
				SetFocus( hit );
				action = ActionOf( hit );
			}
			pressed = null;
		}
		if ( action == AutorunAction.None )
		{
			if ( (keys & (UiKeys.Tab | UiKeys.Down | UiKeys.Right)) != 0 )
				MoveFocus( 1 );
			else if ( (keys & (UiKeys.Up | UiKeys.Left)) != 0 )
				MoveFocus( -1 );
			else if ( (keys & UiKeys.Accept) != 0 && Focus != null )
				action = ActionOf( Focus );
		}
		return action;
	}

	private static AutorunAction ActionOf( AutorunButton button ) => button.Id switch
	{
		AutorunButtonId.Play => AutorunAction.Play,
		AutorunButtonId.Readme => AutorunAction.Readme,
		AutorunButtonId.Exit => AutorunAction.Exit,
		_ => AutorunAction.None,
	};

	private void SetFocus( AutorunButton? button )
	{
		if ( Focus == button )
			return;
		Focus = button;
		dirty = true;
	}

	// Tab order is the script's add_buttons order; unavailable buttons are not windows and are skipped.
	private void MoveFocus( int direction )
	{
		var enabled = Buttons.Where( button => button.Enabled ).ToList();
		if ( enabled.Count == 0 )
			return;
		var index = Focus == null ? (direction > 0 ? -1 : 0) : enabled.IndexOf( Focus );
		SetFocus( enabled[(index + direction + enabled.Count) % enabled.Count] );
	}

	/// <summary>The 640x480 RGBA image the original window shows (top row first). The array is reused between calls.</summary>
	public byte[] Compose()
	{
		if ( !dirty )
			return frame;
		dirty = false;
		Array.Copy( assets.Background.Rgba, frame, frame.Length );
		foreach ( var button in Buttons.Where( button => button.Enabled ) )
			Blit( button );
		if ( Focus is { } focus )
			DrawFocusRectangle( focus );
		return frame;
	}

	private void Blit( AutorunButton button )
	{
		for ( var row = 0; row < button.Height; row++ )
		{
			var y = button.Y + row;
			if ( y < 0 || y >= Height )
				continue;
			var width = Math.Min( button.Width, Width - button.X );
			if ( width > 0 )
				Array.Copy( button.Art.Rgba, row * button.Width * 4, frame, (y * Width + button.X) * 4, width * 4 );
		}
	}

	// DrawFocusRect XORs a 1-pixel dotted frame (every second pixel) onto the button's client rectangle shrunk by 2 pixels.
	// [APPROX:UI-040] the exact dot phase of GDI's focus rectangle brush is not known; dots are the pixels with even x + y, inverted.
	private void DrawFocusRectangle( AutorunButton button )
	{
		var left = button.X + 2;
		var top = button.Y + 2;
		var right = button.X + button.Width - 3;
		var bottom = button.Y + button.Height - 3;
		void Invert( int x, int y )
		{
			if ( x < 0 || y < 0 || x >= Width || y >= Height || ((x + y) & 1) != 0 )
				return;
			var at = (y * Width + x) * 4;
			frame[at] ^= 0xFF;
			frame[at + 1] ^= 0xFF;
			frame[at + 2] ^= 0xFF;
		}
		for ( var x = left; x <= right; x++ )
		{
			Invert( x, top );
			Invert( x, bottom );
		}
		for ( var y = top + 1; y < bottom; y++ )
		{
			Invert( left, y );
			Invert( right, y );
		}
	}

	/// <summary>
	/// Largest integer scale at which the 640x480 image fits the target (at least 1 whenever it fits at all),
	/// centred; smaller targets scale it down proportionally. Rectangle in target pixels.
	/// </summary>
	public static (int X, int Y, int Width, int Height) Fit( int targetWidth, int targetHeight )
	{
		var scale = Math.Min( targetWidth / Width, targetHeight / Height );
		if ( scale >= 1 )
			return ((targetWidth - Width * scale) / 2, (targetHeight - Height * scale) / 2, Width * scale, Height * scale);
		return MoviePresenter.Fit( Width, Height, targetWidth, targetHeight );
	}
}
