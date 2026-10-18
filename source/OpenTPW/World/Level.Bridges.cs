namespace OpenTPW;

public partial class Level
{
	public PoolBridgeWorld? PoolBridges { get; private set; }
	public PoolBridgeRenderer? PoolBridgeRenderer { get; private set; }

	private void SetupPoolBridges()
	{
		if ( Guests == null ) return;
		PoolBridges = new PoolBridgeWorld( Guests.Grid.Cells, SlideGround,
			( x, y ) => x >= Objects.Grid.Width || y >= Objects.Grid.Height || Objects.FindAt( x, y ) != null || IsReservedByPrototype( x, y ) || PoolSlides?.SlideAt( x, y ) != null,
			IsStoodOn, () => Park?.Economy, () => FeatureGates.Pools );
		if ( PoolTool != null ) PoolTool.Bridges = PoolBridges;
	}

	/// <summary>Only land walkers are lifted. Swim-session guests keep their independent water/activity height.</summary>
	private System.Numerics.Vector3 BridgeGuestFeet( Guest guest )
	{
		var feet = GuestRenderer.CellToEngine( OriginalPark!.Heightfield, guest.DrawX, guest.DrawY );
		if ( !guest.InSwimSession && PoolBridges?.At( (int)MathF.Floor( guest.DrawX ), (int)MathF.Floor( guest.DrawY ) ) is { } bridge ) feet.Z = bridge.HeightAt( guest.DrawX, guest.DrawY );
		return feet;
	}
}
