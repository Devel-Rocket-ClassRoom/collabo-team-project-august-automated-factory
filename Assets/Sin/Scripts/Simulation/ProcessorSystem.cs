using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 제련로/조립기 공용 처리 시스템. RecipeRuntime 데이터를 그대로 읽어 소비/생산할 뿐
    // 레시피 id로 분기하지 않는다 — 새 레시피를 데이터로 추가해도 이 코드는 그대로 통과한다.
    public sealed class ProcessorSystem
    {
        public void Tick(float deltaSeconds, GameDatabase database, List<ProcessorInstance> processors)
        {
            for (int i = 0; i < processors.Count; i++)
            {
                var processor = processors[i];
                if (processor.RecipeId < 0) continue;

                var recipe = database.Recipes[processor.RecipeId];

                if (!processor.IsProcessing)
                {
                    if (!TryConsumeInputs(processor, recipe)) continue;
                    processor.IsProcessing = true;
                    processor.Progress = 0f;
                }

                processor.Progress += deltaSeconds * processor.SpeedMultiplier;
                if (processor.Progress >= recipe.ProcessSeconds)
                {
                    ProduceOutputs(processor, recipe);
                    processor.IsProcessing = false;
                    processor.Progress = 0f;
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
