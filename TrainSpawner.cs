using Godot;
using System.Collections.Generic;

public partial class TrainSpawner : Node3D
{
	[Export] public bool EnableSpawnerLogs = true;

	[Export] public PackedScene TrainScene;
	[Export] public int MaxActiveTrains = 3;
	[Export] public float SpawnIntervalSeconds = 5.0f;

	private readonly List<Train3D> _activeTrains = new();
	private bool _stateMachineRunning;

	public override void _Ready()
	{
		StartStateMachine();
	}

	private async void StartStateMachine()
	{
		if (_stateMachineRunning)
		{
			LogSpawner("State machine already running; ignoring start request.");
			return;
		}

		_stateMachineRunning = true;
		LogSpawner("State machine started.");

		if (_activeTrains.Count == 0)
		{
			LogSpawner("No active trains on startup; spawning immediately.");
			TrySpawnTrain();
		}

		while (IsInsideTree())
		{
			CleanupInvalidTrains();

			if (_activeTrains.Count < MaxActiveTrains)
			{
				LogSpawner($"Interval branch: active={_activeTrains.Count}/{MaxActiveTrains}. Waiting {SpawnIntervalSeconds:0.00}s before spawn check.");
				await WaitSeconds(SpawnIntervalSeconds);
				if (!IsInsideTree())
				{
					LogSpawner("Spawner left tree after interval wait; stopping state machine.");
					break;
				}

				CleanupInvalidTrains();
				if (_activeTrains.Count < MaxActiveTrains)
				{
					LogSpawner($"Interval check: active={_activeTrains.Count}/{MaxActiveTrains}; attempting spawn.");
					TrySpawnTrain();
				}
				else
				{
					LogSpawner($"Interval check: active={_activeTrains.Count}/{MaxActiveTrains}; no spawn needed.");
				}
				continue;
			}

			LogSpawner($"At capacity: active={_activeTrains.Count}/{MaxActiveTrains}. Waiting {SpawnIntervalSeconds:0.00}s before next capacity check.");
			await WaitSeconds(SpawnIntervalSeconds);
		}

		_stateMachineRunning = false;
		LogSpawner("State machine stopped.");
	}

	private async System.Threading.Tasks.Task WaitSeconds(float seconds)
	{
		if (seconds <= 0.0f)
		{
			return;
		}

		await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	}

	private bool TrySpawnTrain()
	{
		if (TrainScene == null)
		{
			GD.PushWarning("TrainSpawner: TrainScene is not assigned.");
			LogSpawner("Spawn attempt failed: TrainScene is null.");
			return false;
		}

		Node instance = TrainScene.Instantiate();
		if (instance is not Train3D train)
		{
			GD.PushWarning("TrainSpawner: Assigned TrainScene does not instantiate a Train3D root node.");
			LogSpawner("Spawn attempt failed: scene root is not Train3D.");
			instance.QueueFree();
			return false;
		}

		AddChild(train);
		_activeTrains.Add(train);
		train.CantMoveAnymore += OnTrainCantMoveAnymore;
		LogSpawner($"Spawned train '{train.Name}'. Active trains={_activeTrains.Count}/{MaxActiveTrains}.");
		return true;
	}

	private void OnTrainCantMoveAnymore(Train3D train)
	{
		if (train == null || !_activeTrains.Contains(train))
		{
			return;
		}

		LogSpawner($"Train '{train.Name}' cannot move anymore; despawning immediately.");
		DespawnTrain(train);
	}

	private void DespawnTrain(Train3D train)
	{
		if (train == null)
		{
			return;
		}

		_activeTrains.Remove(train);
		LogSpawner($"Despawning train '{train.Name}'. Active trains={_activeTrains.Count}/{MaxActiveTrains} after removal.");
		if (IsInstanceValid(train))
		{
			train.CantMoveAnymore -= OnTrainCantMoveAnymore;
			if (!train.IsQueuedForDeletion())
			{
				train.QueueFree();
			}
		}
	}

	private void CleanupInvalidTrains()
	{
		for (int i = _activeTrains.Count - 1; i >= 0; i--)
		{
			Train3D train = _activeTrains[i];
			if (train != null && IsInstanceValid(train) && !train.IsQueuedForDeletion())
			{
				continue;
			}

			string trainName = train != null ? train.Name : "<null>";
			LogSpawner($"Cleanup removed invalid train '{trainName}'.");
			_activeTrains.RemoveAt(i);
		}
	}

	private void LogSpawner(string message)
	{
		if (!EnableSpawnerLogs)
		{
			return;
		}

		GD.Print($"TrainSpawner: {message}");
	}
}
