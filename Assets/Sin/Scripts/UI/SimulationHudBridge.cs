using Factory.Simulation;
using UnityEngine;

namespace Factory.UI
{
    // 지금까지 배치된 채굴기/제련로들의 요약 상태를 ResourceHUD 두 줄에 표시.
    public class SimulationHudBridge : MonoBehaviour
    {
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private ResourceHUD hud;

        private void Update()
        {
            if (driver == null || driver.World == null || hud == null) return;

            var miners = driver.World.Miners;
            int bufferedTotal = 0;
            for (int i = 0; i < miners.Count; i++) bufferedTotal += miners[i].BufferedOutput;
            hud.SetLine1($"채굴기 {miners.Count}대 / 대기 산출물 합계 {bufferedTotal}");

            var processors = driver.World.Processors;
            int activeCount = 0;
            for (int i = 0; i < processors.Count; i++) if (processors[i].IsProcessing) activeCount++;
            hud.SetLine2($"제련로 {processors.Count}대 / 가동중 {activeCount}대");
        }
    }
}
