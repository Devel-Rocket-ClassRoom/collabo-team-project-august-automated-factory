using UnityEngine;

namespace Factory.Building
{
    // 두 손가락 팬 + 핀치 줌을 처리하는 카메라 리그. 카메라의 기존 기울기(회전)는 그대로 두고
    // 피벗(XZ 평면 위 한 점) + 거리만 움직여서 프레이밍을 유지한다.
    public class TouchCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        // 원근 카메라용 픽셀당 팬 이동량. 오소그래픽에서는 아래 Pan()이 화면-월드 변환으로
        // 1:1(손가락이 짚은 지점이 손가락을 따라오는) 드래그를 직접 계산하므로 쓰이지 않는다.
        [SerializeField] private float panSpeed = 0.02f;
        // 오소그래픽 1:1 드래그에 곱하는 감도. 1 = 정확히 1:1. 예전엔 픽셀당 고정 계수(panSpeed)를
        // 써서 고해상도 기기일수록 같은 스와이프가 훨씬 크게 움직였다("드래그가 너무 빠름" 피드백).
        [SerializeField] private float panSensitivity = 1f;
        [SerializeField] private float zoomSpeed = 0.2f;
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

            // 픽셀 델타 -> 월드 델타. 오소그래픽이면 화면 높이 전체가 곧 2*orthographicSize라,
            // 픽셀당 월드 이동량을 정확히 계산할 수 있다(해상도·줌 무관 1:1 드래그). 원근이면
            // 옛 방식대로 고정 계수 * 거리 비례로 근사한다.
            float worldPerPixel;
            if (targetCamera.orthographic && Screen.height > 0)
            {
                worldPerPixel = (2f * targetCamera.orthographicSize / Screen.height) * panSensitivity;
            }
            else
            {
                worldPerPixel = panSpeed * (distance / 8f);
            }

            pivot += (-right * screenDelta.x - flatForward * screenDelta.y) * worldPerPixel;
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
