extends GutTest

const SLOTS_SCENE_PATH := "res://test/scenes/pickup/slots.tscn"
const SETTLE_TIMEOUT_SEC := 6.0
const SETTLE_VELOCITY_EPSILON := 0.05
const ZERO_ROTATION_EPSILON_RAD := 0.03
const SAME_POSITION_EPSILON := 0.45
const DIFFERENT_POSITION_EPSILON := 0.4
const SLOT_ZONE_MOVE_X := +10.0
const SLOT_ZONE_MOVE_WAIT_PHYSICS_TICKS := 25


func test_slots_scene_tracks_slotted_vs_not_close_enough_after_settle():
	var scene_res: PackedScene = load(SLOTS_SCENE_PATH) as PackedScene
	assert_not_null(scene_res, "Slots scene should load")
	if scene_res == null:
		return

	var scene: Node = add_child_autoqfree(scene_res.instantiate())
	assert_not_null(scene, "Slots scene should instantiate")
	if scene == null:
		return

	var slot_zone: Node3D = scene.get_node_or_null("SlotZone") as Node3D
	var slots_in: RigidBody3D = scene.get_node_or_null("1x1SlotsIn") as RigidBody3D
	var not_close_enough: RigidBody3D = scene.get_node_or_null("1x1NotCloseEnough") as RigidBody3D
	var slots_in_1x2: RigidBody3D = scene.get_node_or_null("1x2SlotsIn") as RigidBody3D
	var only_in_one_1x2: RigidBody3D = scene.get_node_or_null("1x2OnlyInOne") as RigidBody3D

	assert_true(slot_zone != null, "Scene should contain SlotZone")
	assert_true(slots_in != null, "Scene should contain 1x1SlotsIn")
	assert_true(not_close_enough != null, "Scene should contain 1x1NotCloseEnough")
	assert_true(slots_in_1x2 != null, "Scene should contain 1x2SlotsIn")
	assert_true(only_in_one_1x2 != null, "Scene should contain 1x2OnlyInOne")
	if slot_zone == null or slots_in == null or not_close_enough == null or slots_in_1x2 == null or only_in_one_1x2 == null:
		return

	var slots_in_start_position: Vector3 = slots_in.global_position
	var slots_in_start_rotation: Vector3 = slots_in.rotation
	var not_close_start_position: Vector3 = not_close_enough.global_position
	var not_close_start_rotation: Vector3 = not_close_enough.rotation
	var slots_in_1x2_start_position: Vector3 = slots_in_1x2.global_position

	assert_true(slots_in_start_rotation.length() > ZERO_ROTATION_EPSILON_RAD,
		"1x1SlotsIn should start significantly rotated")
	assert_true(not_close_start_rotation.length() > ZERO_ROTATION_EPSILON_RAD,
		"1x1NotCloseEnough should start significantly rotated")

	var slots_in_settled: bool = await _wait_for_rigidbody_to_settle(slots_in, SETTLE_TIMEOUT_SEC)
	var not_close_settled: bool = await _wait_for_rigidbody_to_settle(not_close_enough, SETTLE_TIMEOUT_SEC)
	var slots_in_1x2_settled: bool = await _wait_for_rigidbody_to_settle(slots_in_1x2, SETTLE_TIMEOUT_SEC)
	var only_in_one_1x2_settled: bool = await _wait_for_rigidbody_to_settle(only_in_one_1x2, SETTLE_TIMEOUT_SEC)
	assert_true(slots_in_settled, "1x1SlotsIn should settle")
	assert_true(not_close_settled, "1x1NotCloseEnough should settle")
	assert_true(slots_in_1x2_settled, "1x2SlotsIn should settle")
	assert_true(only_in_one_1x2_settled, "1x2OnlyInOne should settle")
	if !slots_in_settled or !not_close_settled or !slots_in_1x2_settled or !only_in_one_1x2_settled:
		await _cleanup_scene(scene)
		return

	var slots_in_end_position: Vector3 = slots_in.global_position
	var slots_in_end_rotation: Vector3 = slots_in.rotation
	var not_close_end_position: Vector3 = not_close_enough.global_position
	var not_close_end_rotation: Vector3 = not_close_enough.rotation
	var slots_in_1x2_end_position: Vector3 = slots_in_1x2.global_position

	var slots_in_horizontal_delta := _horizontal_distance(slots_in_start_position, slots_in_end_position)
	var not_close_horizontal_delta := _horizontal_distance(not_close_start_position, not_close_end_position)
	var slots_in_1x2_horizontal_delta := _horizontal_distance(slots_in_1x2_start_position, slots_in_1x2_end_position)

	assert_true(slots_in_horizontal_delta > DIFFERENT_POSITION_EPSILON,
		"1x1SlotsIn should move in X/Z when slotted. start=%s end=%s delta=%.4f" % [slots_in_start_position, slots_in_end_position, slots_in_horizontal_delta])
	assert_true(_is_rotation_near_zero(slots_in_end_rotation, ZERO_ROTATION_EPSILON_RAD),
		"1x1SlotsIn should end flat at ~0 rotation. start=%s end=%s" % [slots_in_start_rotation, slots_in_end_rotation])

	assert_true(not_close_horizontal_delta <= SAME_POSITION_EPSILON,
		"1x1NotCloseEnough should keep roughly same X/Z when not slotted. start=%s end=%s delta=%.4f" % [not_close_start_position, not_close_end_position, not_close_horizontal_delta])
	assert_true(!_is_rotation_near_zero(not_close_end_rotation, ZERO_ROTATION_EPSILON_RAD),
		"1x1NotCloseEnough should not have rotation reset to zero. start=%s end=%s" % [not_close_start_rotation, not_close_end_rotation])

	assert_true(slots_in_1x2_horizontal_delta > DIFFERENT_POSITION_EPSILON,
		"1x2SlotsIn should move in X/Z when slotted. start=%s end=%s delta=%.4f" % [slots_in_1x2_start_position, slots_in_1x2_end_position, slots_in_1x2_horizontal_delta])

	var slots_in_before_slot_zone_move: Vector3 = slots_in.global_position
	var slots_in_1x2_before_slot_zone_move: Vector3 = slots_in_1x2.global_position
	var only_in_one_1x2_before_slot_zone_move: Vector3 = only_in_one_1x2.global_position
	slot_zone.global_position += Vector3(SLOT_ZONE_MOVE_X, 0.0, 0.0)
	await wait_physics_frames(SLOT_ZONE_MOVE_WAIT_PHYSICS_TICKS)

	var slots_in_after_slot_zone_move: Vector3 = slots_in.global_position
	var slots_in_1x2_after_slot_zone_move: Vector3 = slots_in_1x2.global_position
	var only_in_one_1x2_after_slot_zone_move: Vector3 = only_in_one_1x2.global_position
	var slots_in_slot_zone_move_delta: Vector3 = slots_in_after_slot_zone_move - slots_in_before_slot_zone_move
	var slots_in_1x2_slot_zone_move_delta: Vector3 = slots_in_1x2_after_slot_zone_move - slots_in_1x2_before_slot_zone_move
	var only_in_one_1x2_slot_zone_move_delta: Vector3 = only_in_one_1x2_after_slot_zone_move - only_in_one_1x2_before_slot_zone_move

	assert_eq(slots_in_slot_zone_move_delta.x, SLOT_ZONE_MOVE_X,
		"1x1SlotsIn should follow SlotZone by exactly %.1f on X. before=%s after=%s delta=%s" % [SLOT_ZONE_MOVE_X, slots_in_before_slot_zone_move, slots_in_after_slot_zone_move, slots_in_slot_zone_move_delta])
	assert_eq(slots_in_slot_zone_move_delta.y, 0.0,
		"1x1SlotsIn should not move on Y when SlotZone moves. before=%s after=%s delta=%s" % [slots_in_before_slot_zone_move, slots_in_after_slot_zone_move, slots_in_slot_zone_move_delta])
	assert_eq(slots_in_slot_zone_move_delta.z, 0.0,
		"1x1SlotsIn should not move on Z when SlotZone moves. before=%s after=%s delta=%s" % [slots_in_before_slot_zone_move, slots_in_after_slot_zone_move, slots_in_slot_zone_move_delta])

	assert_eq(slots_in_1x2_slot_zone_move_delta.x, SLOT_ZONE_MOVE_X,
		"1x2SlotsIn should follow SlotZone by exactly %.1f on X. before=%s after=%s delta=%s" % [SLOT_ZONE_MOVE_X, slots_in_1x2_before_slot_zone_move, slots_in_1x2_after_slot_zone_move, slots_in_1x2_slot_zone_move_delta])
	assert_eq(slots_in_1x2_slot_zone_move_delta.y, 0.0,
		"1x2SlotsIn should not move on Y when SlotZone moves. before=%s after=%s delta=%s" % [slots_in_1x2_before_slot_zone_move, slots_in_1x2_after_slot_zone_move, slots_in_1x2_slot_zone_move_delta])
	assert_eq(slots_in_1x2_slot_zone_move_delta.z, 0.0,
		"1x2SlotsIn should not move on Z when SlotZone moves. before=%s after=%s delta=%s" % [slots_in_1x2_before_slot_zone_move, slots_in_1x2_after_slot_zone_move, slots_in_1x2_slot_zone_move_delta])

	assert_true(abs(only_in_one_1x2_slot_zone_move_delta.x) <= SAME_POSITION_EPSILON,
		"1x2OnlyInOne should not follow SlotZone on X when not slotted. before=%s after=%s delta=%s" % [only_in_one_1x2_before_slot_zone_move, only_in_one_1x2_after_slot_zone_move, only_in_one_1x2_slot_zone_move_delta])
	assert_true(abs(only_in_one_1x2_slot_zone_move_delta.z) <= SAME_POSITION_EPSILON,
		"1x2OnlyInOne should not follow SlotZone on Z when not slotted. before=%s after=%s delta=%s" % [only_in_one_1x2_before_slot_zone_move, only_in_one_1x2_after_slot_zone_move, only_in_one_1x2_slot_zone_move_delta])

	await _cleanup_scene(scene)


func _wait_for_rigidbody_to_settle(rigidbody: RigidBody3D, timeout_sec: float) -> bool:
	return await wait_until(func() -> bool:
		var current_velocity: float = rigidbody.linear_velocity.length()
		return rigidbody.sleeping or current_velocity <= SETTLE_VELOCITY_EPSILON
	, timeout_sec, "Timed out waiting for rigidbody to settle")


func _horizontal_distance(from_position: Vector3, to_position: Vector3) -> float:
	var from_horizontal := Vector2(from_position.x, from_position.z)
	var to_horizontal := Vector2(to_position.x, to_position.z)
	return from_horizontal.distance_to(to_horizontal)


func _is_rotation_near_zero(rotation_radians: Vector3, epsilon: float) -> bool:
	return abs(rotation_radians.x) <= epsilon \
		and abs(rotation_radians.y) <= epsilon \
		and abs(rotation_radians.z) <= epsilon


func _cleanup_scene(scene: Node) -> void:
	if scene == null:
		return

	scene.queue_free()
	await wait_process_frames(3)
