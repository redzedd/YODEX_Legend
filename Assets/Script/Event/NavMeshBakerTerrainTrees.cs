using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

[RequireComponent(typeof(Terrain))]
[RequireComponent(typeof(NavMeshSurface))]

public class NavMeshBakerTerrainTrees : MonoBehaviour
{
    [ContextMenu("CreateTreeDummiesFromTerrain")]
    public void CreateTreeDummiesFromTerrain()
    {
        //Get Terrain
        var terrain = GetComponent<Terrain>();
        var terrainSize = terrain.terrainData.size;

        // Iterate over terrain treeInstances
        for (int i = 0;
            i < terrain.terrainData.treeInstances.Length;
            i++)
        {
            var tree = terrain.terrainData.treeInstances[i];

            // Get tree meta info
            var prototype = terrain
                .terrainData
                .treePrototypes[tree.prototypeIndex];

            // Convert into world position
            var realTreePosition = new Vector3(
                tree.position.x * terrainSize.x,
                tree.position.y * terrainSize.y,
                tree.position.z * terrainSize.z);

            // -- Create a tree box dummy -- //

            // Make root GameObject and set terrain as parent
            var treeBakeDummy = new GameObject("TreeBakeDummy_" + i);
            treeBakeDummy.transform.parent = transform;

            // Get tree prefab collider for sizes
            var boxCollider = prototype
                .prefab
                .GetComponent<BoxCollider>();

            // Make a cube the size of a tree
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.parent = treeBakeDummy.transform;
            cube.transform.position = boxCollider.center;
            cube.transform.localScale = boxCollider.size;

            // Set position
            treeBakeDummy.transform.position = realTreePosition;
        }
    }

    [ContextMenu("ClearTreeDummiesFromTerrain")]
    public void ClearTreeDummiesFromTerrain()
    {
        // Get childs of terrain
        var childs = new GameObject[transform.childCount];
        for (int i = 0; i < transform.childCount; i++)
        {
            childs[i] = transform.GetChild(i).gameObject;
        }

        // Delete
        foreach (var child in childs)
        {
            DestroyImmediate(child, false);
        }
    }
}
