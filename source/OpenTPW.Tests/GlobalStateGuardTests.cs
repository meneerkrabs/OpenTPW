using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>
/// The guard that fails a test which leaves a global file system changed (GlobalStateGuard.cs). The leak cases run a stand-in test through the guard, nested
/// inside the real one; the others check that MSTest runs every test class, [TestInitialize], [TestCleanup] and [DataRow] of this project inside it.
/// </summary>
[TestClass]
public class GlobalStateGuardTests
{
	public TestContext TestContext { get; set; } = null!;

	private string root = null!;

	[TestInitialize]
	public void Initialize()
	{
		// the initializer runs inside the guard, so what it sets up counts, and so does the cleanup's restore
		Assert.AreEqual( TestContext.TestName, GlobalStateGuard.Running?.TestMethodName, "[TestInitialize] runs outside the guard" );
		root = Directory.CreateTempSubdirectory( "opentpw-global-guard-" ).FullName;
	}

	[TestCleanup]
	public void Cleanup()
	{
		Directory.Delete( root, true );
		Assert.AreEqual( TestContext.TestName, GlobalStateGuard.Running?.TestMethodName, "[TestCleanup] runs outside the guard" );
	}

	[DataTestMethod]
	[DataRow( "FileSystem" )]
	[DataRow( "SaveFileSystem" )]
	[DataRow( "CacheFileSystem" )]
	public void APassingTestThatLeavesAFileSystemBehindFailsNamingItsFolder( string global )
	{
		var before = Read( global );
		var result = RunGuarded( () =>
		{
			Write( global, new BaseFileSystem( root ) );
			return new TestResult { Outcome = UnitTestOutcome.Passed };
		} );

		Assert.AreEqual( UnitTestOutcome.Failed, result.Outcome );
		StringAssert.Contains( result.TestFailureException.Message, $"left global state changed: {global} was " );
		StringAssert.Contains( result.TestFailureException.Message, $"is now the file system at {root}." );
		Assert.AreSame( before, Read( global ), "the guard puts back the value the test started with" );
	}

	[TestMethod]
	public void ATestThatPutsTheFileSystemBackKeepsItsResult()
	{
		var result = RunGuarded( () =>
		{
			var saved = FileSystem;
			FileSystem = new BaseFileSystem( root );
			FileSystem = saved;
			return new TestResult { Outcome = UnitTestOutcome.Passed };
		} );

		Assert.AreEqual( UnitTestOutcome.Passed, result.Outcome );
		Assert.IsNull( result.TestFailureException );
		Assert.IsNull( result.LogError );
	}

	[TestMethod]
	public void AnInconclusiveTestThatLeavesAFileSystemBehindFailsAndKeepsItsReason()
	{
		var result = RunGuarded( () =>
		{
			FileSystem = new BaseFileSystem( root );
			return new TestResult { Outcome = UnitTestOutcome.Inconclusive, TestFailureException = new AssertInconclusiveException( "no game data" ) };
		} );

		Assert.AreEqual( UnitTestOutcome.Failed, result.Outcome );
		StringAssert.Contains( result.TestFailureException.Message, $"is now the file system at {root}." );
		StringAssert.Contains( result.TestFailureException.Message, "The test itself was inconclusive: no game data" );
	}

	[TestMethod]
	public void AFailingTestThatLeavesAFileSystemBehindKeepsItsFailureAndGetsTheLeakInItsErrorOutput()
	{
		var failure = new AssertFailedException( "its own failure" );
		var result = RunGuarded( () =>
		{
			FileSystem = new BaseFileSystem( root );
			return new TestResult { Outcome = UnitTestOutcome.Failed, TestFailureException = failure, LogError = "its own error output" };
		} );

		Assert.AreEqual( UnitTestOutcome.Failed, result.Outcome );
		Assert.AreSame( failure, result.TestFailureException );
		StringAssert.StartsWith( result.LogError, "its own error output" );
		StringAssert.Contains( result.LogError, $"is now the file system at {root}." );
	}

	[TestMethod]
	public void ANewFileSystemAtTheSameFolderIsALeakToo()
	{
		var previous = FileSystem;
		var started = new BaseFileSystem( root );
		FileSystem = started;
		try
		{
			var result = RunGuarded( () =>
			{
				FileSystem = new BaseFileSystem( root );
				return new TestResult { Outcome = UnitTestOutcome.Passed };
			} );

			Assert.AreEqual( UnitTestOutcome.Failed, result.Outcome );
			StringAssert.Contains( result.TestFailureException.Message, $"FileSystem was the file system at {root} and is now another file system at the same folder {root}." );
			Assert.AreSame( started, FileSystem );
		}
		finally
		{
			FileSystem = previous;
		}
	}

	[TestMethod]
	public void ARunnerThatThrowsStillHasItsLeakPutBack()
	{
		var previous = FileSystem;
		var guard = new GlobalStateGuard( new ThrowingRunner( () => FileSystem = new BaseFileSystem( root ) ) );

		Assert.ThrowsException<InvalidOperationException>( () => guard.Execute( new StandInTestMethod( () => new TestResult() ) ) );
		Assert.AreSame( previous, FileSystem );
	}

	[TestMethod]
	public void EveryTestClassOfThisProjectRunsItsTestsThroughTheGuard()
	{
		var assembly = typeof( GlobalStateGuardTests ).Assembly;
		var testClasses = assembly.GetTypes()
			.Where( type => type.IsDefined( typeof( Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute ), false ) )
			.ToArray();
		var unguarded = testClasses
			.Where( type => type.GetCustomAttributes( typeof( Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute ), false )
				.Any( attribute => attribute.GetType() != typeof( TestClassAttribute ) ) )
			.Select( type => type.FullName )
			.OrderBy( name => name, StringComparer.Ordinal )
			.ToArray();

		CollectionAssert.Contains( testClasses, typeof( GlobalStateGuardTests ) );
		Assert.AreEqual( 0, unguarded.Length,
			$"use [TestClass] in the namespace OpenTPW.Tests, which means OpenTPW.Tests.TestClassAttribute (GlobalStateGuard.cs): {string.Join( ", ", unguarded )}" );
		// the guard compares the globals before and after each test, which only works while one test runs at a time
		Assert.IsNull( assembly.GetCustomAttribute<ParallelizeAttribute>() );
	}

	[TestMethod]
	public void TheGuardWrapsEveryKindOfTestMethodAttributeAndKeepsItsDisplayName()
	{
		var attribute = new TestClassAttribute();

		Assert.IsInstanceOfType( attribute.GetTestMethodAttribute( new DataTestMethodAttribute() ), typeof( GlobalStateGuard ) );
		Assert.AreEqual( "Shown name", attribute.GetTestMethodAttribute( new TestMethodAttribute( "Shown name" ) )!.DisplayName );
	}

	[TestMethod]
	public void ThisTestRunsInsideTheGuard() => Assert.AreEqual( nameof( ThisTestRunsInsideTheGuard ), GlobalStateGuard.Running?.TestMethodName );

	[DataTestMethod]
	[DataRow( 1, "one" )]
	[DataRow( 2, "two" )]
	public void EachDataRowRunsInsideTheGuardWithItsOwnArguments( int number, string name )
	{
		var running = GlobalStateGuard.Running;

		Assert.AreEqual( nameof( EachDataRowRunsInsideTheGuardWithItsOwnArguments ), running?.TestMethodName );
		CollectionAssert.AreEqual( new object[] { number, name }, running!.Arguments );
	}

	private static TestResult RunGuarded( Func<TestResult> body ) =>
		new GlobalStateGuard( new TestMethodAttribute() ).Execute( new StandInTestMethod( body ) ).Single();

	private static BaseFileSystem? Read( string global ) => global switch
	{
		"FileSystem" => FileSystem,
		"SaveFileSystem" => SaveFileSystem,
		"CacheFileSystem" => CacheFileSystem,
		_ => throw new ArgumentOutOfRangeException( nameof( global ), global, null ),
	};

	private static void Write( string global, BaseFileSystem value )
	{
		switch ( global )
		{
			case "FileSystem": FileSystem = value; break;
			case "SaveFileSystem": SaveFileSystem = value; break;
			case "CacheFileSystem": CacheFileSystem = value; break;
			default: throw new ArgumentOutOfRangeException( nameof( global ), global, null );
		}
	}

	/// <summary>A test method reduced to its body: the attribute that runs it invokes it as MSTest's own test methods.</summary>
	private sealed class StandInTestMethod : ITestMethod
	{
		private readonly Func<TestResult> body;

		public StandInTestMethod( Func<TestResult> body ) => this.body = body;

		public string TestMethodName => "StandIn";
		public string TestClassName => typeof( StandInTestMethod ).FullName!;
		public Type ReturnType => typeof( void );
		public object[] Arguments => Array.Empty<object>();
		public ParameterInfo[] ParameterTypes => Array.Empty<ParameterInfo>();
		public MethodInfo MethodInfo => typeof( StandInTestMethod ).GetMethod( nameof( Invoke ) )!;
		public TestResult Invoke( object[] arguments ) => body();
		public Attribute[] GetAllAttributes( bool inherit ) => Array.Empty<Attribute>();
		public TAttributeType[] GetAttributes<TAttributeType>( bool inherit ) where TAttributeType : Attribute => Array.Empty<TAttributeType>();
	}

	/// <summary>A test method attribute that changes something and then throws instead of returning results.</summary>
	private sealed class ThrowingRunner : TestMethodAttribute
	{
		private readonly Action change;

		public ThrowingRunner( Action change ) => this.change = change;

		public override TestResult[] Execute( ITestMethod testMethod )
		{
			change();
			throw new InvalidOperationException( "the runner failed" );
		}
	}
}
