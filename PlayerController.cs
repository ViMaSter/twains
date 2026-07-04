using Godot;

namespace Twains;

public partial class PlayerController : Node3D
{
	[Export]
	public CharacterBodyPawn3D Pawn;

	[Export]
	public Camera3D Camera;

	public override void _Ready()
	{
		if (Pawn is null)
		{
			GD.PushError("PlayerController: Pawn is not assigned. Please set the Pawn property in the Inspector.");
		}

		if (Camera is null)
		{
			GD.PushError("PlayerController: Camera is not assigned. Please set the Camera property in the Inspector.");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Return early if required components are not set
		if (Pawn is null || Camera is null)
		{
			return;
		}

		// Handle Jump.
		if (Input.IsActionJustPressed("ui_accept"))
		{
			Pawn.Jump();
		}

		// Get the input direction (normalized).
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		bool isSprinting = Input.IsActionPressed("sprint");

		Pawn.SetSprint(isSprinting);

		// Orient the pawn based on camera direction
		Basis cameraBasis = Camera.GlobalTransform.Basis;
		Vector3 cameraForward = new Vector3(cameraBasis.Z.X, 0.0f, cameraBasis.Z.Z).Normalized();
		
		if (cameraForward != Vector3.Zero)
		{
			Pawn.LookAt(Pawn.GlobalPosition + cameraForward, Vector3.Up);
		}

		// Move the pawn relative to its current orientation
		Pawn.Move(inputDir);
	}
}
