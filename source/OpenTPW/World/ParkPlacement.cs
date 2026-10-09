using System;
using System.Numerics;

namespace OpenTPW;

public static class ParkPlacement
{
	public const float TileSize = 2;
	public const int TileCount = 32;
	public const float MinimumCoordinate = -TileCount / 2 * TileSize - TileSize / 2;
	public const float MaximumCoordinate = MinimumCoordinate + TileCount * TileSize;

	public static bool TryGetPosition( Vector2 mousePosition, Vector2 viewportSize, Matrix4x4 view, Matrix4x4 projection, out Vector3 position )
	{
		position = Vector3.Zero;
		return TryGetGroundPoint( mousePosition, viewportSize, view, projection, out var intersection ) && TrySnapToCell( intersection, out position );
	}

	/// <summary>Intersection of the mouse ray with the Z = 0 ground plane.</summary>
	public static bool TryGetGroundPoint( Vector2 mousePosition, Vector2 viewportSize, Matrix4x4 view, Matrix4x4 projection, out Vector3 position )
	{
		position = Vector3.Zero;
		if ( !float.IsFinite( viewportSize.X ) || !float.IsFinite( viewportSize.Y ) || viewportSize.X <= 0 || viewportSize.Y <= 0 )
			return false;
		if ( !float.IsFinite( mousePosition.X ) || !float.IsFinite( mousePosition.Y ) || mousePosition.X < 0 || mousePosition.Y < 0 || mousePosition.X > viewportSize.X || mousePosition.Y > viewportSize.Y )
			return false;
		if ( !Matrix4x4.Invert( view * projection, out var inverse ) )
			return false;

		var horizontal = mousePosition.X / viewportSize.X * 2 - 1;
		var vertical = 1 - mousePosition.Y / viewportSize.Y * 2;
		var nearPoint = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( horizontal, vertical, 0, 1 ), inverse );
		var farPoint = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( horizontal, vertical, 1, 1 ), inverse );
		if ( nearPoint.W == 0 || farPoint.W == 0 )
			return false;

		var origin = new Vector3( nearPoint.X / nearPoint.W, nearPoint.Y / nearPoint.W, nearPoint.Z / nearPoint.W );
		var destination = new Vector3( farPoint.X / farPoint.W, farPoint.Y / farPoint.W, farPoint.Z / farPoint.W );
		return TryIntersectGround( origin, destination - origin, out position );
	}

	public static bool TryIntersectGround( Vector3 origin, Vector3 direction, out Vector3 position )
	{
		position = Vector3.Zero;
		if ( !IsFinite( origin ) || !IsFinite( direction ) || direction.Z == 0 )
			return false;

		var distance = -(double)origin.Z / direction.Z;
		if ( distance < 0 )
			return false;

		var intersection = new Vector3( (float)(origin.X + direction.X * distance), (float)(origin.Y + direction.Y * distance), 0 );
		if ( !IsFinite( intersection ) )
			return false;

		position = intersection;
		return true;
	}

	public static bool TrySnapToCell( Vector3 intersection, out Vector3 position )
	{
		position = Vector3.Zero;
		if ( !IsWithinBounds( intersection, 0 ) )
			return false;

		var column = Math.Min( TileCount - 1, (int)MathF.Floor( (intersection.X - MinimumCoordinate) / TileSize ) );
		var row = Math.Min( TileCount - 1, (int)MathF.Floor( (intersection.Y - MinimumCoordinate) / TileSize ) );
		position = new Vector3( MinimumCoordinate + (column + 0.5f) * TileSize, MinimumCoordinate + (row + 0.5f) * TileSize, 0 );
		return true;
	}

	public static bool IsWithinBounds( Vector3 position, float footprintRadius )
	{
		return IsFinite( position ) && position.Z == 0 && float.IsFinite( footprintRadius ) && footprintRadius >= 0
			&& position.X - footprintRadius >= MinimumCoordinate && position.X + footprintRadius <= MaximumCoordinate
			&& position.Y - footprintRadius >= MinimumCoordinate && position.Y + footprintRadius <= MaximumCoordinate;
	}

	private static bool IsFinite( Vector3 position )
	{
		return float.IsFinite( position.X ) && float.IsFinite( position.Y ) && float.IsFinite( position.Z );
	}
}
