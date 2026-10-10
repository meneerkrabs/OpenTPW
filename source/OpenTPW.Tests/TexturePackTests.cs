using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
	public void ActivationNeedsAPackNameAndAUsablePack()
	{
		var root = TemporaryDirectory();
		var pack = Path.Combine( root, "enhanced" );
		var diagnostics = new List<string>();
		TexturePack.Activate( "enhanced", diagnostics, root );
		Assert.IsNull( TexturePack.Directory );
		StringAssert.Contains( diagnostics.Single(), "--build-texture-pack" );

		TexturePackBuilder.Build( new (string, Func<TextureData>)[] { ("levels/a/textures/x.wct", () => Pattern( 32, 32 )) }, pack, new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		TexturePack.Activate( "", diagnostics, root );
		Assert.IsNull( TexturePack.Directory, "off by default" );
		TexturePack.Activate( "enhanced", diagnostics, root );
		Assert.AreEqual( Path.Combine( pack, "textures" ), TexturePack.Directory );
		Assert.AreEqual( Path.Combine( pack, "textures", "levels/a/textures/x.wct.png" ), TexturePack.Find( "/Levels/A/Textures/X.WCT" ) );
		Assert.IsNull( TexturePack.Find( "levels/a/textures/missing.wct" ) );
		TexturePack.Activate( "", diagnostics, root );
		Assert.IsNull( TexturePack.Directory, "switching back to the originals clears the pack" );

		var before = diagnostics.Count;
		TexturePack.Activate( "../enhanced", diagnostics, root );
		Assert.IsNull( TexturePack.Directory, "a name is one plain directory name" );
		Assert.AreEqual( before + 1, diagnostics.Count );

		File.WriteAllText( Path.Combine( pack, TexturePack.ManifestFileName ), "{\"Format\": 99}" );
		Assert.IsNull( TexturePack.Open( pack, diagnostics ) );
		StringAssert.Contains( diagnostics.Last(), "expected 1" );
	}

	[TestMethod]
	public void PacksAreNamedDirectoriesAndInstalledOnesAreListedInCycleOrder()
	{
		var root = TemporaryDirectory();
		Assert.IsTrue( TexturePack.IsValidName( "detailed" ) );
		Assert.IsTrue( TexturePack.IsValidName( "my-pack_2.1" ) );
		foreach ( var bad in new[] { "", ".hidden", "a/b", "..", "a b", ".enhanced.building-1" } )
			Assert.IsFalse( TexturePack.IsValidName( bad ), bad );
		Assert.AreEqual( 0, TexturePack.InstalledPacks( Path.Combine( root, "missing" ) ).Count );
		foreach ( var name in new[] { "zeta", "detailed", "enhanced", "alpha" } )
			TexturePackBuilder.Build( new (string, Func<TextureData>)[] { ("levels/a/textures/x.wct", () => Pattern( 32, 32 )) }, Path.Combine( root, name ), new NearestUpscaler(), new TexturePackBuildOptions(), _ => { } );
		Directory.CreateDirectory( Path.Combine( root, "broken" ) );
		Directory.CreateDirectory( Path.Combine( root, ".enhanced.building-1" ) );
		CollectionAssert.AreEqual( new[] { "enhanced", "detailed", "alpha", "zeta" }, TexturePack.InstalledPacks( root ).ToArray() );
	}

	[TestMethod]
	public void GraphicsSettingsStoreThePackNameAndReadTheOldBoolean()
	{
		var diagnostics = new List<string>();
		Assert.AreEqual( "enhanced", GraphicsSettings.FromJson( "{\"EnhancedTextures\": true}", diagnostics ).TexturePackName, "old true = the enhanced pack" );
		Assert.AreEqual( "", GraphicsSettings.FromJson( "{\"EnhancedTextures\": false}", diagnostics ).TexturePackName );
		Assert.AreEqual( "", GraphicsSettings.FromJson( "{}", diagnostics ).TexturePackName, "off by default" );
		Assert.AreEqual( "detailed", GraphicsSettings.FromJson( "{\"TexturePack\": \"detailed\"}", diagnostics ).TexturePackName );
		Assert.AreEqual( "detailed", GraphicsSettings.FromJson( "{\"TexturePack\": \"detailed\", \"EnhancedTextures\": true}", diagnostics ).TexturePackName, "the new key wins" );
		Assert.AreEqual( "", GraphicsSettings.FromJson( "{\"TexturePack\": null}", diagnostics ).TexturePackName );
		Assert.AreEqual( "", GraphicsSettings.FromJson( "{\"TexturePack\": \"\", \"EnhancedTextures\": true}", diagnostics ).TexturePackName, "an explicit empty TexturePack wins over the old boolean" );
		Assert.AreEqual( 0, diagnostics.Count );
		Assert.AreEqual( "", GraphicsSettings.FromJson( "{\"TexturePack\": \"../x\"}", diagnostics ).TexturePackName );
		Assert.AreEqual( 1, diagnostics.Count );
		var json = GraphicsSettings.FromJson( "{\"EnhancedTextures\": true}", new List<string>() ).ToJson();
		StringAssert.Contains( json, "\"TexturePack\": \"enhanced\"" );
		Assert.IsFalse( json.Contains( "EnhancedTextures" ), "the legacy key is migrated away when saved" );
	}

	[TestMethod]
	public void TexturePackIsStoredAndNeedsNoRestart()
	{
		var directory = TemporaryDirectory();
		var path = Path.Combine( directory, GraphicsSettings.FileName );
		File.WriteAllText( path, "{\"EnhancedTextures\": true}" );
		var service = new GraphicsSettingsService( _ => null, GraphicsSettings.Load( path, new List<string>() ), path, () => CompatibilityFlags.Original );
		Assert.AreEqual( "enhanced", service.Current.TexturePackName );
		service.Apply( service.Current with { TexturePackName = "detailed" } );
		Assert.IsFalse( service.RestartRequired, "a pack switch is applied while the game runs" );
		Assert.AreEqual( "detailed", GraphicsSettings.Load( path, new List<string>() ).TexturePackName, "persisted in graphics.json" );
		StringAssert.Contains( File.ReadAllText( path ), "\"TexturePack\": \"detailed\"" );
	}

	private sealed class FakeSwitch( int total, int perPump ) : ITexturePackSwitch
	{
		public int Done { get; private set; }
		public int Total => total;
		public bool Finished => Done >= total;
		public int Pumps { get; private set; }
		public void Pump()
		{
			Pumps++;
			Done = Math.Min( total, Done + perPump );
		}
	}

	[TestMethod]
	public void OptionsRowCyclesOffThenEveryInstalledPackAndSwitchesAtOk()
	{
		var strings = OriginalUiTests.FakeStrings();
		var graphics = new GraphicsSettingsService( _ => null, GraphicsSettings.Default, null, () => CompatibilityFlags.Original );
		var requested = new List<string>();
		FakeSwitch? running = null;
		OptionsServices Services( string[] packs, bool withGraphics = true, bool withSwitch = true ) => new()
		{
			Display = new StubDisplaySettings( DisplaySettings.Default ), Options = new GameOptions(), Languages = new[] { "English" }, CurrentLanguage = "English",
			Graphics = withGraphics ? graphics : null, TexturePacks = packs,
			BeginTexturePackSwitch = withSwitch ? name => { requested.Add( name ); return running = new FakeSwitch( 5, 2 ); } : null
		};

		(UiButton Button, Func<string> Value) Row( UiScreenStack stack, UiScreen main )
		{
			main.Find( "openTpw" )!.Activate();
			var page = stack.Top!;
			return ((UiButton)page.Find( "enhancedTextures" )!, () => ((UiLabel)page.Find( "enhancedTexturesLabel" )!).Text()["Enhanced textures:".Length..]);
		}

		var stack = new UiScreenStack();
		var screen = OptionsScreen.Create( stack, strings, Services( Array.Empty<string>() ), () => { } );
		stack.Push( screen );
		var (row, value) = Row( stack, screen );
		Assert.AreEqual( " No pack built", value() );
		row.Activate();
		Assert.AreEqual( " No pack built", value(), "nothing to choose without a pack" );

		var closed = 0;
		stack = new UiScreenStack();
		screen = OptionsScreen.Create( stack, strings, Services( new[] { "enhanced", "detailed", "mine" } ), () => closed++ );
		stack.Push( screen );
		(row, value) = Row( stack, screen );
		Assert.AreEqual( " Original", value() );
		var seen = new List<string>();
		for ( var step = 0; step < 4; step++ )
		{
			row.Activate();
			seen.Add( value() );
		}
		CollectionAssert.AreEqual( new[] { " Clean", " Detailed", " mine", " Original" }, seen, "Original, Clean, Detailed, unknown names as they are, back to Off" );
		row.Activate();
		row.Activate();
		Assert.AreEqual( " Detailed", value() );
		stack.Pop();
		Assert.AreEqual( "", graphics.Current.TexturePackName, "nothing applied before OK" );
		Assert.AreEqual( 0, requested.Count );

		screen.Find( "ok" )!.Activate();
		Assert.AreEqual( "detailed", graphics.Current.TexturePackName );
		CollectionAssert.AreEqual( new[] { "detailed" }, requested );
		Assert.AreEqual( "textureSwitch", stack.Top!.Name, "a loading screen follows OK" );
		Assert.IsFalse( stack.Top!.Elements.OfType<UiButton>().Any(), "nothing to click while loading" );
		Assert.IsNull( stack.Top!.Back, "no way out while loading" );
		var loading = stack.Top!;
		Assert.AreEqual( "Loading textures... 0 / 5", ((UiLabel)loading.Find( "message" )!).Text() );
		loading.Updating!( null! );
		Assert.AreEqual( 2, running!.Done );
		Assert.AreSame( loading, stack.Top );
		loading.Updating!( null! );
		loading.Updating!( null! );
		Assert.AreEqual( 0, stack.Screens.Count, "the loading screen removes itself when done" );
		Assert.AreEqual( 1, closed, "and no restart notice is shown for a texture pack" );

		var hidden = new UiScreenStack();
		var hiddenMain = OptionsScreen.Create( hidden, strings, Services( new[] { "enhanced" }, withGraphics: false ), () => { } );
		hidden.Push( hiddenMain );
		hiddenMain.Find( "openTpw" )!.Activate();
		Assert.IsNull( hidden.Top!.Find( "enhancedTextures" ), "hidden without graphics settings" );

		// Back to Off, with a display change as well: the pack switch follows the display confirmation, the restart notice only comes for a language change.
		requested.Clear();
		var display = new StubDisplaySettings( DisplaySettings.Default with { Width = 1280, Height = 720 } );
		var combined = new UiScreenStack();
		closed = 0;
		screen = OptionsScreen.Create( combined, strings, new OptionsServices
		{
			Display = display, Options = new GameOptions(), Languages = new[] { "English" }, CurrentLanguage = "English",
			Graphics = graphics, TexturePacks = new[] { "enhanced", "detailed" }, BeginTexturePackSwitch = name => { requested.Add( name ); return running = new FakeSwitch( 0, 1 ); }
		}, () => closed++ );
		combined.Push( screen );
		Row( combined, screen ).Button.Activate();
		combined.Pop();
		((UiSlider)screen.Find( "resolution" )!).Adjust( 1 );
		screen.Find( "ok" )!.Activate();
		Assert.AreNotEqual( "textureSwitch", combined.Top!.Name, "the display confirmation comes first" );
		combined.Top!.Find( "choice0" )!.Activate();
		Assert.AreEqual( "textureSwitch", combined.Top!.Name );
		combined.Top!.Updating!( null! );
		Assert.AreEqual( 0, combined.Screens.Count );
		Assert.AreEqual( 1, closed );
		CollectionAssert.AreEqual( new[] { "" }, requested, "the switch to the originals" );

		// Without a switch service (tests, tools) the setting is only saved.
		graphics.Apply( graphics.Current with { TexturePackName = "" } );
		var saved = new UiScreenStack();
		var savedMain = OptionsScreen.Create( saved, strings, Services( new[] { "enhanced" }, withSwitch: false ), () => { } );
		saved.Push( savedMain );
		Row( saved, savedMain ).Button.Activate();
		saved.Pop();
		savedMain.Find( "ok" )!.Activate();
		Assert.AreEqual( "enhanced", graphics.Current.TexturePackName );
		Assert.AreEqual( 0, saved.Screens.Count );
	}

	[TestMethod]
	public void PackSwitchReloadsEveryTargetInSlicesAndAnnouncesItOnce()
	{
		var targets = Enumerable.Range( 0, 7 ).Select( index => ($"levels/a/t{index}.wct", TextureFlags.None) ).ToList();
		var applied = new List<string>();
		var announced = 0;
		void Announce() => announced++;
		TexturePackSwitch.PackChanged += Announce;
		try
		{
			var textureSwitch = new TexturePackSwitch( targets, TimeSpan.FromMilliseconds( 50 ),
				path => path.EndsWith( "t3.wct", StringComparison.Ordinal ) ? throw new InvalidDataException( "bad" ) : (new byte[16], 2, 2),
				( path, _, data, width, height ) => { Assert.AreEqual( (2, 2, 16), (width, height, data.Length) ); applied.Add( path ); } );
			Assert.AreEqual( 7, textureSwitch.Total );
			for ( var guard = 0; guard < 2000 && !textureSwitch.Finished; guard++ )
			{
				textureSwitch.Pump();
				Thread.Sleep( 1 );
			}
			Assert.IsTrue( textureSwitch.Finished );
			Assert.AreEqual( 7, textureSwitch.Done );
			Assert.AreEqual( 6, applied.Count, "a texture that cannot be decoded keeps its current pixels" );
			Assert.IsFalse( applied.Contains( "levels/a/t3.wct" ) );
			CollectionAssert.AreEqual( targets.Select( target => target.Item1 ).Where( path => !path.EndsWith( "t3.wct", StringComparison.Ordinal ) ).ToList(), applied, "in order" );
			textureSwitch.Pump();
			Assert.AreEqual( 1, announced, "holders of their own GPU copies are told once" );

			var empty = new TexturePackSwitch( new List<(string, TextureFlags)>(), TimeSpan.FromMilliseconds( 8 ), _ => throw new InvalidOperationException() );
			empty.Pump();
			Assert.IsTrue( empty.Finished );
			Assert.AreEqual( 2, announced );
		}
		finally
		{
			TexturePackSwitch.PackChanged -= Announce;
		}
	}

	[TestMethod]
	public void PackSwitchDoesNothingWhenTheEnvironmentPinsAPack()
	{
		var previous = Environment.GetEnvironmentVariable( "OPENTPW_TEXTURE_PACK" );
		Environment.SetEnvironmentVariable( "OPENTPW_TEXTURE_PACK", Path.GetTempPath() );
		try
		{
			Assert.IsNull( TexturePackSwitch.Begin( "enhanced", new List<string>() ) );
		}
		finally
		{
			Environment.SetEnvironmentVariable( "OPENTPW_TEXTURE_PACK", previous );
		}
	}

	private sealed class OffsetPrepass( int offset ) : IImagePrepass
	{
		public int Calls { get; private set; }
		public List<bool> Wraps { get; } = new();
		public string Model => "fake-offset.onnx";
		public byte[] Process( byte[] rgba, int width, int height, bool wrap )
		{
			lock ( Wraps )
			{
				Calls++;
				Wraps.Add( wrap );
			}
			var result = (byte[])rgba.Clone();
			for ( var i = 0; i < width * height; i++ )
				for ( var c = 0; c < 3; c++ )
					result[i * 4 + c] = (byte)Math.Clamp( rgba[i * 4 + c] + offset, 0, 255 );
			return result;
		}
	}

	[TestMethod]
	public void PrepassRunsOnWorldTexturesOnlyAndIsRecorded()
	{
		var root = TemporaryDirectory();
		var grass = Pattern( 64, 32 );
		TextureData Fresh() => new( grass.Width, grass.Height, (byte[])grass.Data.Clone() );
		var textures = new (string, Func<TextureData>)[]
		{
			("levels/jungle/terrain/textures/grass.wct", Fresh),
			("ui/textures/button.wct", () => Pattern( 64, 64 )),
			("esprites/generic/kids/set0.atlas", () => Pattern( 64, 64 ))
		};
		var log = new List<string>();
		var prepass = new OffsetPrepass( 3 );
		var pack = Path.Combine( root, "clean" );
		var manifest = TexturePackBuilder.Build( textures, pack, new NearestUpscaler( 2 ), new TexturePackBuildOptions(), log.Add, new NearestUpscaler( 2 ), prepass );
		Assert.AreEqual( "fake-offset.onnx", manifest.PrepassModel );
		Assert.AreEqual( 1, prepass.Calls, "world texture only; interface art and sprites are left alone by default" );
		Assert.IsTrue( prepass.Wraps.Single(), "world textures tile" );
		Assert.IsTrue( log.Any( line => line.Contains( "Pre-pass fake-offset.onnx: 1 textures" ) ) );
		var image = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( pack, "textures", "levels/jungle/terrain/textures/grass.wct.png" ) ), ColorComponents.RedGreenBlueAlpha );
		var expected = new OffsetPrepass( 3 ).Process( grass.Data, 64, 32, true );
		CollectionAssert.AreEqual( Nearest( expected, 64, 32, 2 ), image.Data, "the cleaned pixels are what gets upscaled" );
		Assert.AreEqual( "fake-offset.onnx", TexturePack.Open( pack, new List<string>() )!.PrepassModel, "written to pack.json" );

		var withSprites = new OffsetPrepass( 3 );
		TexturePackBuilder.Build( textures, Path.Combine( root, "sprites" ), new NearestUpscaler( 2 ), new TexturePackBuildOptions { PrepassSprites = true }, _ => { }, new NearestUpscaler( 2 ), withSprites );
		Assert.AreEqual( new[] { false, true }.Length, withSprites.Calls );
		CollectionAssert.AreEquivalent( new[] { false, true }, withSprites.Wraps, "sprite atlases are edge-padded, not wrapped" );
	}

	[TestMethod]
	public void HeroArtReplacesTheUpscaleWhenTheAspectMatches()
	{
		var root = TemporaryDirectory();
		var hero = Path.Combine( root, "hero" );
		void Write( string key, int width, int height, byte value )
		{
			var path = Path.Combine( hero, key );
			Directory.CreateDirectory( Path.GetDirectoryName( path )! );
			var pixels = new byte[width * height * 4];
			Array.Fill( pixels, value );
			File.WriteAllBytes( path, PngImage.EncodeRgba( width, height, pixels ) );
		}
		Write( "ui/textures/b_buy.wct.png", 256, 256, 200 );      // 64x64 original: aspect matches, any size
		Write( "ui/textures/wide.wct.png", 100, 50, 10 );         // original is square: skipped
		Write( "ui/textures/nothing.wct.png", 64, 64, 10 );       // not a texture: skipped
		Write( "levels/a/textures/x.wct.png", 96, 96, 77 );       // world texture, replaces the upscale
		var textures = new (string, Func<TextureData>)[]
		{
			("ui/textures/b_buy.wct", () => Pattern( 64, 64 )),
			("ui/textures/wide.wct", () => Pattern( 64, 64 )),
			("levels/a/textures/x.wct", () => Pattern( 32, 32 ))
		};
		var log = new List<string>();
		var pack = Path.Combine( root, "pack" );
		var manifest = TexturePackBuilder.Build( textures, pack, new NearestUpscaler( 2 ), new TexturePackBuildOptions { HeroDirectory = hero }, log.Add, new NearestUpscaler( 2 ) );
		Assert.AreEqual( 2, manifest.HeroTextures );
		Assert.IsTrue( log.Any( line => line.Contains( "wide.wct.png" ) && line.Contains( "aspect" ) ) );
		Assert.IsTrue( log.Any( line => line.Contains( "nothing.wct.png" ) ) );
		ImageResult Read( string key ) => ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( pack, "textures", key ) ), ColorComponents.RedGreenBlueAlpha );
		Assert.AreEqual( (256, 256, (byte)200), (Read( "ui/textures/b_buy.wct.png" ).Width, Read( "ui/textures/b_buy.wct.png" ).Height, Read( "ui/textures/b_buy.wct.png" ).Data[0]) );
		Assert.AreEqual( (96, 96), (Read( "levels/a/textures/x.wct.png" ).Width, Read( "levels/a/textures/x.wct.png" ).Height) );
		Assert.AreEqual( (128, 128), (Read( "ui/textures/wide.wct.png" ).Width, Read( "ui/textures/wide.wct.png" ).Height), "the automatic upscale stays" );

		// A rebuild without hero art starts from the automatic upscale again; merging keeps what is there.
		var merged = TexturePackBuilder.Build( textures.Take( 1 ), pack, new NearestUpscaler( 2 ), new TexturePackBuildOptions { Merge = true }, _ => { }, new NearestUpscaler( 2 ) );
		Assert.AreEqual( 0, merged.HeroTextures );
		Assert.AreEqual( 96, Read( "levels/a/textures/x.wct.png" ).Width, "merge keeps earlier hero textures" );
	}

	[TestMethod]
	public void WithoutAPrepassNothingChanges()
	{
		var root = TemporaryDirectory();
		var grass = Pattern( 64, 32 );
		var textures = new (string, Func<TextureData>)[] { ("levels/jungle/terrain/textures/grass.wct", () => grass) };
		var manifest = TexturePackBuilder.Build( textures, Path.Combine( root, "plain" ), new NearestUpscaler( 2 ), new TexturePackBuildOptions(), _ => { } );
		Assert.AreEqual( "", manifest.PrepassModel );
		var image = ImageResult.FromMemory( File.ReadAllBytes( Path.Combine( root, "plain", "textures", "levels/jungle/terrain/textures/grass.wct.png" ) ), ColorComponents.RedGreenBlueAlpha );
		CollectionAssert.AreEqual( Nearest( grass.Data, 64, 32, 2 ), image.Data );
		var old = System.Text.Json.JsonSerializer.Deserialize<TexturePackManifest>( "{\"Format\":1,\"Scale\":4}" );
		Assert.AreEqual( "", old!.PrepassModel, "packs built before the pre-pass existed still open" );
	}

	/// <summary>Stand-in for the ONNX model: every output texel is the mean of its 3x3 neighbourhood inside the tile, so it depends on context like a real model.</summary>
	private sealed class BoxBlurTile( int size ) : IImageTileModel
	{
		public int TileSize => size;
		public int Runs { get; private set; }
		public float[] Run( float[] tile )
		{
			Runs++;
			var plane = size * size;
			var result = new float[tile.Length];
			for ( var c = 0; c < 3; c++ )
				for ( var y = 0; y < size; y++ )
					for ( var x = 0; x < size; x++ )
					{
						float sum = 0;
						for ( var dy = -1; dy <= 1; dy++ )
							for ( var dx = -1; dx <= 1; dx++ )
								sum += tile[c * plane + Math.Clamp( y + dy, 0, size - 1 ) * size + Math.Clamp( x + dx, 0, size - 1 )];
						result[c * plane + y * size + x] = sum / 9f;
					}
			return result;
		}
	}

	private static byte[] BoxBlurReference( byte[] rgba, int width, int height, bool wrap )
	{
		var result = (byte[])rgba.Clone();
		for ( var y = 0; y < height; y++ )
			for ( var x = 0; x < width; x++ )
				for ( var c = 0; c < 3; c++ )
				{
					float sum = 0;
					for ( var dy = -1; dy <= 1; dy++ )
						for ( var dx = -1; dx <= 1; dx++ )
						{
							var sy = wrap ? (y + dy + height) % height : Math.Clamp( y + dy, 0, height - 1 );
							var sx = wrap ? (x + dx + width) % width : Math.Clamp( x + dx, 0, width - 1 );
							sum += rgba[(sy * width + sx) * 4 + c] / 255f;
						}
					result[(y * width + x) * 4 + c] = (byte)Math.Clamp( (int)MathF.Round( sum / 9f * 255f ), 0, 255 );
				}
		return result;
	}

	[TestMethod]
	public void PrepassTilesPlanCoversEveryTexelWithinTheMargin()
	{
		var small = TiledPrepass.Plan( 128, 256, 32 ).Single();
		Assert.AreEqual( (-64, 0, 128), small, "a small texture is centred in the tile" );
		Assert.AreEqual( 2, TiledPrepass.Plan( 256, 256, 32 ).Count, "a 256 texture needs context from the wrap, so two overlapping tiles" );
		foreach ( var length in new[] { 257, 300, 512, 1000 } )
		{
			var plan = TiledPrepass.Plan( length, 256, 32 );
			var covered = new int[length];
			foreach ( var (origin, from, to) in plan )
				for ( var x = from; x < to; x++ )
				{
					covered[x]++;
					Assert.IsTrue( x - origin >= 32 && x - origin < 224, $"texel {x} of {length} lies in the tile's centre" );
				}
			Assert.IsTrue( covered.All( count => count == 1 ), $"each texel of {length} is written exactly once" );
		}
	}

	[TestMethod]
	public void PrepassKeepsSizeAlphaAndTilesSeamlessly()
	{
		foreach ( var (width, height) in new[] { (32, 32), (128, 64), (256, 256), (300, 40), (520, 300) } )
		{
			var texture = Pattern( width, height, 5 );
			for ( var i = 0; i < width * height; i++ )
				texture.Data[i * 4 + 3] = (byte)(i % 251);
			var model = new BoxBlurTile( 256 );
			var result = new TiledPrepass( model, "fake.onnx" ).Process( texture.Data, width, height, true );
			Assert.AreEqual( texture.Data.Length, result.Length );
			for ( var i = 0; i < width * height; i++ )
				Assert.AreEqual( texture.Data[i * 4 + 3], result[i * 4 + 3], "alpha passes through untouched" );
			var expected = BoxBlurReference( texture.Data, width, height, true );
			var worst = 0;
			for ( var i = 0; i < result.Length; i++ )
				worst = Math.Max( worst, Math.Abs( result[i] - expected[i] ) );
			Assert.IsTrue( worst <= 1, $"{width}x{height}: output equals the wrap-around reference, so the borders are seamless (worst difference {worst})" );
		}
		var tiles = new BoxBlurTile( 256 );
		new TiledPrepass( tiles, "fake.onnx" ).Process( Pattern( 128, 128 ).Data, 128, 128, true );
		Assert.AreEqual( 1, tiles.Runs, "a texture of up to 192 texels takes one model run" );
	}

	[TestMethod]
	public void PrepassRepeatsEdgePixelsWhenNotWrapping()
	{
		var texture = Pattern( 64, 48, 9 );
		var result = new TiledPrepass( new BoxBlurTile( 256 ), "fake.onnx" ).Process( texture.Data, 64, 48, false );
		var expected = BoxBlurReference( texture.Data, 64, 48, false );
		var worst = 0;
		for ( var i = 0; i < result.Length; i++ )
			worst = Math.Max( worst, Math.Abs( result[i] - expected[i] ) );
		Assert.IsTrue( worst <= 1, $"worst difference {worst}" );
	}

	/// <summary>Private data: runs the real ONNX model named by OPENTPW_DEJPG_MODEL (never part of the repository).</summary>
	[TestMethod]
	public void RealDeJpgModelKeepsShapeAndWraps()
	{
		var model = Environment.GetEnvironmentVariable( "OPENTPW_DEJPG_MODEL" );
		if ( string.IsNullOrEmpty( model ) || !File.Exists( model ) )
			Assert.Inconclusive( "Set OPENTPW_DEJPG_MODEL to an ONNX 1x de-artifact model with a fixed 3x256x256 input to run this." );
		using var tiles = new OnnxTileModel( model );
		var prepass = new TiledPrepass( tiles, Path.GetFileName( model ) );
		// A tiling texture with soft gradients and a diagonal edge: any seam at the wrap would show up as a jump.
		const int size = 128;
		var data = new byte[size * size * 4];
		for ( var y = 0; y < size; y++ )
			for ( var x = 0; x < size; x++ )
			{
				var index = (y * size + x) * 4;
				var u = 2 * Math.PI * x / size;
				var v = 2 * Math.PI * y / size;
				data[index] = (byte)(128 + 100 * Math.Sin( u ) * Math.Cos( v ));
				data[index + 1] = (byte)(128 + 100 * Math.Sin( u + v ));
				data[index + 2] = (byte)((x + y) % size < size / 2 ? 60 : 200);
				data[index + 3] = 255;
			}
		var timer = System.Diagnostics.Stopwatch.StartNew();
		var result = prepass.Process( data, size, size, true );
		Console.WriteLine( $"Pre-pass on {tiles.Provider}: first 128x128 texture {timer.ElapsedMilliseconds} ms" );
		timer.Restart();
		for ( var repeat = 0; repeat < 5; repeat++ )
			prepass.Process( data, size, size, true );
		Console.WriteLine( $"Pre-pass on {tiles.Provider}: {timer.ElapsedMilliseconds / 5} ms per 128x128 texture afterwards" );
		timer.Restart();
		System.Threading.Tasks.Parallel.For( 0, 8, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, _ => prepass.Process( data, size, size, true ) );
		Console.WriteLine( $"Pre-pass on {tiles.Provider}: {timer.ElapsedMilliseconds / 8} ms per texture with 4 threads" );
		Assert.AreEqual( data.Length, result.Length );
		long Jump( int x1, int y1, int x2, int y2 )
		{
			long sum = 0;
			for ( var c = 0; c < 3; c++ )
				sum += Math.Abs( result[(y1 * size + x1) * 4 + c] - result[(y2 * size + x2) * 4 + c] );
			return sum;
		}
		long wrapJump = 0, innerJump = 0;
		for ( var i = 0; i < size; i++ )
		{
			wrapJump += Jump( size - 1, i, 0, i ) + Jump( i, size - 1, i, 0 );
			innerJump += Jump( size / 2 - 1, i, size / 2, i ) + Jump( i, size / 2 - 1, i, size / 2 );
		}
		Assert.IsTrue( wrapJump <= innerJump * 1.5 + size, $"no seam: jump across the wrap {wrapJump}, across the middle {innerJump}" );
	}

	[TestMethod]
	public void SmallTilesStillAdvanceAndTinyOnesAreRejected()
	{
		var texture = Pattern( 100, 90, 3 );
		var model = new BoxBlurTile( 64 );
		var result = new TiledPrepass( model, "small.onnx" ).Process( texture.Data, 100, 90, true );
		var expected = BoxBlurReference( texture.Data, 100, 90, true );
		Assert.IsTrue( Enumerable.Range( 0, result.Length ).All( i => Math.Abs( result[i] - expected[i] ) <= 1 ), "a 64 texel tile gets a smaller margin and still covers the image" );
		Assert.ThrowsException<ArgumentException>( () => TiledPrepass.Plan( 100, 64, 32 ) );
		Assert.ThrowsException<ArgumentException>( () => new TiledPrepass( new BoxBlurTile( 4 ), "tiny.onnx" ) );
	}

	[TestMethod]
	public void HeroArtLandsForTexturesTheBuildSkipsAndLoadersThatDieWithTheirArchive()
	{
		var root = TemporaryDirectory();
		var hero = Path.Combine( root, "hero" );
		Directory.CreateDirectory( Path.Combine( hero, "ui/textures" ) );
		File.WriteAllBytes( Path.Combine( hero, "ui/textures/tiny.wct.png" ), PngImage.EncodeRgba( 64, 64, new byte[64 * 64 * 4] ) );
		File.WriteAllBytes( Path.Combine( hero, "ui/textures/UPPER.wct.png" ), PngImage.EncodeRgba( 32, 32, new byte[32 * 32 * 4] ) );
		var closed = false;
		IEnumerable<(string, Func<TextureData>)> Archive()
		{
			// Like EnumerateGameTextures: the loaders only work while the archive is open.
			TextureData Read( TextureData data ) => closed ? throw new ObjectDisposedException( "archive" ) : data;
			yield return ("ui/textures/tiny.wct", () => Read( Pattern( 16, 16 ) ));
			yield return ("ui/textures/upper.wct", () => Read( Pattern( 16, 16 ) ));
			closed = true;
		}
		var log = new List<string>();
		var manifest = TexturePackBuilder.Build( Archive(), Path.Combine( root, "pack" ), new NearestUpscaler( 2 ), new TexturePackBuildOptions { HeroDirectory = hero }, log.Add );
		Assert.AreEqual( 2, manifest.HeroTextures, string.Join( "\n", log ) );
		Assert.IsTrue( File.Exists( Path.Combine( root, "pack", "textures", "ui/textures/tiny.wct.png" ) ) );
		Assert.IsTrue( File.Exists( Path.Combine( root, "pack", "textures", "ui/textures/upper.wct.png" ) ), "hero file names are matched case-insensitively" );
	}

	[TestMethod]
	public void PackSwitchIsOwnedByTheGameLoopAndANewOneReplacesAnUnfinishedOne()
	{
		var first = TexturePackSwitch.Begin( "", new List<string>() )!;
		Assert.AreSame( first, TexturePackSwitch.Current );
		var second = TexturePackSwitch.Begin( "", new List<string>() )!;
		Assert.IsTrue( first.Finished, "the older switch was cancelled" );
		Assert.AreSame( second, TexturePackSwitch.Current );
		TexturePackSwitch.PumpCurrent();
		Assert.IsTrue( second.Finished, "the loop alone completes it, no loading screen needed" );
		Assert.IsNull( TexturePackSwitch.Current );
	}

	[TestMethod]
	public void AScreenIgnoresTheRestOfTheFrameAfterItRemovedItself()
	{
		var stack = new UiScreenStack();
		var screen = new UiScreen( "page" );
		var modal = new UiScreen( "modal" );
		screen.Back = () => { stack.Pop(); };
		screen.Add( new UiButton { Id = "go", Bounds = new UiRect( 0, 0, 100, 100 ), Clicked = () => { stack.Pop(); stack.Push( modal ); } } );
		screen.Focus( screen.Find( "go" ) );
		stack.Push( screen );
		var context = new UiContext( OriginalUiTests.FakeStrings(), null!, new UiModels( name => throw new FileNotFoundException( name ) ) );
		// Accept activates the button (which swaps in the modal) and Back arrives in the same frame.
		stack.Update( context, UiInput.Key( UiKeys.Accept | UiKeys.Back ) );
		Assert.AreSame( modal, stack.Top, "Back must not pop the screen the button just opened" );
		Assert.AreEqual( 1, stack.Screens.Count );
	}
}
