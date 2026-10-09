using OpenTPW.UI.Original;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Hud;

/// <summary>
/// Build-menu icon from an object's original preview model (<c>P&lt;name&gt;.MD2</c>, see
/// <c>Info.PreviewAnimType</c>/<c>PreviewAnimNum</c> in Rides.sam). The original shows these previews
/// turning in the menu; OpenTPW projects the model orthographically on the CPU with a fixed
/// 30° tilt and painter-sorted triangles, which is an approximation of the original 3D draw.
/// </summary>
public sealed class PreviewIcon
{
	private readonly List<(NVector3 A, NVector3 B, NVector3 C, NVector2 UvA, NVector2 UvB, NVector2 UvC, string? Texture)> triangles = new();
	private readonly NVector3 center;
	private readonly float radius;

	public PreviewIcon( ModelFile model, Func<string, string?> resolveTexture )
	{
		var min = new NVector3( float.MaxValue );
		var max = new NVector3( float.MinValue );
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Materials.Length == 0 || mesh.TexCoords.Length < mesh.Vertices.Length )
				continue;
			var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex );
			var points = mesh.Vertices.Select( vertex => NVector3.Transform( vertex.Position.GetSystemVector3(), world ) ).ToArray();
			foreach ( var point in points )
			{
				min = NVector3.Min( min, point );
				max = NVector3.Max( max, point );
			}
			for ( var index = 0; index + 2 < mesh.Indices.Length; index += 3 )
			{
				int a = (int)mesh.Indices[index], b = (int)mesh.Indices[index + 1], c = (int)mesh.Indices[index + 2];
				if ( a >= points.Length || b >= points.Length || c >= points.Length )
					continue;
				var material = mesh.Materials[(int)Math.Min( mesh.Vertices[a].TextureIndex, (uint)mesh.Materials.Length - 1 )];
				var texture = material.TextureIndex < 0 ? null : resolveTexture( material.Name );
				triangles.Add( (points[a], points[b], points[c], Uv( mesh, a ), Uv( mesh, b ), Uv( mesh, c ), texture) );
			}
		}
		if ( triangles.Count == 0 )
			throw new InvalidDataException( "Preview model has no textured geometry." );
		center = (min + max) / 2;
		radius = Math.Max( 1e-3f, (max - min).Length() / 2 );
	}

	public int TriangleCount => triangles.Count;

	private static NVector2 Uv( ModelFile.Mesh mesh, int index ) => new( mesh.TexCoords[index].X, 1 - mesh.TexCoords[index].Y );

	/// <summary>Adds the model turned by <paramref name="yaw"/> radians, fitted into <paramref name="rect"/>.</summary>
	public void Draw( UiBatch batch, UiRect rect, float yaw )
	{
		var rotation = NMatrix4x4.CreateTranslation( -center ) * NMatrix4x4.CreateRotationY( yaw ) * NMatrix4x4.CreateRotationX( 30f * MathF.PI / 180 );
		var scale = Math.Min( rect.Width, rect.Height ) / (radius * 2);
		var middle = rect.Center;
		NVector2 Project( NVector3 point, out float depth )
		{
			var turned = NVector3.Transform( point, rotation );
			depth = turned.Z;
			return new NVector2( middle.X + turned.X * scale, middle.Y - turned.Y * scale );
		}
		var projected = triangles.Select( triangle =>
		{
			var a = Project( triangle.A, out var depthA );
			var b = Project( triangle.B, out var depthB );
			var c = Project( triangle.C, out var depthC );
			return (Depth: (depthA + depthB + depthC) / 3, a, b, c, triangle);
		} ).OrderBy( entry => entry.Depth );
		foreach ( var (_, a, b, c, triangle) in projected )
		{
			var texture = triangle.Texture == null ? UiTexture.Solid : UiTexture.Image( triangle.Texture );
			var color = Veldrid.RgbaByte.White;
			batch.AddTriangle( texture, new UiVertex( a, triangle.UvA, color ), new UiVertex( b, triangle.UvB, color ), new UiVertex( c, triangle.UvC, color ) );
		}
	}
}
