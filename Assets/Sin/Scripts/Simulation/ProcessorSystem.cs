using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 제련로/성형기/합성기 공용 처리 시스템. RecipeRuntime 데이터를 그대로 읽어 소비/생산할 뿐
    // 레시피 id로 분기하지 않는다 — 새 레시피를 데이터로 추가해도 이 코드는 그대로 통과한다.
    public sealed class ProcessorSystem
    {
        public void Tick(float deltaSeconds, GameDatabase database, List<ProcessorInstance> processors)
        {
            for (int i = 0; i < processors.Count; i++)
            {
                var processor = processors[i];
                if (processor == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveProcessor 참고).

                if (!processor.IsProcessing)
                {
                    if (processor.RecipeId < 0) continue;
                    if (!TryConsumeInputs(processor, database.Recipes[processor.RecipeId])) continue;
                    processor.ActiveRecipeId = processor.RecipeId;
                    processor.IsProcessing = true;
                    processor.Progress = 0f;
                }

                processor.Progress += deltaSeconds * processor.SpeedMultiplier;

                // while로 돌아 사이클 경계를 넘는 초과분(overshoot)을 다음 사이클로 이어간다
                // (MinerSystem.Tick과 동일한 이유) — 단, 매 사이클은 그 사이클이 실제로 소비한
                // ActiveRecipeId 기준으로 산출해야 한다. RecipeId가 처리 도중 바뀌었어도 이번
                // 사이클은 ActiveRecipeId로 끝맺고, 다음 사이클을 시작할 때 재료가 있으면
                // (바뀌었을 수도 있는) RecipeId로 새로 시작한다.
                while (processor.IsProcessing)
                {
                    var activeRecipe = database.Recipes[processor.ActiveRecipeId];
                    if (processor.Progress < activeRecipe.ProcessSeconds) break;

                    processor.Progress -= activeRecipe.ProcessSeconds;
                    ProduceOutputs(processor, activeRecipe);
                    processor.IsProcessing = false;

                    if (processor.RecipeId < 0) break;
                    if (!TryConsumeInputs(processor, database.Recipes[processor.RecipeId])) break;
                    processor.ActiveRecipeId = processor.RecipeId;
                    processor.IsProcessing = true;
                }
            }
        }

        private static bool TryConsumeInputs(ProcessorInstance processor, in RecipeRuntime recipe)
        {
            var inputs = recipe.Inputs;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (processor.InputBuffer[inputs[i].ResourceId] < inputs[i].Amount) return false;
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                processor.InputBuffer[inputs[i].ResourceId] -= inputs[i].Amount;
            }
            return true;
        }

        private static void ProduceOutputs(ProcessorInstance processor, in RecipeRuntime recipe)
        {
            var outputs = recipe.Outputs;
            for (int i = 0; i < outputs.Length; i++)
            {
                int resourceId = outputs[i].ResourceId;
                int amount = outputs[i].Amount;
                processor.OutputBuffer[resourceId] = System.Math.Min(
                    processor.OutputBuffer[resourceId] + amount,
                    processor.Capacity);
            }
        }
    }
}
