using Factory.Data;
using UnityEngine;

namespace Factory.Simulation
{
    // 고정 틱레이트로 SimulationWorld를 구동하는 MonoBehaviour 어댑터.
    // 렌더링(프레임률)과 시뮬레이션 정확성(틱레이트)을 분리해서, 프레임이 들쭉날쭉해도
    // 시뮬레이션 결과가 항상 동일하게 재현되도록 한다.
    public class SimulationDriver : MonoBehaviour
    {
        [SerializeField] private float tickRate = SimulationConstants.DefaultTickRate;

        public SimulationWorld World { get; private set; }

        private float accumulator;

        private void Awake()
        {
            var database = GameDatabase.Instance ?? GameDatabase.LoadFromResources();
            database.MakeGlobal();
            World = new SimulationWorld(database);
        }

        private void Update()
        {
            if (World == null) return;

            float fixedDelta = 1f / tickRate;
            accumulator += Time.deltaTime;

            while (accumulator >= fixedDelta)
            {
                World.Tick(fixedDelta);
                accumulator -= fixedDelta;
            }
        }
    }
}
