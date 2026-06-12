using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AssetConfigurator.EditorUI
{
    [CustomEditor(typeof(AssetConfigurationController))]
    public class AssetConfigrationController_Inspector: Editor
    {

        private Vector2 AssetListScrollPos = Vector2.zero;
        private Vector2 SceneObjectScrollPos = Vector2.zero;
        private bool showGeneralSettings = true;
        private bool showAssetList = false;
        private bool showSceneList = false;
        private bool showDefaultEditor = false;
        
        public override void OnInspectorGUI()
        {
            GUILayout.Space(10);
            AssetConfigurationController controller = (AssetConfigurationController) target;

            if (GUILayout.Button("General Settings", EditorStyles.toolbarButton))
                showGeneralSettings = !showGeneralSettings;

            if (showGeneralSettings)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.BeginHorizontal();
                        GUILayout.Label("Scene Camera: ", GUILayout.Width(140));
                        controller.SceneCamera = (Camera) EditorGUILayout.ObjectField(controller.SceneCamera, typeof(Camera), true);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                        GUILayout.Label("Asset Scene Parent: ", GUILayout.Width(140));
                        controller.AssetTargetParent = (Transform) EditorGUILayout.ObjectField(controller.AssetTargetParent, typeof(Transform), true);
                    GUILayout.EndHorizontal();            
                    GUILayout.BeginHorizontal();
                        GUILayout.Label("Asset Start Location: ", GUILayout.Width(140));
                        controller.AssetStartLocation = EditorGUILayout.Vector3Field("", controller.AssetStartLocation);
                    GUILayout.EndHorizontal();

                GUILayout.EndVertical();
            }

            if (GUILayout.Button("Preview Asset List", EditorStyles.toolbarButton))
                showAssetList = !showAssetList;

            if (showAssetList)
            {
                bool checkAssets = false;
                GUILayout.BeginVertical(EditorStyles.helpBox);
                AssetListScrollPos = GUILayout.BeginScrollView(AssetListScrollPos, GUILayout.MinHeight(100));
                for (int i = 0; i < controller.AssetPreviewList.Count; i++)
                {
                    EditorGUI.BeginChangeCheck();
                    controller.AssetPreviewList[i] =
                        (AssetConfigurationData) EditorGUILayout.ObjectField(controller.AssetPreviewList[i],
                            typeof(AssetConfigurationData), true);
                    if (EditorGUI.EndChangeCheck())
                        checkAssets = true;

                }

                if (checkAssets == true)
                {
                    for (int i = (controller.AssetPreviewList.Count - 1); i >= 0; i--)
                    {
                        if (controller.AssetPreviewList[i] == null)
                            controller.AssetPreviewList.RemoveAt(i);
                    }
                }

                GUILayout.EndScrollView();

                GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(40));
                GUILayout.Label("Drag and Drop Addtional Assets Here\r\nTo add them to the list",
                    EditorStyles.centeredGreyMiniLabel);
                GUILayout.EndVertical();
                Rect dropArea = GUILayoutUtility.GetLastRect();

                if (dropArea.Contains(Event.current.mousePosition))
                {
                    if (Event.current.type == EventType.DragUpdated)
                        DragAndDrop.visualMode = DragAndDropVisualMode.Link;


                    if (Event.current.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        Object[] refs = DragAndDrop.objectReferences;
                        foreach (Object obj in refs)
                        {
                            if (obj.GetType() == typeof(GameObject))
                            {
                                GameObject go = (GameObject) obj;
                                AssetConfigurationData acd = go.GetComponent<AssetConfigurationData>();

                                if (acd != null)
                                {
                                    controller.AssetPreviewList.Add(acd);
                                }
                                else
                                {
                                    SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
                                    if (smr == null)
                                        smr = go.GetComponentInChildren<SkinnedMeshRenderer>();

                                    if (smr == null)
                                    {
                                        EditorUtility.DisplayDialog("Invalid Asset", "This asset does not contain a skinned mesh rendered, and this can not be automatically configured currently. Please manually configure this asset, then add it.", "Ok");
                                    }
                                    else
                                    {
                                        if (EditorUtility.DisplayDialog("Add Asset Configuration Data?", "This asset does not contain any configuration data, which is required. Should the component be added?", "Indeed", "Negative"))
                                        {
                                            go.AddComponent<AssetConfigurationData>();
                                            acd = go.GetComponent<AssetConfigurationData>();
                                            acd.meshType = MeshType.SkinnedMesh;
                                            Animator anim = go.GetComponent < Animator>();
                                            if (anim != null)
                                                acd.TargetAnimator = anim;
                                            
                                            controller.AssetPreviewList.Add(acd);
                                            
                                            

                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                GUILayout.EndVertical();
            }

            if (GUILayout.Button("Scene Objects", EditorStyles.toolbarButton))
            {
                showSceneList = !showSceneList;

            }

            if (showSceneList)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                SceneObjectScrollPos = GUILayout.BeginScrollView(SceneObjectScrollPos, GUILayout.MinHeight(100));
                if (controller.sceneObjectController != null)
                {
                    List <GameObject> gos = controller.sceneObjectController.ToggleableObjects;
                    for (int i = 0; i < gos.Count; i++)
                    {
                        gos[i] = (GameObject)EditorGUILayout.ObjectField(gos[i], typeof(GameObject), true);
                    }
                    for (int i = (gos.Count-1); i >=0; i--)
                        if (gos[i] == null)
                            gos.RemoveAt(i);
                }
                GUILayout.EndScrollView();
                
                
                
                GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(40));
                GUILayout.Label("Drag and Drop Addtional Assets Here\r\nTo add them to the list",
                    EditorStyles.centeredGreyMiniLabel);
                GUILayout.EndVertical();
                Rect dropArea = GUILayoutUtility.GetLastRect();

                if (dropArea.Contains(Event.current.mousePosition))
                {
                    if (Event.current.type == EventType.DragUpdated)
                        DragAndDrop.visualMode = DragAndDropVisualMode.Link;


                    if (Event.current.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        Object[] refs = DragAndDrop.objectReferences;
                        foreach (Object obj in refs)
                        {
                            if (obj.GetType() == typeof(GameObject))
                            {
                                controller.sceneObjectController.ToggleableObjects.Add((GameObject) obj);
                            }
                        }
                    }
                }

                GUILayout.EndVertical();
            }

            if (GUILayout.Button("Default Editor", EditorStyles.toolbarButton))
                showDefaultEditor = !showDefaultEditor;
            
            if (showDefaultEditor)
                DrawDefaultInspector();
            
            GUILayout.Space(10);
        }
        
        
        
    }
}