# A2Rank 소스

AION2의 랭킹 포인트 변화, 처치·사망 기록, 스킬별 피해를 보여 주는 윈도우용 비공식 도구 **A2Rank**의 소스입니다.
게임에 아무것도 보내지 않고, 게임 서버가 보내는 패킷을 **내 PC에서 읽기만** 합니다. 엔씨소프트와 관계없는 비공식 프로그램입니다.

- **설치 파일은 여기서 받으세요:** [A2Rank/releases](https://github.com/A2Rank/releases) (이 저장소는 소스를 확인하기 위한 것입니다.)
- 이용 안내와 면책: [docs/TERMS.md](docs/TERMS.md), 개인정보 처리방침: [docs/PRIVACY.md](docs/PRIVACY.md)
- 변경 내역: [docs/CHANGELOG.md](docs/CHANGELOG.md)

## 이 저장소는 스냅샷입니다
릴리스마다 그 시점의 소스를 커밋 하나로 올립니다. 개발 기록은 올리지 않습니다. 이슈나 풀 리퀘스트는 받지 않으며, 문의는 프로그램의 설정 → 문의, 또는 arank8934@gmail.com 으로 보내 주세요.
라이선스 파일이 없습니다. 소스는 읽고 확인하는 용도로 공개하며, 복사·수정·재배포 권한은 따로 주지 않습니다.

## 구성
| 폴더 | 내용 |
| --- | --- |
| `src/Aion2Meter.Capture` | 패킷 캡처 (Npcap), TCP 조각 이어 붙이기 |
| `src/Aion2Meter.Protocol` | 패킷 해석: 메시지 종류, 스킬 이름표 |
| `src/Aion2Meter.Core` | 처치 연결, 피해 집계, 파티, 조준 알림, 기록 저장 (SQLite) |
| `src/Aion2Meter.App` | WPF 화면과 설정, 업데이트 |
| `tests` | 단위 테스트 |
| `tools` | 패킷 덤프, 업데이트 서명 도구 등 |
| `docs` | 동작 규칙과 패킷 형식 ([BEHAVIOR.md](docs/BEHAVIOR.md), [PROTOCOL.md](docs/PROTOCOL.md)), 사용 설명서 |

## 빌드
.NET 8 SDK와 윈도우가 필요합니다.

```powershell
dotnet test tests\Aion2Meter.Tests
dotnet build src\Aion2Meter.App -c Release
```

캡처에는 [Npcap](https://npcap.com)이 설치되어 있어야 합니다. 사용한 오픈소스와 라이선스: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
