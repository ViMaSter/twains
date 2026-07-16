using Godot;

namespace Twains;

public partial class PlayerController : Node3D
{
	private CharacterBodyPawn3D _pawn;
	private Camera3D _camera;
	private float _throwHeldSeconds;
	private Vector3 _mouseLookTarget;
	private bool _hasMouseLookTarget;

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

		PickupPawn3D pickupPawn = _pawn.FindChildOfType<PickupPawn3D>();

		if (Input.IsActionJustPressed("interact"))
		{
			// Check if pawn is holding something
			if (pickupPawn is not null && pickupPawn.HasPickup)
			{
				pickupPawn.Place();
			}
			else
			{
				_pawn.UseInteractable();
			}
		}

		if (Input.IsActionJustPressed("throw"))
		{
			_throwHeldSeconds = 0.0f;
		}

		if (Input.IsActionPressed("throw"))
		{
			_throwHeldSeconds += (float)delta;
		}

		if (Input.IsActionJustReleased("throw"))
		{
			float throwSeconds = Mathf.Clamp(_throwHeldSeconds, 0.0f, 1.0f);
			if (pickupPawn is not null && pickupPawn.HasPickup)
			{
				pickupPawn.Throw(throwSeconds);
			}

			_throwHeldSeconds = 0.0f;
		}

		// Handle Jump.
		if (Input.IsActionJustPressed("ui_accept"))
		{
			_pawn.Jump();
		}

		// Get the input direction (normalized).
		bool isSprinting = Input.IsActionPressed("sprint");
		_pawn.SetSprint(isSprinting);

		// Build horizontal movement basis from camera look direction projected onto the floor.
		Basis cameraBasis = _camera.GlobalTransform.Basis;
		Vector3 cameraForward = new Vector3(-cameraBasis.Z.X, 0.0f, -cameraBasis.Z.Z).Normalized();
		if (cameraForward == Vector3.Zero)
		{
			cameraForward = _pawn.GetFacingDirection();
		}

		Vector3 cameraRight = Vector3.Up.Cross(cameraForward).Normalized();

		// Move relative to camera projection (forward/left from camera viewpoint).
		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		Vector3 moveDirection = (cameraRight * -inputDir.X + cameraForward * -inputDir.Y).Normalized();
		_pawn.MoveWorld(moveDirection);

		// Rotate toward the latest world-space point under the mouse cursor.
		if (_hasMouseLookTarget)
		{
			Vector3 lookDirection = _pawn.GlobalPosition - _mouseLookTarget;
			_pawn.SetFacingDirection(lookDirection);
		}
		else
		{
			// Fallback to rotate_* actions when no valid mouse target has been acquired.
			Vector2 rotateInput = Input.GetVector("rotate_left", "rotate_right", "rotate_forward", "rotate_back");
			Vector3 rotateDirection = (cameraRight * rotateInput.X + cameraForward * rotateInput.Y).Normalized();
			_pawn.SetFacingDirection(rotateDirection);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseMotion)
		{
			return;
		}

		if (_pawn is null || _camera is null)
		{
			return;
		}

		_hasMouseLookTarget = TryGetMouseLookTarget(out _mouseLookTarget);
	}

	private bool TryGetMouseLookTarget(out Vector3 target)
	{
		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector3 rayOrigin = _camera.ProjectRayOrigin(mousePos);
		Vector3 rayDirection = _camera.ProjectRayNormal(mousePos);
		Vector3 rayEnd = rayOrigin + rayDirection * 2000.0f;

		PhysicsRayQueryParameters3D rayQuery = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
		rayQuery.CollideWithAreas = true;

		if (_pawn is not null)
		{
			rayQuery.Exclude = new Godot.Collections.Array<Rid> { _pawn.GetRid() };
		}

		Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(rayQuery);
		if (result.Count > 0)
		{
			target = (Vector3)result["position"];
			return true;
		}

		// Fallback: intersect the ray with the pawn's horizontal plane.
		float planeY = _pawn.GlobalPosition.Y;
		if (Mathf.Abs(rayDirection.Y) > 0.0001f)
		{
			float distance = (planeY - rayOrigin.Y) / rayDirection.Y;
			if (distance > 0.0f)
			{
				target = rayOrigin + rayDirection * distance;
				return true;
			}
		}

		target = Vector3.Zero;
		return false;
	}
}
