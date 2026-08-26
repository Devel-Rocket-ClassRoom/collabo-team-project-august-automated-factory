using System.Collections.Generic;
using Factory.Building;
using NUnit.Framework;
using UnityEngine;

public class BeltPathBuilderTests
{
    [Test]
    public void Extend_AdjacentCells_AppendsDirectly()
    {
        var path = new List<Vector2Int>();
        BeltPathBuilder.Extend(path, new Vector2Int(0, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 0));

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) },
            path);
    }

    [Test]
    public void Extend_DiagonalJump_InsertsLShapedCorner()
    {
        var path = new List<Vector2Int>();
        BeltPathBuilder.Extend(path, new Vector2Int(0, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 1)); // 손가락이 빠르게 움직여 대각선으로 튐

        // 우세 축(x, delta=2)을 먼저 채우고 나머지(y, delta=1)를 이어붙여야 함
        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(2, 1) },
            path);
    }

    [Test]
    public void Extend_BacktrackToEarlierCell_TrimsPath()
    {
        var path = new List<Vector2Int>();
        BeltPathBuilder.Extend(path, new Vector2Int(0, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(2, 0));
        BeltPathBuilder.Extend(path, new Vector2Int(1, 0)); // 왔던 길을 되짚음 -> 취소 제스처

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) },
            path);
    }

    [Test]
    public void BuildOrthogonalPath_FromRawCellList_MatchesIncrementalExtend()
    {
        var raw = new List<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 2) };
        var path = BeltPathBuilder.BuildOrthogonalPath(raw);

        CollectionAssert.AreEqual(
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(1, 2) },
            path);
    }
}
