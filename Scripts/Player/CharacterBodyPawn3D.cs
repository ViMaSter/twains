using Godot;
using System.Collections.Generic;

namespace Twains;

public partial class CharacterBodyPawn3D : CharacterBody3D
{
	[Export]
	public float Speed = 5.0f;
	[Export]
	public float SprintSpeed = 10.0f;
	[Export]
	public float JumpVelocity = 4.5f;

	private readonly List<IInteractable> _interactables = new();

	[Export]
	public RichTextLabel InteractableStatusLabel;

	[Export]
	public float MotionSmoothness = 0.15f;

	[Export]
	public float RotationSmoothness = 0.2f;

	private float _currentSpeed = 0.0f;
	private Vector3 _facingDirection = Vector3.Zero;
	private Vector3 _smoothedFacingDirection = Vector3.Zero;

	public override void _Ready()
	{
		_currentSpeed = Speed;
		_facingDirection = new Vector3(-GlobalTransform.Basis.Z.X, 0.0f, -GlobalTransform.Basis.Z.Z).Normalized();
		if (_facingDirection == Vector3.Zero)
		{
			_facingDirection = Vector3.Forward;
		}
		_smoothedFacingDirection = _facingDirection;

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

		// Smoothly rotate toward the target facing direction.
		if (_smoothedFacingDirection != Vector3.Zero && _facingDirection != Vector3.Zero)
		{
			_smoothedFacingDirection = _smoothedFacingDirection.Slerp(_facingDirection, RotationSmoothness).Normalized();
			base.LookAt(GlobalPosition + _smoothedFacingDirection, Vector3.Up);
		}
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
		Basis moveBasis = GlobalTransform.Basis;
		Vector3 forward = new Vector3(-moveBasis.Z.X, 0.0f, -moveBasis.Z.Z).Normalized();
		Vector3 right = new Vector3(moveBasis.X.X, 0.0f, moveBasis.X.Z).Normalized();
		Vector3 worldDirection = (right * move.X + forward * -move.Y).Normalized();
		MoveWorld(worldDirection);
	}

	/// <summary>
	/// Move in a world-space direction on the horizontal plane.
	/// </summary>
	public void MoveWorld(Vector3 worldDirection)
	{
		Vector3 flatDirection = new Vector3(worldDirection.X, 0.0f, worldDirection.Z);
		if (flatDirection == Vector3.Zero)
		{
			// Decelerate
			Velocity = new Vector3(
				Mathf.MoveToward(Velocity.X, 0, _currentSpeed),
				Velocity.Y,
				Mathf.MoveToward(Velocity.Z, 0, _currentSpeed)
			);
			return;
		}

		Vector3 direction = flatDirection.Normalized();
		Velocity = new Vector3(
			direction.X * _currentSpeed,
			Velocity.Y,
			direction.Z * _currentSpeed
		);
	}

	/// <summary>
	/// Set facing direction on the horizontal plane.
	/// </summary>
	public void SetFacingDirection(Vector3 worldDirection)
	{
		Vector3 flatDirection = new Vector3(worldDirection.X, 0.0f, worldDirection.Z);
		if (flatDirection == Vector3.Zero)
		{
			return;
		}

		_facingDirection = flatDirection.Normalized();
	}

	/// <summary>
	/// Get current facing direction on the horizontal plane.
	/// </summary>
	public Vector3 GetFacingDirection()
	{
		if (_facingDirection == Vector3.Zero)
		{
			_facingDirection = new Vector3(-GlobalTransform.Basis.Z.X, 0.0f, -GlobalTransform.Basis.Z.Z).Normalized();
		}

		return _facingDirection;
	}

	/// <summary>
	/// GDScript-friendly alias for Move so tests can call move(...).
	/// </summary>
	public void move(Vector2 move)
	{
		Move(move);
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
	public void SetInteractable(IInteractable interactable)
	{
		if (interactable is null)
		{
			ClearInteractable();
			return;
		}

		// If already tracked, move it to top so most-recent entry wins.
		_interactables.Remove(interactable);
		_interactables.Add(interactable);
		UpdateInteractableStatusLabel();
	}

	/// <summary>
	/// Remove a specific interactable object from this pawn.
	/// </summary>
	public void RemoveInteractable(IInteractable interactable)
	{
		if (interactable is null)
		{
			return;
		}

		_interactables.Remove(interactable);
		UpdateInteractableStatusLabel();
	}

	/// <summary>
	/// Clear the current interactable object for this pawn.
	/// </summary>
	public void ClearInteractable()
	{
		_interactables.Clear();
		UpdateInteractableStatusLabel();
	}

	/// <summary>
	/// Use the currently assigned interactable, if any.
	/// </summary>
	public void UseInteractable()
	{
		if (_interactables.Count == 0)
		{
			return;
		}

		_interactables[_interactables.Count - 1].Use(this);
	}

	private void UpdateInteractableStatusLabel()
	{
		if (InteractableStatusLabel is null)
		{
			return;
		}

		if (_interactables.Count == 0)
		{
			InteractableStatusLabel.Text = "Can interact: [b]empty[/b]";
			return;
		}

		int currentIndex = _interactables.Count - 1;
		string text = $"Can interact: [b]{_interactables[currentIndex].Name}[/b]";

		if (currentIndex > 0)
		{
			for (int i = currentIndex - 1; i >= 0; i--)
			{
				text += $" -> {_interactables[i].Name}";
			}
		}

		InteractableStatusLabel.Text = text;
	}
}
