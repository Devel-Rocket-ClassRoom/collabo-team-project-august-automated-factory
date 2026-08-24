using System;
using UnityEngine;

namespace Factory.Data
{
    [Serializable]
    public struct RecipeIngredient
    {
        public ResourceDef resource;
        public int amount;
    }

    // 레시피 하나의 정의. Assets/Resources/GameData/Recipes 폴더에 애셋을 추가하면
    // 코드 수정 없이 GameDatabase가 자동으로 인식하고, ProcessorSystem이 이 데이터를
    // 그대로 읽어 처리한다 (레시피별 분기 코드가 없다).
    [CreateAssetMenu(menuName = "Factory/Recipe Definition", fileName = "NewRecipe")]
    public class RecipeDef : ScriptableObject
    {
        [Tooltip("안정적인 문자열 키.")]
        public string recipeId;

        public RecipeIngredient[] inputs;
        public RecipeIngredient[] outputs;
        public float processSeconds = 1f;
        public MachineCategory requiredCategory;
    }
}
