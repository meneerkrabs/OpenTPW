using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;
using Veldrid;

namespace OpenTPW.Tests;

[TestClass]
[DoNotParallelize]
public class UiTitleTextureRetirementTests
{
	[TestMethod]
	public void FrameStartRetiresEvictedGpuEntriesWithoutANewUpload()
	{
		var atlas = new FontAtlas( FontAtlasTests.CreateFont( 10, new FontAtlasTests.SyntheticGlyph( 'a', 2, 2, 0, 0, 4 ) ) );
		UiTitleBitmap.BeginFrame();
		var titles = Enumerable.Range( 1, UiTitleBitmap.Capacity + 1 )
			.Select( length => UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, new string( 'a', length ) ), 0x80, 1 )! ).ToArray();
		var oldest = titles[0];
		var active = titles[^1];

		// Exercise the real frame-start and cache-hit paths without a graphics device. The GPU values are placeholders: this checks removal and deferred
		// scheduling, not native resource disposal, so the captured deletion callbacks are deliberately never run.
		var renderer = (UiRenderer)RuntimeHelpers.GetUninitializedObject( typeof( UiRenderer ) );
		InitializeDictionary( renderer, "preparedImages" );
		var textures = InitializeDictionary( renderer, "titleTextures" );
		textures.Add( oldest, ((Veldrid.Texture)null!, (ResourceSet)null!) );
		textures.Add( active, ((Veldrid.Texture)null!, (ResourceSet)null!) );
		var host = (Renderer)RuntimeHelpers.GetUninitializedObject( typeof( Renderer ) );
		var deletions = new List<Action>();
		typeof( Renderer ).GetField( "_deletionQueue", BindingFlags.Instance | BindingFlags.NonPublic )!.SetValue( host, deletions );
		var previous = Global.Render;
		try
		{
			Global.Render = host;
			// GameFlow starts its renderers before advancing the CPU title cache and building the interface batch.
			renderer.BeginFrame();
			UiTitleBitmap.BeginFrame();
			Assert.AreEqual( 2, textures.Count, "previous-frame resources remain available" );
			Assert.AreEqual( 0, deletions.Count );
			Assert.AreSame( active, UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, new string( 'a', titles.Length ) ), 0x80, 1 ) );
			renderer.BeginFrame();
			UiTitleBitmap.BeginFrame();
			Assert.IsTrue( oldest.Evicted );
			Assert.IsFalse( active.Evicted );
			Assert.IsTrue( textures.Contains( oldest ), "CPU eviction is observed at the next renderer frame start" );
			Assert.AreEqual( 0, deletions.Count );
			Assert.AreSame( active, UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, new string( 'a', titles.Length ) ), 0x80, 1 ) );
			renderer.BeginFrame();
			Assert.IsFalse( textures.Contains( oldest ), "a frame with no uploads still removes the stale GPU-cache key" );
			Assert.IsTrue( textures.Contains( active ) );
			Assert.AreEqual( 1, deletions.Count, "GPU disposal is scheduled rather than performed during the frame" );
			UiTitleBitmap.BeginFrame();
			Assert.AreSame( active, UiTitleBitmap.Get( atlas, TextLayout.Create( atlas, new string( 'a', titles.Length ) ), 0x80, 1 ) );
			// The remaining title uses its existing GPU entry; a missing entry would try to upload and fail without a device.
			typeof( UiRenderer ).GetMethod( "GetResourceSet", BindingFlags.Instance | BindingFlags.NonPublic )!.Invoke( renderer, new object[] { active.Texture } );
			renderer.BeginFrame();
			Assert.AreEqual( 1, deletions.Count, "a retired entry is not scheduled twice" );
			Assert.AreEqual( 1, textures.Count );
		}
		finally
		{
			Global.Render = previous;
		}
	}

	private static IDictionary InitializeDictionary( object target, string name )
	{
		var field = target.GetType().GetField( name, BindingFlags.Instance | BindingFlags.NonPublic )!;
		var dictionary = (IDictionary)Activator.CreateInstance( field.FieldType )!;
		field.SetValue( target, dictionary );
		return dictionary;
	}
}
