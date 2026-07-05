using Godot;
using System;

public partial class SlotZone3D : Node3D
{
	[Export] public Vector2I Slots { get; set; } = new Vector2I(5, 5);
	[Export] public PackedScene SlotScene { get; set; }

	private float _margin = 0.1f;
	[Export]
	public float Margin
	{
		get => _margin;
		set => _margin = Mathf.Max(0.0f, value);
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GenerateRuntimeSlots();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	private void GenerateRuntimeSlots()
	{
		if (SlotScene == null)
		{
			GD.PushWarning("SlotZone3D: SlotScene is not assigned; no slots were generated.");
			return;
		}

		int cols = Mathf.Max(1, Slots.X);
		int rows = Mathf.Max(1, Slots.Y);
		float spacing = 1.0f + (Margin * 2.0f);

		float startX = -((cols - 1) * spacing) * 0.5f;
		float startZ = -((rows - 1) * spacing) * 0.5f;

		for (int z = 0; z < rows; z++)
		{
			for (int x = 0; x < cols; x++)
			{
				var slotInstance = SlotScene.Instantiate<Node3D>();
				slotInstance.Name = $"Slot_{x}_{z}";
				slotInstance.Position = new Vector3(startX + (x * spacing), 0.0f, startZ + (z * spacing));
				AddChild(slotInstance);
			}
		}
	}
}
