using Veldrid;

namespace OpenTPW.UI;

public class RootPanel : Entity
{
	public List<Panel> Children { get; set; } = new();

	protected override void OnRender()
	{
		Children.ForEach( child => child.Draw() );
	}

	protected override void OnUpdate()
	{
		Children.ForEach( child => child.Update() );
	}

	public T AddChild<T>( T? obj = null ) where T : Panel
	{
		if ( obj is null )
			obj = Activator.CreateInstance( typeof( T ) ) as T;

		Children.Add( obj );

		return obj;
	}
}
