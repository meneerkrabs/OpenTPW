using System;

namespace OpenTPW.Tests;

/// <summary>The pool tool over a builder without a level: its commands apply straight to its own builder, slides and bridges, as a level's command funnel would apply them.</summary>
internal static class PoolToolRig
{
	public static PoolTool Create( PoolBuilder builder )
	{
		PoolTool tool = null!;
		tool = new PoolTool( builder, command => PoolToolCommand.ApplyTo( command, builder, tool.Slides, tool.Bridges ) );
		return tool;
	}
}
