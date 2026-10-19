using OpenTPW.UI.Original;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW.Hud;

/// <summary>
/// Build-menu icon from an object's original preview model (<c>P&lt;name&gt;.MD2</c>, see
/// <c>Info.PreviewAnimType</c>/<c>PreviewAnimNum</c> in Rides.sam). The original shows these previews
/// turning in the menu (tp.exe 0x486410 → 0x4689f0, updated by 0x468e50); OpenTPW projects the model orthographically on the CPU
/// with painter-sorted triangles, which is an approximation of the original 3D draw. The original draws both faces of every triangle
/// (its only Direct3D cull-mode write is D3DCULL_NONE, 0x566957) and depth tests them; without a depth buffer, the back faces (turned
/// away from the viewer) are drawn first and the front faces over them, so a back face never covers the front face over it (a plain
/// depth sort let the slopes' undersides split the winter kiosk's roof along its diagonals) and a single-sided face seen from behind,
/// such as the ground plates of the space go-karts and water ride previews, is still drawn.
/// </summary>
// [EXT:preview-icon-cpu-projection] the icon is drawn by an orthographic CPU projection with painter sorting (back faces first, then front faces, each back to front), the model centred in its frame, not through the engine's scene renderer; matching the original's 3D draw would tie the HUD to the world renderer
// [APPROX:UI-026] the original draws the preview object through its 3D object pipeline (a ride object whose world matrix 0x468e50 rebuilds each frame with the x column scaled by 0.75 and the depth column by 0.1; its camera is not read) and starts its animation 5 (0x4732a0)
public sealed class PreviewIcon
{
	private readonly List<(NVector3 A, NVector3 B, NVector3 C, NVector2 UvA, NVector2 UvB, NVector2 UvC, UiTexture? Texture, string MaterialName)> triangles = new();
	private readonly NVector3 center;
	private readonly float radius;
	private readonly NVector3 minimum, maximum;

	/// <param name="resolveTexture">Game image path for a material name (null: untextured).</param>
	/// <param name="resolveRuntimeTexture">Textures the game makes at run time, e.g. the object's <c>sign1</c>/<c>sign2</c> text; asked first, null means "not one".</param>
	public PreviewIcon( ModelFile model, Func<string, string?> resolveTexture, Func<string, UiTexture?>? resolveRuntimeTexture = null )
	{
		var min = new NVector3( float.MaxValue );
		var max = new NVector3( float.MinValue );
		foreach ( var mesh in model.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Materials.Length == 0 || mesh.TexCoords.Length < mesh.Vertices.Length )
				continue;
			var world = OriginalTerrain.GetWorldMatrix( model, mesh.NodeIndex );
			var points = mesh.Vertices.Select( vertex => NVector3.Transform( vertex.Position.GetSystemVector3(), world ) ).ToArray();
			var textures = new UiTexture?[mesh.Materials.Length];
			var resolved = new bool[mesh.Materials.Length];
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
				var materialIndex = (int)Math.Min( mesh.Vertices[a].TextureIndex, (uint)mesh.Materials.Length - 1 );
				if ( !resolved[materialIndex] )
				{
					var material = mesh.Materials[materialIndex];
					textures[materialIndex] = material.TextureIndex < 0 ? null : resolveRuntimeTexture?.Invoke( material.Name )
						?? (resolveTexture( material.Name ) is { } path ? UiTexture.Image( path ) : null);
					resolved[materialIndex] = true;
				}
				triangles.Add( (points[a], points[b], points[c], Uv( mesh, a ), Uv( mesh, b ), Uv( mesh, c ), textures[materialIndex], mesh.Materials[materialIndex].Name) );
			}
		}
		if ( triangles.Count == 0 )
			throw new InvalidDataException( "Preview model has no textured geometry." );
		center = (min + max) / 2;
		radius = Math.Max( 1e-3f, (max - min).Length() / 2 );
		minimum = min;
		maximum = max;
	}

	public int TriangleCount => triangles.Count;
	/// <summary>Triangles drawn with a texture the game makes at run time (the sign faces).</summary>
	public int RuntimeTextureTriangleCount => triangles.Count( triangle => triangle.Texture?.Rendered != null );

	private static NVector2 Uv( ModelFile.Mesh mesh, int index ) => new( mesh.TexCoords[index].X, 1 - mesh.TexCoords[index].Y );

	/// <summary>Adds the model turned by <paramref name="yaw"/> radians, fitted into <paramref name="rect"/>.</summary>
	public void Draw( UiBatch batch, UiRect rect, float yaw ) =>
		Draw( batch, rect, yaw, 30f * MathF.PI / 180, Math.Min( rect.Width, rect.Height ) / (radius * 2) );

	/// <summary>The buy window's preview tilt: 45 degrees (tp.exe 0x468e50 tilts by the sine-table entries of ±π/4, 0x6fe7e4 / 0x6fe7e8, through 0x46f240).</summary>
	public const float BuyTilt = MathF.PI / 4;
	/// <summary>The buy window's preview turn in radians per second: 0.0004 a millisecond (tp.exe 0x46908a, 0x6fe804).</summary>
	public const float BuyTurnRate = 0.4f;

	/// <summary>
	/// The buy window's scale for a model of bounds <paramref name="min"/>..<paramref name="max"/> in a frame of <paramref name="width"/> x <paramref name="height"/>:
	/// two thirds of the frame's normalized width over the half-diagonal of the model's ground footprint, or the frame's height over the distance from the
	/// footprint's low corner on the ground to the bounds' high corner, whichever is smaller.
	/// </summary>
	// [BIN:TPW-EXE:0x4689f0 buy preview fit] the preview object takes min(width x 2/3 / sqrt((dx/2)^2 + (dz/2)^2), height / |max - (min x, 0, min z)|) in the
	// normalized screen units of 0x486410 (x over 1024, y over 768 canvas units) and later squeezes x by 0.75 (0x469067), so in canvas units the width term is
	// width / 2 over the half-diagonal
	public static float BuyScale( float width, float height, NVector3 min, NVector3 max )
	{
		var halfDiagonal = MathF.Sqrt( MathF.Pow( (max.X - min.X) / 2, 2 ) + MathF.Pow( (max.Z - min.Z) / 2, 2 ) );
		var reach = (max - new NVector3( min.X, 0, min.Z )).Length();
		return Math.Min( width / 2 / Math.Max( 1e-3f, halfDiagonal ), height / Math.Max( 1e-3f, reach ) );
	}

	/// <summary>Adds the model as the buy window shows it: turned by <paramref name="yaw"/>, tilted 45 degrees and fitted to the frame <paramref name="rect"/> by the original's rule.</summary>
	public void DrawBuyPreview( UiBatch batch, UiRect rect, float yaw ) => Draw( batch, rect, yaw, BuyTilt, BuyScale( rect.Width, rect.Height, minimum, maximum ) );

	private void Draw( UiBatch batch, UiRect rect, float yaw, float tilt, float scale )
	{
		foreach ( var triangle in Arrange( rect, yaw, tilt, scale ) )
		{
			var source = triangles[triangle.Index];
			var texture = source.Texture ?? UiTexture.Solid;
			var color = Veldrid.RgbaByte.White;
			batch.AddTriangle( texture, new UiVertex( triangle.A, source.UvA, color ), new UiVertex( triangle.B, source.UvB, color ), new UiVertex( triangle.C, source.UvC, color ) );
		}
	}

	/// <summary>
	/// One triangle as the preview draws it: its index in the model's triangle list, its material, its corners in canvas units (y down), its depth (larger is nearer),
	/// its outward normal in model space (Y up, not normalized) and whether it faces the viewer.
	/// </summary>
	internal readonly record struct ArrangedTriangle( int Index, string MaterialName, NVector2 A, NVector2 B, NVector2 C, float Depth, NVector3 ModelNormal, bool FacesViewer );

	/// <summary>The triangles the buy window's preview draws, in drawing order (back faces, then front faces, each back to front), at <paramref name="yaw"/> in the frame <paramref name="rect"/>.</summary>
	internal IReadOnlyList<ArrangedTriangle> ArrangeBuyPreview( UiRect rect, float yaw ) => Arrange( rect, yaw, BuyTilt, BuyScale( rect.Width, rect.Height, minimum, maximum ) );

	/// <summary>Turns, tilts and projects the model and orders the triangles: the back faces back to front, then the front faces back to front.</summary>
	private List<ArrangedTriangle> Arrange( UiRect rect, float yaw, float tilt, float scale )
	{
		var rotation = NMatrix4x4.CreateTranslation( -center ) * NMatrix4x4.CreateRotationY( yaw ) * NMatrix4x4.CreateRotationX( tilt );
		var middle = rect.Center;
		NVector2 Project( NVector3 turned ) => new( middle.X + turned.X * scale, middle.Y - turned.Y * scale );
		var arranged = new List<ArrangedTriangle>( triangles.Count );
		for ( var index = 0; index < triangles.Count; index++ )
		{
			var triangle = triangles[index];
			var a = NVector3.Transform( triangle.A, rotation );
			var b = NVector3.Transform( triangle.B, rotation );
			var c = NVector3.Transform( triangle.C, rotation );
			// The model's outward normal is (B - A) x (C - A) (ModelFile.CalculateNormals: 297,540 of 301,629 original corner normals agree with it); the
			// viewer looks down -Z (the sort draws larger Z last, i.e. nearer), so a face whose normal has a +Z part faces the viewer. On a closed shape every
			// back face lies behind the front faces that cover it, so drawing the back faces first stands in for the original's depth test.
			arranged.Add( new ArrangedTriangle( index, triangle.MaterialName, Project( a ), Project( b ), Project( c ), (a.Z + b.Z + c.Z) / 3,
				NVector3.Cross( triangle.B - triangle.A, triangle.C - triangle.A ), NVector3.Cross( b - a, c - a ).Z > 0 ) );
		}
		arranged.Sort( ( left, right ) => left.FacesViewer != right.FacesViewer ? left.FacesViewer.CompareTo( right.FacesViewer )
			: left.Depth != right.Depth ? left.Depth.CompareTo( right.Depth ) : left.Index.CompareTo( right.Index ) );
		return arranged;
	}
}
