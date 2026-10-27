using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>The node hierarchies of the 278 ui.wad models, which the ancestor composition of <see cref="OpenTPW.UI.Original.UiModel"/> (tp.exe 0x57A530) relies on.</summary>
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

	/// <summary>
	/// The ancestor composition moved every frame's geometry and stored endpoints by one common translation and nothing else: drawn into a control, every vertex of every frame of
	/// every model lands where the earlier root-pose shortcut (the node's linear part times the root's matrix) put it.
	/// </summary>
	[TestMethod]
	public void AncestorCompositionDrawsEveryFrameVertexWhereTheRootPoseShortcutDid()
	{
		var data = StringTableTests.OriginalDataDirectory();
		if ( !File.Exists( Path.Combine( data, "ui.wad" ) ) )
			Assert.Inconclusive( "Original ui.wad is not installed." );
		var saved = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		try
		{
			int models = 0, frames = 0, vertices = 0, moved = 0;
			foreach ( var file in FileSystem.GetFiles( "/ui" ).Where( path => path.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ) )
			{
				var source = new ModelFile( file );
				var model = new UiModel( source, Path.GetFileNameWithoutExtension( file ) );
				models++;
				var root = source.Nodes[source.RootNodeIndex];
				var worlds = new System.Collections.Generic.Dictionary<int, System.Numerics.Matrix4x4>();
				var expected = new System.Collections.Generic.List<(string Name, System.Numerics.Matrix4x4 New, System.Numerics.Matrix4x4 Old, ModelFile.Mesh Mesh)>();
				var visited = new System.Collections.Generic.HashSet<int>();
				var queue = new System.Collections.Generic.Queue<int>();
				queue.Enqueue( source.RootNodeIndex );
				while ( queue.Count > 0 )
				{
					var index = queue.Dequeue();
					if ( !visited.Add( index ) )
						continue;
					var node = source.Nodes[index];
					worlds[index] = index == root.Index || !worlds.TryGetValue( node.ParentIndex, out var parent ) ? node.Transform : node.Transform * parent;
					foreach ( var child in source.Nodes.Where( other => other.ParentIndex == index ).OrderBy( other => other.Index ) )
						queue.Enqueue( child.Index );
					if ( node.MeshIndex < 0 || node.MeshIndex >= source.Meshes.Count )
						continue;
					var mesh = source.Meshes[node.MeshIndex];
					if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 || mesh.TexCoords.Length < mesh.Vertices.Length || mesh.Materials.Length == 0 )
						continue;
					var linear = node.Transform;
					linear.M41 = linear.M42 = linear.M43 = 0;
					expected.Add( (node.Name, worlds[index], index == root.Index ? root.Transform : linear * root.Transform, mesh) );
				}
				Assert.AreEqual( expected.Count, model.Frames.Count, Path.GetFileName( file ) );
				for ( var number = 0; number < expected.Count; number++ )
				{
					var (name, now, before, mesh) = expected[number];
					var frame = model.Frames[number];
					Assert.AreEqual( name, frame.NodeName );
					var a = System.Numerics.Vector3.Transform( mesh.BoundsMin.GetSystemVector3(), now );
					var b = System.Numerics.Vector3.Transform( mesh.BoundsMax.GetSystemVector3(), now );
					var c = System.Numerics.Vector3.Transform( mesh.BoundsMin.GetSystemVector3(), before );
					var d = System.Numerics.Vector3.Transform( mesh.BoundsMax.GetSystemVector3(), before );
					var fitNow = new System.Numerics.Vector2( Math.Min( a.X, b.X ), Math.Min( -a.Y, -b.Y ) );
					var fitBefore = new System.Numerics.Vector2( Math.Min( c.X, d.X ), Math.Min( -c.Y, -d.Y ) );
					Assert.AreEqual( fitNow.X, frame.FitBounds!.Value.X, 1e-2f );
					Assert.AreEqual( fitNow.Y, frame.FitBounds!.Value.Y, 1e-2f );
					var drawn = frame.Parts[0].Vertices;
					for ( var vertex = 0; vertex < mesh.Vertices.Length; vertex++ )
					{
						var position = mesh.Vertices[vertex].Position.GetSystemVector3();
						var p = System.Numerics.Vector3.Transform( position, now );
						var q = System.Numerics.Vector3.Transform( position, before );
						Assert.AreEqual( p.X, drawn[vertex].Position.X, 1e-2f );
						// relative to the fit origin the vertex is where the shortcut drew it
						Assert.AreEqual( p.X - fitNow.X, q.X - fitBefore.X, 2e-2f, $"{Path.GetFileName( file )} {name} vertex {vertex} x" );
						Assert.AreEqual( -p.Y - fitNow.Y, -q.Y - fitBefore.Y, 2e-2f, $"{Path.GetFileName( file )} {name} vertex {vertex} y" );
						vertices++;
						if ( Math.Abs( p.X - q.X ) > 1e-3f || Math.Abs( p.Y - q.Y ) > 1e-3f )
							moved++;
					}
					frames++;
				}
			}
			Assert.AreEqual( 278, models );
			Assert.IsTrue( frames > 278 && moved > 0, $"{frames} frames, {moved} of {vertices} vertices moved in the canvas and none relative to their fit" );
		}
		finally
		{
			FileSystem = saved;
		}
	}
}
