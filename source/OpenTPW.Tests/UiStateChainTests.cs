using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The control state selection of tp.exe 0x476E80 (UI-003): the k-th node down the first-child chain, nothing past its end.</summary>
[TestClass]
[DoNotParallelize]
public class UiStateChainTests
{
	[TestMethod]
	public void EveryUiModelsStatesAreItsFirstChildChainAndAMissingStateSelectsNothing()
	{
		var data = StringTableTests.OriginalDataDirectory();
		if ( !File.Exists( Path.Combine( data, "ui.wad" ) ) )
			Assert.Inconclusive( "Original ui.wad is not installed." );
		var saved = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			var models = 0;
			foreach ( var file in FileSystem.GetFiles( "/ui" ).Where( path => path.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ) )
			{
				var source = new ModelFile( file );
				if ( source.Kind != ModelFileKind.Geometry || source.RootNodeIndex < 0 )
					continue;
				models++;
				var model = new UiModel( source, Path.GetFileNameWithoutExtension( file ) );
				// no shipped node has two children, so the chain is every node and the old breadth-first frame list is the same list
				Assert.IsTrue( source.Nodes.GroupBy( node => node.ParentIndex ).Where( group => group.Key >= 0 ).All( group => group.Count() == 1 ), file );
				Assert.AreEqual( source.Nodes.Count, model.States.Count, file );
				CollectionAssert.AreEqual( model.States.Where( state => state != null ).ToArray(), model.Frames.ToArray(), file );
				Assert.IsFalse( model.TryGetState( model.States.Count, out _ ), file );
				Assert.IsFalse( model.TryGetState( -1, out _ ), file );
				Assert.IsTrue( model.TryGetState( 0, out var first ) || model.States[0] == null );
				Assert.AreSame( model.Frames[0], model.GetFrame( model.States.Count ), "the widgets' fallback is the first frame (UI-003 narrowed)" );
			}
			Assert.AreEqual( 278, models );
		}
		finally
		{
			FileSystem = saved;
		}
	}
}
