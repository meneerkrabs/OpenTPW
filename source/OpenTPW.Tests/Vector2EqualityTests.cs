using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>OpenTPW's own Vector2: its equality members compare the components (Equals(Vector2) once called itself and overflowed the stack).</summary>
[TestClass]
public class Vector2EqualityTests
{
	[TestMethod]
	public void EqualsAndTheOperatorsCompareComponentsWithoutRecursing()
	{
		var a = new Vector2( 3f, -4f );
		var same = new Vector2( 3f, -4f );
		var other = new Vector2( 3f, 4f );
		Assert.IsTrue( a.Equals( same ) );
		Assert.IsFalse( a.Equals( other ) );
		Assert.IsTrue( a == same );
		Assert.IsFalse( a != same );
		Assert.IsTrue( a != other );
		Assert.IsTrue( a.Equals( (object)same ) );
		Assert.IsFalse( a.Equals( (object)other ) );
		Assert.AreEqual( a.GetHashCode(), same.GetHashCode() );
	}
}
