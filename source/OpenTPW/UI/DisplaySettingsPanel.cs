using ImGuiNET;

namespace OpenTPW.UI;

/// <summary>
/// ImGui display section: window mode, resolution, upscaling method and render scale, plus the
/// diagnostics the design requires (method, requested/effective scale, internal/output size, fallback).
/// Changes apply at the next frame boundary and are saved to the user display settings.
/// </summary>
internal sealed class DisplaySettingsPanel
{
	private static readonly string[] ModeNames = Enum.GetNames<WindowMode>();
	private static readonly string[] UpscaleNames = Enum.GetNames<UpscaleMode>();
	private int customScale = -1;
	private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds( 15 );

	public void Draw()
	{
		var render = global::Global.Render;
		if ( !ImGui.CollapsingHeader( "Display" ) )
			return;
		var settings = render.DisplaySettings;
		var scaling = render.Scaling;

		ImGui.TextWrapped( $"Output {render.Metrics}; UI scale {Screen.UiScale}x." );
		ImGui.TextWrapped( scaling.Describe() );
		foreach ( var diagnostic in render.DisplayDiagnostics )
			ImGui.TextWrapped( diagnostic );
		if ( render.IsConfirmationPending )
		{
			ImGui.TextWrapped( $"Keep these display settings? Reverting in {render.ConfirmationSecondsRemaining} s." );
			if ( ImGui.Button( "Keep" ) )
				render.Confirm();
			ImGui.SameLine();
			if ( ImGui.Button( "Revert" ) )
				render.Revert();
		}

		var mode = (int)settings.Mode;
		if ( ImGui.Combo( "Window", ref mode, ModeNames, ModeNames.Length ) )
			render.ApplyWithConfirmation( settings with { Mode = (WindowMode)mode }, ConfirmationTimeout );
		ImGui.TextDisabled( "Alt+Enter or F11 toggles fullscreen." );

		var resolutions = render.AvailableResolutions;
		var current = $"{settings.Width}x{settings.Height}";
		if ( ImGui.BeginCombo( "Size", current ) )
		{
			foreach ( var size in resolutions )
			{
				var label = $"{size.X}x{size.Y}";
				if ( ImGui.Selectable( label, label == current ) )
					render.ApplyWithConfirmation( settings with { Width = size.X, Height = size.Y }, ConfirmationTimeout );
			}
			ImGui.EndCombo();
		}

		var upscale = (int)settings.Upscale;
		if ( ImGui.Combo( "Upscaling", ref upscale, UpscaleNames, UpscaleNames.Length ) )
		{
			var chosen = (UpscaleMode)upscale;
			var percent = chosen == UpscaleMode.Native ? 100 : settings.RenderScale == 100 ? RenderScaling.DefaultPreset : settings.RenderScale;
			render.Apply( settings with { Upscale = chosen, RenderScale = percent } );
		}
		if ( settings.Upscale == UpscaleMode.Native )
			return;
		foreach ( var preset in RenderScaling.Presets )
		{
			if ( ImGui.RadioButton( $"{preset}%", settings.RenderScale == preset ) )
				render.Apply( settings with { RenderScale = preset } );
			ImGui.SameLine();
		}
		ImGui.NewLine();
		if ( customScale < 0 || !ImGui.IsAnyItemActive() )
			customScale = settings.RenderScale;
		ImGui.SliderInt( "Custom %", ref customScale, DisplaySettings.MinimumRenderScale, DisplaySettings.MaximumRenderScale );
		// Apply on release so dragging does not recreate the targets every frame.
		if ( ImGui.IsItemDeactivatedAfterEdit() )
			render.Apply( settings with { RenderScale = customScale } );
		ImGui.TextDisabled( "Scale applies to width and height; 50% renders a quarter of the pixels." );
	}
}
