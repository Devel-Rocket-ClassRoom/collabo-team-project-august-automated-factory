using Factory.Simulation;
using UnityEngine;

namespace Factory.Buildings
{
    // 기계가 실제로 작동 중일 때(레시피 처리 중인 Processor, 항상 캐고 있는 Miner) 연기를
    // 뿜어서 "돌아가고 있다"는 걸 눈으로 바로 알 수 있게 한다.
    // 분류기/합류기/코어(RecipeId<0)는 IsProcessing이 절대 true가 안 돼서 자동으로 안 나온다.
    //
    // (예전엔 모델을 살짝 떨게도 했는데, 그 렌더러 위치를 매 프레임 흔드는 바람에 그 위치로
    // 바운드를 재는 MachineWorldIndicator의 이름표까지 덩달아 떨려서 제거했다 — 연기만 남김.)
    public class MachineActivityIndicator : MonoBehaviour
    {
        // 플레이 중 Hierarchy에서 이 기계를 선택하면 Inspector에서 바로 조절/미리보기 가능.
        // 마음에 드는 값 찾으면 여기 기본값을 그 값으로 바꿔달라고 하면 전체 기본값이 바뀐다.
        [SerializeField] private float smokeHeight = 1.5f;      // 연기 나오는 높이(기계 기준 로컬 y)
        [SerializeField] private float puffInterval = 1f;       // 몇 초마다 한 번 "뽕" 하고 나올지
        [SerializeField] private int puffParticleCount = 2;     // 한 번 "뽕" 할 때 몇 개씩(뭉게뭉게 보이게)
        [SerializeField] private Vector3 driftDirection = new Vector3(-0.7f, 0.5f, 0.7f); // 진행 방향(정규화 안 해도 됨) — 기본: 왼쪽 위 대각선
        [SerializeField] private float driftSpeed = 0.6f;       // 그 방향으로 퍼지는 속도

        private Transform target;
        private MachineInstanceKind kind;
        private int instanceIndex;
        private SimulationDriver driver;
        private ParticleSystem smoke;
        private float nextPuffTime;

        public void Initialize(Transform target, MachineInstanceKind kind, int instanceIndex, SimulationDriver driver)
        {
            this.target = target;
            this.kind = kind;
            this.instanceIndex = instanceIndex;
            this.driver = driver;
        }

        private void Update()
        {
            if (target == null || driver == null || driver.World == null) return;

            bool active = IsActive();

            EnsureSmoke();
            smoke.transform.localPosition = new Vector3(0f, smokeHeight, 0f);
            // 위치는 기계를 따라가되(로컬), 회전은 기계 방향(Facing)과 무관하게 항상 월드
            // 기준 고정 — 안 그러면 방향(driftDirection)이 기계가 보는 쪽에 따라 같이 돌아서,
            // 기계마다 연기 나가는 방향이 제각각이 된다.
            smoke.transform.rotation = Quaternion.identity;

            // EnsureSmoke()는 처음 한 번만 실행되니, driftDirection/driftSpeed를 플레이 중
            // Inspector에서 바꿔도 바로 반영되게 여기서 매 프레임 다시 적용한다.
            var velocityOverLifetime = smoke.velocityOverLifetime;
            Vector3 dir = driftDirection.sqrMagnitude > 0.0001f ? driftDirection.normalized : Vector3.up;
            velocityOverLifetime.x = dir.x * driftSpeed;
            velocityOverLifetime.y = dir.y * driftSpeed;
            velocityOverLifetime.z = dir.z * driftSpeed;

            if (active)
            {
                if (!smoke.isPlaying) smoke.Play();
                // 연속 분사 대신 일정 간격으로 한 뭉치씩 터뜨려서 "뽕뽕뽕" 하고 피어오르는
                // 느낌을 낸다(rateOverTime 방식은 계속 줄줄 나와서 뭉게뭉게 느낌이 안 남).
                if (Time.time >= nextPuffTime)
                {
                    smoke.Emit(puffParticleCount);
                    nextPuffTime = Time.time + puffInterval;
                }
            }
            else if (smoke.isPlaying)
            {
                smoke.Stop(); // 이미 나온 연기는 자연히 사라지고, 새 뭉치는 안 나옴(끊기지 않음)
            }
        }

        // 텍스처 에셋 없이 순수 코드로 만든 연기 이펙트 — 부드러운 원형 알파 텍스처를 런타임에
        // 한 번 그려서(GetSmokeTexture) 모든 기계가 공유하는 머티리얼에 물린다.
        private void EnsureSmoke()
        {
            if (smoke != null) return;

            var smokeGO = new GameObject("Smoke");
            smokeGO.transform.SetParent(target, false);
            smokeGO.transform.localPosition = new Vector3(0f, smokeHeight, 0f);

            smoke = smokeGO.AddComponent<ParticleSystem>();
            var main = smoke.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.8f;
            main.startSpeed = 0.05f; // 방향/속도는 velocityOverLifetime이 맡는다 — 초기 속도는 거의 0으로.
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startColor = new Color(0.55f, 0.55f, 0.55f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            // 연속 분사(rateOverTime) 대신 Update()에서 Emit()으로 직접 뭉치를 터뜨린다.
            var emission = smoke.emission;
            emission.rateOverTime = 0f;

            // 방향은 shape가 아니라 velocityOverLifetime이 결정하므로, shape는 그냥 좁은
            // 점 하나에서 나오게만 둔다(퍼짐은 아래 velocityOverLifetime의 좌우 편차로 낸다).
            var shape = smoke.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            // 항상 이 방향(기본: 왼쪽 위 대각선)으로 퍼지게 한다 — 회전(Update에서 identity로
            // 고정)과 무관하게 월드 기준 고정 방향이라 기계가 어느 쪽을 보든 항상 같은 쪽으로 나감.
            // 실제 x/y/z 값은 Update()에서 매 프레임 다시 넣는다(라이브 튜닝 반영용).
            var velocityOverLifetime = smoke.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.World;

            // 위로 올라갈수록 옅어지다 사라지게(연기가 퍼지면서 흩어지는 느낌).
            var colorOverLifetime = smoke.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            // 위로 올라갈수록 커지게(실제 연기가 퍼지는 모양).
            var sizeOverLifetime = smoke.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.2f));

            var renderer = smokeGO.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetSmokeMaterial();

            smoke.Stop();
        }

        private static Material smokeMaterialCache;

        private static Material GetSmokeMaterial()
        {
            if (smokeMaterialCache != null) return smokeMaterialCache;

            // Sprites/Default는 URP에서도 별도 서페이스 설정 없이 바로 알파블렌드+버텍스컬러
            // 곱연산이 되는 가벼운 셰이더라, 파티클용 텍스처 하나 물리는 용도로 딱 맞는다.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(shader);
            var tex = GetSmokeTexture();
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            else if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);

            smokeMaterialCache = mat;
            return mat;
        }

        private static Texture2D smokeTextureCache;

        // 32x32 흰색 원 + 가장자리로 갈수록 부드럽게 흐려지는 알파. 에셋 없이 매끈한 연기
        // 입자 하나를 코드로 그려서 쓴다.
        private static Texture2D GetSmokeTexture()
        {
            if (smokeTextureCache != null) return smokeTextureCache;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Vector2 center = new Vector2(size - 1, size - 1) * 0.5f;
            float maxDist = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    // 중심 60%는 꽉 찬 알파로 두고, 바깥 40%에서만 가장자리로 흐려지게 —
                    // 안쪽까지 옅게 그라데이션 지면 입자 전체가 흐릿해 보인다.
                    float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(maxDist * 0.6f, maxDist, dist));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();

            smokeTextureCache = tex;
            return tex;
        }

        private bool IsActive()
        {
            switch (kind)
            {
                case MachineInstanceKind.Miner:
                    // 채굴기는 블로킹 개념이 없다(MinerSystem) — 존재하면 항상 캐는 중.
                    return instanceIndex >= 0 && instanceIndex < driver.World.Miners.Count
                        && driver.World.Miners[instanceIndex] != null;
                case MachineInstanceKind.Processor:
                    if (instanceIndex < 0 || instanceIndex >= driver.World.Processors.Count) return false;
                    var processor = driver.World.Processors[instanceIndex];
                    return processor != null && processor.IsProcessing;
                default:
                    return false;
            }
        }
    }
}
