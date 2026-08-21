using System.Collections.Generic;

namespace Factory.Simulation
{
    // 벨트 1칸 단위. 다음 칸은 직접 참조가 아니라 id로 연결해서, 이후 분기/합류(분배기)를
    // 도입할 때 세그먼트 하나가 여러 후보 중 목적지를 선택하는 구조로 확장할 수 있게 한다.
    public sealed class BeltSegment
    {
        public int Id;
        public int? NextSegmentId;
        public float Length = 1f;
        public float SpeedUnitsPerSecond = SimulationConstants.DefaultBeltSpeed;
        public float ItemSpacing = SimulationConstants.DefaultItemSpacing;

        // 체인의 첫 세그먼트에만 하나가 설정됨: 기계 산출물을 이 세그먼트로 실어 나른다.
        // 채굴기는 원격 전송(코어로 직배송)이라 벨트 소스가 될 수 없다 — Processor만 있음.
        public int? SourceProcessorId;

        // 체인의 마지막 세그먼트(NextSegmentId == null)에만 설정됨: 도착한 아이템을 받는 기계.
        public int? TargetProcessorId;

        // Position 오름차순 정렬 유지 (Items[0] = 세그먼트 시작에 가장 가까운 아이템).
        public List<BeltItem> Items = new List<BeltItem>();
    }
}
