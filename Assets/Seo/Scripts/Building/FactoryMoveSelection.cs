using System;
using System.Collections.Generic;
using Factory.Simulation;
using UnityEngine;

namespace Seo.Building
{
    // 이동 대상과 원래 위치만 기억한다. 미리보기 동안 월드에는 손대지 않으며,
    // 확정 직전에 전체 목적지를 검사한 후 점유 정보와 경계 연결을 한 번에 갱신한다.
    public sealed class FactoryMoveSelection
    {
        public sealed class Entry
        {
            public CellOccupantType Type { get; internal set; }
            public int Index { get; internal set; }
            public Vector2Int Anchor { get; internal set; }
            public Vector2Int Footprint { get; internal set; }
            public bool Crossing { get; internal set; }
            internal object Instance;
            public string VisualName => Type == CellOccupantType.Belt ? $"Belt_{Index}" : $"{Type}_{Index}";
        }

        private readonly SimulationWorld world;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly HashSet<(CellOccupantType type, int index)> members = new HashSet<(CellOccupantType, int)>();
        private bool committed;
        public IReadOnlyList<Entry> Entries => entries;
        public SimulationWorld World => world;
        public RectInt Bounds { get; private set; }

        public FactoryMoveSelection(SimulationWorld world, IEnumerable<(CellOccupantType type, int index)> selection)
        {
            this.world = world;
            if (world == null || selection == null) return;
            foreach (var selected in selection) Add(selected.type, selected.index);
            // 교차 벨트는 같은 칸의 두 축을 항상 함께 이동한다.
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Type != CellOccupantType.Belt) continue;
                if (world.Grid.TryGetOccupant(entry.Anchor, out var primary) && primary.Type == CellOccupantType.Belt)
                    Add(primary.Type, primary.InstanceIndex);
                if (world.Grid.TryGetCrossingOccupant(entry.Anchor, out var crossing)) Add(crossing.Type, crossing.InstanceIndex);
            }
            if (entries.Count == 0) return;
            Vector2Int min = entries[0].Anchor;
            Vector2Int max = min + entries[0].Footprint;
            foreach (var entry in entries)
            {
                min = Vector2Int.Min(min, entry.Anchor);
                max = Vector2Int.Max(max, entry.Anchor + entry.Footprint);
            }
            Bounds = new RectInt(min, max - min);
        }

        private object GetInstance(CellOccupantType type, int index)
        {
            if (index < 0) return null;
            switch (type)
            {
                case CellOccupantType.Belt: return index < world.Segments.Count ? world.Segments[index] : null;
                case CellOccupantType.Miner: return index < world.Miners.Count ? world.Miners[index] : null;
                case CellOccupantType.Processor: return index < world.Processors.Count ? world.Processors[index] : null;
                default: return null;
            }
        }

        private void Add(CellOccupantType type, int index)
        {
            if (members.Contains((type, index))) return;
            object instance = GetInstance(type, index);
            if (instance == null) return;
            if (instance is ProcessorInstance p && (index == world.CoreProcessorIndex || p.IsGeneratorFuelPort)) return;
            if (!world.Grid.TryGetCellOf(type, index, out var cell)) return;
            var entry = new Entry { Type = type, Index = index, Anchor = cell, Footprint = Vector2Int.one, Instance = instance };
            if (instance is ProcessorInstance processor)
            {
                entry.Anchor = processor.Anchor;
                entry.Footprint = processor.Footprint;
            }
            else if (instance is MinerInstance miner)
                entry.Footprint = world.Database.Machines[miner.MachineId].Footprint;
            else
                entry.Crossing = world.Grid.TryGetCrossingOccupant(cell, out var crossing) && crossing.InstanceIndex == index;
            members.Add((type, index));
            entries.Add(entry);
        }

        public bool Validate(Vector2Int offset, out string reason,
            Func<Vector2Int, bool> externallyBlocked = null, Func<string, Vector2Int, bool> placementAllowed = null)
        {
            reason = null;
            if (committed || entries.Count == 0) { reason = "이동할 기계·벨트를 선택하세요 · 코어와 전력 시설 제외"; return false; }
            if (offset == Vector2Int.zero) { reason = "선택한 묶음을 새 위치로 드래그하세요"; return false; }
            foreach (var entry in entries)
            {
                if (!ReferenceEquals(entry.Instance, GetInstance(entry.Type, entry.Index)))
                { reason = "선택한 설치물이 바뀌었습니다. 다시 선택하세요"; return false; }
                foreach (var source in GridUtility.GetFootprintCells(entry.Anchor, entry.Footprint))
                {
                    CellOccupant occupant;
                    bool present = entry.Crossing ? world.Grid.TryGetCrossingOccupant(source, out occupant)
                        : world.Grid.TryGetOccupant(source, out occupant);
                    if (!present || occupant.Type != entry.Type || occupant.InstanceIndex != entry.Index)
                    { reason = "선택한 설치물의 위치가 바뀌었습니다. 다시 선택하세요"; return false; }
                    var target = source + offset;
                    if ((world.Grid.TryGetOccupant(target, out occupant) && !members.Contains((occupant.Type, occupant.InstanceIndex)))
                        || (world.Grid.TryGetCrossingOccupant(target, out occupant) && !members.Contains((occupant.Type, occupant.InstanceIndex)))
                        || (externallyBlocked?.Invoke(target) ?? false))
                    { reason = "다른 기계·벨트 또는 전력 시설과 겹칩니다"; return false; }
                }
                Vector2Int anchor = entry.Anchor + offset;
                if (entry.Instance is MinerInstance miner)
                {
                    if (!world.Grid.TryGetOreDeposit(anchor, out int depositId)
                        || world.Database.OreDeposits[depositId].ResourceId != miner.OutputResourceId)
                    { reason = "채굴기는 같은 종류의 광물이 있는 칸으로 이동하세요"; return false; }
                }
                int machineId = entry.Instance is ProcessorInstance processor ? processor.MachineId
                    : entry.Instance is MinerInstance m ? m.MachineId : -1;
                string key = machineId >= 0 ? world.Database.Machines[machineId].Key
                    : entry.Instance is BeltSegment belt && belt.IsCrossable ? "CrossBelt" : null;
                if (key != null && placementAllowed != null && !placementAllowed(key, anchor))
                { reason = "현재 설치 제한으로 이동할 수 없는 위치입니다"; return false; }
            }
            return true;
        }

        public bool TryCommit(Vector2Int offset, out string reason,
            Func<Vector2Int, bool> externallyBlocked = null, Func<string, Vector2Int, bool> placementAllowed = null)
        {
            if (!Validate(offset, out reason, externallyBlocked, placementAllowed)) return false;
            // 묶음 내부의 연결은 그대로 보존하고, 경계를 가로지르는 연결만 해제한다.
            for (int i = 0; i < world.Segments.Count; i++)
            {
                var segment = world.Segments[i];
                if (segment == null) continue;
                bool moved = members.Contains((CellOccupantType.Belt, i));
                if (segment.NextSegmentId.HasValue && moved != members.Contains((CellOccupantType.Belt, segment.NextSegmentId.Value)))
                    segment.NextSegmentId = null;
                if (segment.SourceProcessorId.HasValue && moved != members.Contains((CellOccupantType.Processor, segment.SourceProcessorId.Value)))
                {
                    segment.SourceProcessorId = null;
                    segment.LockedSourceResourceId = null;
                    segment.LockedForRecipeId = -1;
                }
                if (segment.TargetProcessorId.HasValue && moved != members.Contains((CellOccupantType.Processor, segment.TargetProcessorId.Value)))
                    segment.TargetProcessorId = null;
            }
            // 먼저 전부 비워야 원래 선택 영역에 일부 겹쳐 놓는 이동도 가능하다.
            foreach (var entry in entries) world.Grid.UnregisterOccupant(entry.Type, entry.Index);
            foreach (var entry in entries)
            {
                Vector2Int anchor = entry.Anchor + offset;
                if (entry.Type == CellOccupantType.Belt)
                {
                    if (entry.Crossing) world.Grid.RegisterCrossingSegment(anchor, entry.Index);
                    else world.Grid.RegisterSegment(anchor, entry.Index);
                }
                else
                {
                    if (entry.Instance is ProcessorInstance processor) processor.Anchor = anchor;
                    if (entry.Instance is MinerInstance miner && world.Grid.TryGetOreDeposit(anchor, out int depositId))
                    {
                        var deposit = world.Database.OreDeposits[depositId];
                        miner.MineIntervalSeconds = deposit.MineIntervalSeconds;
                        miner.YieldPerCycle = deposit.YieldPerCycle;
                    }
                    world.Grid.RegisterBuildingFootprint(GridUtility.GetFootprintCells(anchor, entry.Footprint), entry.Type, entry.Index);
                }
            }
            committed = true;
            return true;
        }
    }
}
