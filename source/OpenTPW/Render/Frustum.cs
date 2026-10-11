namespace OpenTPW;

/// <summary>View frustum tests in clip space, for skipping draws the GPU would clip away completely.</summary>
internal static class Frustum
{
	/// <summary>
	/// True when every corner of the model-space box (<paramref name="min"/>, <paramref name="max"/>) lies outside the
	/// same clip plane of <paramref name="modelViewProjection"/> (model * view * projection, as the shaders use it),
	/// so no triangle inside the box can reach the screen. The near plane is taken at z = -w, which holds for
	/// both the 0..1 and the -1..1 depth range (WebGL), so the test never culls more than the GPU would clip.
	/// </summary>
	public static bool IsOutside( System.Numerics.Vector3 min, System.Numerics.Vector3 max, System.Numerics.Matrix4x4 modelViewProjection )
	{
		int left = 0, right = 0, bottom = 0, top = 0, near = 0, far = 0;
		for ( var corner = 0; corner < 8; corner++ )
		{
			var point = new System.Numerics.Vector3(
				(corner & 1) != 0 ? max.X : min.X,
				(corner & 2) != 0 ? max.Y : min.Y,
				(corner & 4) != 0 ? max.Z : min.Z );
			var clip = System.Numerics.Vector4.Transform( point, modelViewProjection );
			if ( clip.X < -clip.W ) left++;
			if ( clip.X > clip.W ) right++;
			if ( clip.Y < -clip.W ) bottom++;
			if ( clip.Y > clip.W ) top++;
			if ( clip.Z < -clip.W ) near++;
			if ( clip.Z > clip.W ) far++;
		}
		return left == 8 || right == 8 || bottom == 8 || top == 8 || near == 8 || far == 8;
	}
}
