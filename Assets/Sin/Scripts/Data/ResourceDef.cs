using UnityEngine;

namespace Factory.Data
{
    // 자원/아이템 하나의 정의. Assets/Resources/GameData/Resources 폴더에 애셋을 추가하면
    // 코드 수정 없이 GameDatabase가 자동으로 인식한다.
    [CreateAssetMenu(menuName = "Factory/Resource Definition", fileName = "NewResource")]
    public class ResourceDef : ScriptableObject
    {
        [Tooltip("안정적인 문자열 키. 저장 데이터/레시피 참조에 쓰이므로 만든 뒤에는 바꾸지 않는다.")]
        public string resourceId;

        public string displayName;
        public Sprite icon;

        [Tooltip("에셋 없는 프로토타입이라 아이콘/모델 대신 벨트 위 아이템 색으로 자원을 구분한다.")]
        public Color color = Color.white;
    }
}
