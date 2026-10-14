using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

/// <summary>Own New/progress choices: cancellation owns the batch, mounted callbacks activate once.</summary>
internal sealed class ProgressChoiceScreen( string name ) : UiScreen( name )
{
	private bool dispatching;
	private bool activated;
	public bool TryActivate( UiScreenStack stack )
	{
		if ( IsRemoved || !ReferenceEquals( stack.Top, this ) || dispatching && activated ) return false;
		if ( dispatching ) activated = true;
		return true;
	}
	public override bool Update( UiContext context, UiInput input )
	{
		if ( IsRemoved ) return true;
		if ( input.Has( UiKeys.Back ) || input.RightPressed ) { Back?.Invoke(); return true; }
		dispatching = true; activated = false;
		try { return base.Update( context, input ); }
		finally { dispatching = false; }
	}
	public static ProgressChoiceScreen Message( string name, Func<string> text, params (Func<string> Label, Action Clicked)[] choices )
	{
		var template = UiDialogs.Message( name, text, choices );
		var screen = new ProgressChoiceScreen( name ) { Back = template.Back };
		foreach ( var element in template.Elements ) screen.Add( element );
		screen.Focus( template.Focused );
		return screen;
	}
}
