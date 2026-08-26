using UnityEngine;

namespace Factory.Building
{
    // 1터치 건설 제스처(프레스/드래그/릴리즈/취소)를 받는 공통 인터페이스.
    // BuildInputRouter가 터치 개수를 보고 활성 도구에 이 이벤트들을 넘긴다.
    public interface IBuildTool
    {
        void OnPressBegin(Vector2 screenPosition);
        void OnDrag(Vector2 screenPosition);
        void OnReleased(Vector2 screenPosition);
        void OnCancelled();
    }
}
