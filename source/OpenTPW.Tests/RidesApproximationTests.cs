using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenTPW.Tests;

[TestClass]
public class RidesApproximationTests
{
	[TestMethod]
	public void EveryRideApproximationHasOneCodeTagAndDocumentation()
	{
		var ids = RidesApproximations.Entries.Select( entry => entry.Id ).ToList();
		Assert.AreEqual( ids.Count, ids.Distinct().Count() );
		var root = new DirectoryInfo( System.AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "OBJECTS.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var tags = Directory.EnumerateFiles( Path.Combine( root!.FullName, "source", "OpenTPW" ), "*.cs", SearchOption.AllDirectories )
			.Where( path => !path.Contains( $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" ) )
			.SelectMany( path => Regex.Matches( File.ReadAllText( path ), @"\[APPROX:(RIDES-\d+)\]" ).Select( match => match.Groups[1].Value ) ).ToList();
		CollectionAssert.AreEquivalent( ids, tags, "one code tag per registered approximation" );
		var docs = File.ReadAllText( Path.Combine( root.FullName, "docs", "OBJECTS.md" ) );
		foreach ( var id in ids )
			Assert.IsTrue( docs.Contains( $"| {id} |" ), $"{id} is documented" );
	}
}
