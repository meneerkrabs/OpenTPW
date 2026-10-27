using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The node hierarchies of the 278 ui.wad models, which the ancestor composition of <see cref="UiModel"/> (tp.exe 0x57A530) relies on.</summary>
[TestClass]
[DoNotParallelize]
public class UiHierarchyCorpusTests
{
	[TestMethod]
	public void ChildNodesOfEveryUiModelCarryOnlyTranslations()
	{
		var data = StringTableTests.OriginalDataDirectory();
		if ( !File.Exists( Path.Combine( data, "ui.wad" ) ) )
			Assert.Inconclusive( "Original ui.wad is not installed." );
		var saved = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			int models = 0, chained = 0;
			foreach ( var file in FileSystem.GetFiles( "/ui" ).Where( path => path.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ) )
			{
				var model = new ModelFile( file );
				if ( model.Kind != ModelFileKind.Geometry || model.RootNodeIndex < 0 )
					continue;
				models++;
				var depth = 0;
				foreach ( var node in model.Nodes )
				{
					var steps = 0;
					for ( var at = node.Index; model.Nodes[at].ParentIndex >= 0; at = model.Nodes[at].ParentIndex )
						steps++;
					depth = Math.Max( depth, steps );
					if ( node.ParentIndex < 0 )
						continue;
					var m = node.Transform;
					var linear = Math.Abs( m.M11 - 1 ) + Math.Abs( m.M22 - 1 ) + Math.Abs( m.M33 - 1 ) + Math.Abs( m.M12 ) + Math.Abs( m.M13 )
						+ Math.Abs( m.M21 ) + Math.Abs( m.M23 ) + Math.Abs( m.M31 ) + Math.Abs( m.M32 );
					Assert.IsTrue( linear < 1e-3, $"{Path.GetFileName( file )} {node.Name} has a rotation or scale" );
				}
				if ( depth > 1 )
					chained++;
			}
			Assert.AreEqual( 278, models );
			Assert.AreEqual( 168, chained, "models whose state frames are chained below one another" );
		}
		finally
		{
			FileSystem = saved;
		}
	}
}
