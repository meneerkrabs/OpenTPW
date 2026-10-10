using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Online.Packages;
using OpenTPW.UI.Original;
using StbImageSharp;

namespace OpenTPW.Tests;

/// <summary>Optional local texture pack: builder rules, seamless padding, atomic writes, activation and the options row (no GPU, no real upscaler).</summary>
[TestClass]
[DoNotParallelize]
public class TexturePackTests
{
	/// <summary>Nearest-neighbour stand-in for Real-ESRGAN.</summary>
	private sealed class NearestUpscaler( int scale = 2 ) : IImageUpscaler
	{
		public int Scale => scale;
		public string Name => "nearest";
		public string Model => "test";
		public int Calls { get; private set; }

		public void Upscale( string inputDirectory, string outputDirectory )
		{
			Calls++;
			foreach ( var file in Directory.GetFiles( inputDirectory, "*.png" ) )
			{
				var image = ImageResult.FromMemory( File.ReadAllBytes( file ), ColorComponents.RedGreenBlueAlpha );
				File.WriteAllBytes( Path.Combine( outputDirectory, Path.GetFileName( file ) ), PngImage.EncodeRgba( image.Width * scale, image.Height * scale, Nearest( image.Data, image.Width, image.Height, scale ) ) );
			}
		}
	}

	private sealed class FailingUpscaler : IImageUpscaler
	{
		public int Scale => 4;
		public string Name => "failing";
		public string Model => "test";
		public void Upscale( string inputDirectory, string outputDirectory ) => throw new InvalidOperationException( "upscaler crashed" );
	}

	private static byte[] Nearest( byte[] rgba, int width, int height, int scale )
	{
		var result = new byte[width * scale * height * scale * 4];
		for ( var y = 0; y < height * scale; y++ )
			for ( var x = 0; x < width * scale; x++ )
				Buffer.BlockCopy( rgba, ((y / scale) * width + x / scale) * 4, result, (y * width * scale + x) * 4, 4 );
		return result;
	}

	private static TextureData Pattern( int width, int height, byte seed = 1 )
	{
		var data = new byte[width * height * 4];
		for ( var i = 0; i < width * height; i++ )
		{
			data[i * 4] = (byte)(i * 7 + seed);
			data[i * 4 + 1] = (byte)(i * 13);
			data[i * 4 + 2] = (byte)(i * 29 % 200);
			data[i * 4 + 3] = (byte)(255 - i % 3);
		}
		return new TextureData( width, height, data );
	}

	private static string TemporaryDirectory()
	{
		var path = Path.Combine( Path.GetTempPath(), "opentpw-texpack-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( path );
		return path;
	}

	[TestCleanup]
	public void ResetActivePack() => TexturePack.Directory = null;

	[TestMethod]
	public void GamePathsMapToLowerCaseForwardSlashFileNames()
	{
		Assert.AreEqual( "levels/jungle/terrain/textures/jgr_bas1.wct.png", TexturePack.RelativeFileName( "/Levels/Jungle\\Terrain/textures/JGR_BAS1.wct" ) );
		Assert.AreEqual( "levels/jungle/terrain/textures/jgr_bas1.wct.png", TexturePack.RelativeFileName( "levels/jungle/terrain/textures/jgr_bas1.wct" ) );
	}

	[TestMethod]
	public void InterfaceLowDetailSmallAndChromaKeyedTexturesStayOriginal()
	{
		var texture = Pattern( 64, 64 );
		Assert.AreEqual( TexturePackBuilder.Skip.None, TexturePackBuilder.Classify( "levels/jungle/rides/totem/textures/tp_body1.wct", texture, 32 ) );
		Assert.AreEqual( TexturePackBuilder.Skip.Interface, TexturePackBuilder.Classify( "ui/textures/button.wct", texture, 32 ) );
		Assert.AreEqual( TexturePackBuilder.Skip.LowDetail, TexturePackBuilder.Classify( "levels/jungle/rides/totem/stexture/tp_body1.wct", texture, 32 ) );
		Assert.AreEqual( TexturePackBuilder.Skip.LowDetail, TexturePackBuilder.Classify( "levels/jungle/ssharete/m_grass.wct", texture, 32 ) );
		Assert.AreEqual( TexturePackBuilder.Skip.Small, TexturePackBuilder.Classify( "levels/jungle/sharetex/x.wct", Pattern( 16, 64 ), 32 ) );
		var keyed = Pattern( 32, 32 );
		keyed.Data[40] = 255;
		keyed.Data[41] = 0;
		keyed.Data[42] = 255;
		Assert.AreEqual( TexturePackBuilder.Skip.ChromaKey, TexturePackBuilder.Classify( "levels/jungle/sharetex/x.wct", keyed, 32 ) );
	}

	[TestMethod]
	public void WrapPaddingRepeatsTheOppositeEdges()
	{
		var texture = Pattern( 4, 3 );
		var padded = TexturePackBuilder.WrapPad( texture.Data, 4, 3, 2 );
		int Pixel( byte[] data, int width, int x, int y ) => BitConverter.ToInt32( data, (y * width + x) * 4 );
		Assert.AreEqual( Pixel( texture.Data, 4, 2, 1 ), Pixel( padded, 8, 0, 0 ), "top-left padding wraps from the far edges" );
		Assert.AreEqual( Pixel( texture.Data, 4, 3, 2 ), Pixel( padded, 8, 1, 1 ), "the pixel diagonally before the original is its bottom-right corner" );
		Assert.AreEqual( Pixel( texture.Data, 4, 0, 0 ), Pixel( padded, 8, 2, 2 ), "the original starts after the padding" );
		Assert.AreEqual( Pixel( texture.Data, 4, 0, 1 ), Pixel( padded, 8, 6, 3 ), "right padding is the left edge" );
		CollectionAssert.AreEqual( texture.Data, TexturePackBuilder.Crop( padded, 8, 2, 2, 4, 3 ) );
		Assert.AreEqual( 2, TexturePackBuilder.Padding( 16, 32 ) );
		Assert.AreEqual( 8, TexturePackBuilder.Padding( 128, 128 ) );
	}

	[TestMethod]
	public void BuildWritesCroppedUpscaledTexturesAndAManifestLast()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "texture-packs", "enhanced" );
		var grass = Pattern( 64, 32 );
		var textures = new (string, Func<TextureData>)[]
		{
			("levels/jungle/terrain/textures/JGR_BAS1.wct", () => grass),
			("ui/textures/button.wct", () => Pattern( 64, 64 )),
			("levels/jungle/terrain/stexture/jgr_bas1.wct", () => Pattern( 32, 32 )),
			("levels/jungle/sharetex/tiny.wct", () => Pattern( 8, 8 )),
			("levels/jungle/sharetex/broken.wct", () => throw new InvalidDataException( "bad texture" ))
		};
		var log = new List<string>();
		var upscaler = new NearestUpscaler( 2 );
		var manifest = TexturePackBuilder.Build( textures, pack, upscaler, new TexturePackBuildOptions(), log.Add );

		Assert.AreEqual( 1, upscaler.Calls, "one batch call" );
		Assert.AreEqual( (1, 1, 1, 1, 0), (manifest.Textures, manifest.SkippedInterface, manifest.SkippedLowDetail, manifest.SkippedSmall, manifest.SkippedChromaKey) );
		var file = Path.Combine( pack, "textures", "levels/jungle/terrain/textures/jgr_bas1.wct.png" );
		var image = ImageResult.FromMemory( File.ReadAllBytes( file ), ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( (128, 64), (image.Width, image.Height) );
		CollectionAssert.AreEqual( Nearest( grass.Data, 64, 32, 2 ), image.Data, "padding is cropped away exactly" );
		Assert.IsTrue( log.Any( line => line.Contains( "broken.wct" ) ), "unreadable textures are reported and skipped" );
		Assert.AreEqual( 1, manifest.SkippedUnreadable );
		Assert.IsNotNull( TexturePack.Open( pack, new List<string>() ) );
		Assert.AreEqual( 0, Directory.GetDirectories( Path.GetDirectoryName( pack )! ).Count( directory => Path.GetFileName( directory ).StartsWith( "." ) ), "no staging left behind" );
	}

	[TestMethod]
	public void InterfaceArtGoesToTheInterfaceUpscalerWithEdgePadding()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "enhanced" );
		var button = Pattern( 64, 32 );
		var textures = new (string, Func<TextureData>)[]
		{
			("levels/jungle/terrain/textures/grass.wct", () => Pattern( 32, 32 )),
			("ui/textures/purple_button.wct", () => button),
			("ui/stexture/purple_button.wct", () => Pattern( 32, 32 )),
			("ui/textures/tiny.wct", () => Pattern( 16, 16 ))
		};
		var world = new NearestUpscaler( 2 );
		var drawn = new NearestUpscaler( 2 );
		var manifest = TexturePackBuilder.Build( textures, pack, world, new TexturePackBuildOptions(), _ => { }, drawn );

		Assert.AreEqual( (1, 1), (world.Calls, drawn.Calls), "one batch per model" );
		Assert.AreEqual( (2, 1, 1, 1), (manifest.Textures, manifest.InterfaceTextures, manifest.SkippedLowDetail, manifest.SkippedSmall) );
		Assert.AreEqual( "test", manifest.InterfaceModel );
		var image = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( pack, "textures", "ui/textures/purple_button.wct.png" ) ), ColorComponents.RedGreenBlueAlpha );
		CollectionAssert.AreEqual( Nearest( button.Data, 64, 32, 2 ), image.Data, "edge padding is cropped away exactly" );
	}

	[TestMethod]
	public void EdgePaddingRepeatsTheBorderPixels()
	{
		var texture = Pattern( 4, 3 );
		var padded = TexturePackBuilder.ClampPad( texture.Data, 4, 3, 2 );
		int Pixel( byte[] data, int width, int x, int y ) => BitConverter.ToInt32( data, (y * width + x) * 4 );
		Assert.AreEqual( Pixel( texture.Data, 4, 0, 0 ), Pixel( padded, 8, 0, 0 ), "corners repeat the corner pixel" );
		Assert.AreEqual( Pixel( texture.Data, 4, 3, 1 ), Pixel( padded, 8, 7, 3 ), "right padding repeats the right edge" );
		CollectionAssert.AreEqual( texture.Data, TexturePackBuilder.Crop( padded, 8, 2, 2, 4, 3 ) );
	}

	[TestMethod]
	public void AnInterfaceOnlyMergeKeepsTheWorldTexturesOfTheExistingPack()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "enhanced" );
		var textures = new (string, Func<TextureData>)[]
		{
			("levels/a/textures/x.wct", () => Pattern( 32, 32 )),
			("ui/textures/b.wct", () => Pattern( 32, 32 ))
		};
		TexturePackBuilder.Build( textures, pack, new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		var world = new NearestUpscaler();
		var manifest = TexturePackBuilder.Build( textures, pack, world, new TexturePackBuildOptions { InterfaceOnly = true, Merge = true }, _ => { }, new NearestUpscaler() );

		Assert.AreEqual( 0, world.Calls, "world textures are not rebuilt" );
		Assert.AreEqual( (2, 1), (manifest.Textures, manifest.InterfaceTextures) );
		Assert.IsTrue( File.Exists( Path.Combine( pack, "textures", "levels/a/textures/x.wct.png" ) ), "the existing world texture is kept" );
		Assert.IsTrue( File.Exists( Path.Combine( pack, "textures", "ui/textures/b.wct.png" ) ) );
		Assert.ThrowsException<ArgumentException>( () => TexturePackBuilder.Build( textures, pack, world, new TexturePackBuildOptions { InterfaceOnly = true }, _ => { } ),
			"interface-only needs an interface upscaler" );
	}

	[TestMethod]
	public void SpriteAtlasesUseTheWorldUpscalerWithEdgePaddingAndCanBeBuiltAlone()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "enhanced" );
		var atlas = Pattern( 64, 32 );
		var textures = new (string, Func<TextureData>)[]
		{
			("levels/a/textures/x.wct", () => Pattern( 32, 32 )),
			("esprites/generic/kids/spr_be.atlas", () => atlas)
		};
		TexturePackBuilder.Build( textures, pack, new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		var world = new NearestUpscaler();
		var manifest = TexturePackBuilder.Build( textures, pack, world, new TexturePackBuildOptions { SpritesOnly = true, Merge = true }, _ => { } );
		Assert.AreEqual( 1, world.Calls, "sprites go through the world model" );
		Assert.AreEqual( 2, manifest.Textures, "one sprite atlas built, the world texture kept" );
		var image = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( pack, "textures", "esprites/generic/kids/spr_be.atlas.png" ) ), ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( (128, 64), (image.Width, image.Height) );
		CollectionAssert.AreEqual( Nearest( atlas.Data, 64, 32, 2 ), image.Data, "edge padding is cropped away exactly" );
	}

	[TestMethod]
	public void TransparentTexelsTakeTheirNeighboursColourAndStayTransparent()
	{
		// 3x1: red opaque, white transparent, white transparent.
		var rgba = new byte[] { 200, 0, 0, 255, 255, 255, 255, 0, 255, 255, 255, 0 };
		TexturePackBuilder.BleedIntoTransparent( rgba, 3, 1, passes: 1 );
		CollectionAssert.AreEqual( new byte[] { 200, 0, 0, 255, 200, 0, 0, 0, 255, 255, 255, 0 }, rgba, "one pass reaches one texel out; alpha stays 0" );
		TexturePackBuilder.BleedIntoTransparent( rgba, 3, 1, passes: 2 );
		Assert.AreEqual( (200, 0), (rgba[8], rgba[11]) );
	}

	[TestMethod]
	public void AFailedBuildKeepsTheExistingPack()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "enhanced" );
		TexturePackBuilder.Build( new (string, Func<TextureData>)[] { ("levels/a/textures/x.wct", () => Pattern( 32, 32 )) }, pack, new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		Assert.ThrowsException<InvalidOperationException>( () => TexturePackBuilder.Build(
			new (string, Func<TextureData>)[] { ("levels/a/textures/y.wct", () => Pattern( 32, 32 )) }, pack, new FailingUpscaler(), new TexturePackBuildOptions(), _ => { } ) );
		Assert.IsTrue( File.Exists( Path.Combine( pack, "textures", "levels/a/textures/x.wct.png" ) ), "the previous pack is untouched" );
		Assert.IsNotNull( TexturePack.Open( pack, new List<string>() ) );
		Assert.AreEqual( 1, Directory.GetFileSystemEntries( root ).Length, "no staging left behind" );
	}

	/// <summary>Exits successfully but writes nothing, like a crashed batch that still returns 0.</summary>
	private sealed class SilentUpscaler : IImageUpscaler
	{
		public int Scale => 4;
		public string Name => "silent";
		public string Model => "test";
		public void Upscale( string inputDirectory, string outputDirectory ) { }
	}

	[TestMethod]
	public void MissingUpscalerOutputFailsTheBuildAndKeepsTheOldPack()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "enhanced" );
		TexturePackBuilder.Build( new (string, Func<TextureData>)[] { ("levels/a/textures/x.wct", () => Pattern( 32, 32 )) }, pack, new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		var error = Assert.ThrowsException<InvalidOperationException>( () => TexturePackBuilder.Build(
			new (string, Func<TextureData>)[] { ("levels/a/textures/y.wct", () => Pattern( 32, 32 )) }, pack, new SilentUpscaler(), new TexturePackBuildOptions(), _ => { } ) );
		StringAssert.Contains( error.Message, "y.wct" );
		Assert.IsTrue( File.Exists( Path.Combine( pack, "textures", "levels/a/textures/x.wct.png" ) ) );
		Assert.AreEqual( 1, Directory.GetFileSystemEntries( root ).Length, "no staging left behind" );
	}

	[TestMethod]
	public void SubtreeAndKeysCannotLeaveTheirRoots()
	{
		var root = TemporaryDirectory();
		Assert.ThrowsException<ArgumentException>( () => TexturePackBuilder.EnumerateGameTextures( root, "../outside" ).ToList() );
		Assert.ThrowsException<ArgumentException>( () => TexturePackBuilder.EnumerateGameTextures( root, Path.GetTempPath() ).ToList() );
		Assert.AreEqual( 0, TexturePackBuilder.EnumerateGameTextures( root, "levels/missing" ).Count() );
		var pack = Path.Combine( root, "enhanced" );
		var manifest = TexturePackBuilder.Build( new (string, Func<TextureData>)[] { ("levels/../../escape.wct", () => Pattern( 32, 32 )) }, pack, new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		Assert.AreEqual( (0, 1), (manifest.Textures, manifest.SkippedUnreadable) );
		Assert.IsFalse( File.Exists( Path.Combine( root, "escape.wct.png" ) ) );
	}

	[TestMethod]
	public void ActivationNeedsTheSettingAndAUsablePack()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "enhanced" );
		var diagnostics = new List<string>();
		TexturePack.Activate( true, diagnostics, pack );
		Assert.IsNull( TexturePack.Directory );
		StringAssert.Contains( diagnostics.Single(), "--build-texture-pack" );

		TexturePackBuilder.Build( new (string, Func<TextureData>)[] { ("levels/a/textures/x.wct", () => Pattern( 32, 32 )) }, pack, new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		TexturePack.Activate( false, diagnostics, pack );
		Assert.IsNull( TexturePack.Directory, "off by default" );
		TexturePack.Activate( true, diagnostics, pack );
		Assert.AreEqual( Path.Combine( pack, "textures" ), TexturePack.Directory );
		Assert.AreEqual( Path.Combine( pack, "textures", "levels/a/textures/x.wct.png" ), TexturePack.Find( "/Levels/A/Textures/X.WCT" ) );
		Assert.IsNull( TexturePack.Find( "levels/a/textures/missing.wct" ) );

		File.WriteAllText( Path.Combine( pack, TexturePack.ManifestFileName ), "{\"Format\": 99}" );
		Assert.IsNull( TexturePack.Open( pack, diagnostics ) );
		StringAssert.Contains( diagnostics.Last(), "expected 1" );
	}

	[TestMethod]
	public void EnhancedTexturesIsStoredAndNeedsARestart()
	{
		var directory = TemporaryDirectory();
		var path = Path.Combine( directory, GraphicsSettings.FileName );
		var service = new GraphicsSettingsService( _ => null, GraphicsSettings.Default, path, () => CompatibilityFlags.Original );
		Assert.IsFalse( service.Current.EnhancedTextures, "off by default" );
		Assert.IsFalse( service.RestartRequired );
		service.Apply( service.Current with { EnhancedTextures = true } );
		Assert.IsTrue( service.RestartRequired );
		Assert.IsTrue( GraphicsSettings.Load( path, new List<string>() ).EnhancedTextures, "persisted in graphics.json" );
	}

	[TestMethod]
	public void OptionsRowTogglesOnlyWithAPackAndAsksForARestart()
	{
		var strings = OriginalUiTests.FakeStrings();
		var graphics = new GraphicsSettingsService( _ => null, GraphicsSettings.Default, null, () => CompatibilityFlags.Original );
		OptionsServices Services( bool available, bool withGraphics = true ) => new()
		{
			Display = new StubDisplaySettings( DisplaySettings.Default ), Options = new GameOptions(), Languages = new[] { "English" }, CurrentLanguage = "English",
			Graphics = withGraphics ? graphics : null, TexturePackAvailable = available
		};

		(UiButton Button, Func<string> Value) Row( UiScreenStack stack, UiScreen main )
		{
			main.Find( "openTpw" )!.Activate();
			var page = stack.Top!;
			return ((UiButton)page.Find( "enhancedTextures" )!, () => ((UiLabel)page.Find( "enhancedTexturesLabel" )!).Text()["Enhanced textures:".Length..]);
		}

		var stack = new UiScreenStack();
		var screen = OptionsScreen.Create( stack, strings, Services( false ), () => { } );
		stack.Push( screen );
		var (row, value) = Row( stack, screen );
		Assert.AreEqual( " No pack built", value() );
		row.Activate();
		Assert.AreEqual( " No pack built", value(), "nothing to turn on without a pack" );

		stack = new UiScreenStack();
		screen = OptionsScreen.Create( stack, strings, Services( true ), () => { } );
		stack.Push( screen );
		(row, value) = Row( stack, screen );
		Assert.AreEqual( strings[UIStrings.No], value() );
		row.Activate();
		Assert.AreEqual( strings[UIStrings.Yes], value() );
		stack.Pop();
		Assert.IsFalse( graphics.Current.EnhancedTextures, "nothing applied before OK" );
		screen.Find( "ok" )!.Activate();
		Assert.IsTrue( graphics.Current.EnhancedTextures );
		Assert.AreEqual( "restart", stack.Top!.Name );

		var hidden = new UiScreenStack();
		var hiddenMain = OptionsScreen.Create( hidden, strings, Services( true, withGraphics: false ), () => { } );
		hidden.Push( hiddenMain );
		hiddenMain.Find( "openTpw" )!.Activate();
		Assert.IsNull( hidden.Top!.Find( "enhancedTextures" ), "hidden without graphics settings" );

		// Changing the window size as well: the restart notice follows the display confirmation.
		graphics.Apply( graphics.Current with { EnhancedTextures = false } );
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720 } );
		var combined = new UiScreenStack();
		var closed = 0;
		screen = OptionsScreen.Create( combined, strings, new OptionsServices
		{
			Display = display, Options = new GameOptions(), Languages = new[] { "English" }, CurrentLanguage = "English",
			Graphics = graphics, TexturePackAvailable = true
		}, () => closed++ );
		combined.Push( screen );
		Row( combined, screen ).Button.Activate();
		combined.Pop();
		((UiSlider)screen.Find( "resolution" )!).Adjust( 1 );
		screen.Find( "ok" )!.Activate();
		Assert.AreNotEqual( "restart", combined.Top!.Name, "the display confirmation comes first" );
		combined.Top!.Find( "choice0" )!.Activate();
		Assert.AreEqual( "restart", combined.Top!.Name, "keeping the new size then shows the restart notice" );
		combined.Top!.Find( "choice0" )!.Activate();
		Assert.AreEqual( 1, closed );
	}
}
