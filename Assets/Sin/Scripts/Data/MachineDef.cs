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

        [Tooltip("category가 Miner일 때만 쓰임: 이 채굴기가 캐낼 자원. 실제 자원 노드/지형 시스템이 생기기 전까지의 임시 단순화.")]
        public ResourceDef minerOutput;
    }
}
