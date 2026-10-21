using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-commands] Running the game never turns the strict command checks (<see cref="ParkCommands.Strict"/>) off: a test that runs it keeps
/// the test run's setting. A file of its own because it runs the desktop game, which the browser determinism page does not compile
/// (DeterminismPinsExcludedTests in OpenTPW.Web.csproj).
/// </summary>
[TestClass]
public class StrictCommandsGameTests
{
	[TestMethod]
	[DoNotParallelize]
	public void RunningTheGameKeepsTheStrictChecks()
	{
		var kept = ParkCommands.Strict;
		ParkCommands.Strict = true;
		try
		{
			Assert.ThrowsException<ArgumentException>( () => Game.Run( new[] { "--export-fsh-png" } ) );
			Assert.IsTrue( ParkCommands.Strict );
		}
		finally
		{
			ParkCommands.Strict = kept;
		}
	}
}
