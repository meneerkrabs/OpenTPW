using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenTPW.Tests;

[TestClass]
public class EconomyApproximationTests
{
	[TestMethod]
	public void EveryApproximationIsRegisteredTaggedOnceAndDocumented()
	{
		var ids = EconomyApproximations.All.Select( entry => entry.Id ).ToList();
		Assert.AreEqual( ids.Count, ids.Distinct().Count() );
		Assert.IsTrue( EconomyApproximations.All.All( entry => entry.Assumption.Length > 0 && entry.EvidenceNeeded.Length > 0 ) );
		var root = new DirectoryInfo( System.AppContext.BaseDirectory );
		while ( root != null && !File.Exists( Path.Combine( root.FullName, "docs", "ECONOMY.md" ) ) )
			root = root.Parent;
		if ( root == null )
			Assert.Inconclusive( "Repository sources are not next to the test binaries." );
		var tags = Directory.EnumerateFiles( Path.Combine( root!.FullName, "source" ), "*.cs", SearchOption.AllDirectories )
			.Where( path => !path.EndsWith( "EconomyApproximations.cs" ) && !path.Contains( $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" ) )
			.SelectMany( path => Regex.Matches( File.ReadAllText( path ), @"// \[APPROX:(ECON-\d+)\]" ).Select( match => match.Groups[1].Value ) )
			.ToList();
		CollectionAssert.AreEquivalent( ids, tags, "one code tag per registered approximation" );
		var docs = File.ReadAllText( Path.Combine( root.FullName, "docs", "ECONOMY.md" ) );
		foreach ( var id in ids )
			Assert.IsTrue( docs.Contains( $"| {id} |" ), $"{id} is in the approximation register" );
	}
}
