using System;
using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 벨트 위 아이템 이동 + 적재/하차를 처리한다. 아이템은 GameObject가 아니라 각 세그먼트가
    // 들고 있는 BeltItem(struct) 리스트로 표현되고, 서로 추월할 수 없다는 규칙 하나로
    // 정체 시 뒤에서부터 압축되는 동작(back-pressure)이 자연스럽게 나온다.
    //
    // 세그먼트는 반드시 "하류(목적지 쪽)부터" 처리해야 한다 — 상류 세그먼트가 다음 세그먼트로
    // 넘어갈 자리가 있는지 판단하려면 그 다음 세그먼트가 이번 틱에 이미 전진을 마친 상태여야
    // 하기 때문. 예전에는 이걸 "호출자가 배열을 소스→목적지 순서로 넘겨준다"는 가정(+ 역순
    // 순회)으로 처리했는데, 벨트를 여러 번에 나눠 놓다가 기존 벨트의 "앞쪽"에 새 세그먼트를
    // 이어붙이면(새 세그먼트가 배열엔 나중에 추가되지만 실제로는 상류) 이 가정이 깨져서 그
    // 경계에서 한 틱씩 낡은 상태로 판단하는 버그가 있었다. 그래서 매 틱 NextSegmentId를 따라
    // 실제 연결 관계로 처리 순서를 다시 계산한다 (배열 순서에 의존하지 않음).
    public sealed class BeltSystem
    {
        private readonly Dictionary<int, BeltSegment> segmentsById = new Dictionary<int, BeltSegment>();
        private readonly List<BeltSegment> processingOrder = new List<BeltSegment>();
        private readonly HashSet<int> visited = new HashSet<int>();

        // 세그먼트가 새로 추가될 때마다 호출 (드문 이벤트라 매번 재구성해도 무방).
        public void Configure(List<BeltSegment> segments)
        {
            segmentsById.Clear();
            for (int i = 0; i < segments.Count; i++)
            {
                segmentsById[segments[i].Id] = segments[i];
            }
        }

        public void Tick(float deltaSeconds, List<BeltSegment> segments, List<ProcessorInstance> processors, GameDatabase database)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                LoadFromSource(segments[i], processors, database);
            }

            BuildDownstreamFirstOrder(segments);
            for (int i = 0; i < processingOrder.Count; i++)
            {
                AdvanceSegment(processingOrder[i], deltaSeconds, processors);
            }
        }

        // NextSegmentId를 따라가는 후위 순회(post-order DFS): 한 세그먼트를 결과 리스트에
        // 넣기 전에 그 다음(하류) 세그먼트부터 먼저 넣으므로, 리스트 앞쪽이 항상 더 하류다.
        private void BuildDownstreamFirstOrder(List<BeltSegment> segments)
        {
            processingOrder.Clear();
            visited.Clear();

            for (int i = 0; i < segments.Count; i++)
            {
                VisitDownstreamFirst(segments[i]);
            }
        }

        private void VisitDownstreamFirst(BeltSegment segment)
        {
            if (segment == null || !visited.Add(segment.Id)) return;

            if (segment.NextSegmentId.HasValue && segmentsById.TryGetValue(segment.NextSegmentId.Value, out var next))
            {
                VisitDownstreamFirst(next);
            }

            processingOrder.Add(segment);
        }

        private void LoadFromSource(BeltSegment segment, List<ProcessorInstance> processors, GameDatabase database)
        {
            bool headFree = segment.Items.Count == 0 || segment.Items[0].Position > segment.ItemSpacing;
            if (!headFree) return;

            if (!segment.SourceProcessorId.HasValue) return;
            var processor = processors[segment.SourceProcessorId.Value];

            if (processor.UniversalPorts)
            {
                LoadFromCore(segment, processor, processors, database);
                return;
            }

            var buffer = processor.OutputBuffer;

            // 이 라인이 이미 특정 자원으로 굳어졌으면 그것만 계속 내준다("벨트 하나당 한 종류").
            if (segment.LockedSourceResourceId.HasValue)
            {
                int lockedId = segment.LockedSourceResourceId.Value;
                if (buffer[lockedId] <= 0) return;
                buffer[lockedId]--;
                segment.Items.Insert(0, new BeltItem(lockedId, 0f));
                return;
            }

            for (int resourceId = 0; resourceId < buffer.Length; resourceId++)
            {
                if (buffer[resourceId] <= 0) continue;
                buffer[resourceId]--;
                segment.Items.Insert(0, new BeltItem(resourceId, 0f));
                segment.LockedSourceResourceId = resourceId;
                return;
            }
        }

        // 코어는 쌓아둔 걸 아무 벨트에나 무조건 흘려보내지 않는다 — 이 벨트 체인 끝에 실제로
        // 레시피를 지정받은 기계가 있고, 그 레시피가 필요로 하는 자원일 때만 내준다("먼저
        // 레시피를 지정해서 필요한 자원 정보를 전달받아야 준다"는 설계). 막다른 벨트나 아직
        // 레시피 미지정인 기계로는 아무것도 내주지 않는다.
        private void LoadFromCore(BeltSegment segment, ProcessorInstance core, List<ProcessorInstance> processors, GameDatabase database)
        {
            var target = FindTerminalTarget(segment, processors);
            if (target == null) return; // 막다른 벨트 -> 요청하는 대상이 없음

            if (target.UniversalPorts)
            {
                // 코어->코어(창고 재배치) 같은 특수 케이스는 레시피 개념이 없으니 있는 대로 내준다.
                DispenseAny(segment, core.InputBuffer);
                return;
            }

            if (target.RecipeId < 0) return; // 아직 레시피 미지정 -> 뭐가 필요한지 모르니 안 줌

            // 목적지 레시피가 잠금 당시와 달라졌으면(사용자가 기계 탭해서 레시피를 바꿈)
            // 예전 재료로 굳은 잠금은 더 이상 유효하지 않다 — 풀어서 새 레시피 기준으로
            // 다시 담당 자원을 정하게 한다. 안 그러면 벨트가 예전 재료만 계속 실어 날라서
            // 기계가 새 레시피로는 영구히 재료를 못 받는다.
            if (segment.LockedSourceResourceId.HasValue && segment.LockedForRecipeId != target.RecipeId)
            {
                segment.LockedSourceResourceId = null;
            }

            // 이 라인이 담당할 자원은 "지금 재고가 있는지"와 무관하게 딱 한 번만 정해진다
            // ("이 라인은 철판 담당, 저 라인은 구리 담당"). 예전엔 재고 여부로 담당을 정해서,
            // 담당 자원(철판)이 코어에 아직 없는 잠깐 사이에 다른 라인이 이미 맡은 자원(구리)을
            // 대신 실어 날랐는데 — 구리는 이미 목적지 버퍼가 꽉 찰 만큼 계속 들어오다 보니
            // 정작 필요한 철판이 뒤늦게 와도 벨트가 구리로 막혀서 못 지나가는 정체가 실제로
            // 발생했다. 그래서 담당은 재고와 무관하게 즉시 정하고, 담당 자원이 코어에 없으면
            // 다른 자원을 대신 나르지 않고 그냥 빈 채로 기다린다.
            if (!segment.LockedSourceResourceId.HasValue)
            {
                AssignLaneResource(segment, target, database.Recipes[target.RecipeId].Inputs, processors);
                segment.LockedForRecipeId = target.RecipeId;
            }

            if (!segment.LockedSourceResourceId.HasValue) return; // 레시피에 재료가 하나도 없는 등 방어적 처리

            int assignedId = segment.LockedSourceResourceId.Value;
            if (core.InputBuffer[assignedId] <= 0) return; // 담당 자원이 아직 코어에 없음 -> 대신 나르지 않고 대기
            core.InputBuffer[assignedId]--;
            segment.Items.Insert(0, new BeltItem(assignedId, 0f));
        }

        // 여러 라인이 코어에서 같은 목적지로 뻗어있을 때, 라인마다 서로 다른 재료를 담당하도록
        // "아직 아무도 안 맡은 재료"를 찾아 굳힌다(사용자가 실제로 겪은 버그 — 재고 기준으로
        // 정하면 다들 똑같은 순서로 시도해서 전부 하나로 몰렸다). 재료 종류보다 라인 수가 더
        // 많아서 전부 이미 다른 라인이 맡고 있으면, 남는 라인은 어쩔 수 없이 첫 재료를 중복으로
        // 맡는다(안 맡는 것보단 낫다).
        private void AssignLaneResource(BeltSegment segment, ProcessorInstance target, ResourceAmount[] inputs, List<ProcessorInstance> processors)
        {
            if (inputs.Length == 0) return;

            for (int i = 0; i < inputs.Length; i++)
            {
                int resourceId = inputs[i].ResourceId;
                if (IsClaimedByAnotherLane(segment, target, resourceId, processors)) continue;
                segment.LockedSourceResourceId = resourceId;
                return;
            }

            segment.LockedSourceResourceId = inputs[0].ResourceId;
        }

        // segmentsById 전체를 훑어서, "같은 목적지로 흘러가는 다른 라인"이 이미 이 자원으로
        // 굳어져 있는지 확인한다. 세그먼트 수가 적은 프로토타입 규모라 매번 O(N) 스캔이어도 무방.
        private bool IsClaimedByAnotherLane(BeltSegment self, ProcessorInstance target, int resourceId, List<ProcessorInstance> processors)
        {
            foreach (var other in segmentsById.Values)
            {
                if (other == self) continue;
                if (other.LockedSourceResourceId != resourceId) continue;
                if (FindTerminalTarget(other, processors) != target) continue;
                return true;
            }
            return false;
        }

        private static void DispenseAny(BeltSegment segment, int[] buffer)
        {
            for (int resourceId = 0; resourceId < buffer.Length; resourceId++)
            {
                if (buffer[resourceId] <= 0) continue;
                buffer[resourceId]--;
                segment.Items.Insert(0, new BeltItem(resourceId, 0f));
                return;
            }
        }

        // segment의 NextSegmentId를 따라가 최종 목적지(TargetProcessorId가 있는 세그먼트)를
        // 찾는다. 도중에 목적지 없이 끊기면(막다른 벨트) null. 세그먼트 총 개수로 상한을 둬서
        // (있어서는 안 되지만) 순환 연결에도 무한루프에 빠지지 않게 방어한다.
        private ProcessorInstance FindTerminalTarget(BeltSegment segment, List<ProcessorInstance> processors)
        {
            var current = segment;
            int guard = segmentsById.Count + 1;
            while (current != null && guard-- > 0)
            {
                if (current.TargetProcessorId.HasValue) return processors[current.TargetProcessorId.Value];
                if (!current.NextSegmentId.HasValue) return null;
                segmentsById.TryGetValue(current.NextSegmentId.Value, out current);
            }
            return null;
        }

        private void AdvanceSegment(BeltSegment segment, float deltaSeconds, List<ProcessorInstance> processors)
        {
            var items = segment.Items;
            // 프론트 아이템은 이번 세그먼트 안에서 같은 세그먼트의 다른 아이템에 막히지 않는 한
            // 제약이 없어야 한다 — segment.Length로 캡을 걸면 경계를 넘는 이동분(overflow)이
            // 항상 0으로 잘려서 다음 세그먼트 진입 시 매번 position 0부터 다시 시작하는
            // "연결부위 멈칫" 현상이 생긴다. 경계 판정은 아래 reachedEnd/overflow에서 따로 한다.
            float nextMaxPos = float.PositiveInfinity;

            BeltSegment nextSegment = null;
            if (segment.NextSegmentId.HasValue)
            {
                segmentsById.TryGetValue(segment.NextSegmentId.Value, out nextSegment);
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                float desired = item.Position + segment.SpeedUnitsPerSecond * deltaSeconds;
                float maxPos = Math.Min(desired, nextMaxPos);
                bool reachedEnd = maxPos >= segment.Length - 0.0001f;

                if (reachedEnd)
                {
                    float overflow = Math.Max(0f, maxPos - segment.Length);
                    if (TryHandOff(segment, nextSegment, item, overflow, processors))
                    {
                        items.RemoveAt(i);
                        continue; // 다음(새) 프론트 아이템은 segment.Length까지 자유롭게 전진 가능
                    }
                    maxPos = segment.Length; // 넘어갈 자리가 없으면 끝에서 대기 (역압 전파)
                }

                item.Position = maxPos;
                items[i] = item;
                nextMaxPos = item.Position - segment.ItemSpacing;
            }
        }

        private static bool TryHandOff(BeltSegment segment, BeltSegment nextSegment, BeltItem item, float overflow, List<ProcessorInstance> processors)
        {
            if (nextSegment != null)
            {
                bool nextHeadFree = nextSegment.Items.Count == 0 || nextSegment.Items[0].Position > nextSegment.ItemSpacing;
                if (!nextHeadFree) return false;
                // overflow가 다음 세그먼트 길이보다 클 정도로 틱당 이동량이 크면(고속/저틱레이트)
                // 그 나머지는 다음 틱에서 이어서 처리되도록 다음 세그먼트 길이로 방어적으로 clamp.
                float startPos = Math.Min(overflow, nextSegment.Length);
                nextSegment.Items.Insert(0, new BeltItem(item.ResourceId, startPos));
                return true;
            }

            if (segment.TargetProcessorId.HasValue)
            {
                var processor = processors[segment.TargetProcessorId.Value];
                return processor.TryAcceptInput(item.ResourceId, 1);
            }

            return false; // 막다른 벨트: 아이템을 잃지 않고 끝에서 대기시킨다.
        }
    }
}
