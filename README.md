# 빙의했더니 영지 멸망 3년 전

Godot 4.x 클라이언트와 ASP.NET Core 서버로 만드는 인터랙티브 비주얼 노벨입니다.

현재 범위는 개발 1단계입니다.

- Godot 프로젝트 실행
- ASP.NET Core Web API 실행
- Godot에서 서버의 `GET /health` 호출
- 외부 API나 인터넷 없이 로컬에서 동작

## 요구 사항

- Godot 4.x (GDScript)
- .NET SDK 10

## 서버 실행

저장소의 `game-project` 디렉터리에서 다음을 실행합니다.

```bash
dotnet run --project server/Game.Api/Game.Api.csproj
```

서버는 기본적으로 `http://127.0.0.1:5050`에서 대기합니다. 별도 터미널에서 다음으로 확인할 수 있습니다.

```bash
curl http://127.0.0.1:5050/health
```

정상 응답 예시:

```json
{"status":"ok","service":"game-api","version":"0.1.0"}
```

## 클라이언트 실행

Godot에서 `client/project.godot`을 연 뒤 프로젝트를 실행합니다. 시작 화면이 열리면 자동으로 서버 상태를 확인하며, `다시 확인` 버튼으로 재시도할 수 있습니다.

터미널에서는 다음처럼 실행할 수도 있습니다.

```bash
/Applications/Godot.app/Contents/MacOS/Godot --path client
```

서버가 실행 중이면 `서버 연결 성공`이, 실행 중이 아니면 오류 안내가 표시됩니다.

## 구조

```text
game-project/
├─ client/          # Godot 프로젝트
├─ server/          # ASP.NET Core Web API
├─ content/         # NPC, 사건, 선택지 JSON
├─ docs/            # 제품 및 개발 명세
└─ README.md
```

## 다음 단계

Mock 데이터만 사용해 `타이틀 → 에드윈 대화 → 자유 입력 → 선택지 → 상태 변경 → 고정 직업 엔딩`의 세로 슬라이스를 구현합니다. 이 단계가 끝나기 전에는 OpenAI API를 연결하지 않습니다.

