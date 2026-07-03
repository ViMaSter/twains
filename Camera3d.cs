using Godot;

namespace Twains
{
	public partial class Camera3d : Camera3D
	{
		[Export]
		public float FixedDistance = 5.0f;
		
		[Export]
		public Node3D Target;
		
		[Export]
		public float MouseSensitivity = 0.01f;

		[Export]
		public float MinDistance = 1.0f;

		[Export]
		public float MaxDistance = 10.0f;

		[Export]
		public float ZoomSpeed = 5.0f;
		
		private Vector3 offset = Vector3.Zero;
		private float yaw = 0.0f;
		private float pitch = 0.0f;

		// Called when the node enters the scene tree for the first time.
		public override void _Ready()
		{
			if (Target != null)
			{
				offset = GlobalPosition - Target.GlobalPosition;
				if (offset.Length() > 0.0f)
				{
					FixedDistance = offset.Length();
				}
				else
				{
					offset = Vector3.Back * FixedDistance;
				}

				FixedDistance = Mathf.Clamp(FixedDistance, MinDistance, MaxDistance);
			}
		}

		// Called every frame. 'delta' is the elapsed time since the previous frame.
		public override void _Process(double delta)
		{
			if (Target == null)
				return;

			// Get mouse movement
			var mouseMotion = GetMouseMotion();
			yaw -= mouseMotion.X * MouseSensitivity;
			pitch += mouseMotion.Y * MouseSensitivity;
			pitch = Mathf.Clamp(pitch, -Mathf.Pi / 2, Mathf.Pi / 2);

			if (Input.IsActionPressed("camera_zoom_in"))
				FixedDistance -= ZoomSpeed * (float)delta;
			if (Input.IsActionPressed("camera_zoom_out"))
				FixedDistance += ZoomSpeed * (float)delta;

			FixedDistance = Mathf.Clamp(FixedDistance, MinDistance, MaxDistance);

			// Calculate rotated offset
			var rotatedOffset = offset.Normalized() * FixedDistance;
			rotatedOffset = rotatedOffset.Rotated(Vector3.Up, yaw);
			rotatedOffset = rotatedOffset.Rotated(rotatedOffset.Cross(Vector3.Up).Normalized(), pitch);

			// Update camera position
			GlobalPosition = Target.GlobalPosition + rotatedOffset;
			LookAt(Target.GlobalPosition, Vector3.Up);
		}

		private Vector2 GetMouseMotion()
		{
			return Input.GetLastMouseVelocity() * (float)GetProcessDeltaTime();
		}
	}
}
