using Factory.Data;
using NUnit.Framework;
using UnityEngine;

public class GameDatabaseTests
{
    [Test]
    public void Build_AssignsStableIdsAndResolvesRecipeReferences()
    {
        var ore = ScriptableObject.CreateInstance<ResourceDef>();
        ore.resourceId = "IronOre";
        ore.displayName = "Iron Ore";

        var plate = ScriptableObject.CreateInstance<ResourceDef>();
        plate.resourceId = "IronPlate";
        plate.displayName = "Iron Plate";

        var machine = ScriptableObject.CreateInstance<MachineDef>();
        machine.machineId = "Smelter";
        machine.category = MachineCategory.Smelter;

        var recipe = ScriptableObject.CreateInstance<RecipeDef>();
        recipe.recipeId = "SmeltIron";
        recipe.inputs = new[] { new RecipeIngredient { resource = ore, amount = 1 } };
        recipe.outputs = new[] { new RecipeIngredient { resource = plate, amount = 1 } };
        recipe.processSeconds = 1f;
        recipe.requiredCategory = MachineCategory.Smelter;

        var db = GameDatabase.Build(new[] { ore, plate }, new[] { recipe }, new[] { machine });

        Assert.AreEqual(2, db.ResourceCount);
        Assert.AreEqual(1, db.Recipes.Count);
        Assert.AreEqual(1, db.Machines.Count);

        int ironOreId = db.GetResourceId("IronOre");
        int ironPlateId = db.GetResourceId("IronPlate");
        int recipeId = db.GetRecipeId("SmeltIron");

        var recipeRuntime = db.Recipes[recipeId];
        Assert.AreEqual(ironOreId, recipeRuntime.Inputs[0].ResourceId);
        Assert.AreEqual(ironPlateId, recipeRuntime.Outputs[0].ResourceId);
        Assert.AreEqual(MachineCategory.Smelter, db.Machines[db.GetMachineId("Smelter")].Category);

        Object.DestroyImmediate(ore);
        Object.DestroyImmediate(plate);
        Object.DestroyImmediate(machine);
        Object.DestroyImmediate(recipe);
    }
}
