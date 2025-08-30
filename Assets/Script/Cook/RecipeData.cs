using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Cooking/Recipe")]
public class RecipeData : ScriptableObject
{
    [Serializable]
    public class RecipeSlotRequirement
    {
        public ItemData requiredItemData;
        public IngredientType requiredType = IngredientType.None; // �i�� None �N���ˬd���O
    }

    [Header("�t�����]�̦h5�ӡ^")]
    public List<RecipeSlotRequirement> requirements = new List<RecipeSlotRequirement>(5);

    [Header("���X��")]
    public ItemData resultItem;
    public int resultAmount = 1;

    [TextArea]
    public string recipeNote; // �y�z�λ���
}