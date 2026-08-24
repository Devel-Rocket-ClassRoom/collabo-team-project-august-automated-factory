using UnityEngine;

namespace Factory.Data
{
    // 기계 한 종류의 정의. Assets/Resources/GameData/Machines 폴더에 애셋을 추가하면
    // 코드 수정 없이 GameDatabase가 자동으로 인식한다.
    [CreateAssetMenu(menuName = "Factory/Machine Definition", fileName = "NewMachine")]
    public class MachineDef : ScriptableObject
    {
        [Tooltip("안정적인 문자열 키.")]
        public string machineId;

        public string displayName;
        public MachineCategory category;
        public Vector2Int footprint = Vector2Int.one;
        public float speedMultiplier = 1f;

        [Tooltip("이 기계 종류 전용 외형 프리팹. 비어있으면 고스트/배치 도구가 기본 박스로 대체한다.")]
        public GameObject visualPrefab;
    }
}
