using UnityEngine;
using UnityEngine.InputSystem;

// 모바일 터치 / 에디터 마우스 클릭을 동일하게 처리하는 탭 입력 매니저.
// New Input System의 Pointer는 Mouse와 Touchscreen을 모두 아우르는 공통 베이스라
// 플랫폼별 분기 없이 하나의 경로로 처리할 수 있다.
public class TapInputManager : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask interactableLayer = ~0;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        var pointer = Pointer.current;
        if (pointer == null || !pointer.press.wasPressedThisFrame) return;

        HandleTap(pointer.position.ReadValue());
    }

    public void HandleTap(Vector2 screenPosition)
    {
        if (targetCamera == null) return;

        Ray ray = targetCamera.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, interactableLayer))
        {
            hit.collider.GetComponentInParent<IInteractable>()?.OnTap();
        }
    }
}
