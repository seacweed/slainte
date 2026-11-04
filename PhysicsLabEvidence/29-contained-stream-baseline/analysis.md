# 잔 내부 물줄기 회귀 재현

새 `ValidateContainedStreams` 검사를 추가한 독립 씬 복사본에서 런타임/셰이더만 이전 커밋 `433fab0`의 파일로 교체했다. 정확한 파일 해시는 `source-sha256.json`에 기록했다. 원본 작업 파일과 사용자의 Unity는 변경하거나 재시작하지 않았다.

실행은 `Crossing the virtual rim assigns ownership without detaching the stream` 검사에서 종료 코드 1로 실패했다. 기존 `ApplyDeltaAndBoundaries`가 `vesselId != 0`이면 무조건 `detached = 1`로 설정하고, `BuildStreamSurface` 및 토큰 조회도 `vesselId == 0`만 허용하므로 입구 안의 자유 낙하 입자가 원형 메타볼로 바뀐다. 이는 의도한 문제 재현이며 성공 실행이나 Unity 충돌로 분류하지 않는다.

수정본은 소유권 판정을 유지한 채 표시 상태를 실제 벽/액체 접촉과 분리한다. 기존 이웃 그리드와 서브스텝별 접촉 스냅샷을 사용하여 GPU 실행 순서에 따라 분리가 줄기 전체로 전파되는 것을 막는다. 물리 위치·속도·체적과 전역 계산 간격은 변경하지 않는다. 최종 수정 검증은 `32-contained-stream-filled-glass`, 물리 독립성 대조는 `31-contained-stream-isolation`에 별도로 남겼다.
