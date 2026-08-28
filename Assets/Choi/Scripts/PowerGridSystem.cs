using System;
using System.Collections.Generic;
using Bae.Data;
using Factory.Building;
using Factory.Simulation;
using UnityEngine;

namespace Choi.SaveLoad
{
    public enum PowerNodeKind
    {
        Generator = 0,
        Cable = 1,
        TransmissionTower = 2,
    }

    public sealed class PowerNodeRuntime
    {
        public int Id;
        public PowerNodeKind Kind;
        public Vector2Int Cell;
    }

    /// <summary>
    /// 발전기-전선에 연결된 송신탑의 15x15 공급 범위 안에 있는 기계만 작동시킵니다.
    /// 기존 시뮬레이션 코드는 수정하지 않고 각 인스턴스의 SpeedMultiplier만 제어합니다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PowerGridSystem : MonoBehaviour
    {
        public const int GeneratorOutput = 120;

        private static readonly Vector2Int[] Directions =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        private readonly List<PowerNodeRuntime> nodes = new List<PowerNodeRuntime>();
        private readonly Dictionary<Vector2Int, PowerNodeRuntime> nodeByCell = new Dictionary<Vector2Int, PowerNodeRuntime>();
        private readonly Dictionary<MinerInstance, float> minerBaseSpeed = new Dictionary<MinerInstance, float>();
        private readonly Dictionary<ProcessorInstance, float> processorBaseSpeed = new Dictionary<ProcessorInstance, float>();
        private readonly Dictionary<ProcessorInstance, int> processorDesiredRecipe = new Dictionary<ProcessorInstance, int>();
        private readonly Dictionary<string, GameObject> indicators = new Dictionary<string, GameObject>();

        private SimulationDriver driver;
        private float evaluationTimer;
        private int nextNodeId;

        public IReadOnlyList<PowerNodeRuntime> Nodes => nodes;
        public int AvailablePower { get; private set; }
        public int RequestedPower { get; private set; }
        public int UsedPower { get; private set; }
        public int PoweredMachineCount { get; private set; }
        public int TotalMachineCount { get; private set; }
        public int ActiveTowerCount { get; private set; }

        private void Awake()
        {
            driver = FindAnyObjectByType<SimulationDriver>();
        }

        private void Update()
        {
            evaluationTimer -= Time.unscaledDeltaTime;
            if (evaluationTimer > 0f) return;
            evaluationTimer = 0.2f;
            EvaluatePower();
        }

        public bool TryAddNode(PowerNodeKind kind, Vector2Int cell)
        {
            if (nodeByCell.ContainsKey(cell)) return false;

            var node = new PowerNodeRuntime { Id = nextNodeId++, Kind = kind, Cell = cell };
            nodes.Add(node);
            nodeByCell[cell] = node;
            evaluationTimer = 0f;
            return true;
        }

        public bool RemoveNode(Vector2Int cell)
        {
            if (!nodeByCell.TryGetValue(cell, out PowerNodeRuntime node)) return false;
            nodeByCell.Remove(cell);
            nodes.Remove(node);
            evaluationTimer = 0f;
            return true;
        }

        public void ReplaceNodes(List<PowerNodeData> savedNodes)
        {
            nodes.Clear();
            nodeByCell.Clear();
            nextNodeId = 0;

            if (savedNodes != null)
            {
                for (int i = 0; i < savedNodes.Count; i++)
                {
                    PowerNodeData saved = savedNodes[i];
                    var cell = new Vector2Int(saved.cell.x, saved.cell.y);
                    if (nodeByCell.ContainsKey(cell)) continue;

                    var node = new PowerNodeRuntime
                    {
                        Id = saved.id,
                        Kind = (PowerNodeKind)saved.kind,
                        Cell = cell,
                    };
                    nodes.Add(node);
                    nodeByCell[cell] = node;
                    nextNodeId = Mathf.Max(nextNodeId, node.Id + 1);
                }
            }

            evaluationTimer = 0f;
        }

        public List<PowerNodeData> CaptureNodes()
        {
            var result = new List<PowerNodeData>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                result.Add(new PowerNodeData
                {
                    id = nodes[i].Id,
                    kind = (int)nodes[i].Kind,
                    cell = new Int2Data(nodes[i].Cell.x, nodes[i].Cell.y),
                });
            }
            return result;
        }

        public float GetBaseSpeed(MinerInstance miner)
        {
            return miner != null && minerBaseSpeed.TryGetValue(miner, out float speed) ? speed : miner?.SpeedMultiplier ?? 1f;
        }

        public float GetBaseSpeed(ProcessorInstance processor)
        {
            return processor != null && processorBaseSpeed.TryGetValue(processor, out float speed) ? speed : processor?.SpeedMultiplier ?? 1f;
        }

        public int GetDesiredRecipeId(ProcessorInstance processor)
        {
            return processor != null && processorDesiredRecipe.TryGetValue(processor, out int recipeId)
                ? recipeId
                : processor?.RecipeId ?? -1;
        }

        public bool IsMachinePowered(CellOccupantType type, int index)
        {
            string key = IndicatorKey(type, index);
            return indicators.TryGetValue(key, out GameObject indicator) && indicator != null && indicator.name.EndsWith("_ON", StringComparison.Ordinal);
        }

        public void ResetRuntimeTracking()
        {
            minerBaseSpeed.Clear();
            processorBaseSpeed.Clear();
            processorDesiredRecipe.Clear();
            foreach (GameObject indicator in indicators.Values)
            {
                if (indicator != null) Destroy(indicator);
            }
            indicators.Clear();
            evaluationTimer = 0f;
        }

        public void EvaluatePower()
        {
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (driver == null || driver.World == null) return;

            BuildComponents(out Dictionary<Vector2Int, int> componentByCell, out List<int> remainingByComponent);
            AvailablePower = 0;
            for (int i = 0; i < remainingByComponent.Count; i++) AvailablePower += remainingByComponent[i];

            RequestedPower = 0;
            UsedPower = 0;
            PoweredMachineCount = 0;
            TotalMachineCount = 0;
            ActiveTowerCount = CountActiveTowers(componentByCell, remainingByComponent);
            var liveIndicatorKeys = new HashSet<string>();
            Dictionary<(CellOccupantType type, int index), Vector2Int> cells = ScanOccupants(driver.World);

            for (int i = 0; i < driver.World.Miners.Count; i++)
            {
                MinerInstance miner = driver.World.Miners[i];
                if (miner == null) continue;
                if (!minerBaseSpeed.ContainsKey(miner)) minerBaseSpeed[miner] = Mathf.Max(0.0001f, miner.SpeedMultiplier);

                string machineKey = driver.World.Database.Machines[miner.MachineId].Key;
                int demand = GetPowerConsumption(machineKey);
                Vector2Int anchor;
                GameObject minerVisual = GameObject.Find($"Miner_{i}");
                if (minerVisual != null) anchor = GridUtility.WorldToCell(minerVisual.transform.position);
                else cells.TryGetValue((CellOccupantType.Miner, i), out anchor);
                int component = FindSupplyingTowerComponent(anchor, Vector2Int.one, demand,
                    componentByCell, remainingByComponent);
                bool powered = TryConsumePower(component, demand, remainingByComponent);
                miner.SpeedMultiplier = powered ? minerBaseSpeed[miner] : 0f;
                AccumulateMachineStatus(CellOccupantType.Miner, i, demand, powered, anchor, Vector2Int.one, liveIndicatorKeys);
            }

            for (int i = 0; i < driver.World.Processors.Count; i++)
            {
                ProcessorInstance processor = driver.World.Processors[i];
                if (processor == null || processor.UniversalPorts) continue;
                if (!processorBaseSpeed.ContainsKey(processor)) processorBaseSpeed[processor] = Mathf.Max(0.0001f, processor.SpeedMultiplier);
                if (!processorDesiredRecipe.TryGetValue(processor, out int desiredRecipe))
                {
                    desiredRecipe = processor.RecipeId;
                    processorDesiredRecipe[processor] = desiredRecipe;
                }
                else if (processor.RecipeId >= 0 && processor.RecipeId != desiredRecipe)
                {
                    // 전력 차단 중 UI에서 새 레시피를 골라도 선택값은 기억하고, 실제 실행만 막는다.
                    desiredRecipe = processor.RecipeId;
                    processorDesiredRecipe[processor] = desiredRecipe;
                }

                string machineKey = driver.World.Database.Machines[processor.MachineId].Key;
                int demand = GetPowerConsumption(machineKey);
                int component = FindSupplyingTowerComponent(processor.Anchor, processor.Footprint, demand,
                    componentByCell, remainingByComponent);
                bool powered = TryConsumePower(component, demand, remainingByComponent);
                processor.SpeedMultiplier = powered ? processorBaseSpeed[processor] : 0f;
                processor.RecipeId = powered ? desiredRecipe : -1;
                AccumulateMachineStatus(CellOccupantType.Processor, i, demand, powered, processor.Anchor, processor.Footprint, liveIndicatorKeys);
            }

            RemoveDeadIndicators(liveIndicatorKeys);
        }

        private void BuildComponents(out Dictionary<Vector2Int, int> componentByCell, out List<int> capacityByComponent)
        {
            componentByCell = new Dictionary<Vector2Int, int>();
            capacityByComponent = new List<int>();

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector2Int start = nodes[i].Cell;
                if (componentByCell.ContainsKey(start)) continue;

                int component = capacityByComponent.Count;
                int capacity = 0;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(start);
                componentByCell[start] = component;

                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    PowerNodeRuntime node = nodeByCell[cell];
                    if (node.Kind == PowerNodeKind.Generator) capacity += GeneratorOutput;

                    for (int d = 0; d < Directions.Length; d++)
                    {
                        Vector2Int neighbor = cell + Directions[d];
                        if (!nodeByCell.TryGetValue(neighbor, out PowerNodeRuntime neighborNode)
                            || componentByCell.ContainsKey(neighbor)
                            || (node.Kind != PowerNodeKind.Cable && neighborNode.Kind != PowerNodeKind.Cable)) continue;
                        componentByCell[neighbor] = component;
                        queue.Enqueue(neighbor);
                    }
                }

                capacityByComponent.Add(capacity);
            }
        }

        private int FindSupplyingTowerComponent(Vector2Int anchor, Vector2Int footprint, int demand,
            Dictionary<Vector2Int, int> componentByCell, List<int> capacityByComponent)
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                PowerNodeRuntime tower = nodes[n];
                if (tower.Kind != PowerNodeKind.TransmissionTower
                    || !componentByCell.TryGetValue(tower.Cell, out int component)
                    || component < 0 || component >= capacityByComponent.Count
                    || capacityByComponent[component] < demand
                    || !HasAdjacentCable(tower.Cell, component, componentByCell))
                {
                    continue;
                }

                for (int x = 0; x < footprint.x; x++)
                {
                    for (int y = 0; y < footprint.y; y++)
                    {
                        Vector2Int occupied = new Vector2Int(anchor.x + x, anchor.y + y);
                        Vector2Int distance = occupied - tower.Cell;
                        if (Mathf.Abs(distance.x) <= 7 && Mathf.Abs(distance.y) <= 7) return component;
                    }
                }
            }
            return -1;
        }

        private int CountActiveTowers(Dictionary<Vector2Int, int> componentByCell, List<int> capacityByComponent)
        {
            int count = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                PowerNodeRuntime tower = nodes[i];
                if (tower.Kind != PowerNodeKind.TransmissionTower
                    || !componentByCell.TryGetValue(tower.Cell, out int component)
                    || component < 0 || component >= capacityByComponent.Count
                    || capacityByComponent[component] <= 0
                    || !HasAdjacentCable(tower.Cell, component, componentByCell)) continue;
                count++;
            }
            return count;
        }

        private bool HasAdjacentCable(Vector2Int towerCell, int component,
            Dictionary<Vector2Int, int> componentByCell)
        {
            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int neighbor = towerCell + Directions[i];
                if (nodeByCell.TryGetValue(neighbor, out PowerNodeRuntime node)
                    && node.Kind == PowerNodeKind.Cable
                    && componentByCell.TryGetValue(neighbor, out int neighborComponent)
                    && neighborComponent == component) return true;
            }
            return false;
        }

        private static bool TryConsumePower(int component, int demand, List<int> remaining)
        {
            if (component < 0 || component >= remaining.Count || remaining[component] < demand) return false;
            remaining[component] -= demand;
            return true;
        }

        private void AccumulateMachineStatus(CellOccupantType type, int index, int demand, bool powered,
            Vector2Int anchor, Vector2Int footprint, HashSet<string> liveKeys)
        {
            TotalMachineCount++;
            RequestedPower += demand;
            if (powered)
            {
                PoweredMachineCount++;
                UsedPower += demand;
            }

            string key = IndicatorKey(type, index);
            liveKeys.Add(key);
            if (!indicators.TryGetValue(key, out GameObject indicator) || indicator == null)
            {
                indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(indicator.GetComponent<Collider>());
                indicator.transform.localScale = Vector3.one * 0.18f;
                indicators[key] = indicator;
            }

            indicator.name = key + (powered ? "_ON" : "_OFF");
            indicator.transform.position = GridUtility.GetFootprintCenter(anchor, footprint, 1.2f);
            BuildVisuals.Colorize(indicator, powered ? new Color(0.1f, 1f, 0.25f) : new Color(1f, 0.12f, 0.08f));
        }

        private void RemoveDeadIndicators(HashSet<string> liveKeys)
        {
            List<string> dead = null;
            foreach (var pair in indicators)
            {
                if (liveKeys.Contains(pair.Key)) continue;
                if (pair.Value != null) Destroy(pair.Value);
                (dead ??= new List<string>()).Add(pair.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) indicators.Remove(dead[i]);
        }

        private static string IndicatorKey(CellOccupantType type, int index) => $"PowerStatus_{type}_{index}";

        private static int GetPowerConsumption(string machineKey)
        {
            if (DataManager.Instance != null && DataManager.Instance.machineDict.TryGetValue(machineKey, out var data)
                && data.powerConsumption > 0)
            {
                return data.powerConsumption;
            }

            switch (machineKey)
            {
                case "Miner": return 20;
                case "Smelter": return 30;
                case "Former": return 25;
                case "Synthesizer": return 50;
                default: return 25;
            }
        }

        private static Dictionary<(CellOccupantType type, int index), Vector2Int> ScanOccupants(SimulationWorld world)
        {
            var result = new Dictionary<(CellOccupantType, int), Vector2Int>();
            const int range = 64;
            for (int x = -range; x <= range; x++)
            {
                for (int y = -range; y <= range; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!world.Grid.TryGetOccupant(cell, out CellOccupant occupant)) continue;
                    var key = (occupant.Type, occupant.InstanceIndex);
                    if (!result.ContainsKey(key)) result[key] = cell;
                }
            }
            return result;
        }
    }
}
