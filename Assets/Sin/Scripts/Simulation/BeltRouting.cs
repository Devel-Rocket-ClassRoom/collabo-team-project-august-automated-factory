using System.Collections.Generic;

namespace Factory.Simulation
{
    // 벨트 체인을 따라가 "최종 목적 기계"를 찾는 공용 로직. 두 시스템이 같은 규칙을 쓰도록 한 곳에 둔다:
    //  - BeltSystem: 코어가 이 벨트에 무슨 자원을 실을지 판단할 때
    //  - RoutingSystem: 분류기가 어느 출력 갈래로 아이템을 보낼지 판단할 때
    //
    // 핵심 규칙: "요청하지 않은 기계에는 보내지 않는다." 요청한다 = 레시피가 지정돼 있다(무엇이
    // 필요한지 앎) 또는 저장고(UniversalPorts)다. 레시피 미지정 기계는 벨트가 물건을 밀어넣어도
    // 입력 버퍼에 죽은 재고로 쌓이기만 하므로, 애초에 목적지로 치지 않는다.
    public static class BeltRouting
    {
        public static bool IsRequestingConsumer(ProcessorInstance p)
            => p != null && (p.RecipeId >= 0 || p.UniversalPorts);

        // segment에서 NextSegmentId를 따라간 최종 목적 기계. 라우팅 노드(분류기/합류기)는 종착이
        // 아니라 통과 지점이라, 그 출력 갈래 중 "요청하는 기계"로 이어지는 갈래를 우선 따라간다
        // (갈래는 id 오름차순으로 결정적으로 선택, 그런 갈래가 없으면 최저 id 갈래로 폴백).
        // 막다른 벨트면 null.
        public static ProcessorInstance ResolveTerminal(
            BeltSegment segment, List<ProcessorInstance> processors, List<BeltSegment> segments)
        {
            return Resolve(segment, processors, segments, segments.Count + 1);
        }

        private static ProcessorInstance Resolve(
            BeltSegment segment, List<ProcessorInstance> processors, List<BeltSegment> segments, int guard)
        {
            var current = segment;
            while (current != null && guard-- > 0)
            {
                if (current.TargetProcessorId.HasValue)
                {
                    var proc = processors[current.TargetProcessorId.Value];
                    if (proc == null || proc.RoutingRole == RoutingRole.None) return proc;

                    ProcessorInstance wanted = null; int wantedBranchId = int.MaxValue;
                    ProcessorInstance fallback = null; int fallbackBranchId = int.MaxValue;
                    for (int i = 0; i < segments.Count; i++)
                    {
                        var branch = segments[i];
                        if (branch == null || branch.SourceProcessorId != current.TargetProcessorId.Value) continue;
                        var resolved = Resolve(branch, processors, segments, guard);
                        if (resolved == null) continue;
                        if (IsRequestingConsumer(resolved) && branch.Id < wantedBranchId)
                        {
                            wanted = resolved; wantedBranchId = branch.Id;
                        }
                        else if (branch.Id < fallbackBranchId)
                        {
                            fallback = resolved; fallbackBranchId = branch.Id;
                        }
                    }
                    return wanted ?? fallback;
                }
                if (!current.NextSegmentId.HasValue) return null;
                current = FindById(segments, current.NextSegmentId.Value);
            }
            return null;
        }

        private static BeltSegment FindById(List<BeltSegment> segments, int id)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i] != null && segments[i].Id == id) return segments[i];
            }
            return null;
        }
    }
}
