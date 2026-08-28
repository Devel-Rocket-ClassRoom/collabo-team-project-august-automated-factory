using System;
using System.Collections.Generic;
using Factory.Building;
using Factory.Buildings;
using Factory.Rendering;
using Factory.Simulation;
using UnityEngine;

namespace Choi.SaveLoad
{
    /// <summary>SimulationWorld 전체와 전력 배치를 PowerSaveManager의 JSON 항목 하나로 저장합니다.</summary>
    public sealed class FactorySaveBridge : MonoBehaviour, IPowerSaveParticipant
    {
        private const int ScanRange = 64;

        private SimulationDriver driver;
        private PowerGridSystem powerGrid;
        private PowerBuildController powerBuild;

        public string SaveId => "factory-progress";
        public string SaveType => "Choi.FactoryProgress.v1";
        public int SaveOrder => 0;
        public bool CanSave => driver != null && driver.World != null && powerGrid != null;

        private void Awake()
        {
            ResolveReferences();
        }

        public string CaptureStateJson()
        {
            ResolveReferences();
            if (!CanSave) throw new InvalidOperationException("Factory world or power grid is not ready.");

            SimulationWorld world = driver.World;
            var data = new FactoryProgressData
            {
                coreProcessorIndex = world.CoreProcessorIndex,
                powerNodes = powerGrid.CaptureNodes(),
            };

            Dictionary<(CellOccupantType type, int index), Vector2Int> cells = ScanOccupants(world);
            CaptureMiners(world, data, cells);
            CaptureProcessors(world, data);
            CaptureBelts(world, data, cells);
            return JsonUtility.ToJson(data);
        }

        public void RestoreStateJson(string json)
        {
            ResolveReferences();
            if (driver == null || driver.World == null) throw new InvalidOperationException("Factory world is not ready.");

            FactoryProgressData data = JsonUtility.FromJson<FactoryProgressData>(json);
            if (data == null) throw new InvalidOperationException("Factory save data is invalid.");

            SimulationWorld world = driver.World;
            GameObject coreVisual = GameObject.Find("Core");
            ClearCurrentFactory(world, coreVisual);
            powerGrid.ResetRuntimeTracking();

            RestoreMiners(world, data.miners);
            RestoreProcessors(world, data.processors, data.coreProcessorIndex, coreVisual);
            world.CoreProcessorIndex = data.coreProcessorIndex;
            RestoreBelts(world, data.belts);
            powerGrid.ReplaceNodes(data.powerNodes);
            powerBuild?.RebuildVisuals();
            powerGrid.EvaluatePower();
        }

        private void CaptureMiners(SimulationWorld world, FactoryProgressData data,
            Dictionary<(CellOccupantType type, int index), Vector2Int> cells)
        {
            for (int i = 0; i < world.Miners.Count; i++)
            {
                MinerInstance miner = world.Miners[i];
                if (miner == null)
                {
                    data.miners.Add(new MinerProgressData { exists = false });
                    continue;
                }

                Vector2Int anchor;
                GameObject minerVisual = GameObject.Find($"Miner_{i}");
                if (minerVisual != null) anchor = GridUtility.WorldToCell(minerVisual.transform.position);
                else cells.TryGetValue((CellOccupantType.Miner, i), out anchor);
                data.miners.Add(new MinerProgressData
                {
                    exists = true,
                    machineKey = world.Database.Machines[miner.MachineId].Key,
                    outputResourceKey = world.Database.Resources[miner.OutputResourceId].Key,
                    baseSpeed = powerGrid.GetBaseSpeed(miner),
                    mineIntervalSeconds = miner.MineIntervalSeconds,
                    yieldPerCycle = miner.YieldPerCycle,
                    progress = miner.Progress,
                    bufferedOutput = miner.BufferedOutput,
                    anchor = ToData(anchor),
                });
            }
        }

        private void CaptureProcessors(SimulationWorld world, FactoryProgressData data)
        {
            for (int i = 0; i < world.Processors.Count; i++)
            {
                ProcessorInstance processor = world.Processors[i];
                if (processor == null)
                {
                    data.processors.Add(new ProcessorProgressData { exists = false });
                    continue;
                }

                var saved = new ProcessorProgressData
                {
                    exists = true,
                    machineKey = world.Database.Machines[processor.MachineId].Key,
                    recipeKey = powerGrid.GetDesiredRecipeId(processor) >= 0
                        ? world.Database.Recipes[powerGrid.GetDesiredRecipeId(processor)].Key : string.Empty,
                    activeRecipeKey = processor.ActiveRecipeId >= 0 ? world.Database.Recipes[processor.ActiveRecipeId].Key : string.Empty,
                    baseSpeed = processor.UniversalPorts ? processor.SpeedMultiplier : powerGrid.GetBaseSpeed(processor),
                    facing = ToData(processor.Facing),
                    anchor = ToData(processor.Anchor),
                    footprint = ToData(processor.Footprint),
                    universalPorts = processor.UniversalPorts,
                    isProcessing = processor.IsProcessing,
                    progress = processor.Progress,
                    capacity = processor.Capacity,
                    input = CaptureStacks(processor.InputBuffer, world),
                    output = CaptureStacks(processor.OutputBuffer, world),
                };
                data.processors.Add(saved);
            }
        }

        private static void CaptureBelts(SimulationWorld world, FactoryProgressData data,
            Dictionary<(CellOccupantType type, int index), Vector2Int> cells)
        {
            for (int i = 0; i < world.Segments.Count; i++)
            {
                BeltSegment segment = world.Segments[i];
                if (segment == null)
                {
                    data.belts.Add(new BeltProgressData { exists = false });
                    continue;
                }

                cells.TryGetValue((CellOccupantType.Belt, i), out Vector2Int cell);
                GetBeltEndpoints(i, cell, out Vector3 start, out Vector3 end);
                cell = GridUtility.WorldToCell((start + end) * 0.5f);
                var saved = new BeltProgressData
                {
                    exists = true,
                    id = i,
                    hasNext = segment.NextSegmentId.HasValue,
                    nextId = segment.NextSegmentId ?? -1,
                    length = segment.Length,
                    speed = segment.SpeedUnitsPerSecond,
                    itemSpacing = segment.ItemSpacing,
                    hasSourceProcessor = segment.SourceProcessorId.HasValue,
                    sourceProcessorIndex = segment.SourceProcessorId ?? -1,
                    hasTargetProcessor = segment.TargetProcessorId.HasValue,
                    targetProcessorIndex = segment.TargetProcessorId ?? -1,
                    hasLockedResource = segment.LockedSourceResourceId.HasValue,
                    lockedResourceKey = segment.LockedSourceResourceId.HasValue
                        ? world.Database.Resources[segment.LockedSourceResourceId.Value].Key : string.Empty,
                    lockedRecipeKey = segment.LockedForRecipeId >= 0
                        ? world.Database.Recipes[segment.LockedForRecipeId].Key : string.Empty,
                    cell = ToData(cell),
                    start = ToData(start),
                    end = ToData(end),
                };

                for (int j = 0; j < segment.Items.Count; j++)
                {
                    saved.items.Add(new BeltItemProgressData
                    {
                        resourceKey = world.Database.Resources[segment.Items[j].ResourceId].Key,
                        position = segment.Items[j].Position,
                    });
                }
                data.belts.Add(saved);
            }
        }

        private void RestoreMiners(SimulationWorld world, List<MinerProgressData> savedMiners)
        {
            if (savedMiners == null) return;
            for (int i = 0; i < savedMiners.Count; i++)
            {
                MinerProgressData saved = savedMiners[i];
                if (saved == null || !saved.exists)
                {
                    world.AddMiner(null);
                    continue;
                }

                if (!world.Database.TryGetMachineId(saved.machineKey, out int machineId)
                    || !world.Database.TryGetResourceId(saved.outputResourceKey, out int resourceId))
                {
                    world.AddMiner(null);
                    Debug.LogWarning($"[FactorySave] Miner slot {i} references missing data and was skipped.");
                    continue;
                }

                var miner = new MinerInstance
                {
                    MachineId = machineId,
                    OutputResourceId = resourceId,
                    SpeedMultiplier = saved.baseSpeed,
                    MineIntervalSeconds = saved.mineIntervalSeconds,
                    YieldPerCycle = saved.yieldPerCycle,
                    Progress = saved.progress,
                    BufferedOutput = saved.bufferedOutput,
                };
                int index = world.AddMiner(miner);
                Vector2Int anchor = ToVector(saved.anchor);
                Vector2Int footprint = world.Database.Machines[machineId].Footprint;
                world.Grid.RegisterBuildingFootprint(GridUtility.GetFootprintCells(anchor, footprint), CellOccupantType.Miner, index);
                SpawnMachineVisual(world, saved.machineKey, anchor, footprint, Vector2Int.right, MachineInstanceKind.Miner, index, false);
            }
        }

        private void RestoreProcessors(SimulationWorld world, List<ProcessorProgressData> savedProcessors,
            int coreProcessorIndex, GameObject coreVisual)
        {
            if (savedProcessors == null) return;
            for (int i = 0; i < savedProcessors.Count; i++)
            {
                ProcessorProgressData saved = savedProcessors[i];
                if (saved == null || !saved.exists)
                {
                    world.AddProcessor(null);
                    continue;
                }

                if (!world.Database.TryGetMachineId(saved.machineKey, out int machineId))
                {
                    world.AddProcessor(null);
                    Debug.LogWarning($"[FactorySave] Processor slot {i} references missing machine '{saved.machineKey}'.");
                    continue;
                }

                var processor = new ProcessorInstance(world.Database.ResourceCount)
                {
                    MachineId = machineId,
                    RecipeId = ResolveRecipe(world, saved.recipeKey),
                    ActiveRecipeId = ResolveRecipe(world, saved.activeRecipeKey),
                    SpeedMultiplier = saved.baseSpeed,
                    Facing = ToVector(saved.facing),
                    Anchor = ToVector(saved.anchor),
                    Footprint = ToVector(saved.footprint),
                    UniversalPorts = saved.universalPorts,
                    IsProcessing = saved.isProcessing,
                    Progress = saved.progress,
                    Capacity = saved.capacity,
                };
                RestoreStacks(processor.InputBuffer, saved.input, world);
                RestoreStacks(processor.OutputBuffer, saved.output, world);

                int index = world.AddProcessor(processor);
                world.Grid.RegisterBuildingFootprint(
                    GridUtility.GetFootprintCells(processor.Anchor, processor.Footprint), CellOccupantType.Processor, index);
                bool isCore = index == coreProcessorIndex;
                if (isCore && coreVisual != null)
                {
                    RebindCoreVisual(coreVisual, processor.Anchor, processor.Footprint, processor.Facing, index);
                }
                else
                {
                    SpawnMachineVisual(world, saved.machineKey, processor.Anchor, processor.Footprint, processor.Facing,
                        MachineInstanceKind.Processor, index, isCore);
                }
            }
        }

        private static void RestoreBelts(SimulationWorld world, List<BeltProgressData> savedBelts)
        {
            if (savedBelts == null) return;
            for (int i = 0; i < savedBelts.Count; i++)
            {
                BeltProgressData saved = savedBelts[i];
                if (saved == null || !saved.exists)
                {
                    world.AddBeltSegment(null);
                    continue;
                }

                var segment = new BeltSegment
                {
                    Id = i,
                    NextSegmentId = saved.hasNext ? saved.nextId : (int?)null,
                    Length = saved.length,
                    SpeedUnitsPerSecond = saved.speed,
                    ItemSpacing = saved.itemSpacing,
                    SourceProcessorId = saved.hasSourceProcessor ? saved.sourceProcessorIndex : (int?)null,
                    TargetProcessorId = saved.hasTargetProcessor ? saved.targetProcessorIndex : (int?)null,
                    LockedForRecipeId = ResolveRecipe(world, saved.lockedRecipeKey),
                };
                if (saved.hasLockedResource && world.Database.TryGetResourceId(saved.lockedResourceKey, out int lockedId))
                    segment.LockedSourceResourceId = lockedId;

                if (saved.items != null)
                {
                    for (int j = 0; j < saved.items.Count; j++)
                    {
                        if (world.Database.TryGetResourceId(saved.items[j].resourceKey, out int resourceId))
                            segment.Items.Add(new BeltItem(resourceId, saved.items[j].position));
                    }
                }

                int index = world.AddBeltSegment(segment);
                Vector2Int cell = ToVector(saved.cell);
                world.Grid.RegisterSegment(cell, index);
                SpawnBeltVisual(driver: FindAnyObjectByType<SimulationDriver>(), segmentId: index,
                    cell: cell, start: ToVector(saved.start), end: ToVector(saved.end));
            }
        }

        private static List<ResourceStackData> CaptureStacks(int[] buffer, SimulationWorld world)
        {
            var result = new List<ResourceStackData>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == 0) continue;
                result.Add(new ResourceStackData { resourceKey = world.Database.Resources[i].Key, amount = buffer[i] });
            }
            return result;
        }

        private static void RestoreStacks(int[] buffer, List<ResourceStackData> stacks, SimulationWorld world)
        {
            if (stacks == null) return;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (world.Database.TryGetResourceId(stacks[i].resourceKey, out int resourceId))
                    buffer[resourceId] = Mathf.Max(0, stacks[i].amount);
            }
        }

        private static int ResolveRecipe(SimulationWorld world, string recipeKey)
        {
            return !string.IsNullOrEmpty(recipeKey) && world.Database.TryGetRecipeId(recipeKey, out int id) ? id : -1;
        }

        private static void ClearCurrentFactory(SimulationWorld world, GameObject preservedCoreVisual)
        {
            for (int i = 0; i < world.Miners.Count; i++) world.Grid.UnregisterOccupant(CellOccupantType.Miner, i);
            for (int i = 0; i < world.Processors.Count; i++) world.Grid.UnregisterOccupant(CellOccupantType.Processor, i);
            for (int i = 0; i < world.Segments.Count; i++)
            {
                world.Grid.UnregisterOccupant(CellOccupantType.Belt, i);
                GameObject belt = GameObject.Find($"Belt_{i}");
                if (belt != null) Destroy(belt);
            }

            MachineView[] views = FindObjectsByType<MachineView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null && views[i].gameObject != preservedCoreVisual) Destroy(views[i].gameObject);
            }

            world.Miners.Clear();
            world.Processors.Clear();
            world.Segments.Clear();
            world.CoreProcessorIndex = -1;
        }

        private static void RebindCoreVisual(GameObject coreVisual, Vector2Int anchor, Vector2Int footprint,
            Vector2Int facing, int index)
        {
            coreVisual.transform.position = GridUtility.GetFootprintCenter(anchor, footprint, 0.75f);
            coreVisual.transform.rotation = facing == Vector2Int.zero
                ? Quaternion.identity
                : Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);
            coreVisual.name = "Core";

            MachineView view = coreVisual.GetComponent<MachineView>() ?? coreVisual.AddComponent<MachineView>();
            view.Initialize(MachineInstanceKind.Processor, index, FindAnyObjectByType<SimulationDriver>());
        }

        private static void SpawnMachineVisual(SimulationWorld world, string machineKey, Vector2Int anchor,
            Vector2Int footprint, Vector2Int facing, MachineInstanceKind kind, int index, bool isCore)
        {
            GameObject prefab = FindMachinePrefab(machineKey);
            Vector3 position = GridUtility.GetFootprintCenter(anchor, footprint, isCore ? 0.75f : 0.5f);
            Quaternion rotation = facing == Vector2Int.zero
                ? Quaternion.identity
                : Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y), Vector3.up);

            GameObject visual;
            if (prefab != null)
            {
                visual = Instantiate(prefab, position, rotation);
            }
            else
            {
                Color color = kind == MachineInstanceKind.Miner
                    ? new Color(0.55f, 0.4f, 0.25f)
                    : new Color(0.6f, 0.15f, 0.1f);
                visual = BuildVisuals.CreateBox(position, Vector3.one, color, null);
                visual.transform.rotation = rotation;
            }

            visual.name = isCore ? "Core" : $"{kind}_{index}";
            if (!isCore)
            {
                Vector3 baseScale = visual.transform.localScale;
                visual.transform.localScale = new Vector3(baseScale.x * footprint.x, baseScale.y, baseScale.z * footprint.y);
            }
            MachineView view = visual.GetComponent<MachineView>() ?? visual.AddComponent<MachineView>();
            view.Initialize(kind, index, FindAnyObjectByType<SimulationDriver>());
        }

        private static GameObject FindMachinePrefab(string machineKey)
        {
            MachineVisualLibrary[] libraries = Resources.FindObjectsOfTypeAll<MachineVisualLibrary>();
            for (int i = 0; i < libraries.Length; i++)
            {
                if (libraries[i] != null && libraries[i].TryGetPrefab(machineKey, out GameObject prefab)) return prefab;
            }
            return null;
        }

        private static void SpawnBeltVisual(SimulationDriver driver, int segmentId, Vector2Int cell, Vector3 start, Vector3 end)
        {
            if (driver == null) return;
            if ((start - end).sqrMagnitude < 0.001f)
            {
                Vector3 center = GridUtility.CellToWorldCenter(cell, 0.5f);
                start = center - Vector3.forward * 0.5f;
                end = center + Vector3.forward * 0.5f;
            }

            Vector3 stripStart = new Vector3(start.x, 0.5f, start.z);
            Vector3 stripEnd = new Vector3(end.x, 0.5f, end.z);
            Vector3 centerPoint = GridUtility.CellToWorldCenter(cell, 0.5f);
            bool corner = !Mathf.Approximately(stripStart.x, stripEnd.x) && !Mathf.Approximately(stripStart.z, stripEnd.z);

            var root = new GameObject($"Belt_{segmentId}");
            Transform startAnchor = new GameObject("Start").transform;
            startAnchor.SetParent(root.transform);
            startAnchor.position = new Vector3(start.x, 0.745f, start.z);
            Transform endAnchor = new GameObject("End").transform;
            endAnchor.SetParent(root.transform);
            endAnchor.position = new Vector3(end.x, 0.745f, end.z);

            Color color = new Color(0.15f, 0.15f, 0.15f, 1f);
            if (corner)
            {
                BuildVisuals.CreateStrip(stripStart, centerPoint, 0.6f, color, root.transform);
                BuildVisuals.CreateStrip(centerPoint, stripEnd, 0.6f, color, root.transform);
            }
            else
            {
                BuildVisuals.CreateStrip(stripStart, stripEnd, 0.6f, color, root.transform);
            }

            var itemRenderer = root.AddComponent<BeltItemRenderer>();
            itemRenderer.Initialize(driver, segmentId, startAnchor, endAnchor, FindLoadedPrefab("BeltItemVisual"));
        }

        private static GameObject FindLoadedPrefab(string prefabName)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && !objects[i].scene.IsValid() && objects[i].name == prefabName) return objects[i];
            }
            return null;
        }

        private static Dictionary<(CellOccupantType type, int index), Vector2Int> ScanOccupants(SimulationWorld world)
        {
            var result = new Dictionary<(CellOccupantType, int), Vector2Int>();
            for (int x = -ScanRange; x <= ScanRange; x++)
            {
                for (int y = -ScanRange; y <= ScanRange; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!world.Grid.TryGetOccupant(cell, out CellOccupant occupant)) continue;
                    var key = (occupant.Type, occupant.InstanceIndex);
                    if (!result.ContainsKey(key)) result[key] = cell;
                }
            }
            return result;
        }

        private static void GetBeltEndpoints(int index, Vector2Int cell, out Vector3 start, out Vector3 end)
        {
            GameObject root = GameObject.Find($"Belt_{index}");
            Transform startTransform = root != null ? root.transform.Find("Start") : null;
            Transform endTransform = root != null ? root.transform.Find("End") : null;
            if (startTransform != null && endTransform != null)
            {
                start = startTransform.position;
                end = endTransform.position;
                return;
            }

            Vector3 center = GridUtility.CellToWorldCenter(cell, 0.745f);
            start = center - Vector3.forward * 0.5f;
            end = center + Vector3.forward * 0.5f;
        }

        private void ResolveReferences()
        {
            if (driver == null) driver = FindAnyObjectByType<SimulationDriver>();
            if (powerGrid == null) powerGrid = GetComponent<PowerGridSystem>() ?? FindAnyObjectByType<PowerGridSystem>();
            if (powerBuild == null) powerBuild = GetComponent<PowerBuildController>() ?? FindAnyObjectByType<PowerBuildController>();
        }

        private static Int2Data ToData(Vector2Int value) => new Int2Data(value.x, value.y);
        private static Vector2Int ToVector(Int2Data value) => new Vector2Int(value.x, value.y);
        private static Vector3Data ToData(Vector3 value) => new Vector3Data(value.x, value.y, value.z);
        private static Vector3 ToVector(Vector3Data value) => new Vector3(value.x, value.y, value.z);
    }
}
