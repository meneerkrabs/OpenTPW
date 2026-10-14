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
		Wait( "starter-key consent opens", 3 );
		Do( "accept starter-key consent", () =>
		{
			var consent = flow.Menu!.Stack.Top;
			Require( consent is ProgressChoiceScreen && consent.Name == "newProgress", "missing progress history asks for starter-key consent before loading jungle" );
			Require( !File.Exists( SaveFileSystem.GetAbsolutePath( PlayerTicketFile.FileName ) ), "no progress file exists before consent" );
			Click( consent!, "choice0" );
		} );
		Wait( "park loads", 40 );
	}
}
