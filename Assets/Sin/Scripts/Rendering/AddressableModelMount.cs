using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Factory.Rendering
{
    // 기계/광맥 루트에 붙어서 Addressables 키로 모델 프리팹을 비동기 로드해 자식으로 얹는다.
    // 모델의 방향/크기/높이는 프리팹(배리언트) 자체에 저장돼 있으므로 여기서는 손대지 않고
    // 그대로 인스턴스화만 한다 — 조정은 Assets/Sin/Prefabs/Machines|Deposits 의 배리언트를
    // 에디터에서 눈으로 편집한다.
    //
    // 로드 전/실패 시엔 스폰 코드가 미리 만들어둔 폴백 박스가 그대로 보인다. 오브젝트가
    // 파괴되면 반드시 핸들을 해제한다 — Addressables 인스턴스는 레퍼런스 카운팅이라 누수된다.
    [DisallowMultipleComponent]
    public sealed class AddressableModelMount : MonoBehaviour
    {
        private AsyncOperationHandle<GameObject> handle;
        private bool hasHandle;
        private GameObject placeholder;
        private bool destroyed;

        public void Mount(string addressableKey, GameObject fallbackPlaceholder)
        {
            placeholder = fallbackPlaceholder;
            if (string.IsNullOrEmpty(addressableKey)) return; // 키 없음 -> 폴백 박스 유지

            handle = Addressables.InstantiateAsync(addressableKey, transform, instantiateInWorldSpace: false);
            hasHandle = true;
            handle.Completed += OnLoaded;
        }

        private void OnLoaded(AsyncOperationHandle<GameObject> op)
        {
            if (destroyed) return; // 로드 도중 파괴됨 — OnDestroy가 이미 해제 처리
            if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null) return; // 폴백 박스 유지

            // 배리언트는 "월드 원점에 놨을 때 밑면이 지면(y=0)"으로 저장돼 있다(MachineVariantGenerator).
            // 스폰 루트는 칸 중심 높이(y=0.5 등)에 있으므로, 그 높이만큼 내려 모델 밑면을 지면에 맞춘다.
            Transform model = op.Result.transform;
            model.position -= new Vector3(0f, transform.position.y, 0f);

            if (placeholder != null) placeholder.SetActive(false);
        }

        private void OnDestroy()
        {
            destroyed = true;
            if (hasHandle && handle.IsValid())
            {
                Addressables.ReleaseInstance(handle);
                hasHandle = false;
            }
        }
    }
}
