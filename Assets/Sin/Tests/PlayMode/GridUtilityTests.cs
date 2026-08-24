using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

public class GridUtilityTests
{
    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    [TestCase(0, 1)]
    [TestCase(0, -1)]
    public void GetPortCells_Footprint1x1_MatchesSingleCellPlusMinusFacing(int fx, int fy)
    {
        // 회귀 고정: footprint가 1x1이면 GetPortCells는 기존(제련로 등) "cell±Facing" 단일
        // 셀 판정과 정확히 같은 결과를 내야 한다 — 안 그러면 지금까지 통과하던 제련로 관련
        // 테스트들이 전부 깨진다.
        var anchor = new Vector2Int(5, 3);
        var facing = new Vector2Int(fx, fy);

        var outputs = GridUtility.GetPortCells(anchor, Vector2Int.one, facing, isOutputSide: true);
        var inputs = GridUtility.GetPortCells(anchor, Vector2Int.one, facing, isOutputSide: false);

        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual(anchor + facing, outputs[0]);

        Assert.AreEqual(1, inputs.Count);
        Assert.AreEqual(anchor - facing, inputs[0]);
    }

    [Test]
    public void GetPortCells_Footprint2x2_FacingEast_ReturnsTwoCellsPerSide()
    {
        var anchor = new Vector2Int(0, 0);
        var footprint = new Vector2Int(2, 2);
        var facing = new Vector2Int(1, 0);

        var outputs = GridUtility.GetPortCells(anchor, footprint, facing, isOutputSide: true);
        var inputs = GridUtility.GetPortCells(anchor, footprint, facing, isOutputSide: false);

        CollectionAssert.AreEquivalent(new[] { new Vector2Int(2, 0), new Vector2Int(2, 1) }, outputs);
        CollectionAssert.AreEquivalent(new[] { new Vector2Int(-1, 0), new Vector2Int(-1, 1) }, inputs);
    }

    [Test]
    public void GetPortCells_Footprint2x2_FacingNorth_ReturnsTwoCellsPerSide()
    {
        var anchor = new Vector2Int(0, 0);
        var footprint = new Vector2Int(2, 2);
        var facing = new Vector2Int(0, 1);

        var outputs = GridUtility.GetPortCells(anchor, footprint, facing, isOutputSide: true);
        var inputs = GridUtility.GetPortCells(anchor, footprint, facing, isOutputSide: false);

        CollectionAssert.AreEquivalent(new[] { new Vector2Int(0, 2), new Vector2Int(1, 2) }, outputs);
        CollectionAssert.AreEquivalent(new[] { new Vector2Int(0, -1), new Vector2Int(1, -1) }, inputs);
    }
}
