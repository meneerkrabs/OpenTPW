using System.Collections.Generic;
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
				// GetFrame(k) is still the k-th drawable frame, which is the k-th chain node while every node has a mesh
				for ( var k = 0; k < model.Frames.Count; k++ )
				{
					Assert.AreSame( model.Frames[k], model.GetFrame( k ), file );
					Assert.AreSame( model.States[k], model.GetFrame( k ), $"{file}: node {k} has no mesh" );
				}
				foreach ( var part in model.Frames.SelectMany( frame => frame.Parts ) )
					for ( var at = 0; at < part.Indices.Count; at += 3 )
						Assert.IsTrue( UiModel.IsClockwise( part.Vertices[part.Indices[at]].Position, part.Vertices[part.Indices[at + 1]].Position, part.Vertices[part.Indices[at + 2]].Position ) || part.DrawIndices.Count < part.Indices.Count, file );
				// DrawIndices is exactly the clockwise subset of Indices, in order
				foreach ( var part in model.Frames.SelectMany( frame => frame.Parts ) )
				{
					var expected = new List<int>();
					for ( var at = 0; at + 2 < part.Indices.Count; at += 3 )
						if ( UiModel.IsClockwise( part.Vertices[part.Indices[at]].Position, part.Vertices[part.Indices[at + 1]].Position, part.Vertices[part.Indices[at + 2]].Position ) )
							expected.AddRange( new[] { part.Indices[at], part.Indices[at + 1], part.Indices[at + 2] } );
					CollectionAssert.AreEqual( expected, part.DrawIndices.ToArray(), file );
				}
			}
			Assert.AreEqual( 278, models );
		}
		finally
		{
			FileSystem = saved;
		}
	}

	[TestMethod]
	public void TheLanguageFolderMeshesKeepTheirFrameIndices()
	{
		var data = StringTableTests.OriginalDataDirectory();
		var roots = new[] { data, Path.GetDirectoryName( data.TrimEnd( Path.DirectorySeparatorChar ) ) ?? data };
		var files = roots.Where( Directory.Exists ).SelectMany( root => Directory.EnumerateFiles( root, "*.md2", SearchOption.AllDirectories ) )
			.Where( path => path.Contains( "Language", StringComparison.OrdinalIgnoreCase ) || path.Contains( "Meshes", StringComparison.OrdinalIgnoreCase ) ).Distinct().ToArray();
		if ( files.Length == 0 )
			Assert.Inconclusive( "No language meshes are installed." );
		foreach ( var file in files )
		{
			UiModel model;
			try { model = UiModel.LoadHostPath( file ); }
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException )
			{
				continue;
			}
			for ( var k = 0; k < model.Frames.Count; k++ )
			{
				Assert.AreSame( model.Frames[k], model.GetFrame( k ), file );
				Assert.AreSame( model.States[k], model.GetFrame( k ), $"{file}: node {k} has no mesh" );
			}
			Assert.IsFalse( model.TryGetState( model.States.Count, out _ ), file );
		}
	}
}
