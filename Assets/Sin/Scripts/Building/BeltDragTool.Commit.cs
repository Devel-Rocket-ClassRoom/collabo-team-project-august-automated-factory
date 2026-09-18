using System.Collections.Generic;
using Factory.Data;
using Factory.Simulation;
using UnityEngine;

namespace Factory.Building
{
    // 릴리즈 시 실제 BeltSegment를 만들어 세계에 반영하는 부분 — 미리보기(Preview)와
    // 반드시 같은 기준(OnBuilding/Resolved 구분 등)으로 판정해야 어긋나지 않는다.
    public partial class BeltDragTool
    {
        private void Commit()
        {
            if (driver == null || driver.World == null || path.Count < 2) return;

            var grid = driver.World.Grid;
            int last = path.Count - 1;

            // 시작/끝 모두 "그 칸에 직접 닿았는지"(OnBuilding — beltCells를 자를지에만 쓴다)와
            // "연결 대상을 찾았는지"(Resolved — 직접 닿았거나 바로 옆칸이 유효 포트라 자동
            // 연결된 경우 둘 다 포함, 배선에 쓴다)를 구분한다 — HasValidEndpointPreview()와
            // 반드시 같은 기준. 옆칸으로 자동 연결된 경우 그 기계 칸(neighborCell)도 따로
            // 기억해뒀다가, 스트립을 그 칸 쪽으로 자연스럽게 휘게 그리는 데 쓴다.
            bool startOnBuilding = grid.IsOccupied(path[0]);
            CellOccupant startOccupant = default;
            if (startOnBuilding) grid.TryGetOccupant(path[0], out startOccupant);

            bool endOnBuilding = grid.IsOccupied(path[last]);
            CellOccupant endOccupant = default;
            if (endOnBuilding) grid.TryGetOccupant(path[last], out endOccupant);

            bool startFixed = false, endFixed = false;
            EndpointRole startRole = EndpointRole.None;
            Vector2Int? startNeighborCell = null;
            Vector2Int? endNeighborCell = null;

            if (startOnBuilding)
            {
                startRole = ResolveEndpointRole(startOccupant, path[1], true, out startFixed);
            }
            else if (TryFindAdjacentOccupant(path[0], path[1], true, out startOccupant, out startRole, out startFixed, out var startNeighbor))
            {
                startNeighborCell = startNeighbor;
            }

            EndpointRole endRole = EndpointRole.None;
            bool endResolved = false;

            if (endOnBuilding)
            {
                endRole = ResolveEndpointRole(endOccupant, path[last - 1], false, out endFixed);
                endResolved = endRole != EndpointRole.None;
            }
            else if (TryFindAdjacentOccupant(path[last], path[last - 1], false, out endOccupant, out endRole, out endFixed, out var endNeighbor))
            {
                endResolved = true;
                endNeighborCell = endNeighbor;
            }

            // 점유된 칸이 있는데 유효한 포트가 아니면(기계 옆면 등) 거부 — 잘못된 연결을
            // 어설프게 만들지 않는다. 빈 칸이고 옆에도 유효 포트가 없으면(막다른 벨트) 거부가
            // 아니라 나중에 이어질 수도 있는 정상적인 상태다. 다만 시작은 여전히 뭔가에 연결돼야
            // 한다 — 허공에서 시작해서 아무 데도 안 닿는 벨트는 지을 수 없다(끝은 막다른 채로 둘 수 있음).
            if (startRole == EndpointRole.None) return;
            if (endOnBuilding && !endResolved) return;

            // 양쪽 다 같은 역할로 겹치면(둘 다 Source거나 둘 다 Target) 보통 코어처럼 순서
            // 의존적인(고정 아님) 쪽이 반대쪽 고정 포트 방향과 어긋난 경우다 — 예: 제련로
            // 입력면 쪽에서 시작해 코어로 드래그하면, 입력면은 Target인데 코어도 (isStart가
            // 아니라는 이유만으로) Target으로 잡혀버림. 고정 포트가 아닌 쪽을 반대 역할로
            // 바로잡는다. 둘 다 고정이거나 둘 다 유동인데 겹치면 진짜로 애매하니 거부한다.
            if (endResolved && startRole == endRole)
            {
                if (!startFixed && endFixed) startRole = Opposite(endRole);
                else if (startFixed && !endFixed) endRole = Opposite(startRole);
                else return;
            }

            // 소스가 끝 쪽으로 판정됐으면(예: 제련로 입력면에서 시작해 코어 쪽으로 드래그한
            // 경우) 경로를 뒤집어서 소스가 항상 앞에 오게 한다 — 세그먼트 체인은 배열 순서를
            // 그대로 흐름 순서로 쓰기 때문. 역할은 이미 위에서 확정했으니 다시 판정하지 않고
            // 그대로 맞바꾼다(다시 판정하면 위에서 바로잡은 결과가 날아감).
            if (startRole != EndpointRole.Source && endRole == EndpointRole.Source)
            {
                path.Reverse();
                (startOnBuilding, endOnBuilding) = (endOnBuilding, startOnBuilding);
                (startOccupant, endOccupant) = (endOccupant, startOccupant);
                (startRole, endRole) = (endRole, startRole);
                (startNeighborCell, endNeighborCell) = (endNeighborCell, startNeighborCell);
                endResolved = true; // 새 끝(옛 시작)은 위에서 이미 None이 걸러졌으니 항상 유효.
            }

            // 시작 칸이 "이미 다른 곳으로 흐르고 있는" 기존 벨트면 여기서 거부한다 — 안 그러면
            // 그 벨트의 NextSegmentId를 조용히 새 목적지로 덮어써서, 원래 흐르던 곳과의 연결이
            // 몰래 끊기고 두 벨트가 뜻하지 않게 하나로 합쳐진다(합류 자체는 나중에 합류기로
            // 의도적으로 할 수 있어야 하니 막지 않지만, "이미 연결된 벨트를 가로채는" 건 막는다).
            if (startRole == EndpointRole.Source && startOccupant.Type == CellOccupantType.Belt
                && driver.World.Segments[startOccupant.InstanceIndex].NextSegmentId.HasValue)
            {
                return;
            }

            // 그 칸에 직접 닿은 경우만 새 벨트 칸에서 뺀다 — 옆칸에서 자동 연결된 경우는 그
            // 칸 자체가 진짜 새 벨트 칸이므로 자르지 않는다(기계 위가 아니라 옆에 놓는 것).
            var beltCells = new List<Vector2Int>(path);
            if (endOnBuilding) beltCells.RemoveAt(beltCells.Count - 1);
            if (startOnBuilding) beltCells.RemoveAt(0);

            if (beltCells.Count == 0)
            {
                // 새로 놓을 벨트 칸이 아예 없는 경우 (예: 기존 벨트 끝이 제련로 입력면 바로
                // 옆칸이라 사이에 빈 칸이 없음) — 새 세그먼트 없이 기존 것끼리 바로 연결한다.
                TryDirectLink(startOnBuilding, startOccupant, startRole, endOnBuilding, endOccupant, endRole);
                return;
            }

            for (int i = 0; i < beltCells.Count; i++)
            {
                if (grid.IsOccupied(beltCells[i]) || (ExternalCellBlocked?.Invoke(beltCells[i]) ?? false))
                    return; // 기존 건물뿐 아니라 발전기/송전탑 위에도 벨트를 놓지 않는다.
            }

            // 새로 놓을 칸 수만큼 콘크리트를 코어에서 뗀다 — 모자라면 한 칸도 안 짓고
            // 그대로 취소한다(반쯤 지어지는 것 방지, 기계 건설 비용과 같은 원칙).
            if (!TryDeductBeltCost(beltCells.Count)) return;

            var createdSegments = new List<BeltSegment>(beltCells.Count);
            for (int i = 0; i < beltCells.Count; i++)
            {
                createdSegments.Add(new BeltSegment { Id = driver.World.Segments.Count + i, Length = 1f, ConcreteCost = concreteCostPerTile });
            }

            if (startRole == EndpointRole.Source)
            {
                switch (startOccupant.Type)
                {
                    // Miner는 ResolveEndpointRole에서 항상 None이라 여기 Source로 들어올 수 없다.
                    case CellOccupantType.Processor:
                        createdSegments[0].SourceProcessorId = startOccupant.InstanceIndex;
                        break;
                    case CellOccupantType.Belt:
                        // 기존 벨트 끝에 이어서 놓는 경우: 그 세그먼트가 새 첫 세그먼트로 흘러들게 연결.
                        driver.World.Segments[startOccupant.InstanceIndex].NextSegmentId = createdSegments[0].Id;
                        break;
                }
            }

            if (endResolved && endRole == EndpointRole.Target)
            {
                if (endOccupant.Type == CellOccupantType.Processor)
                {
                    createdSegments[createdSegments.Count - 1].TargetProcessorId = endOccupant.InstanceIndex;
                }
                else if (endOccupant.Type == CellOccupantType.Belt)
                {
                    // 기존 벨트 시작 쪽에 이어붙이는 경우: 새 마지막 세그먼트가 그 세그먼트로 흘러들게 연결.
                    createdSegments[createdSegments.Count - 1].NextSegmentId = endOccupant.InstanceIndex;
                }
            }

            for (int i = 0; i < createdSegments.Count - 1; i++)
            {
                createdSegments[i].NextSegmentId = createdSegments[i + 1].Id;
            }

            // beltCells[i]는 항상 renderPath[1 + i]에 대응한다. 시작 쪽엔 그 앞 칸 참조가
            // 언제나 정확히 하나 있다 — 직접 닿았으면 path[0](기계 자신)가 그대로 그 역할을
            // 하고, 옆칸 자동연결이면 그 기계 칸을 가상으로 하나 앞에 붙인다 — 그래서 오프셋이
            // 두 경우 다 1로 같다. 이 참조 칸 덕분에 ComputeCellSpan이 처음/마지막 조각을
            // 기계 쪽으로 자연스럽게 휘어 그린다(옆칸 자동연결도 코너처럼 보이게 — 실제 게임
            // 로직(path/beltCells)은 안 건드리고 스트립 모양 계산에만 쓰는 가상 경로).
            List<Vector2Int> renderPath = path;
            if (startNeighborCell.HasValue || endNeighborCell.HasValue)
            {
                renderPath = new List<Vector2Int>(path);
                if (endNeighborCell.HasValue) renderPath.Add(endNeighborCell.Value);
                if (startNeighborCell.HasValue) renderPath.Insert(0, startNeighborCell.Value);
            }

            for (int i = 0; i < createdSegments.Count; i++)
            {
                ComputeCellSpan(renderPath, 1 + i, out Vector3 entry, out Vector3 exit, out Vector3? bend);

                driver.World.AddBeltSegment(createdSegments[i]);
                grid.RegisterSegment(beltCells[i], createdSegments[i].Id);

                SpawnCommittedVisual(entry, exit, bend, createdSegments[i].Id);
            }

            // 옆에 이어붙여서 흐름이 꺾인 기존 벨트의 스트립/화살표를 실제 방향으로 다시 그린다
            // (스트립은 생성 시점 path로 한 번만 구워지므로, 재배선하면 옛 방향 그대로 남았다 — 사용자 보고).
            if (startRole == EndpointRole.Source && startOccupant.Type == CellOccupantType.Belt)
                RerenderSegmentStrip(startOccupant.InstanceIndex);
            if (endResolved && endRole == EndpointRole.Target && endOccupant.Type == CellOccupantType.Belt)
                RerenderSegmentStrip(endOccupant.InstanceIndex);
        }

        // 기계 건설 비용(MachineGhostTool/PowerBuildController)과 같은 원칙 — 확인/차감은
        // BuildCostUtility 공용 로직에 맡기고, 여기는 "칸 수 → 콘크리트 비용"으로 바꿔주는
        // 것만 한다(둘이 따로 구현하면 판정이 어긋나는 버그가 났었다 — 전력 쪽 참고).
        private bool TryGetBeltCost(int cellCount, out ResourceAmount[] cost, out ProcessorInstance core)
        {
            cost = null;
            core = null;
            if (concreteCostPerTile <= 0 || cellCount <= 0) return false;
            if (driver == null || driver.World == null) return false;

            int coreIndex = driver.World.CoreProcessorIndex;
            if (coreIndex < 0 || coreIndex >= driver.World.Processors.Count) return false;
            core = driver.World.Processors[coreIndex];
            if (core == null) return false;

            if (!driver.World.Database.TryGetResourceId("Concrete", out int concreteId)) return false;
            cost = new[] { new ResourceAmount(concreteId, cellCount * concreteCostPerTile) };
            return true;
        }

        // 미리보기에서 매번 불러도 부작용 없이 "지금 이 칸 수만큼 지을 여유가 있는지"만 본다.
        private bool CanAffordBeltCost(int cellCount)
        {
            if (!TryGetBeltCost(cellCount, out var cost, out var core)) return true;
            return BuildCostUtility.CanAfford(core, cost);
        }

        private bool TryDeductBeltCost(int cellCount)
        {
            if (!TryGetBeltCost(cellCount, out var cost, out var core)) return true;
            return BuildCostUtility.TryPay(core, cost);
        }

        // 새 벨트 칸 없이 기존 벨트를 기존 제련로/기존 벨트에 직접 연결한다 (둘이 바로 붙어있는 경우).
        // 채굴기는 최소 한 칸의 벨트가 있어야 산출물을 실을 수 있으므로 여기서는 다루지 않는다.
        private void TryDirectLink(bool startOccupied, CellOccupant startOccupant, EndpointRole startRole, bool endOccupied, CellOccupant endOccupant, EndpointRole endRole)
        {
            if (!startOccupied || startOccupant.Type != CellOccupantType.Belt || startRole != EndpointRole.Source) return;
            if (!endOccupied || endRole != EndpointRole.Target) return;

            var startSegment = driver.World.Segments[startOccupant.InstanceIndex];
            // 이미 다른 곳으로 흐르고 있는 벨트를 여기서 또 가로채면 안 된다(위 Commit()의
            // 같은 취지 가드 참고) — 안 그러면 원래 목적지와의 연결이 조용히 끊긴다.
            if (startSegment.NextSegmentId.HasValue || startSegment.TargetProcessorId.HasValue) return;

            if (endOccupant.Type == CellOccupantType.Processor)
            {
                startSegment.TargetProcessorId = endOccupant.InstanceIndex;
            }
            else if (endOccupant.Type == CellOccupantType.Belt)
            {
                startSegment.NextSegmentId = endOccupant.InstanceIndex;
                RerenderSegmentStrip(endOccupant.InstanceIndex);
            }

            RerenderSegmentStrip(startOccupant.InstanceIndex);
        }

        // 기존 벨트 세그먼트의 스트립/화살표를, 그 세그먼트의 실제 그리드 이웃(상류·하류 —
        // 벨트든 기계든 둘 다 포함, TryGetUpstreamCell/TryGetDownstreamCell 참고)에 맞춰
        // 다시 그린다. 스트립은 생성 시점의 드래그 path로 한 번만 구워지므로, 나중에 재배선
        // (옆에 이어붙이기 등)으로 흐름이 꺾이면 옛 방향/화살표가 그대로 남는다.
        // DemolishTool도 벨트를 지운 뒤 그 옆에 남은 세그먼트를 다시 그리는 데 이걸 그대로
        // 쓴다 — 안 그러면 잘려나간 이웃 방향 그대로 코너/화살표가 남아서 이상하게 보인다.
        public void RerenderSegmentStrip(int segmentId)
        {
            if (segmentId < 0 || segmentId >= driver.World.Segments.Count) return;
            var segment = driver.World.Segments[segmentId];
            if (segment == null) return;

            var grid = driver.World.Grid;
            if (!grid.TryGetCellOf(CellOccupantType.Belt, segmentId, out var cell)) return;

            bool hasDown = TryGetDownstreamCell(segment, cell, out var downCell);
            bool hasUp = TryGetUpstreamCell(segment, cell, out var upCell);
            if (!hasUp && !hasDown) return; // 방향을 알 수 없으면(양쪽 다 기계/없음) 건드리지 않는다

            var miniPath = new List<Vector2Int>(3);
            if (hasUp) miniPath.Add(upCell);
            miniPath.Add(cell);
            if (hasDown) miniPath.Add(downCell);

            ComputeCellSpan(miniPath, hasUp ? 1 : 0, out Vector3 entry, out Vector3 exit, out Vector3? bend);

            // 이 세그먼트가 미리보기 중 PreviewAdjacentBeltBend/PreviewOnBuildingBeltBend에
            // 의해 숨겨져 있었을 수 있다 — 숨긴 오브젝트는 SetActive(false)라서 GameObject.Find로
            // 못 찾으니, 진짜로 다시 그릴 거면 여기서 직접 참조로 없애야 아래에서 또 하나가
            // 겹쳐 생기지 않는다.
            if (hiddenNeighborVisuals.TryGetValue(segmentId, out var hiddenVisual))
            {
                if (hiddenVisual != null) Destroy(hiddenVisual);
                hiddenNeighborVisuals.Remove(segmentId);
            }

            var existing = GameObject.Find($"Belt_{segmentId}");
            if (existing != null)
            {
                // Destroy는 프레임 끝에 처리되는데 바로 아래에서 같은 이름으로 새로 만든다 —
                // 그 사이 GameObject.Find(철거 도구 등)가 없어질 오브젝트를 집지 않게 이름부터 바꾼다.
                existing.name = $"Belt_{segmentId}_replaced";
                Destroy(existing);
            }
            SpawnCommittedVisual(entry, exit, bend, segmentId);
        }
    }
}
