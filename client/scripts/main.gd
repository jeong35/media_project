extends Control

const API_BASE := "http://127.0.0.1:5050"

@onready var title_screen: VBoxContainer = $Page/TitleScreen
@onready var new_game_button: Button = $Page/TitleScreen/NewGameButton
@onready var connection_status: Label = $Page/TitleScreen/ConnectionStatus
@onready var game_screen: VBoxContainer = $Page/GameScreen
@onready var act_label: Label = $Page/GameScreen/ActLabel
@onready var narration: Label = $Page/GameScreen/Narration
@onready var npc_name: Label = $Page/GameScreen/NpcName
@onready var npc_dialogue: Label = $Page/GameScreen/NpcDialogue
@onready var player_input: TextEdit = $Page/GameScreen/PlayerInput
@onready var send_button: Button = $Page/GameScreen/SendButton
@onready var feedback: Label = $Page/GameScreen/Feedback
@onready var choices: VBoxContainer = $Page/GameScreen/Choices
@onready var choice_buttons: Array[Button] = [
	$Page/GameScreen/Choices/Choice0,
	$Page/GameScreen/Choices/Choice1,
	$Page/GameScreen/Choices/Choice2
]
@onready var farming_screen: VBoxContainer = $Page/FarmingScreen
@onready var farming_title: Label = $Page/FarmingScreen/Title
@onready var farming_instructions: Label = $Page/FarmingScreen/Instructions
@onready var farming_progress: Label = $Page/FarmingScreen/Progress
@onready var obstacle_buttons: Array[Button] = [
	$Page/FarmingScreen/Obstacles/Rock1,
	$Page/FarmingScreen/Obstacles/Weed1,
	$Page/FarmingScreen/Obstacles/Rock2
]
@onready var seed_buttons: Array[Button] = [
	$Page/FarmingScreen/SeedCells/Seed0,
	$Page/FarmingScreen/SeedCells/Seed1,
	$Page/FarmingScreen/SeedCells/Seed2,
	$Page/FarmingScreen/SeedCells/Seed3
]
@onready var water_buttons: Array[Button] = [
	$Page/FarmingScreen/WaterChoices/Community,
	$Page/FarmingScreen/WaterChoices/Ward,
	$Page/FarmingScreen/WaterChoices/Market
]
@onready var method_buttons: Array[Button] = [
	$Page/FarmingScreen/MethodChoices/Manual,
	$Page/FarmingScreen/MethodChoices/Magic,
	$Page/FarmingScreen/MethodChoices/Tools
]
@onready var farming_feedback: Label = $Page/FarmingScreen/Feedback
@onready var farming_finish_button: Button = $Page/FarmingScreen/FinishButton
@onready var ending_screen: VBoxContainer = $Page/EndingScreen
@onready var occupation: Label = $Page/EndingScreen/Occupation
@onready var ending_title: Label = $Page/EndingScreen/EndingTitle
@onready var outcome: Label = $Page/EndingScreen/Outcome
@onready var records: Label = $Page/EndingScreen/Records
@onready var share_line: Label = $Page/EndingScreen/ShareLine
@onready var restart_button: Button = $Page/EndingScreen/RestartButton
@onready var http_request: HTTPRequest = $HealthRequest

var session_id := ""
var current_act := 1
var current_npc_id := ""
var request_kind := ""
var choice_ids: Array[String] = []
var removed_obstacles := 0
var correct_seeds := 0
var water_target := "community"
var farming_method := "manual"
var smoke_test := false
var smoke_runs := 0


func _ready() -> void:
	new_game_button.pressed.connect(_start_new_game)
	send_button.pressed.connect(_submit_dialogue)
	restart_button.pressed.connect(_start_new_game)
	for index in choice_buttons.size():
		choice_buttons[index].pressed.connect(_submit_choice.bind(index))
	for index in obstacle_buttons.size():
		obstacle_buttons[index].pressed.connect(_remove_obstacle.bind(index))
	for index in seed_buttons.size():
		seed_buttons[index].pressed.connect(_plant_seed.bind(index))
	for index in water_buttons.size():
		water_buttons[index].pressed.connect(_select_water.bind(index))
	for index in method_buttons.size():
		method_buttons[index].pressed.connect(_select_method.bind(index))
	farming_finish_button.pressed.connect(_submit_farming)
	http_request.request_completed.connect(_on_request_completed)
	smoke_test = "--smoke-test" in OS.get_cmdline_user_args()
	if smoke_test:
		_start_new_game()


func _start_new_game() -> void:
	session_id = ""
	choice_ids.clear()
	feedback.text = ""
	player_input.text = ""
	player_input.show()
	send_button.show()
	choices.hide()
	title_screen.show()
	game_screen.hide()
	farming_screen.hide()
	ending_screen.hide()
	connection_status.text = "새 세션을 만드는 중…"
	new_game_button.disabled = true
	_request_json("start", HTTPClient.METHOD_POST, "/api/v1/sessions", {"mode": "mock"})


func _submit_dialogue() -> void:
	var text := player_input.text.strip_edges()
	if text.is_empty():
		feedback.text = "%s에게 할 말을 입력해 주세요." % npc_name.text
		return
	send_button.disabled = true
	player_input.editable = false
	feedback.text = "답변을 준비하고 있습니다…"
	_request_json("dialogue", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/dialogues" % session_id, {
		"npcId": current_npc_id,
		"text": text,
		"idempotencyKey": _new_uuid()
	})


func _fetch_choices() -> void:
	_request_json("choices", HTTPClient.METHOD_GET, "/api/v1/sessions/%s/choices" % session_id)


func _submit_choice(index: int) -> void:
	if index >= choice_ids.size():
		return
	for button in choice_buttons:
		button.disabled = true
	feedback.text = "선택 결과를 계산하고 있습니다…"
	_request_json("choice", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/choices" % session_id, {
		"choiceId": choice_ids[index],
		"idempotencyKey": _new_uuid()
	})


func _request_json(kind: String, method: HTTPClient.Method, path: String, payload: Dictionary = {}) -> void:
	request_kind = kind
	var body := "" if method == HTTPClient.METHOD_GET else JSON.stringify(payload)
	var headers := PackedStringArray(["Content-Type: application/json"])
	var error := http_request.request(API_BASE + path, headers, method, body)
	if error != OK:
		_show_error("요청을 시작하지 못했습니다. 오류 코드: %s" % error)


func _on_request_completed(result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	if result != HTTPRequest.RESULT_SUCCESS:
		_show_error("서버에 연결할 수 없습니다. 로컬 서버를 먼저 실행해 주세요.")
		return
	var payload: Variant = JSON.parse_string(body.get_string_from_utf8())
	if not payload is Dictionary:
		_show_error("서버 응답을 읽을 수 없습니다.")
		return
	if response_code < 200 or response_code >= 300:
		var error_payload: Dictionary = payload.get("error", {})
		_show_error(error_payload.get("message", "서버 요청에 실패했습니다."))
		return

	match request_kind:
		"start": _show_session(payload)
		"dialogue": _show_dialogue(payload)
		"choices": _show_choices(payload)
		"choice": _show_choice_result(payload)
		"farming": _show_farming_result(payload)


func _show_session(payload: Dictionary) -> void:
	session_id = str(payload.get("sessionId", ""))
	_show_act(payload)


func _show_act(payload: Dictionary, transition_feedback := "") -> void:
	current_act = int(payload.get("act", 1))
	var scene: Dictionary = payload.get("scene", {})
	current_npc_id = str(scene.get("npcId", ""))
	act_label.text = "ACT %s · %s" % [current_act, payload.get("timeLabel", "")]
	narration.text = scene.get("narration", "")
	npc_name.text = scene.get("npcName", "")
	npc_dialogue.text = payload.get("openingDialogue", "")
	player_input.placeholder_text = "%s에게 답하세요. (최대 300자)" % npc_name.text
	feedback.text = transition_feedback
	player_input.text = ""
	player_input.show()
	send_button.show()
	choices.hide()
	title_screen.hide()
	farming_screen.hide()
	ending_screen.hide()
	game_screen.show()
	player_input.editable = true
	send_button.disabled = false
	new_game_button.disabled = false
	if smoke_test:
		match current_act:
			1: player_input.text = "주민들이 다치지 않는 방법부터 찾아보자."
			2: player_input.text = "징후를 먼저 계산하고 실행할 수 있는 계획을 세우자."
			_: player_input.text = "주민들의 오늘을 지키는 일도 직접 돕겠다."
		_submit_dialogue()


func _show_dialogue(payload: Dictionary) -> void:
	npc_dialogue.text = payload.get("dialogue", "")
	feedback.text = "%s\n%s" % [payload.get("feedback", ""), payload.get("careerFeedback", "")]
	player_input.hide()
	send_button.hide()
	_fetch_choices()


func _show_choices(payload: Dictionary) -> void:
	choice_ids.clear()
	var server_choices: Array = payload.get("choices", [])
	for index in choice_buttons.size():
		choice_buttons[index].disabled = false
		choice_buttons[index].visible = index < server_choices.size()
		if index < server_choices.size():
			var choice: Dictionary = server_choices[index]
			choice_ids.append(str(choice.get("choiceId", "")))
			choice_buttons[index].text = choice.get("label", "")
	choices.show()
	if smoke_test:
		var smoke_choice := 2 if current_act == 1 else 1 if current_act == 2 else 0
		_submit_choice(smoke_choice)


func _show_choice_result(payload: Dictionary) -> void:
	var next_activity: Variant = payload.get("nextActivity")
	if next_activity is Dictionary:
		_show_farming(next_activity, "%s\n%s" % [payload.get("resultNarration", ""), payload.get("feedback", "")])
		return
	var next_act: Variant = payload.get("nextAct")
	if next_act is Dictionary:
		var transition_feedback := "%s\n%s" % [payload.get("resultNarration", ""), payload.get("feedback", "")]
		_show_act(next_act, transition_feedback)
		return
	_show_ending(payload)


func _show_farming(payload: Dictionary, transition_feedback: String) -> void:
	game_screen.hide()
	ending_screen.hide()
	farming_screen.show()
	farming_title.text = payload.get("title", "마력보리 공동밭")
	farming_instructions.text = payload.get("instructions", "")
	farming_feedback.text = transition_feedback
	removed_obstacles = 0
	correct_seeds = 0
	water_target = "community"
	farming_method = "manual"
	var obstacle_labels := ["돌 치우기", "잡초 뽑기", "돌 치우기"]
	var seed_labels := ["비옥한 칸 A", "비옥한 칸 B", "비옥한 칸 C", "메마른 칸"]
	for index in obstacle_buttons.size():
		obstacle_buttons[index].disabled = false
		obstacle_buttons[index].text = obstacle_labels[index]
	for index in seed_buttons.size():
		seed_buttons[index].disabled = false
		seed_buttons[index].text = seed_labels[index]
	for index in water_buttons.size():
		water_buttons[index].button_pressed = index == 0
	for index in method_buttons.size():
		method_buttons[index].button_pressed = index == 0
	farming_finish_button.disabled = false
	_update_farming_progress()
	if smoke_test:
		for index in obstacle_buttons.size(): _remove_obstacle(index)
		for index in 3: _plant_seed(index)
		_submit_farming()


func _remove_obstacle(index: int) -> void:
	if obstacle_buttons[index].disabled:
		return
	obstacle_buttons[index].disabled = true
	obstacle_buttons[index].text = "정리 완료"
	removed_obstacles += 1
	_update_farming_progress()


func _plant_seed(index: int) -> void:
	if seed_buttons[index].disabled:
		return
	seed_buttons[index].disabled = true
	if index < 3:
		seed_buttons[index].text = "씨앗 배치 완료"
		correct_seeds += 1
	else:
		seed_buttons[index].text = "싹트기 어려운 땅"
	_update_farming_progress()


func _select_water(index: int) -> void:
	var targets := ["community", "ward", "market"]
	water_target = targets[index]
	for button_index in water_buttons.size():
		water_buttons[button_index].button_pressed = button_index == index


func _select_method(index: int) -> void:
	var methods := ["manual", "magic", "tools"]
	farming_method = methods[index]
	for button_index in method_buttons.size():
		method_buttons[button_index].button_pressed = button_index == index


func _update_farming_progress() -> void:
	farming_progress.text = "장애물 %d/3 · 올바른 씨앗 %d/3" % [removed_obstacles, correct_seeds]


func _submit_farming() -> void:
	farming_finish_button.disabled = true
	farming_feedback.text = "농사 결과를 계산하고 있습니다…"
	_request_json("farming", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/farming" % session_id, {
		"removedObstacles": removed_obstacles,
		"correctSeeds": correct_seeds,
		"waterTarget": water_target,
		"method": farming_method,
		"idempotencyKey": _new_uuid()
	})


func _show_farming_result(payload: Dictionary) -> void:
	farming_screen.hide()
	_show_ending(payload)


func _show_ending(payload: Dictionary) -> void:
	var ending: Dictionary = payload.get("ending", {})
	var ending_records: Array = ending.get("records", [])
	occupation.text = ending.get("occupationName", "")
	ending_title.text = ending.get("title", "")
	outcome.text = "%s\n%s" % [ending.get("territoryOutcome", ""), payload.get("feedback", "")]
	records.text = "결정적 기록\n• " + "\n• ".join(ending_records)
	share_line.text = ending.get("shareLine", "")
	game_screen.hide()
	farming_screen.hide()
	ending_screen.show()
	if smoke_test:
		smoke_runs += 1
		if smoke_runs == 1:
			call_deferred("_start_new_game")
		else:
			print("MOCK_SLICE_RESTART_OK: %s" % JSON.stringify(payload))
			get_tree().quit()


func _show_error(message: String) -> void:
	connection_status.text = message
	feedback.text = message
	new_game_button.disabled = false
	send_button.disabled = false
	player_input.editable = true
	farming_finish_button.disabled = false
	push_warning(message)
	if smoke_test:
		get_tree().quit(1)


func _new_uuid() -> String:
	var bytes := Crypto.new().generate_random_bytes(16)
	bytes[6] = (bytes[6] & 0x0f) | 0x40
	bytes[8] = (bytes[8] & 0x3f) | 0x80
	var hex := bytes.hex_encode()
	return "%s-%s-%s-%s-%s" % [hex.substr(0, 8), hex.substr(8, 4), hex.substr(12, 4), hex.substr(16, 4), hex.substr(20, 12)]
