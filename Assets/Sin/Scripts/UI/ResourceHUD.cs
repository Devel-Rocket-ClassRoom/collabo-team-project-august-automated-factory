using UnityEngine;
using UnityEngine.UI;

namespace Factory.UI
{
    // 최소 2줄 HUD. ResourceManager 같은 특정 데이터 소스에 묶이지 않고,
    // 외부(SimulationHudBridge 등)에서 텍스트를 밀어넣기만 하는 뷰로 단순화.
    public class ResourceHUD : MonoBehaviour
    {
        [SerializeField] private Text line1;
        [SerializeField] private Text line2;

        public void SetLine1(string text)
        {
            if (line1 != null) line1.text = text;
        }

        public void SetLine2(string text)
        {
            if (line2 != null) line2.text = text;
        }
    }
}
