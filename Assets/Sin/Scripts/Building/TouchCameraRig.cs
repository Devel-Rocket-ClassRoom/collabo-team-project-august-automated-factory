using UnityEngine;

namespace Factory.Building
{
    // 두 손가락 팬 + 핀치 줌을 처리하는 카메라 리그. 카메라의 기존 기울기(회전)는 그대로 두고
    // 피벗(XZ 평면 위 한 점) + 거리만 움직여서 프레이밍을 유지한다.
    public class TouchCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float panSpeed = 0.02f;
        [SerializeField] private float zoomSpeed = 0.02f;
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float minOrthographicSize = 3f;
        [SerializeField] private float maxOrthographicSize = 20f;

        private float distance = 8f;
        private Vector3 pivot;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            Vector3 forward = targetCamera.transform.rotation * Vector3.forward;
            Vector3 pos = targetCamera.transform.position;
            distance = Mathf.Abs(forward.y) > 0.0001f ? Mathf.Clamp(-pos.y / forward.y, minDistance, maxDistance) : 8f;
            pivot = pos + forward * distance;
        }

        public void Pan(Vector2 screenDelta)
        {
            if (targetCamera == null) return;

            Vector3 right = targetCamera.transform.right;
            Vector3 flatForward = Vector3.Cross(right, Vector3.up);

            pivot += (-right * screenDelta.x - flatForward * screenDelta.y) * panSpeed * (distance / 8f);
            ApplyTransform();
        }

        public void Zoom(float pinchDelta)
        {
            if (targetCamera == null) return;

            if (targetCamera.orthographic)
            {
                // 오소그래픽 카메라는 위치를 옮겨도 화면상 크기가 안 바뀐다 — orthographicSize가 곧 줌.
                targetCamera.orthographicSize = Mathf.Clamp(
                    targetCamera.orthographicSize - pinchDelta * zoomSpeed * 0.1f,
                    minOrthographicSize, maxOrthographicSize);
                return;
            }

            distance = Mathf.Clamp(distance - pinchDelta * zoomSpeed, minDistance, maxDistance);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (targetCamera == null) return;
            Vector3 forward = targetCamera.transform.rotation * Vector3.forward;
            targetCamera.transform.position = pivot - forward * distance;
        }
    }
}
