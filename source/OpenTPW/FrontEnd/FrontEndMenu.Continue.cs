using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

public sealed partial class FrontEndMenu
{
	/// <summary>Explicit continuation of the selected theme's manual own save, or the existing new-park mode choice.</summary>
	// [APPROX:UI-931] the theme entry offers distinct New/Continue actions for an own save; the native per-player theme-entry flow is not reproduced — evidence needed: original profile/theme-entry UI behavior
	private void ShowThemeStart()
	{
		var saved = actions.ContinuePark == null ? null : actions.ContinueEntry( Selected.Level );
		if ( saved == null )
		{
			ShowGameMode();
			return;
		}
		var theme = Selected.Level;
		var displayName = saved.Park.DisplayName ?? SelectedName;
		void StartNew()
		{
			Stack.Pop();
			if ( Selected.Level != theme ) SelectLevel( theme );
			ShowGameMode();
		}
		var mode = saved.Mode == ParkGameMode.InstantAction ? UIStrings.InstantAction : UIStrings.FullSimulation;
		Stack.Push( UiDialogs.Message( "themeStart", () => $"{displayName}\n{strings[mode]}",
			(() => strings.Extra( OpenTpwText.ContinueSavedPark ), () => actions.ContinuePark!( saved )),
			(() => strings.Extra( OpenTpwText.NewPark ), StartNew),
			(() => strings.Extra( OpenTpwText.Back ), () => Stack.Pop()) ) );
	}
}
