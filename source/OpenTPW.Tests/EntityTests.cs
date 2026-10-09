using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

[TestClass]
public class EntityTests
{
	[TestMethod]
	public void DeletionRemovesEntityAndIsIdempotent()
	{
		var entity = new Entity();
		Assert.IsTrue( Entity.All.Contains( entity ) );
		entity.Delete();
		Assert.IsFalse( Entity.All.Contains( entity ) );
		entity.Delete();
		Assert.IsFalse( Entity.All.Contains( entity ) );
	}
}
