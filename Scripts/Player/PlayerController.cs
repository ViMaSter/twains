using Godot;

namespace Twains;

public partial class PlayerController : Node3D
{
	private CharacterBodyPawn3D _pawn;
	private Camera3D _camera;

	public override void _Ready()
	{
		_pawn = this.FindChildOfType<CharacterBodyPawn3D>();
		if (_pawn is null)
		{
			GD.PushError("PlayerController: No CharacterBodyPawn3D found in children. Please add a CharacterBodyPawn3D as a child node.");
		}

		_camera = this.FindChildOfType<Camera3D>();
		if (_camera is null)
		{
			GD.PushError("PlayerController: No Camera3D found in children. Please add a Camera3D as a child node.");
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Return early if required components are not found
		if (_pawn is null || _camera is null)
		{
			return;
		}

		if (Input.IsActionJustPressed("interact"))
		{
			// Check if pawn is holding something
			PickupPawn3D pickupPawn = _pawn.FindChildOfType<PickupPawn3D>();
			if (pickupPawn is not null && pickupPawn.HasPickup)
			{
				pickupPawn.Place();
			}
			else
			{
				_pawn.UseInteractable();
			}
		}

		// Handle Jump.
		if (Input.IsActionJustPressed("ui_accept"))
		{
			_pawn.Jump();
		}

		// Get the input direction (normalized).
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		bool isSprinting = Input.IsActionPressed("sprint");

		_pawn.SetSprint(isSprinting);

		// Orient the pawn based on camera direction
		Basis cameraBasis = _camera.GlobalTransform.Basis;
		Vector3 cameraForward = new Vector3(cameraBasis.Z.X, 0.0f, cameraBasis.Z.Z).Normalized();
		
		if (cameraForward != Vector3.Zero)
		{
			_pawn.LookAt(_pawn.GlobalPosition + cameraForward, Vector3.Up);
		}

		// Move the pawn relative to its current orientation
		_pawn.Move(inputDir);
	}
}
