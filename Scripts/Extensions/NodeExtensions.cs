using Godot;

namespace Twains;

public static class NodeExtensions
{
	/// <summary>
	/// Recursively finds the first child of type T.
	/// </summary>
	public static T FindChildOfType<T>(this Node node) where T : Node
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is T match)
				return match;

			T recursive = child.FindChildOfType<T>();
			if (recursive != null)
				return recursive;
		}

		return null;
	}
}
