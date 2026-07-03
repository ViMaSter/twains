using Godot;

namespace Twains;

public partial class CharacterBody3d : CharacterBody3D
{
	public const float Speed = 5.0f;
	public const float SprintSpeed = 10.0f;
	public const float JumpVelocity = 4.5f;

	[Export]
	public Camera3D Camera;

	[Export]
	public float MotionSmoothness = 0.15f;

	[Export]
	public float RotationSmoothness = 0.2f;

	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Velocity;

		// Add the gravity.
		if (!IsOnFloor())
		{
			velocity += GetGravity() * (float)delta;
		}

		// Handle Jump.
		if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
		{
			velocity.Y = JumpVelocity;
		}

		// Get the input direction and handle the movement/deceleration.
		// As good practice, you should replace UI actions with custom gameplay actions.
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		float currentSpeed = Input.IsActionPressed("sprint") ? SprintSpeed : Speed;

		Basis moveBasis = Camera is not null ? Camera.GlobalTransform.Basis : GlobalTransform.Basis;
		Vector3 forward = new Vector3(moveBasis.Z.X, 0.0f, moveBasis.Z.Z).Normalized();
		Vector3 left = new Vector3(moveBasis.X.X, 0.0f, moveBasis.X.Z).Normalized();
		Vector3 direction = (left * inputDir.X + forward * inputDir.Y).Normalized();
		Vector3 cameraLookDir = new Vector3(forward.X, 0.0f, forward.Z).Normalized();

		if (direction != Vector3.Zero)
		{
			velocity.X = direction.X * currentSpeed;
			velocity.Z = direction.Z * currentSpeed;

			Vector3 lookDir = new Vector3(direction.X, 0.0f, direction.Z).Normalized();
			if (lookDir != Vector3.Zero)
			{
				LookAt(GlobalPosition + lookDir, Vector3.Up);
			}
		}
		else
		{
			velocity.X = Mathf.MoveToward(Velocity.X, 0, currentSpeed);
			velocity.Z = Mathf.MoveToward(Velocity.Z, 0, currentSpeed);

			if (cameraLookDir != Vector3.Zero)
			{
				LookAt(GlobalPosition + cameraLookDir, Vector3.Up);
			}
		}

		Velocity = velocity;
		MoveAndSlide();
	}
}
