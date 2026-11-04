namespace OpenTPW.Hud;

/// <summary>The park information window's series mask and period, as tp.exe keeps them in the globals 0x753C48 (initially the people-in-park series) and 0x7CB39C (initially 1 year) for the whole process.</summary>
public sealed class ParkInfoChoice
{
	public ParkInfoSeries Series { get; set; } = ParkInfoGraph.InitialSeries;
	public int Period { get; set; } = ParkInfoGraph.InitialPeriod;
}
