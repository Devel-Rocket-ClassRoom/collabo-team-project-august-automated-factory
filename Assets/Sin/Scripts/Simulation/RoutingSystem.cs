using System.Collections.Generic;

namespace Factory.Simulation
{
    // 분류기/합류기(레시피 없는 벨트 라우팅 노드) 전용 처리. 벨트가 InputBuffer로 배달해준
    // 아이템을(BeltSystem.TryHandOff) 출력 벨트에 직접 얹는다. 라우팅 노드가 소스인 벨트는
    // BeltSystem.LoadFromSource가 건너뛰므로(OutputBuffer 일반 배출 안 함), 출력 벨트 적재는
    // 전적으로 여기서만 한다.
    //
    // 연결 관계는 세그먼트를 스캔해서 파악한다(SourceProcessorId == 노드 인덱스 → 출력 벨트).
    // 입력 벨트는 TargetProcessorId로 노드의 InputBuffer에 이미 배달되므로 따로 안 모은다.
    // 프로토타입 규모라 매 틱 O(세그먼트) 스캔이어도 무방하다(BeltSystem.IsClaimedByAnotherLane와 같은 수준).
    public sealed class RoutingSystem
    {
        // 매 틱 재사용하는 버퍼(GC Alloc 0 유지).
        private readonly List<BeltSegment> outputBelts = new List<BeltSegment>();

        public void Tick(List<ProcessorInstance> processors, List<BeltSegment> segments)
        {
            for (int i = 0; i < processors.Count; i++)
            {
                var node = processors[i];
                if (node == null) continue;

                if (node.RoutingRole == RoutingRole.Splitter) TickSplitter(node, i, segments);
                else if (node.RoutingRole == RoutingRole.Merger) TickMerger(node, i, segments);
            }
        }

        // 입력 벨트가 InputBuffer로 배달해준 것을, 연결된 출력 벨트에 라운드로빈으로 하나씩
        // 얹는다. 커서 벨트의 입구가 막혀 있으면 건너뛰고 다음 빈 벨트로 보낸다(비율은 잠깐
        // 깨지지만 전체가 멈추지 않음 — 설계 결정).
        private void TickSplitter(ProcessorInstance splitter, int splitterIndex, List<BeltSegment> segments)
        {
            CollectOutputBelts(segments, splitterIndex);
            int n = outputBelts.Count;
            if (n == 0) return;

            int start = ((splitter.RoutingCursor % n) + n) % n; // 벨트 개수가 바뀌었을 수 있어 정규화
            for (int k = 0; k < n; k++)
            {
                int idx = (start + k) % n;
                var belt = outputBelts[idx];
                if (!HeadFree(belt)) continue;

                int resourceId = FirstNonEmpty(splitter.InputBuffer);
                if (resourceId < 0) return; // 나눠 보낼 게 없음

                belt.Items.Insert(0, new BeltItem(resourceId, 0f));
                splitter.InputBuffer[resourceId]--;
                splitter.RoutingCursor = (idx + 1) % n; // 다음 틱은 이 벨트 다음부터
                return; // 틱당 1개 — 벨트 입구 간격이 알아서 속도를 제한한다
            }
        }

        // 여러 입력 벨트가 InputBuffer로 배달해준 것을, 단일 출력 벨트에 얹는다. 자원 종류가
        // 섞여 있으면 종류를 번갈아 내보낸다(RoutingCursor = 마지막으로 내보낸 자원 id) — 한
        // 종류만 몰아 내보내면 다운스트림 2입력 기계가 한쪽 재료만 받아 굶는다.
        private void TickMerger(ProcessorInstance merger, int mergerIndex, List<BeltSegment> segments)
        {
            CollectOutputBelts(segments, mergerIndex);
            if (outputBelts.Count == 0) return;

            var output = outputBelts[0]; // 합류기는 출력 1개
            if (!HeadFree(output)) return;

            int resourceId = NextNonEmptyRoundRobin(merger.InputBuffer, merger.RoutingCursor);
            if (resourceId < 0) return;

            output.Items.Insert(0, new BeltItem(resourceId, 0f));
            merger.InputBuffer[resourceId]--;
            merger.RoutingCursor = resourceId;
        }

        private void CollectOutputBelts(List<BeltSegment> segments, int nodeIndex)
        {
            outputBelts.Clear();
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                if (s == null || s.SourceProcessorId != nodeIndex) continue;

                // id 오름차순 삽입 정렬 — 실행마다 동일한 순서가 되도록(결정적 라운드로빈).
                // 목록이 최대 3개라 O(n^2)도 무의미하고, List.Sort(Comparison) 델리게이트
                // 할당을 피할 수 있다.
                int at = outputBelts.Count;
                while (at > 0 && outputBelts[at - 1].Id > s.Id) at--;
                outputBelts.Insert(at, s);
            }
        }

        private static bool HeadFree(BeltSegment belt)
        {
            return belt.Items.Count == 0 || belt.Items[0].Position > belt.ItemSpacing;
        }

        private static int FirstNonEmpty(int[] buffer)
        {
            for (int r = 0; r < buffer.Length; r++)
            {
                if (buffer[r] > 0) return r;
            }
            return -1;
        }

        // afterResourceId 다음 id부터 한 바퀴 돌며 재고가 있는 첫 자원. 없으면 -1.
        private static int NextNonEmptyRoundRobin(int[] buffer, int afterResourceId)
        {
            int len = buffer.Length;
            for (int step = 1; step <= len; step++)
            {
                int r = (((afterResourceId + step) % len) + len) % len;
                if (buffer[r] > 0) return r;
            }
            return -1;
        }
    }
}
