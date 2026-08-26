using Factory.Simulation;
using UnityEngine;

namespace Factory.Data
{
    // 땅 위 광물 노드 하나의 정의. 채굴기는 이제 종류가 하나뿐이고, 실제로 뭘 캐는지는
    // 이 노드가 결정한다 — 채굴기가 이 노드의 자원/속도/산출량을 그대로 물려받는다.
    // Assets/Resources/GameData/OreDeposits 폴더에 애셋을 추가하면 코드 수정 없이 인식된다.
    [CreateAssetMenu(menuName = "Factory/Ore Deposit Definition", fileName = "NewOreDeposit")]
    public class OreDepositDef : ScriptableObject
    {
        [Tooltip("안정적인 문자열 키.")]
        public string depositId;

        [Tooltip("이 노드에서 캐지는 자원 — 별도 '태그' 문자열 없이 이 참조 자체가 태그 역할을 한다.")]
        public ResourceDef resource;

        public float mineIntervalSeconds = SimulationConstants.DefaultMineIntervalSeconds;
        public int yieldPerCycle = 1;
    }
}
