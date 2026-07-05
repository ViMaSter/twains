using Godot;

[GlobalClass]
public partial class SwitchMethodCall : Resource
{
	[Export]
	public NodePath TargetPath;

	[Export]
	public string TargetMethodName = string.Empty;

	public void Trigger(Node owner, int actionIndex = -1)
	{
		if (owner is null)
		{
			GD.PushWarning("SwitchMethodCall: Trigger called with null owner.");
			return;
		}

		if (TargetPath.IsEmpty)
		{
			GD.PushWarning(_Prefix(owner, actionIndex) + "TargetPath is empty.");
			return;
		}

		Node target = owner.GetNodeOrNull(TargetPath);
		if (target is null)
		{
			GD.PushWarning(_Prefix(owner, actionIndex) + $"Target '{TargetPath}' was not found.");
			return;
		}

		if (string.IsNullOrWhiteSpace(TargetMethodName))
		{
			GD.PushWarning(_Prefix(owner, actionIndex) + "TargetMethodName is empty.");
			return;
		}

		StringName method = new StringName(TargetMethodName);
		if (!target.HasMethod(method))
		{
			GD.PushWarning(_Prefix(owner, actionIndex) + $"Target '{target.Name}' does not have method '{TargetMethodName}'.");
			return;
		}

		target.Call(method);
	}

	private static string _Prefix(Node owner, int actionIndex)
	{
		string indexText = actionIndex >= 0 ? $"[{actionIndex}] " : string.Empty;
		return $"Switch '{owner.Name}': PressActions" + indexText;
	}
}
