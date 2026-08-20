using System;
using System.Collections.Generic;

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

        public void Tick(float deltaSeconds, List<BeltSegment> segments, List<MinerInstance> miners, List<ProcessorInstance> processors)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                LoadFromSource(segments[i], miners, processors);
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

        private static void LoadFromSource(BeltSegment segment, List<MinerInstance> miners, List<ProcessorInstance> processors)
        {
            bool headFree = segment.Items.Count == 0 || segment.Items[0].Position > segment.ItemSpacing;
            if (!headFree) return;

            if (segment.SourceMinerId.HasValue)
            {
                var miner = miners[segment.SourceMinerId.Value];
                if (miner.BufferedOutput > 0)
                {
                    miner.BufferedOutput--;
                    segment.Items.Insert(0, new BeltItem(miner.OutputResourceId, 0f));
                }
                return;
            }

            if (segment.SourceProcessorId.HasValue)
            {
                var processor = processors[segment.SourceProcessorId.Value];
                for (int resourceId = 0; resourceId < processor.OutputBuffer.Length; resourceId++)
                {
                    if (processor.OutputBuffer[resourceId] <= 0) continue;
                    processor.OutputBuffer[resourceId]--;
                    segment.Items.Insert(0, new BeltItem(resourceId, 0f));
                    return;
                }
            }
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
