using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>
/// Native smoke test of the autorun screen (part of <c>--front-end --smoke-test</c> when the CD launcher is available):
/// GPU readback of the window must equal the CPU composite of the original bitmaps scaled by whole pixels on black bars,
/// a click on an unavailable button must do nothing, Tab must draw the focus rectangle, and a click on Play must hand over.
/// The front-end smoke test starts afterwards. Captures: <c>artifacts/native-smoke-&lt;language&gt;-autorun*.png</c>.
/// </summary>
internal sealed class AutorunSmokeTest : IDisposable
{
	private const int MaximumFrames = 600;
	private readonly AutorunScreen screen;
	private readonly Queue<(string Name, Func<bool> Step)> steps = new();
	private int frame;
	private int waitUntil;
	private bool completed;

	public AutorunSmokeTest( AutorunScreen screen )
	{
		this.screen = screen;
		global::Global.Render.CaptureOutput = true;
		Plan();
	}

	private AutorunView View => screen.Launcher;

	private void Plan()
	{
		Wait( 8 );
		Do( () =>
		{
			Require( View.Focus == null, "no button has the focus at first" );
			Verify( CaptureFrame( "autorun.png" ), "initial window" );
		} );
		Do( () =>
		{
			var unavailable = View.Buttons.First( button => !button.Enabled );
			screen.InjectedInput = UiInput.Click( Center( unavailable ) );
		} );
		Wait( 2 );
		Do( () => Require( screen.Result == AutorunResult.None && View.Focus == null, "a click on an unavailable button does nothing" ) );
		Do( () => screen.InjectedInput = UiInput.Key( UiKeys.Tab ) );
		Wait( 3 );
		Do( () =>
		{
			Require( View.Focus?.Id == AutorunButtonId.Play, "Tab focuses Play first" );
			Verify( CaptureFrame( "autorun-focus.png" ), "window with the focus rectangle" );
		} );
		Do( () => screen.InjectedInput = UiInput.Click( Center( View.Buttons.First( button => button.Id == AutorunButtonId.Play ) ) ) );
		Wait( 2 );
		Do( () =>
		{
			Require( screen.Result == AutorunResult.Play, "a click on Play ends the launcher" );
			completed = true;
			Log.Trace( $"Native autorun smoke test passed in {GameLanguage.Current.Name}: {frame} frames, {View.Buttons.Count} buttons ({View.Buttons.Count( button => button.Enabled )} available), window readback equals the integer-scaled original bitmaps, focus rectangle, unavailable button inert, Play hands over." );
		} );
	}

	private NVector2 Center( AutorunButton button )
	{
		var (x, y, width, _) = AutorunScreen.Placement();
		var scale = width / (float)AutorunView.Width;
		return new NVector2( x + (button.X + button.Width / 2f) * scale, y + (button.Y + button.Height / 2f) * scale );
	}

	/// <summary>The readback matches the original image drawn at its integer scale; everything else is black.</summary>
	private void Verify( (byte[] Pixels, int Width, int Height) capture, string what )
	{
		var (x0, y0, width, height) = AutorunScreen.Placement();
		var expected = View.Compose();
		long total = 0, matching = 0;
		for ( var y = 0; y < capture.Height; y++ )
		{
			for ( var x = 0; x < capture.Width; x++ )
			{
				byte r = 0, g = 0, b = 0;
				if ( x >= x0 && y >= y0 && x < x0 + width && y < y0 + height )
				{
					var source = ((y - y0) * AutorunView.Height / height * AutorunView.Width + (x - x0) * AutorunView.Width / width) * 4;
					(r, g, b) = (expected[source], expected[source + 1], expected[source + 2]);
				}
				var at = (y * capture.Width + x) * 4;
				total++;
				if ( Math.Abs( capture.Pixels[at] - b ) <= 2 && Math.Abs( capture.Pixels[at + 1] - g ) <= 2 && Math.Abs( capture.Pixels[at + 2] - r ) <= 2 )
					matching++;
			}
		}
		Log.Trace( $"Autorun readback ({what}): {matching}/{total} pixels equal the integer-scaled original bitmaps ({width / AutorunView.Width}x scale)." );
		Require( matching >= total * 999 / 1000, $"{what} equals the integer-scaled original bitmaps in GPU readback" );
	}

	private static (byte[] Pixels, int Width, int Height) CaptureFrame( string name ) => FrontEndSmokeTest.CaptureFrame( name );

	private void Wait( int frames ) => steps.Enqueue( ("wait", () =>
	{
		if ( waitUntil == 0 )
			waitUntil = frame + frames;
		if ( frame < waitUntil )
			return false;
		waitUntil = 0;
		return true;
	}) );

	private void Do( Action action ) => steps.Enqueue( ("step", () => { action(); return true; }) );

	public void Update()
	{
		++frame;
		Require( frame < MaximumFrames, $"finish within {MaximumFrames} frames" );
		while ( steps.Count > 0 && steps.Peek().Step() )
		{
			steps.Dequeue();
			if ( screen.InjectedInput != null )
				break;
		}
	}

	public void VerifyCompleted() => Require( completed, "complete the autorun smoke test" );

	private static void Require( bool condition, string step )
	{
		if ( !condition )
			throw new InvalidOperationException( $"Native autorun smoke test failed: {step}." );
	}

	public void Dispose() => global::Global.Render.PostUpdate -= Update;
}
