extends Control

const API_BASE := "http://127.0.0.1:5050"
const SESSION_FILE := "user://last_session.cfg"
const SMOKE_SESSION_FILE := "user://smoke_last_session.cfg"
const LOADING_CUES_PATH := "res://data/dialogue_loading_cues.json"
const DIALOGUE_PAGE_CHARACTER_LIMIT := 120
const PASSIVE_DIALOGUE_PAGE_CHARACTER_LIMIT := 240
const STORY_PAGE_CHARACTER_LIMIT := 240
const TYPEWRITER_CHARACTERS_PER_SECOND := 36.0
const LOADING_CUE_DELAY_SECONDS := 0.65
const LOADING_FOLLOWUP_DELAY_SECONDS := 5.0
@onready var title_screen: VBoxContainer = $Page/TitleScreen
@onready var page_background_image: TextureRect = $PageBackgroundImage
@onready var page_background_shade: ColorRect = $PageBackgroundShade
@onready var live_mode_toggle: CheckButton = $Page/TitleScreen/LiveModeToggle
@onready var new_game_button: Button = $Page/TitleScreen/NewGameButton
@onready var continue_button: Button = $Page/TitleScreen/ContinueButton
@onready var connection_status: Label = $Page/TitleScreen/ConnectionStatus
@onready var game_screen: Control = $GameScreen
@onready var background_fallback: ColorRect = $GameScreen/BackgroundFallback
@onready var background_image: TextureRect = $GameScreen/BackgroundImage
@onready var character_image: TextureRect = $GameScreen/CharacterImage
@onready var character_placeholder: PanelContainer = $GameScreen/CharacterPlaceholder
@onready var story_fade_overlay: ColorRect = $GameScreen/StoryFadeOverlay
@onready var story_red_flash_overlay: ColorRect = $GameScreen/StoryRedFlashOverlay
@onready var act_label: Label = $GameScreen/SceneHeader/Content/ActLabel
@onready var scene_id_label: Label = $GameScreen/SceneHeader/Content/SceneName
@onready var narration: Label = $GameScreen/SceneHeader/Content/Narration
@onready var npc_name: Label = $GameScreen/DialogueMargin/DialogueShell/NameRow/NpcName
@onready var dialogue_viewport: Control = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/DialogueViewport
@onready var npc_dialogue: Label = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/DialogueViewport/NpcDialogue
@onready var dialogue_page_row: HBoxContainer = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/PageRow
@onready var dialogue_page_position: Label = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/PageRow/PagePosition
@onready var dialogue_page_advance_button: Button = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/PageRow/PageAdvanceButton
@onready var npc_initial: Label = $GameScreen/CharacterPlaceholder/Content/Initial
@onready var npc_emotion: Label = $GameScreen/DialogueMargin/DialogueShell/NameRow/Emotion
@onready var player_input: TextEdit = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/ActionRow/PlayerInput
@onready var send_button: Button = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/ActionRow/SendButton
@onready var action_row: HBoxContainer = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/ActionRow
@onready var history_row: HBoxContainer = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow
@onready var status_label: Label = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/StatusLabel
@onready var history_navigation_group: HBoxContainer = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/NavigationGroup
@onready var history_previous_button: Button = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/NavigationGroup/PreviousButton
@onready var history_position: Label = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/NavigationGroup/Position
@onready var history_next_button: Button = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/NavigationGroup/NextButton
@onready var dialogue_margin: MarginContainer = $GameScreen/DialogueMargin
@onready var next_choices_button: Button = $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/ActionRow/NextChoicesButton
@onready var story_margin: MarginContainer = $GameScreen/StoryMargin
@onready var story_speaker: Label = $GameScreen/StoryMargin/StoryShell/NameRow/Speaker
@onready var story_text: Label = $GameScreen/StoryMargin/StoryShell/StoryBox/SafeArea/Content/Text
@onready var story_continue_button: Button = $GameScreen/StoryMargin/StoryShell/StoryBox/SafeArea/Content/ContinueButton
@onready var choice_margin: MarginContainer = $GameScreen/ChoiceMargin
@onready var choices: VBoxContainer = $GameScreen/ChoiceMargin/ChoiceBox/Content/Choices
@onready var choice_buttons: Array[Button] = [
	$GameScreen/ChoiceMargin/ChoiceBox/Content/Choices/Choice0,
	$GameScreen/ChoiceMargin/ChoiceBox/Content/Choices/Choice1,
	$GameScreen/ChoiceMargin/ChoiceBox/Content/Choices/Choice2
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
@onready var final_choice_screen: VBoxContainer = $Page/FinalChoiceScreen
@onready var final_act_label: Label = $Page/FinalChoiceScreen/ActLabel
@onready var final_narration: Label = $Page/FinalChoiceScreen/Narration
@onready var final_timer_label: Label = $Page/FinalChoiceScreen/TimerLabel
@onready var final_timer_bar: ProgressBar = $Page/FinalChoiceScreen/TimerBar
@onready var final_choice_buttons: Array[Button] = [
	$Page/FinalChoiceScreen/Choices/Choice0,
	$Page/FinalChoiceScreen/Choices/Choice1,
	$Page/FinalChoiceScreen/Choices/Choice2,
	$Page/FinalChoiceScreen/Choices/Choice3
]
@onready var final_feedback: Label = $Page/FinalChoiceScreen/Feedback
@onready var ending_screen: ScrollContainer = $Page/EndingScreen
@onready var occupation: Label = $Page/EndingScreen/Content/Occupation
@onready var ending_title: Label = $Page/EndingScreen/Content/EndingTitle
@onready var career_summary: Label = $Page/EndingScreen/Content/CareerSummary
@onready var outcome: Label = $Page/EndingScreen/Content/Outcome
@onready var epilogue: Label = $Page/EndingScreen/Content/Epilogue
@onready var records: Label = $Page/EndingScreen/Content/Records
@onready var share_line: Label = $Page/EndingScreen/Content/ShareLine
@onready var restart_button: Button = $Page/EndingScreen/Content/RestartButton
@onready var http_request: HTTPRequest = $HealthRequest
@onready var checkpoint_request: HTTPRequest = $CheckpointRequest
@onready var final_choice_timer: Timer = $FinalChoiceTimer
@onready var typewriter_timer: Timer = $TypewriterTimer
@onready var loading_cue_timer: Timer = $LoadingCueTimer
@onready var loading_followup_timer: Timer = $LoadingFollowupTimer
@onready var loading_pulse_timer: Timer = $LoadingPulseTimer

var session_id := ""
var current_act := 1
var current_npc_id := ""
var current_background_id := ""
var current_opening_emotion := "neutral"
var request_kind := ""
var primary_request_active := false
var request_completion_dispatching := false
var decision_bridge_generated := false
var story_beats: Array = []
var story_index := -1
var story_pages: Array[String] = []
var story_page_index := -1
var story_completion := ""
var story_begin_auto_advance := false
var pending_finale_beats: Array = []
var finale_narration_override := ""
var choice_ids: Array[String] = []
var removed_obstacles := 0
var correct_seeds := 0
var removed_obstacle_indexes: Array[int] = []
var planted_seed_indexes: Array[int] = []
var water_target := "community"
var farming_method := "manual"
var smoke_test := false
var smoke_timeout_test := false
var smoke_save_test := false
var smoke_save_dialogue_test := false
var smoke_save_choice_test := false
var smoke_resume_test := false
var smoke_save_farming_progress_test := false
var smoke_save_final_timer_test := false
var smoke_resume_timer_test := false
var smoke_history_test := false
var smoke_dialogue_ui_test := false
var smoke_dialogue_layout_test := false
var smoke_passive_dialogue_layout_test := false
var smoke_story_layout_test := false
var smoke_loading_timing_test := false
var smoke_button_assets_test := false
var smoke_act1_opening_test := false
var smoke_runs := 0
var final_time_limit := 0.0
var timeout_choice_id := ""
var saved_session_id := ""
var mock_failure_scenario := "none"
var checkpoint_request_active := false
var checkpoint_dirty := false
var quit_after_checkpoint := false
var dialogue_history: Array = []
var dialogue_history_index := -1
var pending_player_text := ""
var current_dialogue_remaining := 0
var current_choices_available := false
var latest_status_text := ""
var history_navigation_locked := false
var resume_phase_after_history := ""
var resume_current_dialogue := ""
var dialogue_pages: Array[String] = []
var dialogue_page_index := 0
var dialogue_page_animate_advances := false
var typewriter_character_index := 0
var typewriter_active := false
var dialogue_request_in_flight := false
var dialogue_request_context: Dictionary = {}
var loading_cue_data: Dictionary = {}
var loading_cue_indices: Dictionary = {}
var loading_cue_selection_count := 0
var last_loading_cue := ""
var active_loading_cue := ""
var loading_pulse_step := 0
var last_story_background_id := ""


func _ready() -> void:
	_load_loading_cues()
	typewriter_timer.wait_time = 1.0 / TYPEWRITER_CHARACTERS_PER_SECOND
	loading_cue_timer.wait_time = LOADING_CUE_DELAY_SECONDS
	loading_followup_timer.wait_time = LOADING_FOLLOWUP_DELAY_SECONDS
	new_game_button.pressed.connect(_start_new_game)
	continue_button.pressed.connect(_continue_game)
	live_mode_toggle.toggled.connect(_on_live_mode_toggled)
	player_input.gui_input.connect(_on_player_input_gui_input)
	send_button.pressed.connect(_submit_dialogue)
	next_choices_button.pressed.connect(_on_next_choices_pressed)
	history_previous_button.pressed.connect(_show_previous_dialogue)
	history_next_button.pressed.connect(_show_next_dialogue)
	dialogue_page_advance_button.pressed.connect(_advance_dialogue_page)
	story_continue_button.pressed.connect(_advance_story)
	restart_button.pressed.connect(_start_new_game)
	for index in choice_buttons.size():
		choice_buttons[index].pressed.connect(_submit_choice.bind(index))
	for index in final_choice_buttons.size():
		final_choice_buttons[index].pressed.connect(_submit_final_choice.bind(index))
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
	checkpoint_request.request_completed.connect(_on_checkpoint_completed)
	final_choice_timer.timeout.connect(_on_final_choice_timeout)
	typewriter_timer.timeout.connect(_on_typewriter_tick)
	loading_cue_timer.timeout.connect(_show_initial_loading_cue)
	loading_followup_timer.timeout.connect(_show_followup_loading_cue)
	loading_pulse_timer.timeout.connect(_update_loading_pulse)
	var arguments := OS.get_cmdline_user_args()
	smoke_timeout_test = "--smoke-timeout" in arguments
	smoke_save_dialogue_test = "--smoke-save-dialogue" in arguments
	smoke_save_choice_test = "--smoke-save-choice" in arguments
	smoke_save_test = "--smoke-save" in arguments or smoke_save_dialogue_test or smoke_save_choice_test
	smoke_save_farming_progress_test = "--smoke-save-farming-progress" in arguments
	smoke_save_final_timer_test = "--smoke-save-final-timer" in arguments
	smoke_resume_timer_test = "--smoke-resume-timer" in arguments
	smoke_history_test = "--smoke-history" in arguments
	smoke_dialogue_ui_test = "--smoke-dialogue-ui" in arguments
	smoke_dialogue_layout_test = "--smoke-dialogue-layout" in arguments
	smoke_passive_dialogue_layout_test = "--smoke-passive-dialogue-layout" in arguments
	smoke_story_layout_test = "--smoke-story-layout" in arguments
	smoke_loading_timing_test = "--smoke-loading-timing" in arguments
	smoke_button_assets_test = "--smoke-button-assets" in arguments
	smoke_act1_opening_test = "--smoke-act1-opening" in arguments
	smoke_resume_test = "--smoke-resume" in arguments or smoke_resume_timer_test
	if "--smoke-dialogue-transient" in arguments:
		mock_failure_scenario = "transient_once"
	elif "--smoke-dialogue-invalid" in arguments:
		mock_failure_scenario = "invalid_payload"
	elif "--smoke-dialogue-timeout" in arguments:
		mock_failure_scenario = "timeout"
	smoke_test = "--smoke-test" in arguments or smoke_timeout_test or smoke_save_test or smoke_resume_test \
		or smoke_save_farming_progress_test or smoke_save_final_timer_test \
		or smoke_history_test or mock_failure_scenario != "none"
	if smoke_dialogue_ui_test:
		call_deferred("_run_dialogue_ui_smoke")
		return
	if smoke_dialogue_layout_test:
		call_deferred("_run_dialogue_layout_smoke")
		return
	if smoke_passive_dialogue_layout_test:
		call_deferred("_run_passive_dialogue_layout_smoke")
		return
	if smoke_story_layout_test:
		call_deferred("_run_story_layout_smoke")
		return
	if smoke_loading_timing_test:
		call_deferred("_run_loading_timing_smoke")
		return
	if smoke_button_assets_test:
		call_deferred("_run_button_assets_smoke")
		return
	_load_saved_session_reference()
	if smoke_act1_opening_test:
		_start_new_game()
		return
	if smoke_resume_test:
		_continue_game()
	elif smoke_test:
		_start_new_game()


func _process(_delta: float) -> void:
	if final_choice_timer.is_stopped():
		return
	var remaining := final_choice_timer.time_left
	final_timer_label.text = "결정까지 %.1f초" % remaining
	final_timer_bar.value = remaining


func _start_new_game() -> void:
	_stop_dialogue_loading()
	_stop_typewriter()
	var provider_mode := "live" if live_mode_toggle.button_pressed and not smoke_test else "mock"
	var failure_scenario := mock_failure_scenario if provider_mode == "mock" else "none"
	session_id = ""
	choice_ids.clear()
	pending_finale_beats.clear()
	finale_narration_override = ""
	story_beats.clear()
	story_margin.hide()
	status_label.text = ""
	dialogue_history.clear()
	dialogue_history_index = -1
	pending_player_text = ""
	history_navigation_locked = false
	resume_phase_after_history = ""
	resume_current_dialogue = ""
	dialogue_request_context = {}
	player_input.text = ""
	player_input.show()
	send_button.show()
	choices.hide()
	title_screen.show()
	_hide_page_background()
	game_screen.hide()
	farming_screen.hide()
	final_choice_screen.hide()
	ending_screen.hide()
	final_choice_timer.stop()
	connection_status.text = "Live API 세션을 만드는 중…" if provider_mode == "live" else "Mock 세션을 만드는 중…"
	new_game_button.disabled = true
	continue_button.disabled = true
	live_mode_toggle.disabled = true
	_request_json("start", HTTPClient.METHOD_POST, "/api/v1/sessions", {
		"mode": provider_mode,
		"failureScenario": failure_scenario
	})


func _continue_game() -> void:
	if saved_session_id.is_empty():
		connection_status.text = "이어갈 수 있는 저장 세션이 없습니다."
		if smoke_resume_test:
			get_tree().quit(1)
		return
	session_id = saved_session_id
	connection_status.text = "저장된 세션을 불러오는 중…"
	new_game_button.disabled = true
	continue_button.disabled = true
	live_mode_toggle.disabled = true
	_request_json("resume", HTTPClient.METHOD_GET, "/api/v1/sessions/%s" % session_id)


func _on_live_mode_toggled(enabled: bool) -> void:
	connection_status.text = "Live API 모드 · 실제 호출 비용이 발생합니다." if enabled else "Mock 모드 · 로컬 서버 필요"


func _on_player_input_gui_input(event: InputEvent) -> void:
	if not event is InputEventKey:
		return
	var key_event := event as InputEventKey
	if not key_event.pressed or key_event.echo or key_event.shift_pressed:
		return
	if key_event.keycode not in [KEY_ENTER, KEY_KP_ENTER]:
		return
	player_input.accept_event()
	if player_input.editable and not send_button.disabled:
		_submit_dialogue()


func _submit_dialogue() -> void:
	if request_completion_dispatching:
		call_deferred("_submit_dialogue")
		return
	if primary_request_active:
		return
	if dialogue_request_in_flight:
		return
	var text := player_input.text.strip_edges()
	if text.is_empty():
		_set_dialogue_status("입력이 비어 있습니다.")
		return
	pending_player_text = text
	send_button.disabled = true
	player_input.editable = false
	_set_dialogue_status("답변을 준비하고 있습니다…")
	_start_dialogue_loading()
	_request_json("dialogue", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/dialogues" % session_id, {
		"npcId": current_npc_id,
		"text": text,
		"idempotencyKey": _new_uuid()
	})


func _on_next_choices_pressed() -> void:
	if primary_request_active or request_completion_dispatching or dialogue_request_in_flight:
		return
	if decision_bridge_generated:
		if not pending_finale_beats.is_empty():
			var finale_beats := pending_finale_beats.duplicate(true)
			pending_finale_beats.clear()
			finale_narration_override = _story_summary(finale_beats)
			_play_story_beats(finale_beats, "fetch_choices")
		else:
			_fetch_choices()
	else:
		_request_decision_bridge()


func _request_decision_bridge() -> void:
	next_choices_button.disabled = true
	send_button.disabled = true
	player_input.editable = false
	_set_dialogue_status("선택 단계로 이동하기 위해 대화를 정리하고 있습니다…")
	_request_json("decision_bridge", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/decision-bridge" % session_id, {
		"idempotencyKey": _new_uuid()
	})


func _fetch_choices() -> void:
	next_choices_button.disabled = true
	_request_json("choices", HTTPClient.METHOD_GET, "/api/v1/sessions/%s/choices" % session_id)


func _submit_choice(index: int) -> void:
	if index >= choice_ids.size():
		return
	for button in choice_buttons:
		button.disabled = true
	_set_dialogue_status("선택 결과를 계산하고 있습니다…")
	_request_json("choice", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/choices" % session_id, {
		"choiceId": choice_ids[index],
		"idempotencyKey": _new_uuid(),
		"timedOut": false
	})


func _submit_final_choice(index: int) -> void:
	if index >= choice_ids.size() or final_choice_timer.is_stopped():
		return
	final_choice_timer.stop()
	for button in final_choice_buttons:
		button.disabled = true
	final_feedback.text = "시간을 멈추고 최종 결과를 계산하고 있습니다…"
	_request_json("choice", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/choices" % session_id, {
		"choiceId": choice_ids[index],
		"idempotencyKey": _new_uuid(),
		"timedOut": false
	})


func _on_final_choice_timeout() -> void:
	for button in final_choice_buttons:
		button.disabled = true
	final_timer_label.text = "시간 초과"
	final_timer_bar.value = 0
	final_feedback.text = "결정 시간이 끝나 주민 대피를 자동으로 명령합니다…"
	_request_json("choice", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/choices" % session_id, {
		"choiceId": timeout_choice_id,
		"idempotencyKey": _new_uuid(),
		"timedOut": true
	})


func _request_json(kind: String, method: HTTPClient.Method, path: String, payload: Dictionary = {}) -> void:
	if request_completion_dispatching:
		call_deferred("_request_json", kind, method, path, payload.duplicate(true))
		return
	if primary_request_active:
		_set_dialogue_status("이전 요청을 처리하고 있습니다…")
		return
	request_kind = kind
	var body := "" if method == HTTPClient.METHOD_GET else JSON.stringify(payload)
	var headers := PackedStringArray(["Content-Type: application/json"])
	var error := http_request.request(API_BASE + path, headers, method, body)
	if error != OK:
		primary_request_active = false
		if kind == "dialogue":
			_stop_dialogue_loading()
		_show_error("요청을 시작하지 못했습니다. 오류 코드: %s" % error)
	else:
		primary_request_active = true


func _on_request_completed(result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	var completed_kind := request_kind
	primary_request_active = false
	request_completion_dispatching = true
	call_deferred("_finish_request_completion_dispatch")
	if completed_kind == "dialogue":
		_stop_dialogue_loading()
	if result != HTTPRequest.RESULT_SUCCESS:
		if result == HTTPRequest.RESULT_TIMEOUT:
			_show_error("AI 응답 시간이 초과되었습니다. 잠시 후 다시 시도해 주세요.")
		else:
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

	match completed_kind:
		"start": _show_session(payload)
		"resume": _show_resume(payload)
		"dialogue_history": _show_dialogue_history(payload)
		"dialogue": _show_dialogue(payload)
		"decision_bridge": _show_decision_bridge(payload)
		"choices": _show_choices(payload)
		"choice": _show_choice_result(payload)
		"farming": _show_farming_result(payload)


func _finish_request_completion_dispatch() -> void:
	request_completion_dispatching = false


func _show_session(payload: Dictionary) -> void:
	live_mode_toggle.disabled = false
	session_id = str(payload.get("sessionId", ""))
	_save_session_reference(session_id)
	_show_act(payload)


func _show_resume(payload: Dictionary) -> void:
	live_mode_toggle.disabled = false
	session_id = str(payload.get("sessionId", ""))
	_save_session_reference(session_id)
	var phase := str(payload.get("phase", ""))
	if phase == "Completed" and payload.get("ending") is Dictionary:
		_show_ending(payload)
		return
	if phase == "AwaitingDialogue":
		_show_act(payload, "저장된 세션을 복구했습니다.", false)
		resume_phase_after_history = phase
		_fetch_dialogue_history()
		return
	if phase == "AwaitingChoice":
		_show_act(payload, "대화 이후의 선택 단계부터 복구했습니다.", false)
		resume_phase_after_history = phase
		resume_current_dialogue = str(payload.get("currentDialogue", ""))
		_fetch_dialogue_history()
		return
	if phase == "AwaitingFarming" and payload.get("nextActivity") is Dictionary:
		current_act = int(payload.get("act", 3))
		var progress: Dictionary = payload.get("farmingProgress", {}) if payload.get("farmingProgress") is Dictionary else {}
		_show_farming(payload.get("nextActivity"), "저장된 농사 진행 상태를 복구했습니다.", progress)
		return
	_show_error("복구할 수 없는 세션 단계입니다: %s" % phase)


func _show_act(payload: Dictionary, _transition_feedback := "", auto_advance := true) -> void:
	current_act = int(payload.get("act", 1))
	var scene: Dictionary = payload.get("scene", {})
	current_npc_id = str(scene.get("npcId", ""))
	current_background_id = str(scene.get("backgroundId", "unknown"))
	act_label.text = "ACT %s · %s" % [current_act, payload.get("timeLabel", "")]
	narration.text = scene.get("narration", "")
	npc_name.text = scene.get("npcName", "")
	npc_initial.text = npc_name.text.left(1) if not npc_name.text.is_empty() else "?"
	current_opening_emotion = str(scene.get("npcEmotion", "neutral"))
	npc_emotion.text = _translate_emotion(current_opening_emotion)
	scene_id_label.text = _translate_background(current_background_id)
	_update_scene_art(current_background_id, current_npc_id, current_opening_emotion)
	player_input.placeholder_text = _dialogue_placeholder(current_act, npc_name.text)
	player_input.text = ""
	var dialogue_remaining := int(payload.get("dialogueRemaining", 5))
	var choices_available := bool(payload.get("choicesAvailable", false))
	current_dialogue_remaining = dialogue_remaining
	current_choices_available = choices_available
	latest_status_text = "남은 자유 대화 횟수: %d" % dialogue_remaining
	history_navigation_locked = false
	_reset_dialogue_history(str(payload.get("openingDialogue", "")), current_opening_emotion)
	choices.hide()
	choice_margin.hide()
	dialogue_margin.show()
	story_margin.hide()
	decision_bridge_generated = false
	pending_finale_beats.clear()
	next_choices_button.text = "결정하기"
	_apply_history_input_state()
	title_screen.hide()
	_hide_page_background()
	farming_screen.hide()
	final_choice_screen.hide()
	ending_screen.hide()
	game_screen.show()
	new_game_button.disabled = false
	continue_button.disabled = false
	var intro_beats: Array = scene.get("storyBeats", [])
	if auto_advance and not intro_beats.is_empty():
		story_begin_auto_advance = true
		_play_story_beats(intro_beats, "begin_act")
		if smoke_act1_opening_test:
			call_deferred("_run_act1_opening_smoke")
		return
	_begin_act_interaction(auto_advance)


func _begin_act_interaction(auto_advance: bool) -> void:
	dialogue_margin.show()
	story_margin.hide()
	if auto_advance and dialogue_history.size() == 1:
		_render_dialogue_history(true)
	if smoke_save_dialogue_test:
		print("MOCK_SESSION_SAVE_DIALOGUE_READY: %s" % session_id)
		get_tree().quit()
		return
	if smoke_test and auto_advance:
		match current_act:
			1: player_input.text = "주민들이 다치지 않는 방법부터 찾아보자."
			2: player_input.text = "징후를 먼저 계산하고 실행할 수 있는 계획을 세우자."
			_: player_input.text = "주민들의 오늘을 지키는 일도 직접 돕겠다."
		_submit_dialogue()


func _play_story_beats(beats: Array, completion: String) -> void:
	story_beats = beats.duplicate(true)
	story_index = -1
	story_pages.clear()
	story_page_index = -1
	story_completion = completion
	last_story_background_id = ""
	dialogue_margin.hide()
	choice_margin.hide()
	story_margin.show()
	_advance_story()


func _advance_story() -> void:
	if story_page_index >= 0 and story_page_index < story_pages.size() - 1:
		story_page_index += 1
		story_text.text = story_pages[story_page_index]
		_update_story_continue_button()
		if smoke_test:
			call_deferred("_advance_story")
		return
	story_index += 1
	if story_index >= story_beats.size():
		_finish_story_sequence()
		return
	var beat: Dictionary = story_beats[story_index]
	var kind := str(beat.get("kind", "narration"))
	story_speaker.text = _normalize_story_speaker(str(beat.get("speaker", "나")), kind)
	var compact_story_text := str(beat.get("text", "")).replace("\n\n", "\n")
	story_pages = _paginate_text(compact_story_text, STORY_PAGE_CHARACTER_LIMIT)
	story_page_index = 0
	story_text.text = story_pages[story_page_index]
	story_speaker.add_theme_color_override("font_color", Color("3f2116") if kind == "dialogue" else Color("413047") if kind == "thought" else Color("3a3028"))
	_update_story_continue_button()
	var beat_background := str(beat.get("backgroundId", ""))
	var displayed_background := current_background_id if beat_background.is_empty() else beat_background
	var beat_npc_id := str(beat.get("npcId", ""))
	var beat_npc_name := str(beat.get("npcName", ""))
	var beat_emotion := str(beat.get("emotion", "neutral"))
	if bool(beat.get("hideCharacter", false)):
		_update_scene_art(displayed_background, current_npc_id, current_opening_emotion)
		character_image.hide()
		character_placeholder.hide()
	elif not beat_npc_id.is_empty():
		_update_scene_art(displayed_background, beat_npc_id, beat_emotion)
		npc_initial.text = beat_npc_name.left(1) if not beat_npc_name.is_empty() else "?"
	else:
		_update_scene_art(displayed_background, current_npc_id, current_opening_emotion)
	_play_story_visual_effect(last_story_background_id, displayed_background)
	last_story_background_id = displayed_background
	if smoke_test:
		call_deferred("_advance_story")


func _finish_story_sequence() -> void:
	var completion := story_completion
	story_beats.clear()
	story_index = -1
	story_pages.clear()
	story_page_index = -1
	story_completion = ""
	last_story_background_id = ""
	story_fade_overlay.color.a = 0.0
	story_red_flash_overlay.color.a = 0.0
	story_margin.hide()
	_restore_current_scene_art()
	if completion == "begin_act":
		var auto_advance := story_begin_auto_advance
		story_begin_auto_advance = false
		_begin_act_interaction(auto_advance)
	elif completion == "fetch_choices":
		_fetch_choices()


func _update_story_continue_button() -> void:
	var is_last_page := story_page_index >= story_pages.size() - 1
	var base_text := "시작하기" if is_last_page and story_index == story_beats.size() - 1 \
		and story_completion == "begin_act" else "계속"
	story_continue_button.text = "%s (%d/%d)" % [base_text, story_page_index + 1, story_pages.size()] \
		if story_pages.size() > 1 else base_text


func _restore_current_scene_art() -> void:
	scene_id_label.text = _translate_background(current_background_id)
	npc_initial.text = npc_name.text.left(1) if not npc_name.text.is_empty() else "?"
	npc_emotion.text = _translate_emotion(current_opening_emotion)
	_update_scene_art(current_background_id, current_npc_id, current_opening_emotion)


func _story_summary(beats: Array) -> String:
	var lines := PackedStringArray()
	for value in beats:
		var beat: Dictionary = value
		var kind := str(beat.get("kind", "narration"))
		if kind not in ["card", "narration", "dialogue"]:
			continue
		var speaker := str(beat.get("speaker", ""))
		var text := str(beat.get("text", ""))
		lines.append(text if kind in ["card", "narration"] else "%s: %s" % [speaker, text])
	return "\n\n".join(lines)


func _normalize_story_speaker(speaker: String, kind: String = "") -> String:
	var normalized := speaker.strip_edges()
	if normalized in ["내레이션", "주인공 속마음", "주인공", "기억", "기억 속", "나"]:
		return "나"
	if normalized.is_empty() and kind in ["narration", "thought", "card"]:
		return "나"
	return normalized


func _play_story_visual_effect(previous_background: String, displayed_background: String) -> void:
	if not previous_background.is_empty() and previous_background != displayed_background:
		story_fade_overlay.color = Color(0.0, 0.0, 0.0, 0.82)
		var fade_tween := create_tween()
		fade_tween.tween_property(story_fade_overlay, "color:a", 0.0, 0.38)
	if displayed_background == "cracked_sky_castle_wall" \
		and previous_background != displayed_background:
		story_red_flash_overlay.color = Color(0.72, 0.05, 0.08, 0.28)
		var red_tween := create_tween()
		red_tween.tween_property(story_red_flash_overlay, "color:a", 0.0, 0.55)


func _fetch_dialogue_history() -> void:
	_request_json("dialogue_history", HTTPClient.METHOD_GET,
		"/api/v1/sessions/%s/dialogue-history" % session_id)


func _reset_dialogue_history(opening_dialogue: String, opening_emotion: String) -> void:
	dialogue_history = [{
		"sequence": 0,
		"act": current_act,
		"playerText": "",
		"npcDialogue": opening_dialogue,
		"emotion": opening_emotion,
		"isOpening": true,
		"createdAt": ""
	}]
	dialogue_history_index = 0
	_render_dialogue_history(false)


func _show_dialogue_history(payload: Dictionary) -> void:
	if int(payload.get("act", 0)) != current_act:
		_show_error("현재 장면의 대화 기록을 불러오지 못했습니다.")
		return
	var records: Array = payload.get("records", [])
	if records.is_empty():
		_show_error("현재 장면의 대화 기록이 비어 있습니다.")
		return
	dialogue_history = records.duplicate(true)
	dialogue_history_index = dialogue_history.size() - 1
	_render_dialogue_history(false)
	var resumed_phase := resume_phase_after_history
	resume_phase_after_history = ""
	if resumed_phase == "AwaitingChoice":
		decision_bridge_generated = true
		history_navigation_locked = true
		if not resume_current_dialogue.is_empty():
			_display_dialogue_text(resume_current_dialogue, false)
		resume_current_dialogue = ""
		_apply_history_input_state()
		_fetch_choices()
	elif resumed_phase == "AwaitingDialogue" and smoke_resume_test:
		player_input.text = "복구된 세션에서 계속 진행하겠다."
		_submit_dialogue()


func _append_dialogue_history(player_text: String, dialogue: String, emotion: String) -> void:
	var sequence := 1
	if not dialogue_history.is_empty():
		sequence = int(dialogue_history[-1].get("sequence", dialogue_history.size() - 1)) + 1
	dialogue_history.append({
		"sequence": sequence,
		"act": current_act,
		"playerText": player_text,
		"npcDialogue": dialogue,
		"emotion": emotion,
		"isOpening": false,
		"createdAt": ""
	})
	dialogue_history_index = dialogue_history.size() - 1
	_render_dialogue_history(true)


func _show_previous_dialogue() -> void:
	if history_navigation_locked or dialogue_history_index <= 0:
		return
	dialogue_history_index -= 1
	_render_dialogue_history(false)


func _show_next_dialogue() -> void:
	if history_navigation_locked or dialogue_history_index >= dialogue_history.size() - 1:
		return
	dialogue_history_index += 1
	_render_dialogue_history(false)


func _render_dialogue_history(animate := false) -> void:
	if dialogue_history.is_empty() or dialogue_history_index < 0:
		return
	var record: Dictionary = dialogue_history[dialogue_history_index]
	_display_dialogue_text(str(record.get("npcDialogue", "")), animate)
	var emotion := str(record.get("emotion", "neutral"))
	npc_emotion.text = _translate_emotion(emotion)
	_update_character_art(current_npc_id, emotion)
	history_position.text = "%d / %d" % [dialogue_history_index + 1, dialogue_history.size()]
	history_previous_button.disabled = history_navigation_locked or dialogue_history_index <= 0
	history_next_button.disabled = history_navigation_locked or dialogue_history_index >= dialogue_history.size() - 1
	_apply_history_input_state()


func _apply_history_input_state() -> void:
	if dialogue_history.is_empty():
		return
	var viewing_latest := dialogue_history_index == dialogue_history.size() - 1
	var dialogue_fully_revealed := not typewriter_active \
		and dialogue_page_index >= dialogue_pages.size() - 1
	var can_input := viewing_latest and not decision_bridge_generated and current_dialogue_remaining > 0 \
		and not dialogue_request_in_flight and dialogue_fully_revealed
	player_input.visible = can_input
	player_input.editable = can_input
	send_button.visible = can_input
	send_button.disabled = not can_input
	var choice_action_available := current_choices_available or decision_bridge_generated
	var expects_player_input := viewing_latest and not decision_bridge_generated and current_dialogue_remaining > 0
	action_row.visible = expects_player_input or (viewing_latest and choice_action_available)
	next_choices_button.visible = viewing_latest and choice_action_available
	next_choices_button.disabled = not viewing_latest or dialogue_request_in_flight
	status_label.visible = not decision_bridge_generated
	history_navigation_group.visible = not decision_bridge_generated
	history_row.visible = not decision_bridge_generated or dialogue_page_row.visible
	status_label.text = latest_status_text if viewing_latest else "이전 대화를 읽는 중입니다."


func _paginate_dialogue(text: String, character_limit := DIALOGUE_PAGE_CHARACTER_LIMIT) -> Array[String]:
	return _paginate_text(text, character_limit)


func _paginate_text(text: String, character_limit: int) -> Array[String]:
	var normalized := text.strip_edges()
	if normalized.is_empty():
		return [""]
	var pages: Array[String] = []
	var remaining := normalized
	while remaining.length() > character_limit:
		var split_at := character_limit
		var search_floor := maxi(0, character_limit - 28)
		for index in range(character_limit, search_floor - 1, -1):
			var character := remaining.substr(index - 1, 1)
			if character in [" ", "\n", ".", "!", "?", "。", "！", "？"]:
				split_at = index
				break
		pages.append(remaining.left(split_at).strip_edges())
		remaining = remaining.substr(split_at).strip_edges()
	pages.append(remaining)
	return pages


func _display_dialogue_text(text: String, animate: bool, character_limit := DIALOGUE_PAGE_CHARACTER_LIMIT) -> void:
	_stop_typewriter()
	dialogue_pages = _paginate_dialogue(text, character_limit)
	dialogue_page_index = 0
	dialogue_page_animate_advances = animate
	_show_dialogue_page(animate)


func _show_dialogue_page(animate: bool) -> void:
	if dialogue_pages.is_empty():
		dialogue_pages = [""]
	dialogue_page_index = clampi(dialogue_page_index, 0, dialogue_pages.size() - 1)
	npc_dialogue.text = dialogue_pages[dialogue_page_index]
	dialogue_page_position.text = "%d / %d" % [dialogue_page_index + 1, dialogue_pages.size()]
	typewriter_character_index = 0
	if animate and not npc_dialogue.text.is_empty():
		typewriter_active = true
		npc_dialogue.visible_characters = 0
		typewriter_timer.start()
		_refresh_dialogue_page_controls()
	else:
		typewriter_active = false
		npc_dialogue.visible_characters = -1
		_refresh_dialogue_page_controls()


func _on_typewriter_tick() -> void:
	if not typewriter_active:
		typewriter_timer.stop()
		return
	typewriter_character_index += 1
	npc_dialogue.visible_characters = typewriter_character_index
	if typewriter_character_index >= npc_dialogue.text.length():
		_finish_current_page_typing()


func _finish_current_page_typing() -> void:
	typewriter_active = false
	typewriter_timer.stop()
	npc_dialogue.visible_characters = -1
	_refresh_dialogue_page_controls()
	_apply_history_input_state()


func _advance_dialogue_page() -> void:
	if typewriter_active:
		_finish_current_page_typing()
		return
	if dialogue_page_index >= dialogue_pages.size() - 1:
		return
	dialogue_page_index += 1
	_show_dialogue_page(dialogue_page_animate_advances)
	_apply_history_input_state()


func _refresh_dialogue_page_controls() -> void:
	var has_more_pages := dialogue_page_index < dialogue_pages.size() - 1
	var show_typing_control := typewriter_active and not decision_bridge_generated
	dialogue_page_row.visible = show_typing_control or dialogue_pages.size() > 1
	dialogue_page_advance_button.visible = typewriter_active or has_more_pages
	dialogue_page_advance_button.disabled = not typewriter_active and not has_more_pages
	dialogue_page_advance_button.text = "▼" if typewriter_active or has_more_pages else ""
	dialogue_page_advance_button.tooltip_text = "현재 NPC 대사의 다음 페이지" if has_more_pages \
		else "현재 페이지 전체 보기" if typewriter_active else ""


func _stop_typewriter() -> void:
	typewriter_timer.stop()
	typewriter_active = false
	if is_instance_valid(npc_dialogue):
		npc_dialogue.visible_characters = -1


func _current_dialogue_full_text() -> String:
	if dialogue_history.is_empty() or dialogue_history_index < 0:
		return ""
	var record: Dictionary = dialogue_history[dialogue_history_index]
	return str(record.get("npcDialogue", ""))


func _set_dialogue_status(message: String) -> void:
	latest_status_text = message
	if dialogue_history.is_empty() or dialogue_history_index == dialogue_history.size() - 1:
		status_label.text = message


func _start_dialogue_loading() -> void:
	dialogue_request_in_flight = true
	dialogue_request_context = {
		"sessionId": session_id,
		"act": current_act,
		"npcId": current_npc_id
	}
	active_loading_cue = ""
	loading_pulse_step = 0
	loading_cue_timer.start()
	loading_followup_timer.start()
	loading_pulse_timer.stop()


func _stop_dialogue_loading() -> void:
	dialogue_request_in_flight = false
	loading_cue_timer.stop()
	loading_followup_timer.stop()
	loading_pulse_timer.stop()
	active_loading_cue = ""
	loading_pulse_step = 0


func _load_loading_cues() -> void:
	if not FileAccess.file_exists(LOADING_CUES_PATH):
		push_error("대기 연출 데이터 파일을 찾을 수 없습니다: %s" % LOADING_CUES_PATH)
		return
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(LOADING_CUES_PATH))
	if not parsed is Dictionary:
		push_error("대기 연출 JSON을 읽을 수 없습니다: %s" % LOADING_CUES_PATH)
		return
	loading_cue_data = parsed


func _next_cue_from_pool(pool_key: String, cues: Array) -> String:
	if cues.is_empty():
		return ""
	var next_index := int(loading_cue_indices.get(pool_key, -1))
	for _attempt in cues.size():
		next_index = (next_index + 1) % cues.size()
		var candidate := str(cues[next_index])
		if candidate != last_loading_cue or cues.size() == 1:
			loading_cue_indices[pool_key] = next_index
			last_loading_cue = candidate
			return candidate
	return ""


func _next_loading_cue(npc_id: String) -> String:
	var npc_cues_by_id: Dictionary = loading_cue_data.get("npcCues", {})
	var npc_cues: Array = npc_cues_by_id.get(npc_id, [])
	var common_cues: Array = loading_cue_data.get("commonCues", [])
	var npc_weight_percent := clampi(int(loading_cue_data.get("npcWeightPercent", 70)), 0, 100)
	var npc_slots := clampi(roundi(npc_weight_percent / 10.0), 0, 10)
	var selection_bucket := (loading_cue_selection_count * 3) % 10
	loading_cue_selection_count += 1
	var use_npc_pool := selection_bucket < npc_slots and not npc_cues.is_empty()
	var selected := _next_cue_from_pool(npc_id if use_npc_pool else "__common__",
		npc_cues if use_npc_pool else common_cues)
	if selected.is_empty() and not npc_cues.is_empty():
		selected = _next_cue_from_pool(npc_id, npc_cues)
	if selected.is_empty():
		selected = _next_cue_from_pool("__common__", common_cues)
	return selected


func _show_initial_loading_cue() -> void:
	if not dialogue_request_in_flight or not _is_dialogue_request_context_current():
		return
	active_loading_cue = _next_loading_cue(current_npc_id)
	_stop_typewriter()
	dialogue_pages = [active_loading_cue]
	dialogue_page_index = 0
	npc_dialogue.text = active_loading_cue
	npc_dialogue.visible_characters = -1
	dialogue_page_row.hide()


func _show_followup_loading_cue() -> void:
	if not dialogue_request_in_flight or not _is_dialogue_request_context_current():
		return
	active_loading_cue = _next_loading_cue(current_npc_id)
	npc_dialogue.text = active_loading_cue
	npc_dialogue.visible_characters = -1
	loading_pulse_step = 0
	loading_pulse_timer.start()


func _update_loading_pulse() -> void:
	if not dialogue_request_in_flight or active_loading_cue.is_empty():
		loading_pulse_timer.stop()
		return
	loading_pulse_step = (loading_pulse_step + 1) % 4
	var pulse_suffixes := ["", "…", "……", "………"]
	npc_dialogue.text = active_loading_cue + pulse_suffixes[loading_pulse_step]


func _is_dialogue_request_context_current() -> bool:
	return str(dialogue_request_context.get("sessionId", "")) == session_id \
		and int(dialogue_request_context.get("act", -1)) == current_act \
		and str(dialogue_request_context.get("npcId", "")) == current_npc_id \
		and game_screen.visible and dialogue_margin.visible


func _show_dialogue(payload: Dictionary) -> void:
	if not _is_dialogue_request_context_current():
		pending_player_text = ""
		return
	var dialogue_text := str(payload.get("dialogue", ""))
	var response_emotion := str(payload.get("emotion", "neutral"))
	var remaining := int(payload.get("dialogueRemaining", 0))
	var choices_available := bool(payload.get("choicesAvailable", false))
	current_dialogue_remaining = remaining
	current_choices_available = choices_available
	if payload.get("fallbackUsed", false):
		latest_status_text = "네트워크 오류로 준비된 대사를 사용했습니다. 남은 자유 대화 횟수: %d" % remaining
	else:
		latest_status_text = "남은 자유 대화 횟수: %d" % remaining
	_append_dialogue_history(pending_player_text, dialogue_text, response_emotion)
	pending_player_text = ""
	decision_bridge_generated = false
	next_choices_button.text = "결정하기"
	player_input.text = ""
	player_input.placeholder_text = _dialogue_placeholder(current_act, npc_name.text)
	if remaining <= 0:
		latest_status_text = "남은 자유 대화 횟수: 0 · 결정 단계로 이동해 주세요."
	_apply_history_input_state()
	if smoke_history_test:
		_show_previous_dialogue()
		var past_locked := not player_input.visible and not send_button.visible
		_show_next_dialogue()
		var latest_unlocked := player_input.visible and player_input.editable and send_button.visible
		if past_locked and latest_unlocked and dialogue_history_index == dialogue_history.size() - 1:
			print("MOCK_DIALOGUE_HISTORY_OK: %d/%d" % [dialogue_history_index + 1, dialogue_history.size()])
			get_tree().quit()
		else:
			push_error("대화 기록 탐색 중 입력 잠금 상태가 올바르지 않습니다.")
			get_tree().quit(1)
		return
	if smoke_test:
		if remaining <= 0:
			_request_decision_bridge()
		else:
			match current_act:
				1: player_input.text = "그럼 비용과 위험도를 비교해서 가장 급한 일부터 정하자."
				2: player_input.text = "확실한 사실과 가설을 나눠서 다시 설명해줘."
				3: player_input.text = "주민들이 당장 가장 부족한 게 뭔지 알려줘."
				_: player_input.text = "지금까지 준비한 것 중 가장 확실한 것부터 확인하자."
			_submit_dialogue()


func _show_decision_bridge(payload: Dictionary) -> void:
	var response_emotion := str(payload.get("emotion", "neutral"))
	npc_emotion.text = _translate_emotion(response_emotion)
	_update_character_art(current_npc_id, response_emotion)
	latest_status_text = "네트워크 오류로 준비된 전환 대사를 사용했습니다. 선택 단계로 이동합니다." \
		if payload.get("fallbackUsed", false) else "선택 단계로 이동합니다."
	status_label.text = latest_status_text
	player_input.hide()
	send_button.hide()
	decision_bridge_generated = true
	history_navigation_locked = true
	pending_finale_beats = payload.get("storyBeats", []).duplicate(true)
	_display_dialogue_text(str(payload.get("dialogue", "")), true, PASSIVE_DIALOGUE_PAGE_CHARACTER_LIMIT)
	next_choices_button.text = "선택지 보기"
	next_choices_button.disabled = false
	next_choices_button.show()
	history_previous_button.disabled = true
	history_next_button.disabled = true
	_apply_history_input_state()
	if smoke_test:
		call_deferred("_on_next_choices_pressed")


func _dialogue_placeholder(act: int, name: String) -> String:
	match act:
		1:
			return "예: 에드윈, 너는 무슨 일을 담당해? / 내가 원래 어떤 영주였지? / 결계핵이 뭐야?"
		2:
			return "리아에게 궁금한 점을 물어보거나 의견을 말해 보세요. 예: 결계가 왜 위험해? / 다른 방법은 없어?"
		3:
			return "마르타와 자유롭게 이야기해 보세요. 예: 주민들은 요즘 어때? / 마력보리가 뭐야? / 그냥 아무 말도 괜찮아요."
		_:
			return "%s에게 마지막으로 확인하고 싶은 점이나 판단 기준을 말해 보세요." % name


func _show_choices(payload: Dictionary) -> void:
	choice_ids.clear()
	if smoke_save_choice_test:
		print("MOCK_SESSION_SAVE_CHOICE_READY: %s" % session_id)
		get_tree().quit()
		return
	var server_choices: Array = payload.get("choices", [])
	var time_limit_value: Variant = payload.get("timeLimitSeconds")
	var time_limit := 0 if time_limit_value == null else int(time_limit_value)
	if time_limit > 0:
		var remaining_value: Variant = payload.get("remainingSeconds")
		var remaining := float(time_limit) if remaining_value == null else float(remaining_value)
		_show_final_choices(server_choices, time_limit, str(payload.get("timeoutChoiceId", "")), remaining)
		return
	dialogue_margin.hide()
	choice_margin.show()
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


func _show_final_choices(server_choices: Array, time_limit: int, automatic_choice_id: String, remaining_seconds: float) -> void:
	game_screen.hide()
	farming_screen.hide()
	ending_screen.hide()
	_show_page_background("cracked_sky_castle_wall")
	final_choice_screen.show()
	final_act_label.text = act_label.text
	final_narration.text = finale_narration_override if not finale_narration_override.is_empty() else \
		"%s\n\n%s: %s" % [narration.text, npc_name.text, _current_dialogue_full_text()]
	final_feedback.text = "20초 안에 최종 대응을 선택하세요. 서버 응답을 기다리는 동안에는 시간이 흐르지 않습니다."
	timeout_choice_id = automatic_choice_id
	for index in final_choice_buttons.size():
		final_choice_buttons[index].disabled = false
		final_choice_buttons[index].visible = index < server_choices.size()
		if index < server_choices.size():
			var choice: Dictionary = server_choices[index]
			choice_ids.append(str(choice.get("choiceId", "")))
			final_choice_buttons[index].text = choice.get("label", "")
	final_time_limit = 0.2 if smoke_timeout_test else maxf(0.0, remaining_seconds)
	final_timer_bar.max_value = float(time_limit)
	final_timer_bar.value = final_time_limit
	final_timer_label.text = "결정까지 %.1f초" % final_time_limit
	if final_time_limit <= 0.0:
		call_deferred("_on_final_choice_timeout")
	else:
		final_choice_timer.start(final_time_limit)
	if smoke_save_final_timer_test:
		print("MOCK_FINAL_TIMER_SAVE_READY: %s" % session_id)
		get_tree().quit()
		return
	if smoke_resume_timer_test:
		print("MOCK_FINAL_TIMER_RESUMED: %.2f" % final_time_limit)
	if smoke_test and not smoke_timeout_test:
		_submit_final_choice(1)


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


func _show_farming(payload: Dictionary, transition_feedback: String, saved_progress: Dictionary = {}) -> void:
	game_screen.hide()
	ending_screen.hide()
	_show_page_background("community_barley_field")
	farming_screen.show()
	farming_title.text = payload.get("title", "마력보리 공동밭")
	farming_instructions.text = payload.get("instructions", "")
	farming_feedback.text = transition_feedback
	removed_obstacles = 0
	correct_seeds = 0
	removed_obstacle_indexes.clear()
	planted_seed_indexes.clear()
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
	for value in saved_progress.get("removedObstacleIndexes", []):
		var index := int(value)
		if index >= 0 and index < obstacle_buttons.size():
			removed_obstacle_indexes.append(index)
			obstacle_buttons[index].disabled = true
			obstacle_buttons[index].text = "정리 완료"
	for value in saved_progress.get("plantedSeedIndexes", []):
		var index := int(value)
		if index >= 0 and index < seed_buttons.size():
			planted_seed_indexes.append(index)
			seed_buttons[index].disabled = true
			seed_buttons[index].text = "씨앗 배치 완료" if index < 3 else "싹트기 어려운 땅"
	removed_obstacles = removed_obstacle_indexes.size()
	correct_seeds = planted_seed_indexes.filter(func(index: int) -> bool: return index < 3).size()
	var saved_water := str(saved_progress.get("waterTarget", "community"))
	var saved_method := str(saved_progress.get("method", "manual"))
	var water_index := ["community", "ward", "market"].find(saved_water)
	var method_index := ["manual", "magic", "tools"].find(saved_method)
	_select_water(0 if water_index < 0 else water_index, false)
	_select_method(0 if method_index < 0 else method_index, false)
	farming_finish_button.disabled = false
	_update_farming_progress()
	if smoke_save_test:
		print("MOCK_SESSION_SAVE_READY: %s" % session_id)
		get_tree().quit()
		return
	if smoke_save_farming_progress_test:
		quit_after_checkpoint = true
		_remove_obstacle(0)
		_remove_obstacle(2)
		_plant_seed(0)
		_plant_seed(3)
		_select_water(2)
		_select_method(2)
		return
	if smoke_resume_test and not saved_progress.is_empty():
		print("MOCK_FARMING_PROGRESS_RESUMED: %s" % JSON.stringify(saved_progress))
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
	removed_obstacle_indexes.append(index)
	_update_farming_progress()
	_queue_farming_checkpoint()


func _plant_seed(index: int) -> void:
	if seed_buttons[index].disabled:
		return
	seed_buttons[index].disabled = true
	planted_seed_indexes.append(index)
	if index < 3:
		seed_buttons[index].text = "씨앗 배치 완료"
		correct_seeds += 1
	else:
		seed_buttons[index].text = "싹트기 어려운 땅"
	_update_farming_progress()
	_queue_farming_checkpoint()


func _select_water(index: int, save_checkpoint := true) -> void:
	var targets := ["community", "ward", "market"]
	water_target = targets[index]
	for button_index in water_buttons.size():
		water_buttons[button_index].button_pressed = button_index == index
	if save_checkpoint:
		_queue_farming_checkpoint()


func _select_method(index: int, save_checkpoint := true) -> void:
	var methods := ["manual", "magic", "tools"]
	farming_method = methods[index]
	for button_index in method_buttons.size():
		method_buttons[button_index].button_pressed = button_index == index
	if save_checkpoint:
		_queue_farming_checkpoint()


func _update_farming_progress() -> void:
	farming_progress.text = "장애물 %d/3 · 올바른 씨앗 %d/3" % [removed_obstacles, correct_seeds]


func _submit_farming() -> void:
	checkpoint_dirty = false
	if checkpoint_request_active:
		checkpoint_request.cancel_request()
		checkpoint_request_active = false
	farming_finish_button.disabled = true
	farming_feedback.text = "농사 결과를 계산하고 있습니다…"
	_request_json("farming", HTTPClient.METHOD_POST, "/api/v1/sessions/%s/farming" % session_id, {
		"removedObstacles": removed_obstacles,
		"correctSeeds": correct_seeds,
		"waterTarget": water_target,
		"method": farming_method,
		"idempotencyKey": _new_uuid()
	})


func _queue_farming_checkpoint() -> void:
	if session_id.is_empty():
		return
	checkpoint_dirty = true
	call_deferred("_flush_farming_checkpoint")


func _flush_farming_checkpoint() -> void:
	if checkpoint_request_active or not checkpoint_dirty:
		return
	checkpoint_dirty = false
	checkpoint_request_active = true
	var payload := {
		"removedObstacleIndexes": removed_obstacle_indexes,
		"plantedSeedIndexes": planted_seed_indexes,
		"waterTarget": water_target,
		"method": farming_method,
		"idempotencyKey": _new_uuid()
	}
	var error := checkpoint_request.request(API_BASE + "/api/v1/sessions/%s/farming-progress" % session_id,
		PackedStringArray(["Content-Type: application/json"]), HTTPClient.METHOD_POST, JSON.stringify(payload))
	if error != OK:
		checkpoint_request_active = false
		push_warning("농사 진행 상태 저장을 시작하지 못했습니다. 오류 코드: %s" % error)


func _on_checkpoint_completed(result: int, response_code: int, _headers: PackedStringArray, _body: PackedByteArray) -> void:
	checkpoint_request_active = false
	if result != HTTPRequest.RESULT_SUCCESS or response_code < 200 or response_code >= 300:
		push_warning("농사 진행 상태를 서버에 저장하지 못했습니다.")
	if checkpoint_dirty:
		call_deferred("_flush_farming_checkpoint")
	elif quit_after_checkpoint:
		print("MOCK_FARMING_PROGRESS_SAVE_READY: %s" % session_id)
		get_tree().quit()


func _show_farming_result(payload: Dictionary) -> void:
	farming_screen.hide()
	var next_act: Variant = payload.get("nextAct")
	if next_act is Dictionary:
		_show_act(next_act, "농사 점수 %s점\n%s" % [payload.get("score", 0), payload.get("feedback", "")])
		return
	_show_ending(payload)


func _show_ending(payload: Dictionary) -> void:
	_hide_page_background()
	var ending: Dictionary = payload.get("ending", {})
	var ending_records: Array = ending.get("records", [])
	occupation.text = ending.get("occupationName", "")
	ending_title.text = ending.get("title", "")
	career_summary.text = "직업 위상 %s · 엔딩 톤 %s" % [
		_translate_career_rank(str(ending.get("careerRank", "survivor"))),
		_translate_ending_tone(str(ending.get("endingTone", "orthodox")))
	]
	outcome.text = "%s\n%s" % [ending.get("territoryOutcome", ""), payload.get("feedback", "")]
	epilogue.text = ending.get("epilogue", "")
	records.text = "결정적 기록\n• " + "\n• ".join(ending_records)
	share_line.text = ending.get("shareLine", "")
	game_screen.hide()
	farming_screen.hide()
	final_choice_screen.hide()
	final_choice_timer.stop()
	ending_screen.show()
	ending_screen.scroll_vertical = 0
	if smoke_test:
		if smoke_resume_test:
			print("MOCK_SESSION_RESUME_OK: %s" % JSON.stringify(payload))
			get_tree().quit()
			return
		smoke_runs += 1
		if smoke_runs == 1:
			call_deferred("_start_new_game")
		else:
			print("MOCK_SLICE_RESTART_OK: %s" % JSON.stringify(payload))
			get_tree().quit()


func _translate_career_rank(rank_id: String) -> String:
	return {
		"fallen": "몰락",
		"survivor": "생존",
		"accomplished": "성취",
		"legendary": "전설"
	}.get(rank_id, rank_id)


func _translate_ending_tone(tone_id: String) -> String:
	return {
		"orthodox": "정통",
		"prestige": "명예",
		"unexpected": "의외",
		"b_grade": "B급"
	}.get(tone_id, tone_id)


func _translate_emotion(emotion_id: String) -> String:
	return {
		"neutral": "차분함",
		"surprised": "놀람",
		"nervous": "긴장",
		"urgent": "긴박함",
		"determined": "결연함",
		"resolved": "각오",
		"warm": "다정함",
		"smiling": "미소"
	}.get(emotion_id, emotion_id.replace("_", " "))


func _translate_background(background_id: String) -> String:
	return {
		"modern_library": "발표 전날 도서관",
		"study_night": "밤의 영주 집무실",
		"mage_tower_interior": "리아의 마탑 연구실",
		"community_barley_field": "마력보리 공동밭",
		"cracked_sky_castle_wall": "균열 아래 성벽"
	}.get(background_id, background_id.replace("_", " "))


func _update_scene_art(background_id: String, npc_id: String, emotion_id: String) -> void:
	var background_path := "res://assets/backgrounds/%s.png" % background_id
	if ResourceLoader.exists(background_path):
		background_image.texture = load(background_path)
		background_image.show()
	else:
		background_image.texture = null
		background_image.hide()
	background_fallback.color = _background_fallback_color(background_id)
	_update_character_art(npc_id, emotion_id)


func _show_page_background(background_id: String) -> void:
	var background_path := "res://assets/backgrounds/%s.png" % background_id
	if ResourceLoader.exists(background_path):
		page_background_image.texture = load(background_path)
		page_background_image.show()
		page_background_shade.show()
	else:
		_hide_page_background()


func _hide_page_background() -> void:
	page_background_image.texture = null
	page_background_image.hide()
	page_background_shade.hide()


func _update_character_art(npc_id: String, emotion_id: String) -> void:
	var character_path := "res://assets/characters/%s/%s.png" % [npc_id, emotion_id]
	var neutral_path := "res://assets/characters/%s/neutral.png" % npc_id
	if not ResourceLoader.exists(character_path) and ResourceLoader.exists(neutral_path):
		character_path = neutral_path
	if ResourceLoader.exists(character_path):
		character_image.texture = load(character_path)
		character_image.show()
		character_placeholder.hide()
	else:
		character_image.texture = null
		character_image.hide()
		character_placeholder.show()


func _background_fallback_color(background_id: String) -> Color:
	return {
		"modern_library": Color("1b2233"),
		"study_night": Color("152038"),
		"mage_tower_interior": Color("263544"),
		"community_barley_field": Color("30452f"),
		"cracked_sky_castle_wall": Color("462936")
	}.get(background_id, Color("141b28"))


func _show_error(message: String) -> void:
	var dialogue_failed := request_kind == "dialogue" and not dialogue_request_context.is_empty()
	if dialogue_failed:
		_stop_dialogue_loading()
		pending_player_text = ""
		if not dialogue_history.is_empty():
			dialogue_history_index = dialogue_history.size() - 1
			_render_dialogue_history(false)
	connection_status.text = message
	_set_dialogue_status(message)
	new_game_button.disabled = false
	continue_button.disabled = saved_session_id.is_empty()
	live_mode_toggle.disabled = false
	if dialogue_failed:
		_apply_history_input_state()
	else:
		send_button.disabled = false
		player_input.editable = true
	farming_finish_button.disabled = false
	for button in final_choice_buttons:
		button.disabled = false
	push_warning(message)
	if smoke_test:
		get_tree().quit(1)


func _run_dialogue_ui_smoke() -> void:
	var long_dialogue := ("긴 응답도 고정된 대사창 안에서 읽기 쉽게 나뉘어야 합니다. " + \
		"문장 경계를 우선하고 이미 읽은 기록은 타이핑 없이 보여야 합니다. ").repeat(4)
	var pages := _paginate_dialogue(long_dialogue)
	var page_lengths_valid := pages.size() >= 2
	for page in pages:
		page_lengths_valid = page_lengths_valid and page.length() <= DIALOGUE_PAGE_CHARACTER_LIMIT
	var cue_one := _next_loading_cue("steward_edwin")
	var cue_two := _next_loading_cue("steward_edwin")
	var cues_rotate := cue_one != cue_two
	var npc_cues_by_id: Dictionary = loading_cue_data.get("npcCues", {})
	var edwin_cues: Array = npc_cues_by_id.get("steward_edwin", [])
	var common_cues: Array = loading_cue_data.get("commonCues", [])
	var cue_content_count := common_cues.size()
	for npc_cues_value in npc_cues_by_id.values():
		cue_content_count += (npc_cues_value as Array).size()
	loading_cue_indices.clear()
	loading_cue_selection_count = 0
	last_loading_cue = ""
	var npc_cue_count := 0
	var common_cue_count := 0
	var loading_cues_never_repeat := true
	var previous_cue := ""
	for _index in 100:
		var selected_cue := _next_loading_cue("steward_edwin")
		loading_cues_never_repeat = loading_cues_never_repeat and selected_cue != previous_cue
		previous_cue = selected_cue
		if edwin_cues.has(selected_cue):
			npc_cue_count += 1
		elif common_cues.has(selected_cue):
			common_cue_count += 1
	var loading_mix_valid := npc_cue_count == 70 and common_cue_count == 30 \
		and cue_content_count == 14 and loading_cues_never_repeat
	dialogue_history = [{
		"sequence": 0,
		"act": 1,
		"playerText": "",
		"npcDialogue": long_dialogue,
		"emotion": "neutral",
		"isOpening": true,
		"createdAt": ""
	}]
	dialogue_history_index = 0
	current_dialogue_remaining = 0
	_display_dialogue_text(long_dialogue, true)
	var typing_started := typewriter_active and npc_dialogue.visible_characters == 0
	_advance_dialogue_page()
	var rapid_next_revealed := not typewriter_active and npc_dialogue.visible_characters == -1
	_advance_dialogue_page()
	var next_page_started := dialogue_page_index == 1 and typewriter_active
	_display_dialogue_text(long_dialogue, false)
	_advance_dialogue_page()
	var history_has_no_typing := dialogue_page_index == 1 and not typewriter_active
	session_id = "smoke-session"
	current_act = 1
	current_npc_id = "steward_edwin"
	dialogue_request_context = {"sessionId": session_id, "act": current_act, "npcId": current_npc_id}
	game_screen.show()
	dialogue_margin.show()
	var context_active := _is_dialogue_request_context_current()
	game_screen.hide()
	var context_rejected_after_screen_change := not _is_dialogue_request_context_current()
	var timing_rules_valid := is_equal_approx(LOADING_CUE_DELAY_SECONDS, 0.65) \
		and is_equal_approx(LOADING_FOLLOWUP_DELAY_SECONDS, 5.0) \
		and is_equal_approx(TYPEWRITER_CHARACTERS_PER_SECOND, 36.0)
	current_choices_available = true
	decision_bridge_generated = true
	history_navigation_locked = true
	dialogue_request_in_flight = false
	var passive_sample := ("채팅 입력이 없는 전환 대사는 남는 본문 영역을 사용합니다. " + \
		"불필요하게 다음 페이지로 나누지 않습니다. ").repeat(2)
	dialogue_pages = _paginate_dialogue(passive_sample, PASSIVE_DIALOGUE_PAGE_CHARACTER_LIMIT)
	dialogue_page_index = dialogue_pages.size() - 1
	dialogue_page_row.hide()
	_apply_history_input_state()
	var transition_button_enabled := next_choices_button.visible and not next_choices_button.disabled
	var passive_layout_uses_full_body := dialogue_pages.size() == 1 and not history_row.visible and action_row.visible
	active_loading_cue = _next_loading_cue("steward_edwin")
	dialogue_request_in_flight = true
	_stop_dialogue_loading()
	_display_dialogue_text("실제 NPC 응답입니다.", false)
	var response_replaced_loading_cue := active_loading_cue.is_empty() \
		and npc_dialogue.text == "실제 NPC 응답입니다."
	if page_lengths_valid and cues_rotate and typing_started and rapid_next_revealed \
		and next_page_started and history_has_no_typing and context_active \
		and context_rejected_after_screen_change and timing_rules_valid and transition_button_enabled \
		and passive_layout_uses_full_body and loading_mix_valid and response_replaced_loading_cue:
		print("MOCK_DIALOGUE_UI_OK: pages=%d, cps=%.0f, cue_delay=%.2f, cues=%d, mix=%d/%d" % [
			pages.size(), TYPEWRITER_CHARACTERS_PER_SECOND, LOADING_CUE_DELAY_SECONDS,
			cue_content_count, npc_cue_count, common_cue_count
		])
		get_tree().quit()
		return
	push_error("대사 페이지·타이핑·대기 연출 스모크 검증에 실패했습니다.")
	get_tree().quit(1)


func _run_story_layout_smoke() -> void:
	title_screen.hide()
	game_screen.show()
	dialogue_margin.hide()
	choice_margin.hide()
	story_margin.show()
	background_fallback.color = Color(0.12, 0.08, 0.1, 1)
	background_image.hide()
	character_image.hide()
	character_placeholder.hide()
	story_speaker.text = _normalize_story_speaker("내레이션", "narration")
	story_text.text = "새벽 3시 47분. 발표까지 다섯 시간.\n팀원들의 연락을 기다리며 마지막 자료를 정리했다."
	story_continue_button.text = "계속"
	await get_tree().process_frame
	await get_tree().process_frame

	var story_box := $GameScreen/StoryMargin/StoryShell/StoryBox as Control
	var story_shell := $GameScreen/StoryMargin/StoryShell as Control
	var speaker_rect := story_speaker.get_global_rect()
	var box_rect := story_box.get_global_rect()
	var text_rect := story_text.get_global_rect()
	var button_rect := story_continue_button.get_global_rect()
	var badge_valid := speaker_rect.size == Vector2(220.0, 70.0)
	var fixed_shell_height := is_equal_approx(story_shell.size.y, 324.0)
	var text_safe := text_rect.position.x >= box_rect.position.x + 28.0 \
		and text_rect.position.y >= speaker_rect.end.y + 12.0 \
		and text_rect.end.x <= box_rect.end.x - 28.0
	var button_valid := button_rect.size.x >= 180.0 and button_rect.size.x <= 240.0 \
		and button_rect.size.y >= 48.0 and button_rect.size.y <= 60.0 \
		and story_continue_button.get_theme_color("font_color") == Color.WHITE
	var header_hidden: bool = not ($GameScreen/SceneHeader as Control).visible
	var speaker_normalization_valid := story_speaker.text == "나" \
		and _normalize_story_speaker("주인공 속마음", "thought") == "나" \
		and _normalize_story_speaker("기억 속", "card") == "나" \
		and _normalize_story_speaker("에드윈", "dialogue") == "에드윈"
	var screenshot_path := "/tmp/media-project-story-layout-after.png"
	var screenshot_error := OK
	if DisplayServer.get_name() == "headless":
		screenshot_path = "headless-dummy-renderer"
	else:
		var viewport_image := get_viewport().get_texture().get_image()
		if viewport_image == null:
			screenshot_error = ERR_CANT_CREATE
		else:
			screenshot_error = viewport_image.save_png(screenshot_path)
	if badge_valid and fixed_shell_height and text_safe and button_valid and header_hidden \
		and speaker_normalization_valid and screenshot_error == OK:
		print("MOCK_STORY_LAYOUT_OK: badge=%s text=%s button=%s screenshot=%s" % [
			speaker_rect, text_rect, button_rect, screenshot_path
		])
		get_tree().quit()
		return
	push_error("내레이션 레이아웃 검증에 실패했습니다: badge=%s fixed=%s text=%s button=%s header=%s speaker=%s" % [
		badge_valid, fixed_shell_height, text_safe, button_valid, header_hidden,
		speaker_normalization_valid
	])
	get_tree().quit(1)


func _run_passive_dialogue_layout_smoke() -> void:
	title_screen.hide()
	game_screen.show()
	story_margin.hide()
	choice_margin.hide()
	dialogue_margin.show()
	background_fallback.color = Color(0.12, 0.08, 0.1, 1)
	background_image.hide()
	character_image.hide()
	character_placeholder.hide()
	dialogue_history = [{
		"sequence": 2,
		"act": 2,
		"playerText": "",
		"npcDialogue": "",
		"emotion": "neutral",
		"isOpening": false,
		"createdAt": ""
	}]
	dialogue_history_index = 0
	current_choices_available = true
	current_dialogue_remaining = 0
	npc_name.text = "리아"
	_show_decision_bridge({
		"dialogue": ("탑의 결계는 서로 연결되어 있어서, 북부 탑이 제 기능을 못 하게 되면 " + \
			"그 부담이 나머지 두 탑으로 옮겨갑니다. 그러면 다른 탑들의 결계에도 무리가 갈 수 있습니다. ").repeat(2),
		"emotion": "neutral",
		"fallbackUsed": false,
		"storyBeats": []
	})
	_stop_typewriter()
	npc_dialogue.visible_characters = -1
	_refresh_dialogue_page_controls()
	_apply_history_input_state()
	await get_tree().process_frame
	await get_tree().process_frame
	var body_rect := dialogue_viewport.get_global_rect()
	var passive_uses_space := not history_row.visible and action_row.visible \
		and body_rect.size.y >= 120.0 and dialogue_pages.size() == 1
	var choice_enabled := next_choices_button.visible and not next_choices_button.disabled
	var screenshot_path := "/tmp/media-project-passive-dialogue-after.png"
	var screenshot_error := OK
	if DisplayServer.get_name() == "headless":
		screenshot_path = "headless-dummy-renderer"
	else:
		var viewport_image := get_viewport().get_texture().get_image()
		if viewport_image == null:
			screenshot_error = ERR_CANT_CREATE
		else:
			screenshot_error = viewport_image.save_png(screenshot_path)
	if passive_uses_space and choice_enabled and screenshot_error == OK:
		print("MOCK_PASSIVE_DIALOGUE_LAYOUT_OK: body=%s pages=%d screenshot=%s" % [
			body_rect, dialogue_pages.size(), screenshot_path
		])
		get_tree().quit()
		return
	push_error("비입력 대화 레이아웃 검증에 실패했습니다: space=%s choice=%s pages=%d" % [
		passive_uses_space, choice_enabled, dialogue_pages.size()
	])
	get_tree().quit(1)


func _run_dialogue_layout_smoke() -> void:
	title_screen.hide()
	game_screen.show()
	story_margin.hide()
	choice_margin.hide()
	dialogue_margin.show()
	background_fallback.color = Color(0.12, 0.08, 0.1, 1)
	background_image.hide()
	character_image.hide()
	character_placeholder.hide()
	npc_name.text = "에드윈"
	npc_emotion.text = "차분함"
	npc_dialogue.text = "현재 상태라면 균열은 일주일 뒤입니다. 결계탑 수리 순서를 정하고, 주민 대피 계획도 함께 준비해야 합니다."
	npc_dialogue.visible_characters = -1
	player_input.text = ""
	player_input.placeholder_text = "에드윈에게 답하세요."
	send_button.show()
	next_choices_button.hide()
	status_label.text = "남은 자유 대화 3회"
	dialogue_page_row.show()
	dialogue_page_position.text = "1 / 2"
	history_position.text = "3 / 3"
	await get_tree().process_frame
	await get_tree().process_frame

	var shell := $GameScreen/DialogueMargin/DialogueShell as Control
	var dialogue_box := $GameScreen/DialogueMargin/DialogueShell/DialogueBox as Control
	var action_row := $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/ActionRow as Control
	var history_row := $GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow as Control
	var name_rect := npc_name.get_global_rect()
	var box_rect := dialogue_box.get_global_rect()
	var body_rect := npc_dialogue.get_global_rect()
	var input_rect := player_input.get_global_rect()
	var action_rect := action_row.get_global_rect()
	var history_rect := history_row.get_global_rect()
	var button_rect := send_button.get_global_rect()
	var nav_rect := ($GameScreen/DialogueMargin/DialogueShell/DialogueBox/SafeArea/Content/HistoryRow/NavigationGroup as Control).get_global_rect()
	var name_size_valid := name_rect.size.x >= 180.0 and name_rect.size.x <= 260.0 \
		and name_rect.size.y >= 66.0 and name_rect.size.y <= 74.0
	var name_font := npc_name.get_theme_font("font")
	var name_font_size := npc_name.get_theme_font_size("font_size")
	var longer_name_fits := name_font.get_string_size("수석 마법사 리아", HORIZONTAL_ALIGNMENT_LEFT, -1, name_font_size).x \
		<= name_rect.size.x - 64.0
	var body_safe := body_rect.position.x >= box_rect.position.x + 28.0 \
		and body_rect.end.x <= box_rect.end.x - 28.0 \
		and body_rect.position.y >= name_rect.end.y
	var action_valid := input_rect.end.x + 8.0 <= button_rect.position.x \
		and button_rect.size.x >= 180.0 and button_rect.size.x <= 240.0 \
		and button_rect.size.y >= 48.0 and button_rect.size.y <= 60.0
	var navigation_separate := history_rect.position.y >= action_rect.end.y \
		and nav_rect.position.x >= box_rect.position.x + 128.0 \
		and nav_rect.end.x <= box_rect.end.x - 128.0
	var fixed_shell_height := is_equal_approx(shell.size.y, 324.0)
	var body_size_before := body_rect.size
	var sample_dialogues := [
		"한 줄 대사입니다.",
		"첫 번째 줄입니다.\n두 번째 줄입니다.\n세 번째 줄입니다.",
		("다섯 줄 이상 분량의 긴 대사는 고정 영역을 밀어내지 않고 페이지로 나뉩니다. " + \
		"본문과 입력창, 탐색 행이 서로 겹치지 않아야 합니다. ").repeat(4)
	]
	var text_heights_stable := true
	for sample in sample_dialogues:
		npc_dialogue.text = sample
		await get_tree().process_frame
		text_heights_stable = text_heights_stable and npc_dialogue.size == body_size_before \
			and npc_dialogue.get_global_rect().end.y <= action_rect.position.y
	npc_dialogue.text = "현재 상태라면 균열은 일주일 뒤입니다. 결계탑 수리 순서를 정하고, 주민 대피 계획도 함께 준비해야 합니다."
	var screenshot_path := "/tmp/media-project-dialogue-layout-after.png"
	var screenshot_error := OK
	var viewport_image: Image = null
	if DisplayServer.get_name() == "headless":
		screenshot_path = "headless-dummy-renderer"
	else:
		viewport_image = get_viewport().get_texture().get_image()
		if viewport_image == null:
			screenshot_error = ERR_CANT_CREATE
		else:
			screenshot_error = viewport_image.save_png(screenshot_path)
	if name_size_valid and longer_name_fits and body_safe and action_valid and navigation_separate \
		and fixed_shell_height and text_heights_stable and screenshot_error == OK:
		print("MOCK_DIALOGUE_LAYOUT_OK: name=%s body=%s action=%s nav=%s screenshot=%s" % [
			name_rect, body_rect, action_rect, nav_rect, screenshot_path
		])
		get_tree().quit()
		return
	push_error("대화창 A/B/C/D 레이아웃 검증에 실패했습니다: name=%s long_name=%s body=%s text=%s action=%s nav=%s fixed=%s" % [
		name_size_valid, longer_name_fits, body_safe, text_heights_stable, action_valid,
		navigation_separate, fixed_shell_height
	])
	get_tree().quit(1)


func _run_loading_timing_smoke() -> void:
	session_id = "loading-smoke-session"
	current_act = 1
	current_npc_id = "steward_edwin"
	game_screen.show()
	dialogue_margin.show()
	_start_dialogue_loading()
	await get_tree().create_timer(0.3).timeout
	var fast_response_has_no_cue := active_loading_cue.is_empty()
	await get_tree().create_timer(0.5).timeout
	var first_cue := active_loading_cue
	var three_second_has_one_cue := not first_cue.is_empty()
	await get_tree().create_timer(2.2).timeout
	three_second_has_one_cue = three_second_has_one_cue and active_loading_cue == first_cue
	await get_tree().create_timer(2.2).timeout
	var followup_cue := active_loading_cue
	var followup_changed := not followup_cue.is_empty() and followup_cue != first_cue
	await get_tree().create_timer(5.0).timeout
	var ten_second_only_pulses := active_loading_cue == followup_cue \
		and npc_dialogue.text.begins_with(followup_cue) and not loading_pulse_timer.is_stopped()
	_stop_dialogue_loading()
	var stopped_cleanly := not dialogue_request_in_flight and loading_cue_timer.is_stopped() \
		and loading_followup_timer.is_stopped() and loading_pulse_timer.is_stopped()
	if fast_response_has_no_cue and three_second_has_one_cue and followup_changed \
		and ten_second_only_pulses and stopped_cleanly:
		print("MOCK_LOADING_TIMING_OK: fast=none, 3s=one, 5s=followup, 10s=pulse")
		get_tree().quit()
		return
	push_error("NPC 대기 연출 시간 경계 검증에 실패했습니다.")
	get_tree().quit(1)


func _run_button_assets_smoke() -> void:
	title_screen.show()
	game_screen.hide()
	farming_screen.hide()
	final_choice_screen.hide()
	ending_screen.hide()
	await get_tree().process_frame
	await get_tree().process_frame
	var texture := load("res://assets/ui/basic_button.png") as Texture2D
	var image := texture.get_image() if texture != null else null
	var alpha_bounds := Rect2i()
	if image != null:
		var min_x := image.get_width()
		var min_y := image.get_height()
		var max_x := -1
		var max_y := -1
		for y in image.get_height():
			for x in image.get_width():
				if image.get_pixel(x, y).a > 0.01:
					min_x = mini(min_x, x)
					min_y = mini(min_y, y)
					max_x = maxi(max_x, x)
					max_y = maxi(max_y, y)
		if max_x >= min_x and max_y >= min_y:
			alpha_bounds = Rect2i(min_x, min_y, max_x - min_x + 1, max_y - min_y + 1)
	var button_rect := new_game_button.get_global_rect()
	var source_ratio := float(image.get_width()) / float(image.get_height()) if image != null else 0.0
	var rendered_ratio := button_rect.size.x / button_rect.size.y
	var style_states_valid := true
	for state_name in ["normal", "hover", "pressed", "disabled"]:
		var state_style := new_game_button.get_theme_stylebox(state_name) as StyleBoxTexture
		style_states_valid = style_states_valid and state_style != null \
			and state_style.region_rect == Rect2(0, 0, 600, 130) \
			and is_equal_approx(state_style.texture_margin_left, 70.0) \
			and is_equal_approx(state_style.texture_margin_top, 24.0) \
			and is_equal_approx(state_style.texture_margin_right, 70.0) \
			and is_equal_approx(state_style.texture_margin_bottom, 24.0)
	var screenshot_path := "/tmp/media-project-button-assets.png"
	var screenshot_error := OK
	if DisplayServer.get_name() != "headless":
		var viewport_image := get_viewport().get_texture().get_image()
		if viewport_image == null:
			screenshot_error = ERR_CANT_CREATE
		else:
			screenshot_error = viewport_image.save_png(screenshot_path)
	else:
		screenshot_path = "headless-dummy-renderer"
	if image != null and image.get_size() == Vector2i(600, 130) \
		and alpha_bounds.size.x > 0 and alpha_bounds.size.y > 0 \
		and absf(source_ratio - rendered_ratio) <= 0.05 and style_states_valid \
		and screenshot_error == OK:
		print("MOCK_BUTTON_ASSETS_OK: source=%s alpha=%s source_ratio=%.3f rendered=%s rendered_ratio=%.3f screenshot=%s" % [
			image.get_size(), alpha_bounds, source_ratio, button_rect, rendered_ratio, screenshot_path
		])
		get_tree().quit()
		return
	push_error("버튼 에셋 측정에 실패했습니다.")
	get_tree().quit(1)


func _run_act1_opening_smoke() -> void:
	await get_tree().process_frame
	await get_tree().process_frame
	var modern_path := "/tmp/media-project-act1-modern-after.png"
	var office_path := "/tmp/media-project-act1-office-after.png"
	var surprised_path := "/tmp/media-project-act1-surprised-after.png"
	var red_memory_path := "/tmp/media-project-act1-red-memory-after.png"
	var modern_saved := _save_viewport_png(modern_path) == OK
	var office_saved := false
	var surprised_saved := false
	var red_memory_saved := false
	var player_speakers_valid := story_speaker.text == "나"
	while story_index < story_beats.size() - 1:
		_advance_story()
		await get_tree().process_frame
		var beat: Dictionary = story_beats[story_index]
		var beat_speaker := str(beat.get("speaker", ""))
		if beat_speaker == "나":
			player_speakers_valid = player_speakers_valid and story_speaker.text == "나"
		if not office_saved and str(beat.get("backgroundId", "")) == "study_night" \
			and bool(beat.get("hideCharacter", false)):
			office_saved = _save_viewport_png(office_path) == OK
		if not surprised_saved and str(beat.get("emotion", "")) == "surprised":
			surprised_saved = _save_viewport_png(surprised_path) == OK
		if not red_memory_saved and str(beat.get("backgroundId", "")) == "cracked_sky_castle_wall":
			await get_tree().create_timer(0.12).timeout
			red_memory_saved = _save_viewport_png(red_memory_path) == OK
	var story_contract_valid := story_beats.size() >= 25 \
		and story_beats.any(func(beat: Dictionary) -> bool: return str(beat.get("text", "")).contains("원래 이 몸의 기억은 하나도 없는")) \
		and story_beats.any(func(beat: Dictionary) -> bool: return str(beat.get("text", "")).contains("결정하기 전에 몇 가지부터 확인"))
	if modern_saved and office_saved and surprised_saved and red_memory_saved \
		and player_speakers_valid and story_contract_valid:
		print("MOCK_ACT1_OPENING_OK: beats=%d screenshots=%s,%s,%s,%s" % [
			story_beats.size(), modern_path, office_path, surprised_path, red_memory_path
		])
		get_tree().quit()
		return
	push_error("Act 1 오프닝 렌더링 검증에 실패했습니다: modern=%s office=%s surprised=%s red=%s speaker=%s story=%s" % [
		modern_saved, office_saved, surprised_saved, red_memory_saved,
		player_speakers_valid, story_contract_valid
	])
	get_tree().quit(1)


func _save_viewport_png(path: String) -> Error:
	if DisplayServer.get_name() == "headless":
		return ERR_UNAVAILABLE
	var viewport_image := get_viewport().get_texture().get_image()
	return ERR_CANT_CREATE if viewport_image == null else viewport_image.save_png(path)


func _session_file_path() -> String:
	return SMOKE_SESSION_FILE if smoke_test else SESSION_FILE


func _load_saved_session_reference() -> void:
	var config := ConfigFile.new()
	if config.load(_session_file_path()) == OK:
		saved_session_id = str(config.get_value("session", "id", ""))
	continue_button.disabled = saved_session_id.is_empty()
	continue_button.text = "이어하기" if not saved_session_id.is_empty() else "이어하기 · 저장된 세션 없음"
	if not saved_session_id.is_empty() and not smoke_test:
		connection_status.text = "저장된 세션이 있습니다. 서버 실행 후 이어갈 수 있습니다."


func _save_session_reference(id: String) -> void:
	if id.is_empty():
		return
	saved_session_id = id
	var config := ConfigFile.new()
	config.set_value("session", "id", id)
	var error := config.save(_session_file_path())
	if error != OK:
		push_warning("세션 참조를 저장하지 못했습니다. 오류 코드: %s" % error)
	continue_button.text = "이어하기"
	continue_button.disabled = false


func _new_uuid() -> String:
	var bytes := Crypto.new().generate_random_bytes(16)
	bytes[6] = (bytes[6] & 0x0f) | 0x40
	bytes[8] = (bytes[8] & 0x3f) | 0x80
	var hex := bytes.hex_encode()
	return "%s-%s-%s-%s-%s" % [hex.substr(0, 8), hex.substr(8, 4), hex.substr(12, 4), hex.substr(16, 4), hex.substr(20, 12)]
