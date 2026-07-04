using Godot;

public partial class GlobalEvents : Node
{
	public static GlobalEvents Instance { get; private set; }

	[Signal]
	public delegate void TrainPlacedOnTrackEventHandler(Train3D train, RailRoad3D rail);

	[Signal]
	public delegate void TrainStoppedOnFinalRailEventHandler(Train3D train, RailRoad3D rail);

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void EmitTrainPlacedOnTrack(Train3D train, RailRoad3D rail)
	{
		EmitSignal(SignalName.TrainPlacedOnTrack, train, rail);
	}

	public void EmitTrainStoppedOnFinalRail(Train3D train, RailRoad3D rail)
	{
		EmitSignal(SignalName.TrainStoppedOnFinalRail, train, rail);
	}
}