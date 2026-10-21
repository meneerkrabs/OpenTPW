using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>Opt-in native GPU binding/readback check; run alone with OPENTPW_TEXTURE_FRAME_GPU=1.</summary>
[TestClass]
[DoNotParallelize]
public class ObjectTextureFrameGpuTests
{
	[TestMethod]
	public void BellyBounceFrameSelectionRebindsItsGpuTextureAndHeldFramesReuseResources()
	{
		if ( Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_FRAME_GPU" ) != "1" )
			Assert.Inconclusive( "Set OPENTPW_TEXTURE_FRAME_GPU=1 and run this test alone on a native graphical desktop." );
		if ( OperatingSystem.IsMacOS() && Thread.CurrentThread.IsThreadPoolThread )
			Assert.Inconclusive( "macOS requires this GPU check to run through a native main-thread harness; see docs/MD2-MODELS.md." );
		if ( ObjectCatalogCorpusTests.UseOriginalData( out var previous ) == null )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH." );
		ObjectCatalog.BonusDataRoot = null;
		OriginalObject? item = null;
		try
		{
			Log = new Logger();
			Global.Render = new Renderer();
			var entry = ObjectCatalog.Load( "space" ).Entries.Single( candidate => candidate.ArchiveName == "bouncy" );
			item = new OriginalObject( entry, new ObjectPlacement( 0, 0, 0, Vector2.Zero, 0 ) );
			var animation = entry.Animations.Select( member => (Member: member, Clip: ObjectAssets.LoadModel( entry.FileSystem, member.Path ).Clip) )
				.First( candidate => candidate.Clip?.TextureFramesEnabled == true && candidate.Clip.TextureFrameTracks.Any( track => track.Slot == 19 && track.Keys.Any( key => key.Frame != 0 ) ) );
			var track = animation.Clip!.TextureFrameTracks.Last( candidate => candidate.Slot == 19 );
			var key = track.Keys.First( candidate => candidate.Frame != 0 );
			var renderedModels = (List<Model>)typeof( OriginalObject ).GetField( "models", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( item )!;
			var initialPath = ObjectAssets.ResolveTexture( entry, item.Runtime.Model.Textures[19].FrameNames[0] )!.Value.Path;
			var initialNative = Texture.FindLoaded( initialPath )!.NativeTexture;
			var material = renderedModels.Select( model => model.Material ).First( candidate => BoundTextures( candidate ).Any( texture => ReferenceEquals( texture.NativeTexture, initialNative ) ) );
			var textures = BoundTextures( material );
			var binding = Array.FindIndex( textures, texture => ReferenceEquals( texture.NativeTexture, initialNative ) );
			var before = textures[binding];
			var beforePixels = before.ReadPixels();
			material.Set( "ObjectUniformBuffer", default( ObjectUniformBuffer ) );
			var beforeResources = material.GetResourceSets();
			var apply = (Action)typeof( OriginalObject ).GetMethod( "UpdateTextures", BindingFlags.Instance | BindingFlags.NonPublic )!.CreateDelegate( typeof( Action ), item );
			item.Runtime.Animator.Play( 0, animation.Clip, animation.Member.Name, loop: false );
			item.Runtime.Animator.Advance( (key.Tick + 1.0) / item.Runtime.Animator.TicksPerSecond );
			apply();
			var after = textures[binding];
			Assert.AreEqual( key.Frame, item.Runtime.Animator.GetTextureFrame( 19 ) );
			Assert.AreNotSame( before.NativeTexture, after.NativeTexture, "selected frame reaches the material's GPU texture" );
			Assert.IsFalse( beforePixels.SequenceEqual( after.ReadPixels() ), "the selected frame has different GPU pixels" );
			Assert.AreNotSame( beforeResources, material.GetResourceSets(), "the next draw uses new resource bindings" );
			var heldResources = material.GetResourceSets();
			apply();
			var allocated = GC.GetAllocatedBytesForCurrentThread();
			for ( var index = 0; index < 100; index++ )
				apply();
			Assert.AreEqual( allocated, GC.GetAllocatedBytesForCurrentThread(), "held-frame binding updates allocate nothing" );
			Assert.AreSame( heldResources, material.GetResourceSets() );
			Console.WriteLine( $"GPU texture-frame binding: slot 19, {initialPath} -> frame {key.Frame}; distinct native textures and pixels, stable held resource set." );
			var ownedMaterials = renderedModels.Select( model => model.Material ).ToArray();
			item.Delete();
			Assert.IsTrue( ownedMaterials.All( candidate => !Asset.All.Contains( candidate ) ), "object deletion retires its independently owned materials" );
		}
		finally
		{
			item?.Delete();
			FileSystem = previous;
			ObjectCatalog.BonusDataRoot = null;
			Global.Device?.WaitForIdle();
			if ( Global.Render != null )
			{
				Global.Render.FlushDeletions();
				Global.Render.Window.SdlWindow.Close();
			}
		}
	}

	private static Texture[] BoundTextures( Material material ) =>
		((Dictionary<string, (Texture[] Textures, string[] Names)>)typeof( Material )
			.GetField( "_boundTextureArrays", BindingFlags.Instance | BindingFlags.NonPublic )!.GetValue( material )!)["Color"].Textures;
}
