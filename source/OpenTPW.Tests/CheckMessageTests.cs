using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>A check's message formatted only when the check fails (<see cref="CheckMessage"/>, the layout snapshot's checks).</summary>
[TestClass]
public class CheckMessageTests
{
	private static string? Failure( bool condition, [InterpolatedStringHandlerArgument( nameof( condition ) )] ref CheckMessage message ) => condition ? null : message.ToStringAndClear();

	[TestMethod]
	public void AHoldingCheckEvaluatesNoHoleAndAFailedOneReadsAsTheInterpolatedString()
	{
		var evaluated = 0;
		int Hole()
		{
			evaluated++;
			return 7;
		}
		Assert.IsNull( Failure( true, $"cell ({Hole()}, {Hole()}) is recorded twice" ) );
		Assert.AreEqual( 0, evaluated, "a check that holds evaluates no hole" );
		var flags = (ushort)0x2A;
		var expected = $"cell ({Hole()}, 4) carries flags {flags:X4} in {"jungle",-8}|{3.5}, {"×"}";
		Assert.AreEqual( expected, Failure( false, $"cell ({Hole()}, 4) carries flags {flags:X4} in {"jungle",-8}|{3.5}, {"×"}" ) );
		Assert.AreEqual( 2, evaluated, "a failed check evaluates its holes once" );
	}
}
