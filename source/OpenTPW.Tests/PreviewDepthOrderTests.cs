using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.Mods;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// The buy window's preview against a depth-tested reference, over every preview model of the four themes and of the official extensions, at sixteen turns
/// (with the original game: OPENTPW_GAME_PATH).
/// </summary>
[TestClass]
public class PreviewDepthOrderTests
{
	private static readonly string[] Themes = { "jungle", "hallow", "fantasy", "space" };
	private BaseFileSystem? previous;
	private IDisposable? extensions;

	[TestInitialize]
	public void Initialize()
	{
		var data = StringTableTests.OriginalDataDirectory();
		if ( !Directory.Exists( Path.Combine( data, "levels" ) ) )
			Assert.Inconclusive( "Set OPENTPW_GAME_PATH to the original game for the preview corpus." );
		Log ??= new();
		previous = FileSystem;
		FileSystem = new BaseFileSystem( data );
		FileSystem.RegisterArchiveHandler<WadArchive>( ".wad" );
		var settings = new ModSettings { Enabled = OfficialExtensions.All.ToDictionary( extension => extension.Id, _ => true ) };
		extensions = ModContentRoots.Use( ModSet.Resolve( Array.Empty<DiscoveredMod>(), settings, null, GameEdition.ThemeParkWorld, OfficialExtensions.All ), FileSystem );
	}

	[TestCleanup]
	public void Cleanup()
	{
		extensions?.Dispose();
		if ( previous != null )
			FileSystem = previous;
	}

	private static IEnumerable<(string Label, ObjectCatalogEntry Entry, PreviewIcon Icon)> Previews()
	{
		foreach ( var theme in Themes )
			foreach ( var entry in ObjectCatalog.Load( theme ).Entries )
			{
				var path = entry.PreviewModelPath ?? entry.ModelPath;
				if ( path == null )
					continue;
				PreviewIcon icon;
				try
				{
					icon = new PreviewIcon( ObjectAssets.LoadModel( entry.FileSystem, path ), _ => null );
				}
				catch ( Exception exception ) when ( exception is IOException or InvalidDataException )
				{
					continue;
				}
				yield return ($"{theme} {entry.InfoId} {path}", entry, icon);
			}
	}

	[TestMethod]
	public void EveryBuyPreviewShowsTheNearestKeptFaceAtEveryPixelAtSixteenTurns()
	{
		// The image the renderer draws (its triangles, the depths mapped into the depth buffer's range and held as 32-bit floats) against a reference that
		// depth tests the same kept triangles in doubles with the original's depths: every pixel must show the same triangle, except where two triangles meet
		// at the same depth (coplanar faces, which no depth buffer orders). The painter's order the preview used before (each face by its centroid's depth, far to
		// near) is measured on the same faces for comparison.
		var canvasScale = 0.5f;
		var frame = new UiRect( 437 * canvasScale, 202 * canvasScale, 360 * canvasScale, 363 * canvasScale );
		var (models, extensionModels, images, pixels, errors, painterPixels, painterModels) = (0, 0, 0, 0L, 0L, 0L, 0);
		var failures = new List<string>();
		foreach ( var (label, entry, icon) in Previews() )
		{
			models++;
			if ( entry.IsExternal )
				extensionModels++;
			var painterWrong = false;
			for ( var step = 0; step < 16; step++ )
			{
				var arrangement = icon.ArrangeBuyPreview( frame, step * MathF.PI / 8, canvasScale );
				var image = icon.ToDepthImage( arrangement );
				images++;
				var kept = arrangement.Triangles.Where( triangle => triangle.FacesViewer ).ToArray();
				var origin = new System.Numerics.Vector3( image.Rect.X, image.Rect.Y, 0 );
				var reference = new DepthRaster( image.Width, image.Height );
				foreach ( var triangle in kept )
					reference.Draw( triangle.Index, triangle.A - origin, triangle.B - origin, triangle.C - origin, single: false );
				var drawn = new DepthRaster( image.Width, image.Height );
				var vertices = image.Draws.SelectMany( draw => draw.Vertices ).ToArray();
				// the image's vertices are the kept triangles grouped by texture; their ids follow from matching corners
				var byCorners = kept.GroupBy( triangle => (triangle.A.X - origin.X, triangle.A.Y - origin.Y, triangle.B.X - origin.X, triangle.B.Y - origin.Y, triangle.C.X - origin.X, triangle.C.Y - origin.Y) ).ToDictionary( group => group.Key, group => group.Select( triangle => triangle.Index ).ToList() );
				for ( var index = 0; index + 2 < vertices.Length; index += 3 )
				{
					var (a, b, c) = (vertices[index].Position, vertices[index + 1].Position, vertices[index + 2].Position);
					// triangles with the same corners (duplicated faces) take their ids in the model's order, as the image keeps them
					var candidates = byCorners[(a.X, a.Y, b.X, b.Y, c.X, c.Y)];
					drawn.Draw( candidates[0], a, b, c, single: true );
					candidates.RemoveAt( 0 );
				}
				var painter = new DepthRaster( image.Width, image.Height );
				foreach ( var triangle in kept.OrderByDescending( triangle => (triangle.A.Z + triangle.B.Z + triangle.C.Z) / 3 ) )
					painter.Draw( triangle.Index, triangle.A - origin, triangle.B - origin, triangle.C - origin, single: false, test: false );
				var all = arrangement.Triangles.ToDictionary( triangle => triangle.Index );
				var tolerance = 1e-5 * (arrangement.FarDepth - arrangement.NearDepth);
				for ( var pixel = 0; pixel < reference.Winner.Length; pixel++ )
				{
					var expected = reference.Winner[pixel];
					if ( expected < 0 )
						continue;
					pixels++;
					var actual = drawn.Winner[pixel];
					if ( actual != expected && !SameDepth( all, actual, expected, pixel, image.Width, origin, tolerance ) )
					{
						errors++;
						if ( failures.Count < 20 )
							failures.Add( $"{label} at {step} x 22.5 degrees, pixel {pixel % image.Width},{pixel / image.Width}: {all.GetValueOrDefault( actual ).MaterialName} over {all[expected].MaterialName}" );
					}
					if ( painter.Winner[pixel] != expected && !SameDepth( all, painter.Winner[pixel], expected, pixel, image.Width, origin, tolerance ) )
					{
						painterPixels++;
						painterWrong = true;
					}
				}
			}
			if ( painterWrong )
				painterModels++;
		}
		Console.WriteLine( $"{models} preview models ({extensionModels} from the extensions), {images} images, {pixels} covered pixels; depth-tested image: {errors} pixels differ from the reference; painter's order: {painterPixels} pixels ({100.0 * painterPixels / Math.Max( 1, pixels ):0.00}%) in {painterModels} models" );
		Assert.IsTrue( models >= 274, $"the corpus has every original preview model ({models})" );
		Assert.IsTrue( extensionModels > 0, "the corpus has the extensions' preview models" );
		Assert.AreEqual( 0L, errors, string.Join( "\n", failures ) );
	}

	/// <summary>Whether triangles <paramref name="first"/> and <paramref name="second"/> lie at the same depth at the pixel (coplanar there): the reference cannot order them either.</summary>
	private static bool SameDepth( Dictionary<int, PreviewIcon.ArrangedTriangle> all, int first, int second, int pixel, int width, System.Numerics.Vector3 origin, double tolerance )
	{
		if ( first < 0 || second < 0 )
			return false;
		var point = new System.Numerics.Vector2( origin.X + pixel % width + 0.5f, origin.Y + pixel / width + 0.5f );
		return PreviewDepthRaster.DepthAt( all[first], point ) is { } a && PreviewDepthRaster.DepthAt( all[second], point ) is { } b && Math.Abs( a - b ) <= tolerance;
	}

	[TestMethod]
	public void ThePreviewsWithAnAnimationFiveClipPlayItBelowTheirRoot()
	{
		// tp.exe 0x4689f0 starts animation 5, variant 0, looping, on every new preview; the clip moves the nodes below the root (the root frame takes the
		// preview's matrix every frame, 0x468e50)
		var frame = new UiRect( 437, 202, 360, 363 );
		var (withClip, moved) = (0, 0);
		foreach ( var (label, entry, _) in Previews() )
		{
			var path = entry.PreviewModelPath ?? entry.ModelPath!;
			if ( PreviewIcon.BuyClipMember( path, entry.Models ) is not { } member )
				continue;
			var clip = ObjectAssets.LoadModel( entry.FileSystem, member.Path ).Clip;
			if ( clip == null )
				continue;
			withClip++;
			var icon = new PreviewIcon( ObjectAssets.LoadModel( entry.FileSystem, path ), _ => null, buyClip: clip, buyClipName: member.Name );
			Assert.IsTrue( icon.HasBuyClip, label );
			var rest = icon.ArrangeBuyPreview( frame, 0, 1 );
			icon.StartBuyAnimation( 10 );
			var batch = new UiBatch();
			var changed = false;
			for ( var step = 1; step <= 8 && !changed; step++ )
			{
				icon.DrawBuyPreview( batch, frame, 0, 1, 10 + step * 0.25 );
				var posed = icon.ArrangeBuyPreview( frame, 0, 1, animated: true );
				changed = posed.Triangles.Count != rest.Triangles.Count || posed.Triangles.Zip( rest.Triangles ).Any( pair => (pair.First.A - pair.Second.A).Length() > 0.01f || pair.First.UvA != pair.Second.UvA );
			}
			if ( changed )
				moved++;
		}
		Console.WriteLine( $"{withClip} previews have an animation 5 clip; {moved} of them change within two seconds" );
		Assert.IsTrue( withClip > 0 && moved > 0, $"{withClip} with a clip, {moved} moved" );
	}

	[TestMethod]
	public void TheSpaceRacersAndBlobulatorGroundPlatesShowAtEveryTurnAndEveryOtherFaceKeepsTheOriginalsTest()
	{
		// [EXT:preview-ground-plates]: the PURPLE_BASE plates face down, which tp.exe's face test (0x581320) drops seen from above; the preview draws them
		// at the user's request, and only them
		var frame = new UiRect( 437, 202, 360, 363 );
		var plated = new System.Collections.Generic.HashSet<int>();
		var withMaterial = new System.Collections.Generic.HashSet<int>();
		foreach ( var (label, entry, icon) in Previews() )
		{
			for ( var step = 0; step < 16; step++ )
			{
				var arrangement = icon.ArrangeBuyPreview( frame, step * MathF.PI / 8, 1 );
				foreach ( var triangle in arrangement.Triangles )
				{
					var clockwise = (triangle.B.X - triangle.A.X) * (triangle.C.Y - triangle.A.Y) - (triangle.C.X - triangle.A.X) * (triangle.B.Y - triangle.A.Y) > 0;
					if ( PreviewIcon.IsShownGroundPlate( triangle.MaterialName ) )
					{
						withMaterial.Add( entry.InfoId );
						Assert.IsTrue( triangle.FacesViewer, $"{label} at {step} x 22.5 degrees: the plate is drawn" );
						// a plate that faces up passes the face test anyway (the preview looks from above): the exception changes only the downward plates
						if ( triangle.ModelNormal.Y > 0 )
							Assert.IsTrue( clockwise, $"{label} at {step} x 22.5 degrees: an upward plate passes the original's face test" );
						else
						{
							Assert.IsFalse( clockwise, $"{label} at {step} x 22.5 degrees: the original's face test drops the downward plate" );
							plated.Add( entry.InfoId );
						}
					}
					else
						Assert.AreEqual( clockwise, triangle.FacesViewer, $"{label} at {step} x 22.5 degrees, triangle {triangle.Index} ({triangle.MaterialName}): the original's face test" );
				}
				if ( !plated.Contains( entry.InfoId ) )
					continue;
				// the plate shows: it is the nearest kept face at some pixels of the image
				var image = icon.ToDepthImage( arrangement );
				var origin = new System.Numerics.Vector3( image.Rect.X, image.Rect.Y, 0 );
				var raster = new DepthRaster( image.Width, image.Height );
				foreach ( var triangle in arrangement.Triangles.Where( triangle => triangle.FacesViewer ) )
					raster.Draw( triangle.Index, triangle.A - origin, triangle.B - origin, triangle.C - origin, single: false );
				var plates = arrangement.Triangles.Where( triangle => PreviewIcon.IsShownGroundPlate( triangle.MaterialName ) && triangle.ModelNormal.Y < 0 ).Select( triangle => triangle.Index ).ToHashSet();
				Assert.IsTrue( raster.Winner.Count( plates.Contains ) > image.Width * image.Height / 20, $"{label} at {step} x 22.5 degrees: the plate shows" );
			}
		}
		Console.WriteLine( $"previews with the plate material: {string.Join( ", ", withMaterial.Order() )}; with a downward plate: {string.Join( ", ", plated.Order() )}" );
		CollectionAssert.AreEquivalent( new[] { 3150, 3160 }, plated.ToArray(), "only the Space Racers and The Blobulator previews have a downward plate" );
	}
}
