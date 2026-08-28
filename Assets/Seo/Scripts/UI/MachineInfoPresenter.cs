using System.Collections.Generic;
using Factory.Buildings;
using Factory.Data;
using Factory.Simulation;
using UnityEngine;

namespace Seo.UI
{
    // SimulationWorld를 읽어 화면에 필요한 문자열/수치로 변환하는 어댑터.
    // 패널은 이 클래스를 통해서만 데이터를 받아 시뮬레이션 규칙과 분리된다.
    public static class MachineInfoPresenter
    {
        public static bool TryBuild(
            SimulationDriver driver,
            MachineInstanceKind kind,
            int instanceIndex,
            out MachineInfoViewData data)
        {
            data = default;
            if (driver == null || driver.World == null) return false;

            return kind == MachineInstanceKind.Miner
                ? TryBuildMiner(driver.World, instanceIndex, out data)
                : TryBuildProcessor(driver.World, instanceIndex, out data);
        }

        public static string GetMachineDisplayName(SimulationWorld world, int machineId)
        {
            if (world == null || machineId < 0 || machineId >= world.Database.Machines.Count) return "기계";
            return GetMachineDisplayName(world.Database.Machines[machineId].Key);
        }

        // 공동 GameDatabase에 UI 표시용 필드를 추가하지 않고 Seo UI 안에서만 이름을 관리한다.
        // 추후 아이콘/다국어 테이블이 정해지면 이 메서드만 교체하면 된다.
        public static string GetMachineDisplayName(string machineKey)
        {
            switch (machineKey)
            {
                case "Miner": return "채굴기";
                case "Smelter": return "제련로";
                case "Former": return "성형기";
                case "Synthesizer": return "합성기";
                case "Core": return "코어";
                default: return string.IsNullOrEmpty(machineKey) ? "기계" : machineKey;
            }
        }

        public static Color GetMachineColor(string machineKey)
        {
            switch (machineKey)
            {
                case "Miner": return new Color(1f, 0.68f, 0.08f);
                case "Smelter": return new Color(0.85f, 0.2f, 0.14f);
                case "Former": return new Color(0.25f, 0.72f, 0.78f);
                case "Synthesizer": return new Color(0.62f, 0.35f, 0.82f);
                case "Core": return new Color(0.32f, 0.58f, 0.78f);
                default: return new Color(0.35f, 0.75f, 0.45f);
            }
        }

        private static bool TryBuildMiner(SimulationWorld world, int index, out MachineInfoViewData data)
        {
            data = default;
            if (index < 0 || index >= world.Miners.Count) return false;
            var miner = world.Miners[index];
            if (miner == null) return false;

            string machineKey = world.Database.Machines[miner.MachineId].Key;
            string outputName = ResourceName(world.Database, miner.OutputResourceId);
            float progress01 = miner.MineIntervalSeconds > 0f ? miner.Progress / miner.MineIntervalSeconds : 0f;
            string status = miner.BufferedOutput > 0 ? "코어 전송 대기" : "채굴 중";

            data = new MachineInfoViewData(
                GetMachineDisplayName(world, miner.MachineId),
                status,
                "광물 노드 자동 채굴",
                "입력\n광물 노드",
                $"출력\n{outputName} · 대기 {miner.BufferedOutput}개",
                $"채굴 진행 {Mathf.RoundToInt(Mathf.Clamp01(progress01) * 100f)}%",
                "벨트 포트 없음 · 생산 자원은 코어로 자동 전송",
                progress01,
                false,
                GetMachineColor(machineKey));
            return true;
        }

        private static bool TryBuildProcessor(SimulationWorld world, int index, out MachineInfoViewData data)
        {
            data = default;
            if (index < 0 || index >= world.Processors.Count) return false;
            var processor = world.Processors[index];
            if (processor == null) return false;

            var db = world.Database;
            var machine = db.Machines[processor.MachineId];
            string title = GetMachineDisplayName(world, processor.MachineId);
            Color accent = GetMachineColor(machine.Key);

            if (processor.UniversalPorts)
            {
                string stored = FormatNonZeroBuffer(db, processor.InputBuffer);
                data = new MachineInfoViewData(
                    title,
                    "중앙 저장소",
                    "레시피 없음",
                    "보유 자원\n" + stored,
                    "출력\n연결된 기계가 요청한 자원을 자동 공급",
                    "저장 용량 " + Sum(processor.InputBuffer) + " / " + processor.Capacity,
                    "상·하·좌·우 4방향 공용 입출력 포트",
                    (float)Sum(processor.InputBuffer) / Mathf.Max(1, processor.Capacity),
                    false,
                    accent);
                return true;
            }

            if (processor.RecipeId < 0 || processor.RecipeId >= db.Recipes.Count)
            {
                data = new MachineInfoViewData(
                    title,
                    "레시피 대기",
                    "레시피를 선택하세요",
                    "입력\n-",
                    "출력\n-",
                    "생산 진행 0%",
                    FormatPorts(processor.Facing),
                    0f,
                    true,
                    accent);
                return true;
            }

            var recipe = db.Recipes[processor.RecipeId];
            int activeRecipeId = processor.IsProcessing ? processor.ActiveRecipeId : processor.RecipeId;
            float processSeconds = activeRecipeId >= 0 && activeRecipeId < db.Recipes.Count
                ? db.Recipes[activeRecipeId].ProcessSeconds
                : recipe.ProcessSeconds;
            float progress01 = processor.IsProcessing && processSeconds > 0f ? processor.Progress / processSeconds : 0f;
            string status = ResolveProcessorStatus(processor, recipe);
            string recipeName = recipe.Outputs.Length > 0
                ? ResourceName(db, recipe.Outputs[0].ResourceId) + " 제작"
                : recipe.Key;

            data = new MachineInfoViewData(
                title,
                status,
                recipeName,
                "입력\n" + FormatRequirements(db, recipe.Inputs, processor.InputBuffer),
                "출력\n" + FormatRequirements(db, recipe.Outputs, processor.OutputBuffer, false),
                $"생산 진행 {Mathf.RoundToInt(Mathf.Clamp01(progress01) * 100f)}% · {recipe.ProcessSeconds:0.#}초",
                FormatPorts(processor.Facing),
                progress01,
                true,
                accent);
            return true;
        }

        private static string ResolveProcessorStatus(ProcessorInstance processor, in RecipeRuntime recipe)
        {
            if (processor.IsProcessing) return "가동 중";

            for (int i = 0; i < recipe.Outputs.Length; i++)
            {
                if (processor.OutputBuffer[recipe.Outputs[i].ResourceId] >= processor.Capacity) return "출력 막힘";
            }

            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                var input = recipe.Inputs[i];
                if (processor.InputBuffer[input.ResourceId] < input.Amount) return "입력 재료 대기";
            }

            return "가동 준비";
        }

        private static string FormatRequirements(
            GameDatabase db,
            ResourceAmount[] amounts,
            int[] buffer,
            bool showRequired = true)
        {
            if (amounts == null || amounts.Length == 0) return "-";
            var parts = new List<string>(amounts.Length);
            for (int i = 0; i < amounts.Length; i++)
            {
                var amount = amounts[i];
                string name = ResourceName(db, amount.ResourceId);
                parts.Add(showRequired
                    ? $"{name} {buffer[amount.ResourceId]} / {amount.Amount}"
                    : $"{name} {buffer[amount.ResourceId]}개");
            }
            return string.Join("\n", parts);
        }

        private static string FormatNonZeroBuffer(GameDatabase db, int[] buffer)
        {
            var parts = new List<string>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] > 0) parts.Add($"{ResourceName(db, i)} {buffer[i]}개");
            }
            return parts.Count == 0 ? "비어 있음" : string.Join(" · ", parts);
        }

        private static string FormatPorts(Vector2Int facing)
        {
            if (facing == Vector2Int.right) return "입력: 왼쪽  ·  출력: 오른쪽";
            if (facing == Vector2Int.left) return "입력: 오른쪽  ·  출력: 왼쪽";
            if (facing == Vector2Int.up) return "입력: 아래쪽  ·  출력: 위쪽";
            if (facing == Vector2Int.down) return "입력: 위쪽  ·  출력: 아래쪽";
            return "입출력 방향 미지정";
        }

        private static string ResourceName(GameDatabase db, int resourceId)
        {
            if (resourceId < 0 || resourceId >= db.Resources.Count) return "알 수 없음";
            return db.Resources[resourceId].DisplayName;
        }

        private static int Sum(int[] values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; i++) total += values[i];
            return total;
        }
    }
}
