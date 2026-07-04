using Godot;

namespace Twains;

public partial class CharacterBodyPawn3D : CharacterBody3D
{
	public const float Speed = 5.0f;
	public const float SprintSpeed = 10.0f;
	public const float JumpVelocity = 4.5f;

	private Switch _interactable;

	[Export]
	public RichTextLabel InteractableStatusLabel;

	[Export]
	public float MotionSmoothness = 0.15f;

	[Export]
	public float RotationSmoothness = 0.2f;

	private float _currentSpeed = Speed;

	public override void _Ready()
	{
		if (InteractableStatusLabel is null)
		{
			GD.PushWarning("CharacterBodyPawn3D: InteractableStatusLabel is not assigned. Interactable status text will not be updated.");
			return;
		}

		InteractableStatusLabel.BbcodeEnabled = true;
		InteractableStatusLabel.Text = "Can interact: [b]empty[/b]";
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 velocity = Velocity;

		// Add the gravity.
		if (!IsOnFloor())
		{
			velocity += GetGravity() * (float)delta;
		}

		Velocity = velocity;
		MoveAndSlide();
	}

	/// <summary>
	/// Look at a target position.
	/// </summary>
	public new void LookAt(Vector3 target, Vector3? up = null)
	{
		Vector3 upVector = up ?? Vector3.Up;
		base.LookAt(target, upVector);
	}

	/// <summary>
	/// Move in a direction relative to the pawn's current facing.
	/// Movement is purely horizontal (ground plane), ignoring any vertical component.
	/// </summary>
	public void Move(Vector2 move)
	{
		if (move == Vector2.Zero)
		{
			// Decelerate
			Velocity = new Vector3(
				Mathf.MoveToward(Velocity.X, 0, _currentSpeed),
				Velocity.Y,
				Mathf.MoveToward(Velocity.Z, 0, _currentSpeed)
			);
			return;
		}

		// Extract basis vectors and project onto ground plane
		Basis moveBasis = GlobalTransform.Basis;
		Vector3 forward = new Vector3(moveBasis.Z.X, 0.0f, moveBasis.Z.Z).Normalized();
		Vector3 left = new Vector3(moveBasis.X.X, 0.0f, moveBasis.X.Z).Normalized();

		// Convert input to movement relative to pawn's orientation
		Vector3 direction = (left * -move.X + forward * -move.Y).Normalized();

		if (direction != Vector3.Zero)
		{
			// Apply movement only to horizontal plane
			Velocity = new Vector3(
				direction.X * _currentSpeed,
				Velocity.Y,
				direction.Z * _currentSpeed
			);
		}
	}

	/// <summary>
	/// Jump if on the floor.
	/// </summary>
	public void Jump()
	{
		if (IsOnFloor())
		{
			Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
		}
	}

	/// <summary>
	/// Set whether the pawn is sprinting.
	/// </summary>
	public void SetSprint(bool isSprinting)
	{
		_currentSpeed = isSprinting ? SprintSpeed : Speed;
	}

	/// <summary>
	/// Set the current interactable object for this pawn.
	/// </summary>
	public void SetInteractable(Switch interactable)
	{
		if (interactable is null)
		{
			ClearInteractable();
			return;
		}

		_interactable = interactable;

		if (InteractableStatusLabel is not null)
		{
			InteractableStatusLabel.Text = $"Can interact: [b]{interactable.Name}[/b]";
		}
	}

	/// <summary>
	/// Clear the current interactable object for this pawn.
	/// </summary>
	public void ClearInteractable()
	{
		_interactable = null;

		if (InteractableStatusLabel is not null)
		{
			InteractableStatusLabel.Text = "Can interact: [b]empty[/b]";
		}
	}
}
