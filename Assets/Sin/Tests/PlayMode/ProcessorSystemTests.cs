using System.Collections.Generic;
using Bae.Data;
using Factory.Data;
using Factory.Simulation;
using NUnit.Framework;

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
        var ore = new ItemData { itemID = "IronOre" };
        var plate = new ItemData { itemID = "IronPlate" };
        var machine = new MachineData { machineID = "Smelter" };
        var recipe = new RecipeData
        {
            recipeID = "SmeltIron",
            machineID = "Smelter",
            timeToCraft = 1f,
            inputItems = new List<string> { "IronOre", "IronOre" },
            outputItems = new List<string> { "IronPlate" },
        };

        var db = GameDatabase.Build(new[] { ore, plate }, new[] { machine }, new[] { recipe });
        oreId = db.GetResourceId("IronOre");
        plateId = db.GetResourceId("IronPlate");
        recipeId = db.GetRecipeId("SmeltIron");

        return db;
    }
}
