using System.Collections.Generic;
using Factory.Simulation;
using UnityEngine;

namespace Factory.UI
{
    // 코어(창고)에 지금 쌓여있는 자원을 작게 나열해서 보여준다 — 몇 대 지었는지 같은 건
    // 화면에서 직접 세면 되니 굳이 HUD로 안 알려줘도 된다는 피드백에 따라 단순화.
    public class SimulationHudBridge : MonoBehaviour
    {
        [SerializeField] private SimulationDriver driver;
        [SerializeField] private ResourceHUD hud;

        private readonly List<string> parts = new List<string>();

        private void Update()
        {
            if (driver == null || driver.World == null || hud == null) return;

            hud.SetLine1("코어 보유 자원");

            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count)
            {
                hud.SetLine2("(코어 없음)");
                return;
            }

            var core = driver.World.Processors[coreIndex];
            var resources = driver.World.Database.Resources;

            parts.Clear();
            for (int i = 0; i < resources.Count; i++)
            {
                if (core.InputBuffer[i] > 0) parts.Add($"{resources[i].DisplayName} {core.InputBuffer[i]}");
            }

            hud.SetLine2(parts.Count > 0 ? string.Join(" / ", parts) : "(비어있음)");
        }
    }
}
