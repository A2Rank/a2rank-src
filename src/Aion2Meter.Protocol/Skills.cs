namespace Aion2Meter.Protocol;

/// <summary>
/// Skill names by <see cref="DamageMessage.SkillKey"/>. Only skills seen together with the game's own combat-log line (name and
/// amount) are listed; anything else shows as its key. See docs/PROTOCOL.md §3.6.
/// </summary>
public static class Skills
{
    /// <summary>Not a skill: the life the 재생 stat restores when it triggers on a hit I take (counted under this key).</summary>
    public const uint RegenStat = 0xFFF0;

    private static readonly Dictionary<uint, string> Names = new()
    {
        // 성직자 계열 (my character and 목우)
        [0x6563] = "대지의 응보",
        [0x65CE] = "약화의 낙인",
        [0x6590] = "심판의 번개",
        [0x69AD] = "주신의 은총",
        [0x65BF] = "고통의 연쇄",
        [0x676A] = "단죄",
        [0x65A0] = "천벌",
        [0x65AF] = "벽력",
        [0x6638] = "신성한 기운 (소환수)",
        // 궁성 계열 (POK)
        [0x5390] = "저격",
        [0x5818] = "근접 사격",
        [0x5837] = "사냥꾼의 혼",
        [0x5438] = "올가미 화살",
        [0x5579] = "속사",
        // 성직자 회복 (쾌유의 광휘: 나와 파티원에게, 가호: 나에게)
        [0x660B] = "쾌유의 광휘",
        [0x66C5] = "충격 해제",   // the 행동력 1000 it gives arrives as 100,000 (100 times, like 보호의 방패); 00:17:30, 0.4 s before the 쾌유의 광휘 heals of the same minute
        [0x67D4] = "유스티엘의 권능",   // 05 38 type 09 (an effect with an amount): to me 19,070 at 00:35:50 = the +19,070 shield on the HP bar (30% of 63,567); to party members their own 30%
        [0x670E] = "면죄",         // 15,956 then 15,372 at 00:27:02 = the two chat lines "면죄에 의해 15,956 / 15,372"; also 13,828 / 13,118 at 00:17:17
        [0x66F0] = "구원",         // 61,489 at 00:29:35 = "구원에 의해 61,489의 생명력"; the 52,473 of 00:17:21 is the same skill
        [0x6CB8] = "차단의 권능",   // 17,614 at 00:25:16 = "차단의 권능에 의해 17,614의 생명력"
        [0x699E] = "주신의 가호",
        [0x6A18] = "찬란한 가호",
        [0x65DD] = "재생의 빛",
        [RegenStat] = "재생 (스탯)",
        // 상대 종족 3직업: 전투 로그의 "~의 <스킬>에 의해 N의 대미지를 받았습니다"와 금액이 같은 것만 (2026-10-12 19:15 녹화)
        [0x4E43] = "문양 폭발",
        // 전투 분석 살성(깨끗) 스킬 표와 우리 녹화(2026-11-21 02:37:56~02:38:47, 같은 시전자) 대조: 타수·합계가 정확히 같은 것만
        [0x4F46] = "트리니엘의 비수",   // 4 hits 5,012,253
        [0x4F18] = "맹수의 송곳니",    // 6 hits 3,310,684
        [0x4EBD] = "회오리 베기",      // 4 hits 1,753,553
        [0x4FA1] = "침투",            // 6 hits 780,272
        [0x51A8] = "강습 습격",        // 2 hits 297,685
        [0x5B64] = "겨울의 환영",      // 전투 분석 마도성 표(같은 구간, 2026-11-21 02:38): 4 hits 6,240,348 (최고 2,273,852)
        [0x5569] = "화살 난사",        // 전투 분석 궁성 표(같은 구간, 다른 시전자): 104 hits 5,720,483 (최고 118,788)
        [0x4E15] = "맹수의 포효",
        [0x4E24] = "맹수의 뒷발차기",
        [0x4DD8] = "기습",
        [0x5213] = "기습 자세",
        [0x5241] = "각오",
        [0x4F55] = "환영 분신",
        [0x51C7] = "빈틈 노리기",
        [0x6DAC] = "관통쇄",
        [0x6BE2] = "암격쇄",
        [0x700E] = "바람의 약속",
        // 2026-10-12 19:45 녹화 (아라 = 성직자): 같은 이름이 두 번호인 것은 로그에서 구별되지 않았다
        [0x6572] = "뇌전",
        [0x6648] = "생명의 권능",
        [0x65EC] = "치유의 빛",
        // 상대 종족 정령사 계열과 소환수
        [0x6037] = "협공: 저주",
        [0x63C7] = "침식",
        [0x5F9E] = "화염 전소",
        [0x63B7] = "정령 강림",
        [0x5FAE] = "재의 부름",
        [0x0098] = "정령 기본 공격 (소환수)",
        // 상대 종족 궁성 계열
        [0x5381] = "조준 화살",   // 옛 로그에는 "조준 화살 - Max"; 전투 분석기 표(19:03)는 "조준 화살"이다. 5382도 같은 스킬로 합친다 (별칭)
        [0x5809] = "속박의 눈",
        [0x5410] = "질풍 화살",   // 20:53 PvP log vs recording 204418: 사랑의 질풍 화살 "1" and "[방패 막기]1,684" = the hits of key 5410 at 20:53:07 (1) and 20:53:10 (1,684)
        [0x541A] = "광풍 화살",
        [0x5447] = "족쇄 화살",
        [0x53BF] = "송곳 화살",
        // 20:11 녹화 (사랑의 검 계열, 막기 줄 포함)
        [0x4D8F] = "빠른 베기",
        [0x4DAD] = "절혼 베기",
        [0x4DBD] = "쾌속 베기",
        // 21:46 허수아비 로그 (내 성직자 스킬): 번호는 스킬을 올리면 1씩 바뀌기도 한다 (심판의 번개 6590 -> 6591, 빠른 베기 4d8f -> 4d90)
        [0x67F3] = "고결한 기운",
        [0x67B6] = "대지의 축복",
        [0x1067B6] = "대지의 징벌",   // the same key 67b6, told apart by the multi-hit flag (DamageLedger.ResolveKey)
        [0x6582] = "방전",
        [0x65B0] = "벽력 MAX",
        // 이전 로그를 새 파서(다단히트·흡혈·보호막 타격 포함)로 다시 맞춘 것 (19:15, 19:45)
        [0x6D41] = "마르쿠탄의 분노",
        [0x4F27] = "나선 베기",
        [0x6155] = "공간 지배",
        [0x5FF6] = "소환: 불의 정령",   // 로그에는 "불의 정령: 도약 찍기"로 나온 키. 전투 분석기 표(17:04)를 따른다
        [0x6024] = "소환: 땅의 정령",   // 로그에는 "땅의 정령: 몸통 박치기"로 나온 키. 전투 분석기 표(17:04)를 따른다
        [0x5FB9] = "흡인",
        [0x612B] = "원소 융합",
        [0x55B6] = "지원 사격",
        [0x53FC] = "표적 화살",
        // 21:29 결투 로그 (사랑의 암살 계열)
        [0x4DE7] = "암습",
        [0x4EF9] = "연막탄",
        [0x4D9A] = "암검 투척",
        [0x4E05] = "암검 추적",
        [0x4F96] = "심장 찌르기",
        [0x4DC8] = "섬광 베기",
        // 23:47 PvP 로그 + 00:02 로그 (녹화 232940과 맞춤). 방전/저격/조준 화살은 스킬을 올리면 번호가 1씩 바뀌어 인접 키도 함께 둔다
        [0x6B68] = "공명쇄",
        [0x6B87] = "백열격",
        [0x6B59] = "격파쇄",
        [0x6B96] = "파열격",
        [0x6D7F] = "폭풍쇄",
        [0x53A0] = "연사",
        [0x48F5] = "심판",
        [0x4299] = "내려찍기",
        [0x42B3] = "도약 찍기",
        [0x4435] = "올려치기",
        // 00:02 로그 (녹화 232940). 숫자 1개로만 맞춘 것은 잠정 이름 - 더 확실한 이름이 나오면 고친다: 살기 파열, 파멸의 맹타, 맹렬한 일격, 응징의 일격, 회심의 일격, 빙결, 생기 증발, 불꽃 화살
        [0x4412] = "격파의 맹타",
        [0x43F3] = "돌격 자세",
        [0x41A0] = "절단의 맹타",
        [0x4655] = "살기 파열",
        // 12:15 로그 (녹화 114032, 연남[루미]의 공격, 모두 같은 시각의 같은 공격자): 숫자 2개 이상이 같은 키로 맞은 이름만.
        [0x43B9] = "돌진 일격",      // 2,496 / 2,491 / 2,949 / 2,814 / 1,860 / 2,265
        [0x4627] = "파괴",           // 2,021 / 1,580
        [0x4402] = "파동의 갑주",    // 1,040 / 992 / 1,245 / 1,074 / 1,293 / 1,431
        [0x43E3] = "격노 폭발",      // 2,315 / 1,270
        [0x422C] = "파멸의 맹타",
        [0x4796] = "맹렬한 일격",
        [0x47E5] = "응징의 일격",
        [0x47A5] = "회심의 일격",
        [0x5A4D] = "빙결",
        [0x5E2C] = "생기 증발",
        [0x5AA9] = "불꽃 화살",
        // 전투 분석기 표 (허수아비, 16:44). 타격 수와 합계가 맞은 것: 권능 폭발, 속박. 파멸의 목소리(671d)는 지속 피해 43틱(05 38 0A)까지 합쳐 표와 정확히 같다. 강습 낙인(6980)은 타격과 같은 순간의 고정 추가 390(플래그 0c)까지 합쳐 표와 같다
        [0x66FF] = "권능 폭발",
        [0x6675] = "속박",
        [0x671D] = "파멸의 목소리",
        [0x6980] = "강습 낙인",
        // 전투 분석기 표 (17:04 전투, 35행). 타격 수와 합계(또는 지속 피해 틱)가 모두 맞은 것. 협공: 부식(6043)은 지속 피해 20틱 87,820까지 맞는다
        [0x60DB] = "소환: 고대의 정령",
        [0x6192] = "불길의 축복",
        [0x6005] = "소환: 물의 정령",
        [0x6015] = "소환: 바람의 정령",
        [0x6422] = "연속 역류",
        [0x5F7D] = "진공 폭발",
        [0x5F8C] = "대지 진동",
        [0x5F6D] = "냉기 충격",
        [0x604A] = "고대의 정령: 부수기",
        [0x60CC] = "협공: 파멸의 공세",
        [0x6044] = "불의 정령: 격노 폭발",
        [0x5FC9] = "영혼의 절규",
        [0x6047] = "땅의 정령: 도발",
        [0x11E1] = "신석",   // the stigma stone effect: one key for every stone (전투 분석 2026-11-21 02:38: 메스람타에다의 후회 20 hits 134,903, 트리니엘의 이성 9 + 트라시메데스의 현명 28 hits = 37 hits 531,316 under 11e1); the stone's own name is not in the key
        [0x6043] = "협공: 부식",
        // 전투 분석기 표 (17:26, 8행). 마법 차단, 마법 강탈, 공포의 절규는 타격 수와 합계가, 저주의 구름(60ad)은 지속 피해 19틱까지 합쳐 맞았다. 강습 공포(638a)는 고정 추가 390 두 번(780)까지 합쳐 맞았다
        [0x60EA] = "마법 차단",
        [0x60BD] = "마법 강탈",
        [0x5FD8] = "공포의 절규",
        [0x638A] = "강습 공포",
        [0x60AD] = "저주의 구름",
        // 전투 분석기 표 (18:03 전투, 18행). 타격 수와 합계(또는 지속 피해 틱과 표를 찍은 시각)가 모두 맞았다: 불의 장벽 4틱 18,293, 냉기 폭풍 5틱 15,920
        [0x59C5] = "지옥의 화염",
        [0x59B5] = "불꽃 폭발",
        [0x5A04] = "냉기 파동",
        [0x59A5] = "불꽃 작살",
        [0x59F5] = "얼음 사슬",
        [0x5AB8] = "빙결 폭발",
        [0x5A10] = "겨울의 속박",
        [0x5B50] = "지연 폭발",
        [0x5DA3] = "불의 표식",
        [0x5E0E] = "강화의 은혜",
        [0x5AE6] = "열화",
        [0x5996] = "작렬",
        [0x5BBB] = "불의 장벽",
        [0x5B15] = "혹한의 바람",
        [0x5A99] = "냉기 폭풍",
        [0x5DC2] = "냉기 소환",
        // 전투 분석기 표 (18:30 전투, 12행). 타격 수와 합계가 모두 맞았다. 5b31은 루미엘의 공간과 나락이 같이 쓰는 키다: 시전마다 첫 타가 루미엘의 공간, 0.25초 안의 두 번째 타가 나락 (FollowUps)
        [0x5A2E] = "영혼 동결",
        [0x5B8D] = "신성 폭발",
        [0x5A1F] = "빙하 강타",
        [0x5D94] = "강습 폭격",
        [0x5B31] = "루미엘의 공간",
        [0x105B31] = "나락",
        // 전투 분석기 표 (18:37 전투, 20행). 타격 수와 합계가 모두 맞았다. 징벌(480f)은 지속 피해 15틱까지 합쳐 맞는다. 위협의 맹타, 연속 난타, 응징의 일격은 키 두 개를 합쳐야 맞는다 (아래 별칭)
        [0x47B4] = "필사의 일격",
        [0x4950] = "섬멸",
        [0x47F1] = "파멸의 방패",
        [0x499C] = "비호의 일격",
        [0x4BE0] = "단죄의 가호",
        [0x481F] = "방패 강타",
        [0x49F8] = "처형의 검",
        [0x4922] = "쇠약의 맹타",
        [0x483D] = "도발",
        [0x4A16] = "방패 돌격",
        [0x484C] = "포획",
        [0x495F] = "주신의 징벌",
        [0x48D6] = "나포",
        [0x480F] = "징벌",
        [0x4A26] = "위협의 맹타",
        [0x47C3] = "연속 난타",
        // 전투 분석기 표 (19:03 궁성 전투, 22행). 타격 수와 합계(또는 지속 피해 틱)가 맞았다. 치명타·완벽·강타·전방·후방·다단 퍼센트도 21행 모두 앱과 같다. 폭발의 덫(5475)은 표에 첫 타 3개(458,878)만 있고, 트랩마다 3초 뒤의 두 번째 타(합 189,485)는 표에 없다: 앱은 같은 이름으로 센다
        [0x53CD] = "그리폰 화살",
        [0x53EC] = "파열 화살",
        [0x5597] = "폭발 화살",
        [0x53DD] = "제압 화살",
        [0x57BC] = "집중 포화",
        [0x53AF] = "나선 화살",
        [0x579E] = "강습 강타",
        [0x5475] = "폭발의 덫",
        // 받은 회복 (수호성·성직자 로그, 18:41~18:59): 로그의 "N의 생명력을 회복했습니다"와 금액이 같다. 보호의 방패(482e)는 막기 성공 시 정신력 260 (스탬프 67eb)과 행동력 260 (67ec, 26000으로 온다)을 회복한다: 생명력이 아니다
        [0x4BD1] = "비호의 방패",
        [0x48E5] = "고결의 갑주",
        [0x482E] = "보호의 방패",
        // 전투 분석기 표 (19:17 전투, 16행). 타격 수와 합계가 모두 맞았고, 치명타·완벽·강타·전방·후방·다단 퍼센트도 16행 모두 앱과 같다
        [0x6D04] = "회전격",
        [0x6B77] = "벽력쇄",
        [0x6BA5] = "타격쇄",
        [0x6BD3] = "돌진 격파",
        [0x6BC4] = "파동격",
        [0x6C8A] = "진동쇄",
        [0x6C2F] = "열파격",
        // 전투 분석기 표 (19:24~19:27 전투, 4행과 1행). 타격 수와 합계가 모두 맞았고 퍼센트 6칸도 앱과 같다
        [0x6C10] = "분쇄격",
        [0x6C99] = "멸화",
        [0x6CA8] = "결박의 낙인",
        [0x6F75] = "강습 충격",

        // 정령(고대·불·바람·땅·물): 같은 번호를 여러 정령이 쓴다 (0098 기본 공격, 5f5f, 5f60). 등장 메시지의 종류(41 36)로 나눈 이름이고, 번호는 SpiritKey가 만든다 (종류 << 16 | 번호).
        // 전투 분석기 표(17:04, 소스 번호별 합계가 표와 정확히 맞음)로 확인했다.
        [0x10098] = "고대의 정령: 기본 공격", [0x20098] = "불의 정령: 기본공격", [0x30098] = "바람의 정령: 기본 공격", [0x40098] = "땅의 정령: 기본 공격", [0x50098] = "물의 정령: 기본 공격",
        [0x15F5F] = "고대의 정령: 파괴", [0x25F5F] = "불의 정령: 화염 폭발", [0x35F5F] = "바람의 정령: 회오리",
        [0x15F60] = "고대의 정령: 자기 폭풍", [0x25F60] = "불의 정령: 운석 소환",
        // The game's skill codes and names (checked against our own recordings; the packet stamp is RawSkillCode * 100, the key its high 16 bits: key = (raw * 100) >> 16, checked on 148 of 159 names we already had). Keys whose names are shared with another skill (potions, scrolls, spirit skills) are left out.
        [0x716E] = "강격",
        [0x7469] = "강격[폭주]",
        [0x4BB2] = "강습 맹격",
        [0x7383] = "강습 타격",
        [0x5A5C] = "강철 보호막",
        [0x6080] = "강화: 정령의 가호",
        [0x433C] = "검기 난무",
        [0x6FE0] = "격노의 주문",
        [0x57AD] = "경계의 눈",
        [0x7604] = "경량화",
        [0x435A] = "공중 결박",
        [0x72FA] = "공파참",
        [0x41EC] = "광폭 파동",
        [0x6BF4] = "굳건한 의지",
        [0x7392] = "권기 방출",
        [0x4ECC] = "그림자 낙하",
        [0x43D4] = "근성",
        [0x5429] = "기습 차기",
        [0x496E] = "네자칸의 방패",
        [0xA0F3] = "달인의 포구스 구이",
        [0x80C9] = "달인의 푸제리 찜",
        [0x5588] = "대자연의 숨결",
        [0x51D6] = "독 바르기",
        [0x4E33] = "맹수의 후려치기",
        [0x6061] = "명령: 대역",
        [0x42A4] = "무모한 일격",
        [0x5FF5] = "무적",
        [0x6046] = "물의 정령: 얼음 사슬",
        [0x6049] = "바람의 정령: 돌풍",
        [0x42C2] = "발목 베기",
        [0x7DDC] = "백룡의 허브 차",
        [0x55A7] = "번개 화살",
        [0x6788] = "벼락 난사",
        [0x45DB] = "보호의 갑옷",
        [0x5466] = "봉인 화살",
        [0x72EB] = "분노 회복",
        [0x41CD] = "분노의 일격",
        [0x41DD] = "분쇄 파동",
        [0x7337] = "분신",
        [0x717D] = "비각",
        [0x7478] = "비각[폭주]",
        [0x6BDA] = "비동의적 결속",
        [0x5AC7] = "빙설의 갑주",
        [0x498E] = "섬광 난무",
        [0xEE7D] = "수호받는 영혼",
        [0x6DCA] = "수호의 축복",
        [0x718C] = "승강권",
        [0x7487] = "승강권[폭주]",
        [0x7234] = "승천타",
        [0x7224] = "승천타[폭주]",
        [0x6BD1] = "시련 흡수",
        [0xB2DF] = "신석: 페르크의 본능",
        [0x6F94] = "십자 방어",
        [0x4E52] = "암영보",
        [0x4786] = "에레슈란타 하층 아티팩트 III",
        [0x714F] = "연격",
        [0x744A] = "연격[폭주]",
        [0x6164] = "연속 난사",
        [0x73C2] = "연환권",
        [0x73D1] = "연환권[폭주]",
        [0x41AF] = "예리한 일격",
        [0x6BE3] = "온기",
        [0x434B] = "유린의 검",
        [0x51B7] = "육감 극대화",
        [0x48A8] = "이중 갑옷",
        [0x7374] = "전력",
        [0x63F4] = "정령 교감",
        [0x63D6] = "정령 회생",
        [0x758A] = "정면 돌파",
        [0x77E0] = "정신의 물약",
        [0x715E] = "중격",
        [0x7459] = "중격[폭주]",
        [0x71C9] = "지면 강타",
        [0x71D8] = "지면 강타[폭주]",
        [0x6C3D] = "질주의 진언",
        [0x6D13] = "질풍 난무",
        [0x71AB] = "질풍격",
        [0x71B9] = "질풍격[폭주]",
        [0x6CC7] = "질풍의 권능",
        [0x4238] = "집중 막기",
        [0x6C1F] = "집중 방어",
        [0x740C] = "참영권",
        [0x729F] = "철산고",
        [0x72AE] = "철산고[폭주]",
        [0xA0F4] = "축제의 샴페인",
        [0x75E5] = "충격타",
        [0x719B] = "충권",
        [0x7496] = "충권[폭주]",
        [0x6C4D] = "쾌유의 손길",
        [0x6C01] = "쾌유의 주문",
        [0x72CC] = "태풍 연타[폭주]",
        [0x7318] = "파쇄",
        [0x41BE] = "파열의 일격",
        [0x7271] = "폭렬권",
        [0x7261] = "폭렬권[폭주]",
        [0x4F83] = "폭풍 난무",
        [0x45EA] = "피의 흡수",
        [0x08F7] = "피해 반사",
        [0x550E] = "화살 폭풍",
        [0x5977] = "화염 난사",
        [0x728F] = "환영각[폭주]",
        [0x5231] = "회생의 계약",
        [0x7243] = "회전각",
        [0x7252] = "회전각[폭주]",
        [0x4DF6] = "회피 자세",
        [0x80F3] = "흑룡의 씨푸드 파스타",
        [0x4397] = "흡혈의 검",
    };

    /// <summary>
    /// Skills whose hits come in more than one key but are one skill in the combat log: key -> the key they are counted under.
    /// 천벌: every cast is a <c>65a0</c> hit and, 0.1 s later, a <c>6591</c> hit on the same target (and on the summons next to it); the log calls both "천벌" (docs/PROTOCOL.md §3.6).
    /// </summary>
    private static readonly Dictionary<uint, uint> Aliases = new()
    {
        // 6591 is 천벌's follow-up hit but ALSO 심판의 번개 after its upgrade (21:46): the ledger tells them apart (DamageLedger.ResolveKey);
        // this alias only covers what was stored before.
        [0x6591] = 0x65A0,
        [0x676D] = 0x676A,   // 단죄 (1,238,064 and 619,033 in the 21:46 log)
        // 사랑의 베기 3종: every use is a chain - hit 1 under one key, hits 2..4 0.05-0.15 s later under another (docs/PROTOCOL.md §3.6).
        // The log names of the chain keys were checked line by line (20:11 and 21:29): 4d8c 423/714/760/621/82, 4daa 585/601/252/641, 4db9 569/337/599/335.
        [0x4D8C] = 0x4D8F,
        [0x4DAA] = 0x4DAD,
        [0x4DB9] = 0x4DBD,
        // The same chains under a changed first key (4d90, 4daf, 4dbe: 20:18, same follow-up keys right after; no log line of their own).
        [0x4D90] = 0x4D8F,
        [0x4DAF] = 0x4DAD,
        [0x4DBE] = 0x4DBD,
        // Upgraded skills get a new number (see the 21:46 note above); same name, counted together: 송곳 화살 53c0 (23:47/00:02 logs), 방전 6581, 저격 5391.
        // 내려찍기 3,790 / 2,729 / 3,071 and 올려치기 5,853 of 연남[루미] (12:15 log) come under 4296 and 4432, the keys of 4299 and 4435 (the same log names) +3 lower: counted together.
        [0x4296] = 0x4299,
        [0x4432] = 0x4435,
        [0x53C0] = 0x53BF,
        [0x6581] = 0x6582,
        [0x5391] = 0x5390,
        [0x4229] = 0x422C,   // 파멸의 맹타: 10,845 under 422c, 1,082 under 4229 (00:02 log)
        [0x6127] = 0x612B,   // 원소 융합: 612b 10 hits 6,645,733 + 6127 10 hits 1,742,936 = the table's 20 hits 8,388,669 (17:04)
        [0x6034] = 0x6037,   // 협공: 저주: 4 hits 525,061 under 6034 + 1 damage-over-time tick 10,054 = the table's 535,115 (17:04)
        [0x6033] = 0x6037,   // the tick key of that debuff (05 38 0A) is one below its hit key
        [0x6042] = 0x6043,   // 협공: 부식: its ticks (20 of them, 87,820) carry the key one below the hit key 6043
        [0x4A29] = 0x4A26,   // 위협의 맹타: 4a26 + 4a29 = the table's 18 hits 1,454,934 (18:37)
        [0x4A2B] = 0x4A26,   // 위협의 맹타: 4a26 16 hits 4,554,866 + 4a2b 16 hits 4,463,977 = 전투 분석 표 (수호성, 2026-11-21 02:38)의 32 hits 9,018,843 (the second hit of each use)
        [0x4810] = 0x480F,   // 징벌: 4810 4 hits 4,392,597 + 608,898 지속 피해 = the table's 5,001,495 (최고 2,367,495)
        [0x47C4] = 0x47C3,   // 연속 난타: 47c3 + 47c4 = the table's 21 hits 680,573 (18:37)
        [0x47E2] = 0x47E5,   // 응징의 일격: 47e2 + 47e7 = the table's 12 hits 722,907 (18:37); 47e5 (older log) is taken to be a variant of it
        [0x47E7] = 0x47E5,
        [0x5382] = 0x5381,   // 조준 화살: both keys are the aimed shot (the table names 5381 "조준 화살")
        [0x53BE] = 0x53BF,   // 송곳 화살: 53be 30 hits + 11 damage-over-time ticks 20,042 = the table's 30 hits 3,729,227 (19:03)
        // 전투 분석 마도성 표(같은 구간): 이미 이름이 있는 번호의 이웃 번호 (한 스킬이 단계마다 번호가 다르다). 타수·합계가 정확히 같거나(5a15 4 hits 3,563,078, 59f1 3 hits 519,462, 5a01 2 hits 424,456) 한 번의 최고 피해가 같다(59b9 1,069,141, 59a8 339,892)
        [0x5A15] = 0x5A10,   // 겨울의 속박
        [0x59F1] = 0x59F5,   // 얼음 사슬
        [0x5A01] = 0x5A04,   // 냉기 파동
        [0x59B9] = 0x59B5,   // 불꽃 폭발
        [0x59A8] = 0x59A5,   // 불꽃 작살
        [0x53ED] = 0x53EC,   // 파열 화살: 53ed 6 hits 13,261,201 + 53ec 6 hits 5,886,713 = the combat table's 12 hits 19,147,914 (2026-11-21 02:38; the biggest hit 3,524,031 is 53ed's): one skill, two keys
        [0x4E42] = 0x4E43,   // 문양 폭발: 4e42 27 hits 28,947,889 = 전투 분석 표 (2026-11-21 02:38); 4e43 is the key of the 2026-10-12 table
        [0x4F92] = 0x4F96,   // 심장 찌르기: 4f92 115 hits 61,645,619 (최고 1,992,148) = 전투 분석 표 (같은 날 02:38); 4f96 was the name from the 2026-10-12 table
        [0x53FB] = 0x53FC,   // 표적 화살: 53fb 3 hits 456,421 = the table's (19:03)
        [0x0867] = 0x5410,   // 질풍 화살: the 19:03 table named 0867; the 20:53 recording has the hits under 5410 (one skill, two keys)
        [0x4DD7] = 0x4DD8,   // 기습: the key just below (120 hits in the recordings; a guess from the neighbouring key, not checked against a table)
        [0x48F4] = 0x48F5,   // 심판 (guess from the neighbouring key: 149 hits, max 869,692)
        [0x65BE] = 0x65BF,   // 고통의 연쇄: the tick key (420 hits, guess)
        [0x6D05] = 0x6D04,   // 회전격 (11 hits, max 4,006,190; guess)
        [0x6B86] = 0x6B87,   // 백열격 (26 hits; guess)
        [0x42C3] = 0x42C2,   // 발목 베기 (14 hits; guess)
        // keys found by the skill number inside them (key = raw * 100 >> 16, the first four digits of raw are the skill): the same skill at another step
        [0x65DF] = 0x65DD,   // 재생의 빛
        [0x65E0] = 0x65DD,   // 재생의 빛
        [0x6FA3] = 0x6F94,   // 십자 방어
        [0x59AA] = 0x59A5,   // 불꽃 작살
        [0x5B18] = 0x5B15,   // 혹한의 바람
        [0x6C00] = 0x6C01,   // 쾌유의 주문
        [0x42B7] = 0x42B3,   // 도약 찍기
        [0x42B4] = 0x42B3,   // 도약 찍기
        [0x42B6] = 0x42B3,   // 도약 찍기
        [0x6128] = 0x612B,   // 원소 융합
        [0x65A3] = 0x65A0,   // 천벌
        [0x65C2] = 0x65BF,   // 고통의 연쇄
        [0x73D0] = 0x73D1,   // 연환권[폭주]
        [0x4923] = 0x4922,   // 쇠약의 맹타
        [0x4927] = 0x4922,   // 쇠약의 맹타
        [0x5457] = 0x5447,   // 족쇄 화살
        [0x7229] = 0x7224,   // 승천타[폭주]
        [0x6C32] = 0x6C2F,   // 열파격
        [0x7309] = 0x72FA,   // 공파참
        [0x4904] = 0x48F5,   // 심판
        [0x556C] = 0x5569,   // 화살 난사
        [0x65D1] = 0x65CE,   // 약화의 낙인
        [0x6D09] = 0x6D04,   // 회전격
        [0x5B63] = 0x5B64,   // 겨울의 환영
        [0x497E] = 0x496E,   // 네자칸의 방패
        [0x4821] = 0x481F,   // 방패 강타
        [0x4795] = 0x4796,   // 맹렬한 일격
        [0x6156] = 0x6155,   // 공간 지배
        // from the game's skill table (see the note at the end of Names): more keys of skills that are already named
        [0x746D] = 0x7469,   // 강격[폭주]
        [0x6F76] = 0x6F75,   // 강습 충격
        [0x4341] = 0x433C,   // 검기 난무
        [0x5A12] = 0x5A10,   // 겨울의 속박
        [0x5A13] = 0x5A10,   // 겨울의 속박
        [0x5B5F] = 0x5B64,   // 겨울의 환영
        [0x6B5C] = 0x6B59,   // 격파쇄
        [0x6B5E] = 0x6B59,   // 격파쇄
        [0x4413] = 0x4412,   // 격파의 맹타
        [0x4415] = 0x4412,   // 격파의 맹타
        [0x4416] = 0x4412,   // 격파의 맹타
        [0x6159] = 0x6155,   // 공간 지배
        [0x6B69] = 0x6B68,   // 공명쇄
        [0x6B6B] = 0x6B68,   // 공명쇄
        [0x6B6C] = 0x6B68,   // 공명쇄
        [0x6B6D] = 0x6B68,   // 공명쇄
        [0x6DAE] = 0x6DAC,   // 관통쇄
        [0x6DAF] = 0x6DAC,   // 관통쇄
        [0x6DB1] = 0x6DAC,   // 관통쇄
        [0x41EE] = 0x41EC,   // 광폭 파동
        [0x541C] = 0x541A,   // 광풍 화살
        [0x541F] = 0x541A,   // 광풍 화살
        [0x4DD9] = 0x4DD8,   // 기습
        [0x4DDB] = 0x4DD8,   // 기습
        [0x53B0] = 0x53AF,   // 나선 화살
        [0x53B1] = 0x53AF,   // 나선 화살
        [0x53B2] = 0x53AF,   // 나선 화살
        [0x53B3] = 0x53AF,   // 나선 화살
        [0x53B4] = 0x53AF,   // 나선 화살
        [0x4294] = 0x4299,   // 내려찍기
        [0x5F6F] = 0x5F6D,   // 냉기 충격
        [0x5F70] = 0x5F6D,   // 냉기 충격
        [0x6576] = 0x6572,   // 뇌전
        [0x5F8D] = 0x5F8C,   // 대지 진동
        [0x5F8F] = 0x5F8C,   // 대지 진동
        [0x6566] = 0x6563,   // 대지의 응보
        [0x42B2] = 0x42B3,   // 도약 찍기
        [0x43B6] = 0x43B9,   // 돌진 일격
        [0x43B7] = 0x43B9,   // 돌진 일격
        [0x6048] = 0x6047,   // 땅의 정령: 도발
        [0x4799] = 0x4796,   // 맹렬한 일격
        [0x479B] = 0x4796,   // 맹렬한 일격
        [0x4E27] = 0x4E24,   // 맹수의 뒷발차기
        [0x4E28] = 0x4E24,   // 맹수의 뒷발차기
        [0x4E18] = 0x4E15,   // 맹수의 포효
        [0x4E37] = 0x4E33,   // 맹수의 후려치기
        [0x4E46] = 0x4E43,   // 문양 폭발
        [0x6544] = 0x30098,   // 바람의 정령: 기본 공격
        [0x6585] = 0x6582,   // 방전
        [0x4A17] = 0x4A16,   // 방패 돌격
        [0x6B8A] = 0x6B87,   // 백열격
        [0x6B8C] = 0x6B87,   // 백열격
        [0x6789] = 0x6788,   // 벼락 난사
        [0x65B3] = 0x65AF,   // 벽력
        [0x65B4] = 0x65AF,   // 벽력
        [0x6B78] = 0x6B77,   // 벽력쇄
        [0x6B7B] = 0x6B77,   // 벽력쇄
        [0x6B7C] = 0x6B77,   // 벽력쇄
        [0x41CE] = 0x41CD,   // 분노의 일격
        [0x41DF] = 0x41DD,   // 분쇄 파동
        [0x59B4] = 0x59B5,   // 불꽃 폭발
        [0x59B6] = 0x59B5,   // 불꽃 폭발
        [0x5AA8] = 0x5AA9,   // 불꽃 화살
        [0x5AAC] = 0x5AA9,   // 불꽃 화살
        [0x5AAD] = 0x5AA9,   // 불꽃 화살
        [0x6045] = 0x6044,   // 불의 정령: 격노 폭발
        [0x7180] = 0x717D,   // 비각
        [0x7181] = 0x717D,   // 비각
        [0x747B] = 0x7478,   // 비각[폭주]
        [0x747C] = 0x7478,   // 비각[폭주]
        [0x499D] = 0x499C,   // 비호의 일격
        [0x5AB7] = 0x5AB8,   // 빙결 폭발
        [0x4DC9] = 0x4DC8,   // 섬광 베기
        [0x4952] = 0x4950,   // 섬멸
        [0x6006] = 0x6005,   // 소환: 물의 정령
        [0x6009] = 0x6005,   // 소환: 물의 정령
        [0x5FF7] = 0x5FF6,   // 소환: 불의 정령
        [0x5FFA] = 0x5FF6,   // 소환: 불의 정령
        [0x557B] = 0x5579,   // 속사
        [0x557C] = 0x5579,   // 속사
        [0x53C2] = 0x53BF,   // 송곳 화살
        [0x53C3] = 0x53BF,   // 송곳 화살
        [0x718F] = 0x718C,   // 승강권
        [0x748A] = 0x7487,   // 승강권[폭주]
        [0x748C] = 0x7487,   // 승강권[폭주]
        [0x7237] = 0x7234,   // 승천타
        [0x7228] = 0x7224,   // 승천타[폭주]
        [0x6639] = 0x6638,   // 신성한 기운
        [0x663E] = 0x6638,   // 신성한 기운
        [0x4F94] = 0x4F96,   // 심장 찌르기
        [0x4F97] = 0x4F96,   // 심장 찌르기
        [0x48FA] = 0x48F5,   // 심판
        [0x6592] = 0x6590,   // 심판의 번개
        [0x4D9B] = 0x4D9A,   // 암검 투척
        [0x6BE4] = 0x6BE2,   // 암격쇄
        [0x6BE6] = 0x6BE2,   // 암격쇄
        [0x6BE7] = 0x6BE2,   // 암격쇄
        [0x7154] = 0x714F,   // 연격
        [0x744F] = 0x744A,   // 연격[폭주]
        [0x53A2] = 0x53A0,   // 연사
        [0x53A3] = 0x53A0,   // 연사
        [0x53A5] = 0x53A0,   // 연사
        [0x6165] = 0x6164,   // 연속 난사
        [0x47C5] = 0x47C3,   // 연속 난타
        [0x47C7] = 0x47C3,   // 연속 난타
        [0x47C8] = 0x47C3,   // 연속 난타
        [0x73C5] = 0x73C2,   // 연환권
        [0x73D5] = 0x73D1,   // 연환권[폭주]
        [0x6C33] = 0x6C2F,   // 열파격
        [0x5AE9] = 0x5AE6,   // 열화
        [0x5AEA] = 0x5AE6,   // 열화
        [0x5FC8] = 0x5FC9,   // 영혼의 절규
        [0x5439] = 0x5438,   // 올가미 화살
        [0x543C] = 0x5438,   // 올가미 화살
        [0x543D] = 0x5438,   // 올가미 화살
        [0x4430] = 0x4435,   // 올려치기
        [0x47E4] = 0x47E5,   // 응징의 일격
        [0x5999] = 0x5996,   // 작렬
        [0x599B] = 0x5996,   // 작렬
        [0x65E2] = 0x65DD,   // 재생의 빛
        [0x5FAF] = 0x5FAE,   // 재의 부름
        [0x5393] = 0x5390,   // 저격
        [0x5394] = 0x5390,   // 저격
        [0x5396] = 0x5390,   // 저격
        [0x41A1] = 0x41A0,   // 절단의 맹타
        [0x41A3] = 0x41A0,   // 절단의 맹타
        [0x41A5] = 0x41A0,   // 절단의 맹타
        [0x5385] = 0x5381,   // 조준 화살
        [0x5448] = 0x5447,   // 족쇄 화살
        [0x544B] = 0x5447,   // 족쇄 화살
        [0x544D] = 0x5447,   // 족쇄 화살
        [0x745E] = 0x7459,   // 중격[폭주]
        [0x59C4] = 0x59C5,   // 지옥의 화염
        [0x59C7] = 0x59C5,   // 지옥의 화염
        [0x59C9] = 0x59C5,   // 지옥의 화염
        [0x5F7C] = 0x5F7D,   // 진공 폭발
        [0x5F7E] = 0x5F7D,   // 진공 폭발
        [0x5F80] = 0x5F7D,   // 진공 폭발
        [0x6C3E] = 0x6C3D,   // 질주의 진언
        [0x6D14] = 0x6D13,   // 질풍 난무
        [0x540B] = 0x5410,   // 질풍 화살
        [0x540D] = 0x5410,   // 질풍 화살
        [0x540E] = 0x5410,   // 질풍 화살
        [0x71BA] = 0x71B9,   // 질풍격[폭주]
        [0x7355] = 0x4238,   // 집중 막기
        [0x4811] = 0x480F,   // 징벌
        [0x4813] = 0x480F,   // 징벌
        [0x4815] = 0x480F,   // 징벌
        [0x65A2] = 0x65A0,   // 천벌
        [0x431D] = 0x66C5,   // 충격 해제
        [0x431E] = 0x66C5,   // 충격 해제
        [0x4913] = 0x66C5,   // 충격 해제
        [0x4F09] = 0x66C5,   // 충격 해제
        [0x54FF] = 0x66C5,   // 충격 해제
        [0x5AD6] = 0x66C5,   // 충격 해제
        [0x608F] = 0x66C5,   // 충격 해제
        [0x66C2] = 0x66C5,   // 충격 해제
        [0x6C7A] = 0x66C5,   // 충격 해제
        [0x6C7B] = 0x66C5,   // 충격 해제
        [0x719E] = 0x719B,   // 충권
        [0x7499] = 0x7496,   // 충권[폭주]
        [0x749B] = 0x7496,   // 충권[폭주]
        [0x65ED] = 0x65EC,   // 치유의 빛
        [0x65EE] = 0x65EC,   // 치유의 빛
        [0x660C] = 0x660B,   // 쾌유의 광휘
        [0x660D] = 0x660B,   // 쾌유의 광휘
        [0x660E] = 0x660B,   // 쾌유의 광휘
        [0x6C02] = 0x6C01,   // 쾌유의 주문
        [0x6C03] = 0x6C01,   // 쾌유의 주문
        [0x72CD] = 0x72CC,   // 태풍 연타[폭주]
        [0x6BC3] = 0x6BC4,   // 파동격
        [0x422B] = 0x422C,   // 파멸의 맹타
        [0x422E] = 0x422C,   // 파멸의 맹타
        [0x53EE] = 0x53EC,   // 파열 화살
        [0x53EF] = 0x53EC,   // 파열 화살
        [0x53F0] = 0x53EC,   // 파열 화살
        [0x53F1] = 0x53EC,   // 파열 화살
        [0x6B99] = 0x6B96,   // 파열격
        [0x6B9B] = 0x6B96,   // 파열격
        [0x41BF] = 0x41BE,   // 파열의 일격
        [0x484D] = 0x484C,   // 포획
        [0x7272] = 0x7271,   // 폭렬권
        [0x7274] = 0x7271,   // 폭렬권
        [0x7263] = 0x7261,   // 폭렬권[폭주]
        [0x7265] = 0x7261,   // 폭렬권[폭주]
        [0x6D81] = 0x6D7F,   // 폭풍쇄
        [0x6D82] = 0x6D7F,   // 폭풍쇄
        [0x6D83] = 0x6D7F,   // 폭풍쇄
        [0x53FD] = 0x53FC,   // 표적 화살
        [0x0997] = 0x08F7,   // 피해 반사
        [0x0998] = 0x08F7,   // 피해 반사
        [0x0999] = 0x08F7,   // 피해 반사
        [0x0AC9] = 0x08F7,   // 피해 반사
        [0x0ACA] = 0x08F7,   // 피해 반사
        [0x0ACB] = 0x08F7,   // 피해 반사
        [0x47B5] = 0x47B4,   // 필사의 일격
        [0x47B8] = 0x47B4,   // 필사의 일격
        [0x47B9] = 0x47B4,   // 필사의 일격
        [0x6038] = 0x6037,   // 협공: 저주
        [0x5B17] = 0x5B15,   // 혹한의 바람
        [0x556A] = 0x5569,   // 화살 난사
        [0x5978] = 0x5977,   // 화염 난사
        [0x5F9B] = 0x5F9E,   // 화염 전소
        [0x5FA0] = 0x5F9E,   // 화염 전소
        [0x5827] = 0x5231,   // 회생의 계약
        [0x5E1D] = 0x5231,   // 회생의 계약
        [0x6413] = 0x5231,   // 회생의 계약
        [0x75F5] = 0x5231,   // 회생의 계약
        [0x47A8] = 0x47A5,   // 회심의 일격
        [0x47AA] = 0x47A5,   // 회심의 일격
        [0x6D06] = 0x6D04,   // 회전격
        [0x6D07] = 0x6D04,   // 회전격
    };

    /// <summary>
    /// Keys one cast hits twice that belong to two skills: the first hit is the key's own skill, a second hit of the same caster within
    /// <see cref="FollowUpWindow"/> is the skill under the key it maps to. 5b31: 루미엘의 공간 then 나락 (18:30 table: the first hits add up to
    /// the 555,487 of 루미엘의 공간 and the second hits to the 376,241 of 나락; 3 casts, 0.1-0.2 s apart).
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, uint> FollowUps = new Dictionary<uint, uint> { [0x5B31] = 0x105B31 };

    public static readonly TimeSpan FollowUpWindow = TimeSpan.FromMilliseconds(250);

    public static uint Canonical(uint skillKey) => Aliases.GetValueOrDefault(skillKey, skillKey);

    public static string Name(uint skillKey) => Names.TryGetValue(Canonical(skillKey), out var n) ? n : $"스킬 {skillKey:x4}";

    public static bool IsKnown(uint skillKey) => Names.ContainsKey(Canonical(skillKey));

    /// <summary>
    /// Recoveries that are not life: 충격 해제 (66c5, 행동력 1000 as 100,000) and 보호의 방패 (482e), which gives 정신력 and 행동력 (the action points arrive as 100 times the amount), which look like heals in the message.
    /// </summary>
    public static bool IsLifeRecovery(uint skillKey) => Canonical(skillKey) is not (0x482E or 0x66C5 or 0x67D4);   // 66c5: action points (100 times the amount), not listed as a heal in the chat log

    /// <summary>A shield (a protective barrier, not life and not damage): 유스티엘의 권능 (67d4). Its ticks reach other party members as the same kind of 05 38 message; they are ignored.</summary>
    public static bool IsShield(uint skillKey) => Canonical(skillKey) == 0x67D4;

    private static readonly HashSet<string> HelpfulOnly = ["면죄", "십자 방어", "대자연의 숨결", "재생의 빛", "유스티엘의 권능"];

    /// <summary>Skills that only heal or shield (면죄, 십자 방어, 대자연의 숨결, 재생의 빛, 유스티엘의 권능): a tick of one of them that arrives flagged as damage over time is not damage.</summary>
    public static bool IsHelpfulOnly(uint skillKey) => HelpfulOnly.Contains(Name(skillKey));

    /// <summary>The keys that have exactly this name (for tools: a name that exists under another key means the new key is a variant of it, an alias).</summary>
    public static IEnumerable<uint> KeysNamed(string name) => Names.Where(x => x.Value == name).Select(x => x.Key);

    /// <summary>
    /// What kind of elemental spirit a spawn message's template is (1 고대, 2 불, 3 바람, 4 땅, 5 물), 0 for anything else. The spirits have
    /// no owner name in their spawn message; the template is what tells a fire spirit from a wind spirit.
    /// </summary>
    public static int SpiritKind(uint template) => template switch
    {
        0x2C8EFE => 1, 0x2C8EB3 => 2, 0x2C8ED3 => 3, 0x2C8EEF => 4, 0x2C8EC7 => 5,
        _ => 0,
    };

    /// <summary>The key a spirit's hit is counted under: the spirits share some numbers, so (kind, key) gets its own number when the table names it; otherwise the key itself.</summary>
    public static uint SpiritKey(uint template, uint skillKey)
    {
        int kind = SpiritKind(template);
        return kind == 0 || !Names.ContainsKey((uint)kind << 16 | skillKey) ? skillKey : (uint)kind << 16 | skillKey;
    }
}
