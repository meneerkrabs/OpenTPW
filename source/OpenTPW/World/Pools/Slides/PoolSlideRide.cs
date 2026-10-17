namespace OpenTPW;

/// <summary>
/// The numbers of a slide ride (docs/POOLS.md, "Water slides"), all OpenTPW's own: the original game has no pools. A park turn is about a quarter of a second (the swim session's turn).
/// </summary>
// [EXT:pools-slides] OpenTPW's own slide ride; the original game has no pools
public static class PoolSlideSettings
{
	/// <summary>Gravity along the track: the pull each turn is the slope in thousandths times this over a thousand, in 1/1024 cell per turn (<see cref="PoolSlideRide.Step"/>).</summary>
	public const int Acceleration = 100;
	/// <summary>Drag: a rider loses 1/8 of its speed each turn, so its speed tends to eight times the pull of the slope.</summary>
	public const int DragShift = 3;
	/// <summary>The speed a rider leaves the tower with, 1/1024 cell per turn.</summary>
	public const int LaunchSpeed = 64;
	/// <summary>The least speed a rider keeps, so a gentle slide never stalls.</summary>
	public const int MinimumSpeed = 24;
	/// <summary>The next rider leaves the tower when the one before is this far down the track (1/1024 cell): a cell and a half.</summary>
	public const int Gap = PoolSlide.Unit * 3 / 2;
	/// <summary>Park turns a swimmer needs to climb the tower's stairs (12 turns, about 3 seconds).</summary>
	public const int ClimbTurns = 12;
	/// <summary>Park turns the splash lasts before the rider swims on (4 turns, about 1 second).</summary>
	public const int SplashTurns = 4;
	/// <summary>The most turns a swimmer takes to swim to the foot of the stairs before it gives up and swims on.</summary>
	public const int ApproachLimitTurns = 160;
	/// <summary>The most turns a rider waits in the tower before it gives up and climbs down.</summary>
	public const int WaitLimitTurns = 240;
}

/// <summary>The physics of a rider on a slide: integers only, deterministic, from the slide's slope.</summary>
// [EXT:pools-slides] OpenTPW's own slide ride; the original game has no pools
public static class PoolSlideRide
{
	/// <summary>
	/// One park turn along the track. The pull is the slope in thousandths times <see cref="PoolSlideSettings.Acceleration"/> over a thousand; drag takes an eighth of the speed, so the speed settles
	/// at about eight times the pull: a steep slide (0.8) runs at 640 (0.6 cells a turn), a gentle one (0.2) at 160. Returns whether the rider has reached the end of the track.
	/// </summary>
	public static bool Step( PoolSlide slide, ref int position, ref int speed )
	{
		var pull = slide.SlopePermille * PoolSlideSettings.Acceleration / 1000;
		speed += pull - (speed >> PoolSlideSettings.DragShift);
		if ( speed < PoolSlideSettings.MinimumSpeed )
			speed = PoolSlideSettings.MinimumSpeed;
		position += speed;
		if ( position >= slide.Length )
		{
			position = slide.Length;
			return true;
		}
		return false;
	}
}
