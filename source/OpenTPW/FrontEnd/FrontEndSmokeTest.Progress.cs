using OpenTPW.UI.Original;
using OpenTPW.FrontEnd;

namespace OpenTPW;

internal sealed partial class FrontEndSmokeTest
{
	// Shared by the native plan and asset-free CPU controls; native text readback stays in the caller.
	internal void PlanFullSimulationStart( Action verifyGameMode )
	{
		Do( "choose game mode", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "gameMode", "Enter on the focused enter-park button asks for the game mode" );
			verifyGameMode();
			Click( flow.Menu.Stack.Top!, "fullSimulation" );
		} );
		Do( "confirm game mode", () =>
		{
			Require( flow.Menu!.Stack.Top?.Name == "gameMode" && flow.Menu.Stack.Top.Find( "fullSimulation" ) is UiButton { Selected: { } selected } && selected(), "the Full Simulation toggle latches" );
			Click( flow.Menu.Stack.Top!, "ok" );
		} );
		Wait( "starter-key consent opens", 3 );
		Do( "accept starter-key consent", () =>
		{
			var consent = flow.Menu!.Stack.Top;
			Require( consent is ProgressChoiceScreen && consent.Name == "newProgress", "missing progress history asks for starter-key consent before loading jungle" );
			Require( !File.Exists( SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ) ), "no progress file exists before consent" );
			Click( consent!, "choice0" );
		} );
		steps.Enqueue( ("jungle park construction completes and activates its HUD", () =>
		{
			Require( flow.Menu?.Stack.Top?.Name != "progressFailed" && flow.Hud?.Stack.Top?.Name != "progressFailed",
				"park construction must not end in a progress failure dialog" );
			// Use the existing queued readiness protocol and strict MaximumFrames budget. Construction may
			// take more than forty frames; allocated candidate fields alone do not mean it is activated.
			return !flow.BuildingPark && flow.Level?.OriginalPark?.LevelName == "jungle" && flow.Hud != null;
		} ) );
	}
}
