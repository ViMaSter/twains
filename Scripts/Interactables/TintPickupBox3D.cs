using Godot;
using System;
using Twains;

public partial class TintPickupBox3D : PickupBox3D
{
	[Export] public Color TintColor { get; set; } = Colors.White;

	public override void _Ready()
	{
		base._Ready();
		ApplyTint();
	}

	private void ApplyTint()
	{
		MeshInstance3D meshInstance = this.FindChildOfType<MeshInstance3D>();
		if (meshInstance is null)
		{
			throw new InvalidOperationException($"TintPickupBox3D '{Name}': No MeshInstance3D child found.");
		}

		StandardMaterial3D material = GetOrCreateMaterialOverride(meshInstance);
		material.AlbedoColor = TintColor;
	}

	private static StandardMaterial3D GetOrCreateMaterialOverride(MeshInstance3D meshInstance)
	{
		if (meshInstance.MaterialOverride is StandardMaterial3D overrideMaterial)
		{
			// Duplicate shared resources so tinting one instance does not tint all boxes.
			StandardMaterial3D uniqueOverride = overrideMaterial.Duplicate() as StandardMaterial3D ?? overrideMaterial;
			meshInstance.MaterialOverride = uniqueOverride;
			return uniqueOverride;
		}

		if (meshInstance.Mesh != null && meshInstance.Mesh.GetSurfaceCount() > 0)
		{
			Material surfaceMaterial = meshInstance.Mesh.SurfaceGetMaterial(0);
			if (surfaceMaterial is StandardMaterial3D standardSurfaceMaterial)
			{
				StandardMaterial3D uniqueSurface = standardSurfaceMaterial.Duplicate() as StandardMaterial3D ?? standardSurfaceMaterial;
				meshInstance.MaterialOverride = uniqueSurface;
				return uniqueSurface;
			}
		}

		StandardMaterial3D newMaterial = new();
		meshInstance.MaterialOverride = newMaterial;
		return newMaterial;
	}
}
