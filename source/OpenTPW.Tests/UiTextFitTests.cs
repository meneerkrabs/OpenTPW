using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>Button text fitting: family sizes first, then a lower whole scale, then wrapping.</summary>
[TestClass]
public class UiTextFitTests
{
	[TestMethod]
	public void KeepsThePreferredFontWhenItFits() =>
		Assert.AreEqual( new UiTextFit.Choice( 0, 2, false ), UiTextFit.Choose( 2, new[] { (100, 20), (80, 16) }, 200, 60 ) );

	[TestMethod]
	public void FallsBackToASmallerSizeOfTheFamily() =>
		Assert.AreEqual( new UiTextFit.Choice( 1, 2, false ), UiTextFit.Choose( 2, new[] { (120, 20), (90, 16), (70, 12) }, 200, 60 ) );

	[TestMethod]
	public void LowersTheWholeScaleBeforeWrapping() =>
		Assert.AreEqual( new UiTextFit.Choice( 1, 2, false ), UiTextFit.Choose( 3, new[] { (120, 20), (90, 16) }, 200, 60 ), "the largest lower scale that fits" );

	[TestMethod]
	public void WrapsOnlyWhenNothingElseFits() =>
		Assert.AreEqual( new UiTextFit.Choice( 1, 2, true ), UiTextFit.Choose( 2, new[] { (400, 20), (300, 16) }, 200, 60 ) );

	[TestMethod]
	public void HeightCountsToo() =>
		Assert.AreEqual( new UiTextFit.Choice( 1, 1, false ), UiTextFit.Choose( 2, new[] { (50, 40), (50, 30) }, 200, 40 ) );
}
