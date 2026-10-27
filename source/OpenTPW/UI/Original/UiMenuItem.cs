using Veldrid;

namespace OpenTPW.UI.Original;

/// <summary>
/// One line of a frameless text menu (the pause menu): large cyan text that ramps to white while it is focused or pressed, and grey when disabled.
/// </summary>
// [BIN:TPW-EXE:0x48b2b9 pause line colour] the line's initialise message 0x11 gives the control the colour (0, 175, 190, 255); the hover ramp is MenuLineRamp (0x48b2a0, 0x48b220)
public sealed class UiMenuItem : UiElement
{
	public Func<string> Text { get; set; } = () => "";
	public Action? Clicked { get; set; }
	public Func<UiFonts, FontAtlas> Font { get; set; } = fonts => fonts.Title;
	public override bool Focusable => Visible && Enabled;

	public override void Activate()
	{
		if ( !Enabled )
			return;
		// the UI message hook plays cat_ui 0x1F for every activated control (see UiButton)
		GameAudio.PlayUi( GameAudio.ClickEvent );
		Clicked?.Invoke();
	}

	private readonly MenuLineRamp ramp = new();
	private readonly ReferenceFrameClock frames = new();
	private bool wasFocused;

	/// <summary>The line's current colour ramp (tests).</summary>
	internal MenuLineRamp Ramp => ramp;

	public override void Draw( UiContext context, bool focused, bool pressed )
	{
		// [BIN:TPW-EXE:0x48b2a0 pause line hover] message 0x10001 (the pointer enters) / 0x10002 (leaves) set the line's ramp slot to +1 / -1 and the tick message 0x1e, sent once per
		// drawn frame, steps it (0x48b220); [EXT:pause-menu-hover-focus] OpenTPW's focus stands for the pointer entering (the user-approved design choice; no original behaviour left to match) and the tick runs at the 60 Hz reference ([EXT:frame-driven-at-60hz])
		var hot = Enabled && (focused || pressed);
		if ( hot != wasFocused )
		{
			wasFocused = hot;
			if ( hot )
				ramp.Enter();
			else
				ramp.Leave();
		}
		for ( var count = frames.Advance( context.Delta ); count > 0; count-- )
			ramp.Step();
		var color = !Enabled ? UiColors.Disabled : ramp.Color;
		// [BIN:TPW-EXE:0x48c150 pause menu lines] the lines are made through 0x492f60, which constructs a title control (0x491920 at 0x49309a), so they carry its shadow
		context.DrawFittedText( Font( context.Fonts ), Text(), ScreenRect( context.Canvas ), color, UiAlign.Center, shadow: true );
	}
}

/// <summary>
/// The colour of one pause-menu line as tp.exe's line procedure 0x48b2a0 keeps it: the base colour (0, 175, 190) set on the
/// initialise message 0x11, a ramp slot set to +1 by the pointer entering (0x10001) or to -1 by leaving (0x10002), and the
/// step of 0x48b220 on every tick (message 0x1e).
/// </summary>
internal sealed class MenuLineRamp
{
	/// <summary>The ramp slot: 0 idle, positive while the grey rises, negative while the line is restoring.</summary>
	public int Slot { get; private set; }
	/// <summary>The line's colour.</summary>
	public RgbaByte Color { get; private set; } = UiColors.MenuItem;

	public void Enter() => Slot = 1;
	public void Leave() => Slot = -1;

	/// <summary>The tick: 0x48b220.</summary>
	public void Step()
	{
		if ( Slot > 0 )
		{
			// slot + 0x20 as the grey of all three channels, white and finished once past 0xff
			var value = Slot + 0x20;
			var grey = value > 0xff ? 0xff : value;
			Slot = value > 0xff ? 0 : value;
			Color = new RgbaByte( (byte)grey, (byte)grey, (byte)grey, 255 );
		}
		else if ( Slot < 0 )
		{
			// the cyan is set again on every step, the slot walks by -0x20 until slot + 0xff drops below zero
			var value = Slot - 0x20;
			Slot = value + 0xff < 0 ? 0 : value;
			Color = UiColors.MenuItem;
		}
	}
}
