using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW;

/// <summary>
/// The cross-runtime determinism check (tools/web-determinism, docs/DETERMINISM.md "Browser and native"): only in a page built with
/// <c>-p:OpenTpwDeterminismPins=true</c>, which compiles the asset-free unit tests into this assembly. The harness asks for the
/// tests of a class and runs them one by one in the browser runtime; a pinned digest asserts the value the desktop tests assert,
/// so a pass in the browser means the browser computes the desktop's bits. Nothing here ships in the published page.
/// </summary>
public static partial class Program
{
	private static readonly HashSet<Type> initializedTestClasses = new();
	private static bool determinismContentWritten;

	/// <summary>The test methods of the classes named (comma-separated, short names in OpenTPW.Tests), as JSON [{ class, method, informative }].</summary>
	[JSExport]
	public static string DeterminismTests( string classNames )
	{
		var result = new JsonArray();
		foreach ( var name in classNames.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var type = TestClass( name );
			foreach ( var method in type.GetMethods( BindingFlags.Public | BindingFlags.Instance ).Where( method => method.GetCustomAttribute<TestMethodAttribute>() != null
				&& method.GetCustomAttribute<IgnoreAttribute>() == null && method.GetParameters().Length == 0 ).OrderBy( method => method.MetadataToken ) )
				result.Add( new JsonObject
				{
					["class"] = name, ["method"] = method.Name,
					// a comparison with the runtime's own C library (DetMathTests.PlatformLibrary) informs, it does not gate
					["informative"] = method.GetCustomAttributes<TestCategoryAttribute>().Any( category => category.TestCategories.Contains( OpenTPW.Tests.DetMathTests.PlatformLibrary ) ),
				} );
		}
		return result.ToJsonString();
	}

	/// <summary>Runs one test method (with its class and test initializers and cleanup) and returns { outcome, message, ms }: passed, failed or inconclusive.</summary>
	[JSExport]
	public static string DeterminismRun( string className, string methodName )
	{
		var type = TestClass( className );
		var method = type.GetMethod( methodName, BindingFlags.Public | BindingFlags.Instance ) ?? throw new ArgumentException( $"{className}.{methodName} is not a test." );
		// the page's content files (the shipped data mods, the official extensions' objects) where the tests find them next to the program, as a game start writes them
		if ( !determinismContentWritten )
		{
			WriteContent();
			determinismContentWritten = true;
		}
		var watch = Stopwatch.StartNew();
		string outcome;
		string? message = null;
		try
		{
			if ( initializedTestClasses.Add( type ) )
				foreach ( var initializer in type.GetMethods( BindingFlags.Public | BindingFlags.Static ).Where( candidate => candidate.GetCustomAttribute<ClassInitializeAttribute>() != null ) )
					initializer.Invoke( null, new object?[] { null } );
			var instance = Activator.CreateInstance( type )!;
			var cleanups = type.GetMethods( BindingFlags.Public | BindingFlags.Instance ).Where( candidate => candidate.GetCustomAttribute<TestCleanupAttribute>() != null ).ToList();
			try
			{
				foreach ( var initializer in type.GetMethods( BindingFlags.Public | BindingFlags.Instance ).Where( candidate => candidate.GetCustomAttribute<TestInitializeAttribute>() != null ) )
					initializer.Invoke( instance, null );
				var value = method.Invoke( instance, null );
				if ( value is Task task )
					task.GetAwaiter().GetResult();
			}
			finally
			{
				foreach ( var cleanup in cleanups )
					cleanup.Invoke( instance, null );
			}
			outcome = "passed";
		}
		catch ( Exception exception )
		{
			var inner = exception is TargetInvocationException { InnerException: { } wrapped } ? wrapped : exception;
			outcome = inner is AssertInconclusiveException ? "inconclusive" : "failed";
			message = $"{inner.GetType().Name}: {inner.Message}";
		}
		return new JsonObject { ["outcome"] = outcome, ["message"] = message, ["ms"] = watch.ElapsedMilliseconds }.ToJsonString();
	}

	/// <summary>The per-function digests of <see cref="OpenTPW.Tests.CrossRuntimeMathProbe"/>: the platform's System.Math and <see cref="DetMath"/> over fixed inputs.</summary>
	[JSExport]
	public static string DeterminismMathProbe() => OpenTPW.Tests.CrossRuntimeMathProbe.DigestsJson();

	private static Type TestClass( string name ) =>
		typeof( Program ).Assembly.GetType( $"OpenTPW.Tests.{name}" ) is { } type && type.GetCustomAttribute<TestClassAttribute>() != null
			? type
			: throw new ArgumentException( $"OpenTPW.Tests.{name} is not a test class in this page." );
}
