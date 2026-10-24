using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// Every <c>[TestClass]</c> of this project means this attribute, not MSTest's: all test classes are in the namespace <c>OpenTPW.Tests</c>, and C# finds a type
/// of the enclosing namespace before one that a using directive imports. <see cref="GlobalStateGuardTests"/> fails when a test class escapes it. MSTest still
/// finds the classes, since this derives from its attribute, and asks the class attribute for the attribute that runs each test method; this one wraps that in
/// a <see cref="GlobalStateGuard"/>.
/// </summary>
public sealed class TestClassAttribute : Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute
{
	public override TestMethodAttribute? GetTestMethodAttribute( TestMethodAttribute? testMethodAttribute ) =>
		base.GetTestMethodAttribute( testMethodAttribute ) is { } runner ? new GlobalStateGuard( runner ) : null;
}

/// <summary>
/// Runs one test, that is its [TestInitialize], the method and its [TestCleanup] (each [DataRow] on its own), and fails it when it ends with another global
/// game-data, save or cache file system than it started with. Whatever a test left there, the tests after it would read as their own, and pass only in the full
/// suite, or only alone. PRs #818 and #819 fixed 32 classes that left their game data or a deleted temporary folder as <c>FileSystem</c>.
/// <list type="bullet">
/// <item>A passing or inconclusive test that leaked fails with a message naming the value it left. A failing one keeps its own message and stack trace and gets
/// the leak in its error output.</item>
/// <item>The guard puts the old value back, so only the leaking test fails and the tests after it start as they would alone.</item>
/// <item>Values are compared by reference: a new file system at the same folder is a leak too, since it carries its own overrides and lookups.</item>
/// <item>The comparison relies on MSTest running this project's tests one at a time (there is no <c>[assembly: Parallelize]</c>).</item>
/// </list>
/// <c>Log</c> is not watched: a <see cref="Logger"/> keeps no state of its own, so every instance logs alike, and about 150 test files set one
/// (<c>Log ??= new()</c>) and never put it back.
/// </summary>
public sealed class GlobalStateGuard : TestMethodAttribute
{
	/// <summary>The globals a test has to leave as it found them.</summary>
	private static readonly (string Name, Func<BaseFileSystem?> Read, Action<BaseFileSystem?> Write)[] watched =
	{
		(nameof( FileSystem ), () => FileSystem, value => FileSystem = value!),
		(nameof( SaveFileSystem ), () => SaveFileSystem, value => SaveFileSystem = value!),
		(nameof( CacheFileSystem ), () => CacheFileSystem, value => CacheFileSystem = value!),
	};

	private readonly TestMethodAttribute runner;

	/// <param name="runner">The attribute that runs the test: its [TestMethod] or [DataTestMethod].</param>
	public GlobalStateGuard( TestMethodAttribute runner ) : base( runner.DisplayName ) => this.runner = runner;

	/// <summary>The test the guard runs at the moment, null between tests: lets a test see that it runs inside the guard.</summary>
	public static ITestMethod? Running { get; private set; }

	public override TestResult[] Execute( ITestMethod testMethod )
	{
		var before = Array.ConvertAll( watched, global => global.Read() );
		var outer = Running;
		Running = testMethod;
		TestResult[] results;
		string[] leaks;
		try
		{
			results = runner.Execute( testMethod );
		}
		finally
		{
			Running = outer;
			// also when the runner throws: MSTest reports that as a failure, and the leak must still not reach the tests after it
			leaks = PutBack( before );
		}
		if ( leaks.Length > 0 )
			Fail( results, leaks );
		return results;
	}

	/// <summary>Puts back each watched global the test changed and describes the change.</summary>
	private static string[] PutBack( BaseFileSystem?[] before )
	{
		var leaks = new List<string>();
		for ( var index = 0; index < watched.Length; index++ )
		{
			var (name, read, write) = watched[index];
			var after = read();
			if ( ReferenceEquals( after, before[index] ) )
				continue;
			leaks.Add( $"{name} was {Describe( before[index], null )} and is now {Describe( after, before[index] )}" );
			write( before[index] );
		}
		return leaks.ToArray();
	}

	/// <summary>A file system by its root folder: the folder whose files the tests after a leak would read.</summary>
	private static string Describe( BaseFileSystem? files, BaseFileSystem? started )
	{
		if ( files == null )
			return "null";
		var root = Root( files );
		return started != null && root == Root( started ) ? $"another file system at the same folder {root}" : $"the file system at {root}";
	}

	private static string Root( BaseFileSystem files )
	{
		try
		{
			return files.GetAbsolutePath( "" );
		}
		catch ( Exception exception )
		{
			return $"an unknown folder ({exception.Message})";
		}
	}

	/// <summary>Turns each passing or inconclusive result into a failure that names the leaks; a failure keeps its own and gets them in its error output.</summary>
	private static void Fail( TestResult[] results, string[] leaks )
	{
		var message = $"GlobalStateGuard: the test left global state changed: {string.Join( "; ", leaks )}. Save the value as the first statement of [TestInitialize] "
			+ "and put it back unconditionally in [TestCleanup], null included; the tests after this one would otherwise read it as their own. The guard has put the "
			+ "old value back (docs/RUNNING.md, \"Tests\").";
		foreach ( var result in results )
		{
			if ( result.Outcome is UnitTestOutcome.Passed or UnitTestOutcome.Inconclusive )
			{
				var inconclusive = result.Outcome == UnitTestOutcome.Inconclusive ? $" The test itself was inconclusive: {result.TestFailureException?.Message}" : "";
				result.Outcome = UnitTestOutcome.Failed;
				result.TestFailureException = new AssertFailedException( message + inconclusive );
			}
			else
				result.LogError = string.IsNullOrEmpty( result.LogError ) ? message : $"{result.LogError}{Environment.NewLine}{message}";
		}
	}
}
