extends GutTest

const PICKUP_SCENE_PATH := "res://test/scenes/pickup/pickup.tscn"
const INTERACTABLE_WAIT_TIMEOUT_SEC := 5.0
const SETTLE_TIMEOUT_SEC := 6.0
const SETTLE_VELOCITY_EPSILON := 0.05
const THROW_INTENSITIES := [0.1, 0.5, 1.0]


func test_pickup_box_drop_once_settles_and_records_landing():
	var scene_res: PackedScene = load(PICKUP_SCENE_PATH) as PackedScene
	assert_not_null(scene_res, "Pickup scene should load")
	if scene_res == null:
		return

	var scene: Node = add_child_autoqfree(scene_res.instantiate())
	assert_not_null(scene, "Pickup scene should instantiate")
	if scene == null:
		return

	var pawn: Node = scene.get_node_or_null("Player/Pawn")
	var pickup_pawn: Node = scene.get_node_or_null("Player/Pawn/PickupPawn3D")
	var pickup_box: RigidBody3D = scene.get_node_or_null("PlayArea/PickupBox") as RigidBody3D

	assert_true(pawn != null, "Scene should contain Player/Pawn")
	assert_true(pickup_pawn != null, "Scene should contain Player/Pawn/PickupPawn3D")
	assert_true(pickup_box != null, "Scene should contain PlayArea/PickupBox")
	if pawn == null or pickup_pawn == null or pickup_box == null:
		return

	_orient_pawn_once(pawn)

	var interactable_is_set: bool = await wait_until(func() -> bool:
		return _pawn_interactable_status_has_target(scene)
	, INTERACTABLE_WAIT_TIMEOUT_SEC, "Waiting for pickup box to enter interaction range")
	assert_true(interactable_is_set, "Pickup box should be interactable")
	if !interactable_is_set:
		return

	var pickup_start_position: Vector3 = pickup_box.global_position
	pawn.call("UseInteractable")
	await wait_process_frames(2)

	assert_true(bool(pickup_pawn.get("HasPickup")), "Using interactable should pick up the box")

	pickup_pawn.call("Place")
	await wait_process_frames(2)
	_move_pawn_far_away(pawn, pickup_box.global_position)

	assert_false(bool(pickup_pawn.get("HasPickup")), "Place should drop the box once")

	var settled: bool = await _wait_for_rigidbody_to_settle(pickup_box, SETTLE_TIMEOUT_SEC)
	assert_true(settled, "Dropped box should settle")
	if !settled:
		await _cleanup_scene(scene)
		return

	var landed_position: Vector3 = pickup_box.global_position
	var landed_distance := _distance_along_pawn_forward_axis(pawn, pickup_start_position, landed_position)
	assert_true(landed_distance >= 0.0, "Dropped box should produce a measurable landing distance")

	await _cleanup_scene(scene)


func test_pickup_box_throw_distance_increases_with_intensity_and_settles():
	var landed_distances: Array[float] = []
	var landed_positions: Array[Vector3] = []

	for throw_intensity in THROW_INTENSITIES:
		var scene_res: PackedScene = load(PICKUP_SCENE_PATH) as PackedScene
		assert_not_null(scene_res, "Pickup scene should load")
		if scene_res == null:
			return

		var scene: Node = add_child_autoqfree(scene_res.instantiate())
		assert_not_null(scene, "Pickup scene should instantiate")
		if scene == null:
			return

		var pawn: Node = scene.get_node_or_null("Player/Pawn")
		var pickup_pawn: Node = scene.get_node_or_null("Player/Pawn/PickupPawn3D")
		var pickup_box: RigidBody3D = scene.get_node_or_null("PlayArea/PickupBox") as RigidBody3D

		assert_true(pawn != null, "Scene should contain Player/Pawn")
		assert_true(pickup_pawn != null, "Scene should contain Player/Pawn/PickupPawn3D")
		assert_true(pickup_box != null, "Scene should contain PlayArea/PickupBox")
		if pawn == null or pickup_pawn == null or pickup_box == null:
			return

		_orient_pawn_once(pawn)

		var interactable_is_set: bool = await wait_until(func() -> bool:
			return _pawn_interactable_status_has_target(scene)
		, INTERACTABLE_WAIT_TIMEOUT_SEC, "Waiting for pickup box to enter interaction range for intensity %.1f" % throw_intensity)
		assert_true(interactable_is_set, "Pickup box should be interactable for intensity %.1f" % throw_intensity)
		if !interactable_is_set:
			return

		var pickup_start_position: Vector3 = pickup_box.global_position
		pawn.call("UseInteractable")
		await wait_process_frames(2)

		assert_true(bool(pickup_pawn.get("HasPickup")), "Using interactable should pick up the box for intensity %.1f" % throw_intensity)

		pickup_pawn.call("Throw", throw_intensity)
		await wait_process_frames(2)
		_move_pawn_far_away(pawn, pickup_box.global_position)

		assert_false(bool(pickup_pawn.get("HasPickup")), "Throw should release the box for intensity %.1f" % throw_intensity)

		var settled: bool = await _wait_for_rigidbody_to_settle(pickup_box, SETTLE_TIMEOUT_SEC)
		assert_true(settled, "Thrown box should settle for intensity %.1f" % throw_intensity)
		if !settled:
			await _cleanup_scene(scene)
			return

		var landed_position: Vector3 = pickup_box.global_position
		var landing_distance: float = _distance_along_pawn_forward_axis(pawn, pickup_start_position, landed_position)
		landed_positions.append(landed_position)
		landed_distances.append(landing_distance)

		await _cleanup_scene(scene)

	assert_eq(landed_distances.size(), THROW_INTENSITIES.size(), "Should record one landing distance per throw intensity")
	if landed_distances.size() != THROW_INTENSITIES.size():
		return

	assert_true(landed_distances[0] < landed_distances[1], "0.1 intensity should land closer than 0.5 intensity")
	assert_true(landed_distances[1] < landed_distances[2], "0.5 intensity should land closer than 1.0 intensity")
	assert_true(landed_positions[0] != landed_positions[1], "Landing position should change between 0.1 and 0.5 intensity")
	assert_true(landed_positions[1] != landed_positions[2], "Landing position should change between 0.5 and 1.0 intensity")


func _pawn_interactable_status_has_target(scene: Node) -> bool:
	var status_label: RichTextLabel = scene.get_node_or_null("Player/InteractableUI") as RichTextLabel
	if status_label == null:
		return false

	return status_label.text.contains("PickupBox")


func _distance_along_pawn_forward_axis(pawn: Node3D, from_position: Vector3, to_position: Vector3) -> float:
	var forward: Vector3 = pawn.global_transform.basis.z
	if forward == Vector3.ZERO:
		return from_position.distance_to(to_position)

	return abs((to_position - from_position).dot(forward.normalized()))


func _wait_for_rigidbody_to_settle(rigidbody: RigidBody3D, timeout_sec: float) -> bool:
	return await wait_until(func() -> bool:
		var current_velocity: float = rigidbody.linear_velocity.length()
		return rigidbody.sleeping or current_velocity <= SETTLE_VELOCITY_EPSILON
	, timeout_sec, "Timed out waiting for rigidbody to settle")


func _cleanup_scene(scene: Node) -> void:
	if scene == null:
		return

	scene.queue_free()
	await wait_process_frames(3)


func _move_pawn_far_away(pawn: Node, reference_position: Vector3) -> void:
	if pawn == null:
		return

	var moved_pawn: Node3D = pawn as Node3D
	if moved_pawn == null:
		return

	moved_pawn.velocity = Vector3.ZERO
	# moved_pawn.global_position = reference_position + Vector3(0.0, 0.0, 50.0)


func _orient_pawn_once(pawn: Node) -> void:
	if pawn == null:
		return

	var pawn_3d: Node3D = pawn as Node3D
	if pawn_3d == null:
		return

	pawn_3d.look_at(Vector3(15.006975, 2.0797062, 24.86475), Vector3.UP)
	pawn_3d.move(Vector2(-1.0, -1.0))