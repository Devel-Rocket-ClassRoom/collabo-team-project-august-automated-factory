using System.Collections.Generic;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;
using UnityEngine;

public class ProcessorSystemTests
{
    [Test]
    public void Tick_ConsumesInputsImmediately_AndProducesOutputsAfterProcessTime()
    {
        var db = BuildFixtureDatabase(out int oreId, out int plateId, out int recipeId);
        var processor = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId };
        processor.InputBuffer[oreId] = 2;

        var system = new ProcessorSystem();

        var processors = new List<ProcessorInstance> { processor };

        system.Tick(0.5f, db, processors);
        Assert.IsTrue(processor.IsProcessing);
        Assert.AreEqual(0, processor.InputBuffer[oreId], "투입은 처리 시작 시점에 즉시 소모되어야 함");
        Assert.AreEqual(0, processor.OutputBuffer[plateId]);

        system.Tick(0.6f, db, processors); // 누적 1.1s >= processSeconds(1s)

        Assert.IsFalse(processor.IsProcessing);
        Assert.AreEqual(1, processor.OutputBuffer[plateId]);
    }

    [Test]
    public void Tick_DoesNotStart_WhenInputsInsufficient()
    {
        var db = BuildFixtureDatabase(out int oreId, out _, out int recipeId);
        var processor = new ProcessorInstance(db.ResourceCount) { RecipeId = recipeId };
        processor.InputBuffer[oreId] = 1; // 레시피는 2개 필요

        var system = new ProcessorSystem();
        system.Tick(1f, db, new List<ProcessorInstance> { processor });

        Assert.IsFalse(processor.IsProcessing);
        Assert.AreEqual(1, processor.InputBuffer[oreId]);
    }

    private static GameDatabase BuildFixtureDatabase(out int oreId, out int plateId, out int recipeId)
    {
        var ore = ScriptableObject.CreateInstance<ResourceDef>();
        ore.resourceId = "IronOre";

        var plate = ScriptableObject.CreateInstance<ResourceDef>();
        plate.resourceId = "IronPlate";

        var machine = ScriptableObject.CreateInstance<MachineDef>();
        machine.machineId = "Smelter";
        machine.category = MachineCategory.Smelter;

        var recipe = ScriptableObject.CreateInstance<RecipeDef>();
        recipe.recipeId = "SmeltIron";
        recipe.inputs = new[] { new RecipeIngredient { resource = ore, amount = 2 } };
        recipe.outputs = new[] { new RecipeIngredient { resource = plate, amount = 1 } };
        recipe.processSeconds = 1f;
        recipe.requiredCategory = MachineCategory.Smelter;

        var db = GameDatabase.Build(new[] { ore, plate }, new[] { recipe }, new[] { machine });
        oreId = db.GetResourceId("IronOre");
        plateId = db.GetResourceId("IronPlate");
        recipeId = db.GetRecipeId("SmeltIron");

        Object.DestroyImmediate(ore);
        Object.DestroyImmediate(plate);
        Object.DestroyImmediate(machine);
        Object.DestroyImmediate(recipe);

        return db;
    }
}
