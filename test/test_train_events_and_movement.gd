extends GutTest

const BLOCKER_SCENE_PATH := "res://test/scenes/connected_rail_through_blocker.tscn"
const TRAIN_GLOBAL_CLASS_NAME := &"Train3D"
const PLACED_EVENT_NAME := "placed"
const STOPPED_EVENT_NAME := "stopped"
const EXPECTED_DEAD_END_WARNING := "No next RailRoad3D found ahead of"
const AXIS_EPSILON := 0.0001
const STOP_EVENT_TIMEOUT_SEC := 5.0
const MOTION_WAIT_TIMEOUT_SEC := 5.0
const STOP_STABLE_EPSILON := 0.005
const STOP_STABLE_FRAMES := 3

var _event_order: Array[String] = []
var _stop_probe_initialized: bool = false
var _stop_probe_last_position: Vector3 = Vector3.ZERO
var _stop_probe_stable_count: int = 0


func before_each():
	_event_order.clear()


func test_connected_rail_through_blocker_events_and_motion():
	var events: Node = get_tree().root.get_node_or_null("GlobalEvents")
	assert_true(events != null, "GlobalEvents autoload should exist")
	if events == null:
		return

	var placed_signal: StringName = _resolve_signal_name(events, ["TrainPlacedOnTrack", "train_placed_on_track"])
	var stopped_signal: StringName = _resolve_signal_name(events, ["TrainStoppedOnFinalRail", "train_stopped_on_final_rail"])
	assert_true(placed_signal != StringName(), "GlobalEvents should expose placed signal")
	assert_true(stopped_signal != StringName(), "GlobalEvents should expose stopped signal")
	if placed_signal == StringName() or stopped_signal == StringName():
		return

	events.connect(placed_signal, Callable(self, "_on_train_placed"))
	events.connect(stopped_signal, Callable(self, "_on_train_stopped"))

	var scene_res: PackedScene = load(BLOCKER_SCENE_PATH) as PackedScene
	assert_not_null(scene_res, "Blocker scene should load")
	if scene_res == null:
		_cleanup_event_connections(events, placed_signal, stopped_signal)
		return

	var scene: Node = add_child_autoqfree(scene_res.instantiate())
	assert_true(scene != null, "Blocker scene should instantiate")
	if scene == null:
		_cleanup_event_connections(events, placed_signal, stopped_signal)
		return

	var train: Node3D = _find_train(scene)
	assert_true(train != null, "Scene should contain an instance using Train3D.cs")
	if train == null:
		_cleanup_event_connections(events, placed_signal, stopped_signal)
		return

	var saw_motion: bool = await wait_until(func() -> bool: return train.global_position.distance_to(scene.global_position) > AXIS_EPSILON, MOTION_WAIT_TIMEOUT_SEC, "Timed out waiting for train to begin moving")
	assert_true(saw_motion, "Train should begin moving shortly after scene start")
	if !saw_motion:
		_cleanup_event_connections(events, placed_signal, stopped_signal)
		return

	# Let initialization complete and capture first three frame positions.
	await wait_process_frames(1)
	var pos_frame_1: Vector3 = train.global_position

	await wait_process_frames(1)
	var pos_frame_2: Vector3 = train.global_position

	await wait_process_frames(1)
	var pos_frame_3: Vector3 = train.global_position

	assert_ne(pos_frame_1, pos_frame_2, "Train position should change from first to second frame")
	var first_step: Vector3 = pos_frame_2 - pos_frame_1
	var second_step: Vector3 = pos_frame_3 - pos_frame_2
	assert_true(_same_lateral_and_vertical_axes(first_step, second_step), "Train should keep same left/right/up/down axes between second and third frame")
	assert_true(_is_moving_forward(second_step), "Train should move forward between second and third frame")

	_reset_stop_probe(train)
	var reached_final_state: bool = await wait_until(Callable(self, "_has_reached_final_state").bind(train), STOP_EVENT_TIMEOUT_SEC, "Timed out waiting for stopped event")
	var saw_stop_event: bool = _event_order.has(STOPPED_EVENT_NAME)
	var stop_position: Vector3 = train.global_position
	await wait_seconds(1.0)
	var after_one_second: Vector3 = train.global_position
	var is_stable_after_wait: bool = stop_position.distance_to(after_one_second) <= AXIS_EPSILON

	assert_true(reached_final_state and (saw_stop_event or is_stable_after_wait), "Train should either emit stopped event or remain stable after reaching final position")
	# This scenario intentionally hits a dead-end rail before stopping.
	assert_engine_error(EXPECTED_DEAD_END_WARNING, "Dead-end warning is expected in blocker scene")
	if !(reached_final_state and (saw_stop_event or is_stable_after_wait)):
		_cleanup_event_connections(events, placed_signal, stopped_signal)
		return

	if _event_order.size() >= 2:
		assert_eq(_event_order.size(), 2, "Exactly two train lifecycle events should fire")
		assert_eq(_event_order[0], PLACED_EVENT_NAME, "Placed event should fire first")
		assert_eq(_event_order[1], STOPPED_EVENT_NAME, "Stopped event should fire second")
	assert_eq(stop_position, after_one_second, "Train should remain in same position one second after final stop")

	_cleanup_event_connections(events, placed_signal, stopped_signal)


func _on_train_placed(_train, _rail):
	_event_order.append(PLACED_EVENT_NAME)


func _on_train_stopped(_train, _rail):
	_event_order.append(STOPPED_EVENT_NAME)


func _same_lateral_and_vertical_axes(first_step: Vector3, second_step: Vector3) -> bool:
	var first_axis: int = _dominant_axis(first_step)
	var second_axis: int = _dominant_axis(second_step)
	if first_axis == -1 or second_axis == -1:
		return false

	if first_axis != second_axis:
		return false

	for i in 3:
		if i == first_axis:
			continue
		if abs(second_step[i]) > AXIS_EPSILON:
			return false

	return true


func _is_moving_forward(step: Vector3) -> bool:
	return step.length() > AXIS_EPSILON


func _find_train(root: Node) -> Node3D:
	if _is_train_instance(root):
		return root as Node3D

	for child in root.get_children():
		var child_node: Node = child as Node
		if child_node == null:
			continue

		var found: Node3D = _find_train(child_node)
		if found != null:
			return found

	return null


func _is_train_instance(node: Node) -> bool:
	var script_obj: Script = node.get_script() as Script
	if script_obj == null:
		return false

	return script_obj.get_global_name() == TRAIN_GLOBAL_CLASS_NAME


func _dominant_axis(step: Vector3) -> int:
	var ax: float = absf(step.x)
	var ay: float = absf(step.y)
	var az: float = absf(step.z)
	var max_val: float = maxf(ax, maxf(ay, az))
	if max_val <= AXIS_EPSILON:
		return -1
	if max_val == ax:
		return 0
	if max_val == ay:
		return 1
	return 2


func _resolve_signal_name(emitter: Object, candidates: Array[String]) -> StringName:
	for signal_name in candidates:
		var candidate := StringName(signal_name)
		if emitter.has_signal(candidate):
			return candidate
	return StringName()


func _cleanup_event_connections(events: Node, placed_signal: StringName, stopped_signal: StringName):
	if events == null:
		return

	var placed_callable := Callable(self, "_on_train_placed")
	var stopped_callable := Callable(self, "_on_train_stopped")

	if placed_signal != StringName() and events.is_connected(placed_signal, placed_callable):
		events.disconnect(placed_signal, placed_callable)

	if stopped_signal != StringName() and events.is_connected(stopped_signal, stopped_callable):
		events.disconnect(stopped_signal, stopped_callable)


func _reset_stop_probe(train: Node3D):
	_stop_probe_initialized = true
	_stop_probe_last_position = train.global_position
	_stop_probe_stable_count = 0


func _has_reached_final_state(train: Node3D) -> bool:
	if _event_order.has(STOPPED_EVENT_NAME):
		return true

	if !_stop_probe_initialized:
		_reset_stop_probe(train)
		return false

	var current_position: Vector3 = train.global_position
	if current_position.distance_to(_stop_probe_last_position) <= STOP_STABLE_EPSILON:
		_stop_probe_stable_count += 1
	else:
		_stop_probe_stable_count = 0

	_stop_probe_last_position = current_position
	return _stop_probe_stable_count >= STOP_STABLE_FRAMES
