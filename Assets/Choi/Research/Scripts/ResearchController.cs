using System;
using System.Collections.Generic;
using Choi.SaveLoad;
using Factory.Simulation;
using Optimization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Choi.Research
{
    public sealed class ResearchController : MonoBehaviour, IPowerSaveParticipant
    {
        public static ResearchController Instance { get; private set; }

        [Header("씬에서 수정 가능한 패널")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text goalsText;
        [SerializeField] private TMP_Text unlocksText;
        [SerializeField] private Button supplyButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform tierTabRoot;
        [SerializeField] private Transform rewardRoot;
        [SerializeField] private Button tierTabPrefab;
        [SerializeField] private GameObject rewardCardPrefab;
        [Header("티어 데이터")]
        [SerializeField] private List<ResearchTierAsset> tiers = new List<ResearchTierAsset>();
        [SerializeField] private int completedTier;
        [SerializeField] private List<int> suppliedAmounts = new List<int>();

        private SimulationDriver driver;
        private FloorSystemManager floor;
        private int viewedTier;
        private readonly List<Button> tierTabs = new List<Button>();
        private readonly List<GameObject> rewardCards = new List<GameObject>();
        public int CompletedTier => completedTier;
        public event Action UnlocksChanged;

        public string SaveId => "research-progress";
        public string SaveType => "Choi.ResearchProgress.v1";
        public int SaveOrder => 100; // 공장과 코어 재고를 복원한 뒤 연구 해금을 갱신한다.
        public bool CanSave => true;

        [Serializable]
        private sealed class ResearchProgressData
        {
            public int completedTier;
            public List<int> suppliedAmounts = new List<int>();
            public int unlockedMapSize;
        }

        public string CaptureStateJson()
        {
            Resolve();
            FloorChunkManager chunks = FindFirstObjectByType<FloorChunkManager>();
            return JsonUtility.ToJson(new ResearchProgressData
            {
                completedTier = completedTier,
                suppliedAmounts = new List<int>(suppliedAmounts),
                unlockedMapSize = floor != null ? floor.unlockedSize : chunks != null ? chunks.unlockedSize : 0,
            });
        }

        public void RestoreStateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Research save JSON is empty.", nameof(json));

            ResearchProgressData data = JsonUtility.FromJson<ResearchProgressData>(json);
            if (data == null) throw new InvalidOperationException("Research save data is invalid.");

            completedTier = Mathf.Clamp(data.completedTier, 0, tiers.Count);
            suppliedAmounts = data.suppliedAmounts ?? new List<int>();
            EnsureProgressSize(completedTier < tiers.Count ? tiers[completedTier].resourceGoals.Count : 0);
            for (int i = 0; i < suppliedAmounts.Count; i++)
                suppliedAmounts[i] = Mathf.Clamp(suppliedAmounts[i], 0, tiers[completedTier].resourceGoals[i].amount);

            Resolve();
            // 이전 저장을 불러오면 지도도 그 시점의 해금 범위로 되돌린다.
            int mapSize = data.unlockedMapSize;
            for (int i = 0; i < completedTier; i++)
                mapSize = Mathf.Max(mapSize, tiers[i].unlockedMapSize);
            if (mapSize > 0)
            {
                floor?.SetUnlockedSize(mapSize);
                FloorChunkManager chunks = FindFirstObjectByType<FloorChunkManager>();
                if (chunks != null)
                {
                    chunks.unlockedSize = mapSize;
                    chunks.RefreshAllActiveChunks();
                }
            }

            viewedTier = Mathf.Clamp(completedTier, 0, Mathf.Max(0, tiers.Count - 1));
            Refresh();
            UnlocksChanged?.Invoke();
        }

        private void Awake()
        {
            Instance = this;
            panelRoot?.SetActive(false);
            supplyButton?.onClick.AddListener(SupplyFromCore);
            closeButton?.onClick.AddListener(Close);
            BuildTierTabs();
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
        public void Open() { panelRoot?.SetActive(true); Refresh(); }
        public void Close() { panelRoot?.SetActive(false); }

        public bool IsMachineUnlocked(string id) => IsUnlocked(id, false);
        public bool IsRecipeUnlocked(string id) => IsUnlocked(id, true);

        private bool IsUnlocked(string id, bool recipe)
        {
            if (string.IsNullOrEmpty(id)) return true;
            bool gated = false;
            for (int i = 0; i < tiers.Count; i++)
            {
                if (recipe)
                {
                    for (int n = 0; n < tiers[i].recipeRewards.Count; n++)
                        if (tiers[i].recipeRewards[n] != null && tiers[i].recipeRewards[n].recipeID == id) { gated = true; if (i < completedTier) return true; }
                }
                else
                {
                    for (int n = 0; n < tiers[i].machineRewards.Count; n++)
                        if (tiers[i].machineRewards[n]?.machine != null && tiers[i].machineRewards[n].machine.machineID == id) { gated = true; if (i < completedTier) return true; }
                }
            }
            return !gated;
        }

        public void SupplyFromCore()
        {
            Resolve();
            if (completedTier >= tiers.Count || driver == null || driver.World == null) return;
            var tier = tiers[completedTier];
            EnsureProgressSize(tier.resourceGoals.Count);
            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count) return;
            var core = driver.World.Processors[coreIndex];

            for (int i = 0; i < tier.resourceGoals.Count; i++)
            {
                var goal = tier.resourceGoals[i];
                if (!driver.World.Database.TryGetResourceId(goal.resourceId, out int resourceId)) continue;
                int remaining = Mathf.Max(0, goal.amount - suppliedAmounts[i]);
                int moved = Mathf.Min(remaining, core.InputBuffer[resourceId]);
                core.InputBuffer[resourceId] -= moved;
                suppliedAmounts[i] += moved;
            }

            bool complete = true;
            for (int i = 0; i < tier.resourceGoals.Count; i++)
                complete &= suppliedAmounts[i] >= tier.resourceGoals[i].amount;
            if (complete)
            {
                completedTier++;
                suppliedAmounts.Clear();
                ApplyTerrainUnlock(tier.unlockedMapSize);
                UnlocksChanged?.Invoke();
                // 완료한 탭에 남아 빈 버튼처럼 보이지 않고 바로 다음 연구를 보여준다.
                viewedTier = Mathf.Min(completedTier, tiers.Count - 1);
            }
            Refresh();
        }

        private void Refresh()
        {
            Resolve();
            if (tiers.Count == 0) return;
            viewedTier = Mathf.Clamp(viewedTier, 0, tiers.Count - 1);
            if (completedTier >= tiers.Count && viewedTier >= tiers.Count)
            {
                if (titleText != null) titleText.text = "연구 완료";
                if (descriptionText != null) descriptionText.text = "모든 티어가 개방되었습니다.";
                if (goalsText != null) goalsText.text = string.Empty;
                if (unlocksText != null) unlocksText.text = string.Empty;
                if (supplyButton != null) supplyButton.gameObject.SetActive(false);
                return;
            }
            var tier = tiers[viewedTier];
            if (viewedTier == completedTier) EnsureProgressSize(tier.resourceGoals.Count);
            if (titleText != null) titleText.text = $"TIER {tier.tier}  {tier.displayName}";
            if (descriptionText != null) descriptionText.text = tier.description;
            var lines = new List<string>();
            for (int i = 0; i < tier.resourceGoals.Count; i++)
                lines.Add($"{DisplayName(tier.resourceGoals[i].resourceId)}   {(viewedTier < completedTier ? tier.resourceGoals[i].amount : viewedTier == completedTier ? suppliedAmounts[i] : 0)} / {tier.resourceGoals[i].amount}");
            if (goalsText != null) goalsText.text = string.Join("\n", lines);
            if (unlocksText != null) unlocksText.text = viewedTier < completedTier ? "해금 완료" : viewedTier > completedTier ? "선행 티어를 먼저 해금하세요" : "요구 자원을 납품해 해금하세요";
            if (supplyButton != null) supplyButton.gameObject.SetActive(viewedTier == completedTier);
            RefreshRewards(tier);
        }

        private void BuildTierTabs()
        {
            if (tierTabRoot == null || tierTabPrefab == null) return;
            tierTabPrefab.gameObject.SetActive(false);
            for (int i = 0; i < tiers.Count; i++)
            {
                int index = i;
                Button tab = Instantiate(tierTabPrefab, tierTabRoot);
                tab.gameObject.SetActive(true);
                tab.GetComponentInChildren<TMP_Text>().text = $"TIER {tiers[i].tier}";
                tab.onClick.AddListener(() => { viewedTier = index; Refresh(); });
                tierTabs.Add(tab);
            }
        }

        private void RefreshRewards(ResearchTierAsset tier)
        {
            for (int i = 0; i < rewardCards.Count; i++) Destroy(rewardCards[i]);
            rewardCards.Clear();
            if (rewardRoot == null || rewardCardPrefab == null) return;
            for (int i = 0; i < tier.machineRewards.Count; i++)
            {
                ResearchMachineReward reward = tier.machineRewards[i];
                if (reward?.machine == null) continue;
                GameObject card = Instantiate(rewardCardPrefab, rewardRoot); card.SetActive(true);
                TMP_Text label = card.GetComponentInChildren<TMP_Text>(true); if (label != null) label.text = reward.machine.machineName;
                Image image = card.transform.Find("Icon")?.GetComponent<Image>(); if (image != null) { image.sprite = reward.icon; image.enabled = reward.icon != null; }
                rewardCards.Add(card);
            }
        }

        private string DisplayName(string id)
        {
            if (driver != null && driver.World != null && driver.World.Database.TryGetResourceId(id, out int index))
                return driver.World.Database.Resources[index].DisplayName;
            return id;
        }

        private void EnsureProgressSize(int count)
        {
            while (suppliedAmounts.Count < count) suppliedAmounts.Add(0);
            while (suppliedAmounts.Count > count) suppliedAmounts.RemoveAt(suppliedAmounts.Count - 1);
        }

        private void Resolve()
        {
            if (driver == null) driver = FindFirstObjectByType<SimulationDriver>();
            if (floor == null) floor = FindFirstObjectByType<FloorSystemManager>();
        }

        private void ApplyTerrainUnlock(int requestedSize)
        {
            Resolve();
            if (floor != null)
            {
                // 잘못 설정된 티어 값 때문에 기존 땅이 줄어들지 않게 확장만 허용한다.
                floor.SetUnlockedSize(Mathf.Max(floor.unlockedSize, requestedSize));
            }

            // FloorSystemManager 초기화 순서와 관계없이 현재 로드된 타일을 즉시 다시 칠한다.
            FloorChunkManager chunks = FindFirstObjectByType<FloorChunkManager>();
            if (chunks != null)
            {
                chunks.unlockedSize = Mathf.Max(chunks.unlockedSize, requestedSize);
                chunks.RefreshAllActiveChunks();
            }
        }
    }
}
