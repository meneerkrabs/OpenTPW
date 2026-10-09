namespace OpenTPW;

public sealed class RideMotion
{
	public bool IsRunning { get; private set; }
	public double ElapsedSeconds { get; private set; }
	public double Phase { get; private set; }
	public float Height { get; private set; }
	public float MaximumHeight { get; }
	public float CycleDuration { get; }

	public RideMotion( float maximumHeight = 6f, float cycleDuration = 8f )
	{
		if ( !float.IsFinite( maximumHeight ) || maximumHeight <= 0 )
			throw new ArgumentOutOfRangeException( nameof( maximumHeight ) );
		if ( !float.IsFinite( cycleDuration ) || cycleDuration <= 0 )
			throw new ArgumentOutOfRangeException( nameof( cycleDuration ) );

		MaximumHeight = maximumHeight;
		CycleDuration = cycleDuration;
	}

	public void Start()
	{
		IsRunning = true;
	}

	public void Stop()
	{
		IsRunning = false;
		ElapsedSeconds = 0;
		Phase = 0;
		Height = 0;
	}

	public void Update( float deltaTime )
	{
		if ( !float.IsFinite( deltaTime ) || deltaTime < 0 )
			throw new ArgumentOutOfRangeException( nameof( deltaTime ) );
		if ( !IsRunning || deltaTime == 0 )
			return;

		ElapsedSeconds += deltaTime;
		Phase = (Phase + (double)deltaTime % CycleDuration / CycleDuration) % 1;
		Height = (float)((1 - Math.Cos( Phase * Math.Tau )) * 0.5 * MaximumHeight);
	}
}
