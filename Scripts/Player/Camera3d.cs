using Godot;

namespace Twains
{
	public partial class Camera3d : Camera3D
	{
		[Export]
		public Node3D Target;

			private Vector3 offset = Vector3.Zero;

		// Called when the node enters the scene tree for the first time.
		public override void _Ready()
		{
			if (Target != null)
			{
				offset = GlobalPosition - Target.GlobalPosition;
					if (offset.Length() == 0.0f)
					{
						offset = Vector3.Back * 5.0f;
					}
			}
		}

		// Called every frame. 'delta' is the elapsed time since the previous frame.
		public override void _Process(double delta)
		{
			if (Target == null)
				return;

				// Keep a fixed camera angle and distance from the initial scene setup.
				GlobalPosition = Target.GlobalPosition + offset;
			LookAt(Target.GlobalPosition, Vector3.Up);
		}
	}
}
