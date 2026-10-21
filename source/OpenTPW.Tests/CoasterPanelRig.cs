using System;

namespace OpenTPW.Tests;

/// <summary>The coaster tool's panel over a builder without a level: its commands apply straight to the builder and the rig's own edits, as a level's command funnel would apply them.</summary>
internal static class CoasterPanelRig
{
	public static CoasterToolPanel Create( CoasterBuilder builder, Func<int, int, bool>? isFreeCell = null )
	{
		var edits = new CoasterEdits();
		return new CoasterToolPanel( builder, isFreeCell, 0, command => CoasterPanelCommand.ApplyTo( command, builder, edits ) );
	}
}
