using Godot;

namespace Twains;

/// <summary>
/// Deprecated: This class has been refactored into CharacterBodyPawn3D and PlayerController.
/// Keep this file as a reference or remove it if no longer needed.
/// </summary>
public partial class CharacterBody3d : Node3D
{
	[Export]
	public CharacterBodyPawn3D Pawn;

	[Export]
	public PlayerController Controller;

	public override void _Ready()
	{
		// Wire up the controller to the pawn if not already set
		if (Controller is not null && Pawn is not null)
		{
			Controller.Pawn = Pawn;
		}
	}
}
