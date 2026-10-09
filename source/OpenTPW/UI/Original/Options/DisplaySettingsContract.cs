namespace OpenTPW;

// Contract for the display slice (resolutions, window mode, upscaling, UI scale). The display slice
// owns the real interface (source/OpenTPW/Client/Display/IDisplaySettings.cs); this copy lets the
// options screen compile and is replaced by it at integration. Keep member names aligned.

public enum DisplayWindowMode { Windowed, Fullscreen, Borderless }

public enum DisplayUpscaleMethod { Native, Linear, Nearest }

public readonly record struct DisplayResolution( int Width, int Height )
{
	public override string ToString() => $"{Width} x {Height}";
}

/// <summary>Result of <see cref="IDisplaySettings.Apply"/>.</summary>
public enum DisplayApplyResult
{
	/// <summary>Nothing changed.</summary>
	Unchanged,
	/// <summary>Applied live; the player must confirm within the timeout or it is reverted.</summary>
	NeedsConfirmation,
	/// <summary>Stored; takes effect after a restart.</summary>
	RestartRequired
}

public interface IDisplaySettings
{
	IReadOnlyList<DisplayResolution> AvailableResolutions { get; }
	DisplayResolution Resolution { get; set; }
	DisplayWindowMode WindowMode { get; set; }
	DisplayUpscaleMethod UpscaleMethod { get; set; }
	/// <summary>Render-scale presets in percent (77, 67, 59, 50).</summary>
	IReadOnlyList<int> RenderScalePresets { get; }
	/// <summary>Render scale in percent, 50–100 (100 = native).</summary>
	int RenderScalePercent { get; set; }
	/// <summary>User UI scale factor (1 = fit the 2048×1536 layout to the output).</summary>
	float UiScale { get; set; }
	DisplayResolution EffectiveInternalSize { get; }
	DisplayResolution OutputSize { get; }
	/// <summary>Why the requested mode was not used, or null.</summary>
	string? FallbackReason { get; }
	/// <summary>Applies pending changes.</summary>
	DisplayApplyResult Apply();
	/// <summary>Keeps the applied settings after <see cref="DisplayApplyResult.NeedsConfirmation"/>.</summary>
	void Confirm();
	/// <summary>Restores the settings from before the last <see cref="Apply"/>.</summary>
	void Revert();
}
