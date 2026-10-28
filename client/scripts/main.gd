extends Control

const HEALTH_URL := "http://127.0.0.1:5050/health"

@onready var status_label: Label = $Center/Panel/Margin/Content/Status
@onready var retry_button: Button = $Center/Panel/Margin/Content/RetryButton
@onready var health_request: HTTPRequest = $HealthRequest


func _ready() -> void:
	retry_button.pressed.connect(_check_server)
	health_request.request_completed.connect(_on_health_request_completed)
	_check_server()


func _check_server() -> void:
	if health_request.get_http_client_status() != HTTPClient.STATUS_DISCONNECTED:
		return

	status_label.text = "서버 연결 확인 중…"
	status_label.modulate = Color.WHITE
	retry_button.disabled = true

	var error := health_request.request(HEALTH_URL)
	if error != OK:
		_show_connection_error("요청을 시작하지 못했습니다. 오류 코드: %s" % error)


func _on_health_request_completed(
	result: int,
	response_code: int,
	_headers: PackedStringArray,
	body: PackedByteArray
) -> void:
	retry_button.disabled = false

	if result != HTTPRequest.RESULT_SUCCESS:
		_show_connection_error("서버에 연결할 수 없습니다. 서버가 실행 중인지 확인해 주세요.")
		return

	if response_code != 200:
		_show_connection_error("서버가 HTTP %d을(를) 반환했습니다." % response_code)
		return

	var payload: Variant = JSON.parse_string(body.get_string_from_utf8())
	if not payload is Dictionary or payload.get("status") != "ok":
		_show_connection_error("서버 응답 형식이 올바르지 않습니다.")
		return

	status_label.text = "서버 연결 성공 · %s %s" % [payload.get("service", "game-api"), payload.get("version", "")]
	status_label.modulate = Color("7bd88f")
	print("HEALTH_CHECK_OK: %s" % JSON.stringify(payload))


func _show_connection_error(message: String) -> void:
	retry_button.disabled = false
	status_label.text = message
	status_label.modulate = Color("ff7b72")
	push_warning("HEALTH_CHECK_FAILED: %s" % message)

