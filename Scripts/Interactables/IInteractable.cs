using Godot;

public interface IInteractable
{
    public bool Use(Node3D user);
    public StringName Name { get; }
}