namespace OpenTPW;

/// <summary>
/// UI-agnostic display options API for the in-game Options screen (and the ImGui debug panel).
/// Implemented by the renderer; get it from <see cref="Instance"/>. All sizes are
/// <see cref="Point2"/> (X = width, Y = height). Changes apply at the next frame boundary (targets
/// are recreated safely) and are persisted in the user display settings, never in saves.
/// </summary>
/// <remarks>
/// "Keep these settings?" flow (original strings UIStrings.ChangeScreenResolution = 400 and
/// UIStrings.ChangeScreenResolutionRestored = 401, ids corrected against UITEXT): call
/// <see cref="ApplyWithConfirmation"/>, show 400 with <see cref="ConfirmationSecondsRemaining"/>, then
/// <see cref="Confirm"/> on "yes" or <see cref="Revert"/> on "no". If nothing is called before the
/// timeout the previous settings return automatically and <see cref="Reverted"/> fires (show 401). Unconfirmed settings are never saved.
/// </remarks>
public interface IDisplaySettings
{
	/// <summary>The live instance (null before the renderer exists, e.g. headless commands).</summary>
	public static IDisplaySettings? Instance { get; internal set; }

	/// <summary>Current (requested) settings: size, window mode, upscale method, render scale, UI scale.</summary>
	DisplaySettings Current { get; }

	/// <summary>Current resolution: windowed size in windowed mode, else the fullscreen output in logical units.</summary>
	Point2 CurrentResolution { get; }

	/// <summary>
	/// Selectable sizes for <paramref name="mode"/>, deduplicated and sorted by area: the display's SDL
	/// modes plus the current window and drawable sizes; for windowed/borderless also the original
	/// game's sizes (400x300 .. 1600x1200) and common wide sizes that fit the desktop. Exclusive
	/// fullscreen only lists sizes the display reports as modes.
	/// </summary>
	IReadOnlyList<Point2> GetResolutions( WindowMode mode );

	/// <summary>OpenTPW render-scale presets in percent: 77, 67, 59, 50.</summary>
	IReadOnlyList<int> RenderScalePresets { get; }
	/// <summary>Custom render-scale range in percent (50..100).</summary>
	int MinimumRenderScale { get; }
	int MaximumRenderScale { get; }
	/// <summary>Highest selectable explicit UI scale (0 = automatic).</summary>
	int MaximumUiScale { get; }

	/// <summary>What is actually rendered: effective method/scale, internal and output size, fallback reason.</summary>
	RenderScaleResult Effective { get; }
	/// <summary>Logical window size and drawable pixel size (they differ on HiDPI displays).</summary>
	DisplayMetrics Metrics { get; }
	/// <summary>Integer UI scale in use (resolved from automatic when <see cref="DisplaySettings.UiScale"/> is 0).</summary>
	int EffectiveUiScale { get; }
	/// <summary>Human-readable problems and fallbacks (invalid config, unavailable exclusive mode, ...).</summary>
	IReadOnlyList<string> Diagnostics { get; }

	/// <summary>Applies and saves the settings (invalid values fall back with a diagnostic). Cancels a pending confirmation by keeping its settings.</summary>
	void Apply( DisplaySettings settings );

	/// <summary>Applies the settings now but reverts after <paramref name="timeout"/> unless <see cref="Confirm"/> is called.</summary>
	void ApplyWithConfirmation( DisplaySettings settings, TimeSpan timeout );
	bool IsConfirmationPending { get; }
	/// <summary>Whole seconds left before the automatic revert (0 when nothing is pending).</summary>
	int ConfirmationSecondsRemaining { get; }
	/// <summary>Keeps and saves the pending settings.</summary>
	void Confirm();
	/// <summary>Restores the settings from before <see cref="ApplyWithConfirmation"/> immediately.</summary>
	void Revert();

	/// <summary>Raised after settings change (applied, confirmed, reverted, or the window was resized).</summary>
	event Action? Changed;
	/// <summary>Raised when a pending change was reverted (by <see cref="Revert"/> or the timeout).</summary>
	event Action<DisplaySettings>? Reverted;
}

/// <summary>Pure state machine for the confirm-or-revert flow (time is passed in, so it is testable).</summary>
public sealed class DisplayChangeConfirmation
{
	private DisplaySettings? previous;
	private TimeSpan deadline;

	public bool IsPending => previous != null;
	public DisplaySettings? Previous => previous;

	public void Begin( DisplaySettings restoreTo, TimeSpan now, TimeSpan timeout )
	{
		if ( timeout <= TimeSpan.Zero )
			throw new ArgumentOutOfRangeException( nameof( timeout ), "The confirmation timeout must be positive." );
		// A second pending change still reverts to the last confirmed settings.
		previous ??= restoreTo;
		deadline = now + timeout;
	}

	public int SecondsRemaining( TimeSpan now ) =>
		previous == null ? 0 : (int)Math.Ceiling( Math.Max( 0, (deadline - now).TotalSeconds ) );

	/// <summary>Ends the pending change; returns the settings to restore when it timed out, else null.</summary>
	public DisplaySettings? Poll( TimeSpan now ) => previous != null && now >= deadline ? Take() : null;

	/// <summary>Ends the pending change and returns the settings to restore (null if none was pending).</summary>
	public DisplaySettings? Take()
	{
		var result = previous;
		previous = null;
		return result;
	}

	public void Clear() => previous = null;
}
