using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>RIDES-032: the render callback belongs to the whole mesh object (tp.exe 0x57A750 calls it before any mesh test), so a part's render offers it before its own hidden and view tests.</summary>
[TestClass]
public class RideCallbackOrderTests
{
	private static string Source( params string[] parts )
	{
		var directory = new DirectoryInfo( System.AppContext.BaseDirectory );
		while ( directory != null && !Directory.Exists( Path.Combine( directory.FullName, "source", "OpenTPW" ) ) )
			directory = directory.Parent;
		Assert.IsNotNull( directory, "the repository checkout" );
		return File.ReadAllText( Path.Combine( [directory!.FullName, "source", "OpenTPW", .. parts] ) ).Replace( "\r\n", "\n" );
	}

	[TestMethod]
	public void APartOffersTheCallbackBeforeItsOwnHiddenAndViewTests()
	{
		var source = Source( "World", "Entity", "ModelEntity.cs" );
		var render = source.IndexOf( "protected override void OnRender()", System.StringComparison.Ordinal );
		Assert.IsTrue( render >= 0 );
		var body = source[render..];
		var prepared = body.IndexOf( "Prepared?.Invoke();", System.StringComparison.Ordinal );
		var hidden = body.IndexOf( "if ( Hidden )", System.StringComparison.Ordinal );
		var cull = body.IndexOf( "Frustum.IsOutside", System.StringComparison.Ordinal );
		Assert.IsTrue( prepared >= 0 && prepared < hidden && hidden < cull, "Prepared, then Hidden, then the view test" );
		Assert.AreEqual( 1, System.Text.RegularExpressions.Regex.Matches( source, "Prepared\\?\\.Invoke\\(\\)" ).Count, "one call site" );
	}

	[TestMethod]
	public void TheObjectDecidesItsViewFromAllItsParts()
	{
		var source = Source( "World", "Objects", "OriginalObject.cs" );
		Assert.IsTrue( source.Contains( "Prepared = PresentIfInView" ) );
		var method = source.IndexOf( "private bool AnyPartInView()", System.StringComparison.Ordinal );
		Assert.IsTrue( method >= 0 );
		var body = source[method..System.Math.Min( source.Length, method + 700 )];
		Assert.IsTrue( body.Contains( "foreach ( var (entity, _) in parts )" ) && body.Contains( "return true" ) );
	}
}
