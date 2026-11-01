# NPC persona data

NPC 페르소나는 캐릭터마다 하나의 JSON 파일로 관리한다.

- `Edwin.json`: 에드윈
- `Lia.json`: 리아
- `Marta.json`: 마르타

`hidden_traits`와 `backstory_elements`는 플레이어에게 처음부터 공개하지 않는 정보다. 메인 스토리 확정 전에는 빈 배열로 유지하고, 이후 관계 수치와 사건 조건에 따라 단계적으로 드러나는 항목을 추가한다.

`disclosure_policy`는 향후 숨겨진 정보의 공개 방식을 선택하기 위한 정책 식별자다. 현재 대화 프롬프트는 공개 필드만 화이트리스트 방식으로 전달하며 `hidden_traits`와 `backstory_elements`를 전달하지 않는다. 메인 스토리 확정 후 항목별 공개 조건과 정책 평가기를 추가한다.

플레이어 세션의 `career_affinities`는 `martial`, `arcane`, `diplomacy`, `commerce`, `leadership`, `craft` 여섯 축을 사용한다. NPC의 `career_influence_weights`는 해당 NPC와 관계를 쌓을 때 각 축에 영향을 줄 수 있는 상대적 성향이며, 점수를 직접 더하지 않는다. 실제 점수 변화는 `career_influence_rules`의 행동 신호가 확인될 때만 발생한다.

파일 안의 NPC ID는 장면 콘텐츠에서 사용하는 ID와 동일하게 유지한다. 기사·마법사·주민 같은 역할 분류는 파일명이 아니라 `archetype` 필드로 표현한다.
