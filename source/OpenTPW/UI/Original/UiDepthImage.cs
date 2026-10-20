using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.UI.Original;

/// <summary>A vertex of a depth-tested image: its position in the image's pixels (y down) with its depth in (0, 1), smaller nearer, and its texture coordinates.</summary>
public readonly record struct UiDepthVertex( NVector3 Position, NVector2 TexCoords );

/// <summary>The triangles of a depth-tested image that share one texture.</summary>
public sealed record UiDepthDraw( UiTexture Texture, IReadOnlyList<UiDepthVertex> Vertices );

/// <summary>
/// An image drawn with a depth buffer (the turning model previews): <see cref="UiRenderer"/> draws its triangles into the top-left
/// <see cref="Width"/> x <see cref="Height"/> pixels of a target at least that large, cleared to transparent and to the far depth, with the depth test LESSEQUAL and depth
/// writes on, texels below half opacity discarded; the batch then draws that target as a quad at <see cref="Rect"/>.
/// </summary>
public sealed class UiDepthImage( UiRect rect, int width, int height, IReadOnlyList<UiDepthDraw> draws )
{
	public UiRect Rect { get; } = rect;
	public int Width { get; } = width;
	public int Height { get; } = height;
	public IReadOnlyList<UiDepthDraw> Draws { get; } = draws;
	public int TriangleCount
	{
		get
		{
			var count = 0;
			for ( var index = 0; index < Draws.Count; index++ )
				count += Draws[index].Vertices.Count;
			return count / 3;
		}
	}
}
