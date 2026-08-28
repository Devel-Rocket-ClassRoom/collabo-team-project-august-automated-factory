using System.Reflection;
using Factory.Building;
using UnityEngine;

namespace Seo.UI
{
    // 공동 MachineGhostTool을 수정하지 않고 Seo UI가 고스트 표시 상태만 읽는 어댑터.
    internal static class MachineGhostAdapter
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo MachineIdField = typeof(MachineGhostTool).GetField("selectedMachineId", PrivateInstance);
        private static readonly FieldInfo GhostField = typeof(MachineGhostTool).GetField("ghost", PrivateInstance);
        private static readonly FieldInfo FacingField = typeof(MachineGhostTool).GetField("currentFacing", PrivateInstance);

        public static bool TryRead(MachineGhostTool tool, out GhostSelection selection)
        {
            selection = default;
            if (tool == null || MachineIdField == null || GhostField == null || FacingField == null) return false;

            string machineId = MachineIdField.GetValue(tool) as string;
            var ghost = GhostField.GetValue(tool) as GameObject;
            if (string.IsNullOrEmpty(machineId) || ghost == null || !ghost.activeInHierarchy) return false;

            selection = new GhostSelection(machineId, ghost, (Vector2Int)FacingField.GetValue(tool));
            return true;
        }
    }

    internal readonly struct GhostSelection
    {
        public readonly string MachineId;
        public readonly GameObject Ghost;
        public readonly Vector2Int Facing;

        public GhostSelection(string machineId, GameObject ghost, Vector2Int facing)
        {
            MachineId = machineId;
            Ghost = ghost;
            Facing = facing;
        }
    }
}
