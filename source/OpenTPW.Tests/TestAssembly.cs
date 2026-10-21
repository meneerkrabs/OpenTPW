using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Settings for the whole test run.</summary>
[TestClass]
public static class TestAssembly
{
	/// <summary>[EXT:online-coop-commands] A command issued inside a simulation tick throws in every test, so no code path that does it goes unnoticed (docs/COOP.md, "Nested edits").</summary>
	[AssemblyInitialize]
	public static void Initialize( TestContext context ) => ParkCommands.ThrowOnIssueInTick = true;
}
