using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Settings for the whole test run.</summary>
[TestClass]
public static class TestAssembly
{
	/// <summary>
	/// [EXT:online-coop-commands] The command funnel's strict checks in every test (docs/COOP.md, "No command inside a tick"): a command applied inside a simulation tick throws, and
	/// so does a command that throws, so neither goes unnoticed.
	/// </summary>
	[AssemblyInitialize]
	public static void Initialize( TestContext context ) => ParkCommands.Strict = true;
}
