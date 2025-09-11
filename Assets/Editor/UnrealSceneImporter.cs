using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

public class UnrealSceneImporter : EditorWindow
{
    private string jsonPath = "Assets/Asset/UE_Export/scene_data.json";
    
    [MenuItem("Tools/Unreal Importer/Import Scene Layout")]
        public static void ShowWindow()
    {
        GetWindow<UnrealSceneImporter>("Unreal Scene Importer");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Unreal to Unity Scene Importer", EditorStyles.boldLabel);
        
        jsonPath = EditorGUILayout.TextField("JSON Path:", jsonPath);
        
        if (GUILayout.Button("Import Scene Layout"))
        {
            ImportSceneFromJSON();
        }
        
       if (GUILayout.Button("Clean Up Empty Objects"))
        {
            CleanUpEmptyObjects();
        }
    }

    [MenuItem("Tools/Unreal Importer/Assign Materials to Scene")]
    public static void AssignMaterialsToScene()
    {
        GameObject sceneRoot = GameObject.Find("Imported_UE_Scene");
        if (sceneRoot == null)
        {
            Debug.LogWarning("Imported_UE_Scene not found.");
            return;
        }
    
        int assignedCount = 0;
    
        foreach (Transform objectTransform in sceneRoot.transform)
        {
            Transform meshTransform = objectTransform.Find("Mesh");
            if (meshTransform != null)
            {
                Renderer renderer = meshTransform.GetComponent<Renderer>();
                if (renderer != null)
                {
                    string materialName = objectTransform.name.Replace("SM_", "MI_");
                    string materialPath = $"Assets/Asset/UE_Export/Materials/{materialName}.mat";
                
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material != null)
                    {
                        renderer.material = material;
                        assignedCount++;
                        Debug.Log($"Assigned {materialName} to {objectTransform.name}");
                    }
                }
            }
        }
    
        Debug.Log($"Assigned materials to {assignedCount} objects");
    }
    
    
    private void ImportSceneFromJSON()
    {
        if (!File.Exists(jsonPath))
        {
            Debug.LogError("JSON file not found: " + jsonPath);
            return;
        }
        
        try
        {
            string jsonData = File.ReadAllText(jsonPath);
            
            string wrappedJson = "{\"objects\":" + jsonData + "}";
            
            SceneDataWrapper wrapper = JsonUtility.FromJson<SceneDataWrapper>(wrappedJson);
            
            if (wrapper == null || wrapper.objects == null)
            {
                Debug.LogError("Failed to parse JSON data");
                return;
            }
            
            GameObject sceneRoot = new GameObject("Imported_UE_Scene");
            
            int meshesCreated = 0;
            int emptyObjectsCreated = 0;
            
            foreach (SceneObject objData in wrapper.objects)
            {
                if (ShouldImportObject(objData))
                {
                    if (CreateUnityObject(objData, sceneRoot.transform))
                    {
                        meshesCreated++;
                    }
                    else
                    {
                        emptyObjectsCreated++;
                    }
                }
            }
            
            Debug.Log($"Imported {meshesCreated} meshes and {emptyObjectsCreated} empty objects from Unreal scene");
            EditorUtility.DisplayDialog("Success", $"Imported {meshesCreated} meshes and {emptyObjectsCreated} empty objects", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing JSON: {e.Message}");
        }
    }
    
    private List<SceneObject> ParseJSONManually(string jsonPath)
    {
        List<SceneObject> objects = new List<SceneObject>();
        
        try
        {
            string[] lines = File.ReadAllLines(jsonPath);
            string currentObjectJson = "";
            bool inObject = false;
            int braceCount = 0;
            
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("{") && !inObject)
                {
                    inObject = true;
                    braceCount = 1;
                    currentObjectJson = "{";
                }
                else if (inObject)
                {
                    currentObjectJson += line;
                    
                    foreach (char c in line)
                    {
                        if (c == '{') braceCount++;
                        if (c == '}') braceCount--;
                    }
                    
                    if (braceCount == 0)
                    {
                       
                        SceneObject obj = JsonUtility.FromJson<SceneObject>(currentObjectJson);
                        if (obj != null)
                        {
                            objects.Add(obj);
                        }
                        
                        inObject = false;
                        currentObjectJson = "";
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Manual JSON parsing failed: {e.Message}");
        }
        
        return objects;
    }
    
    private bool ShouldImportObject(SceneObject objData)
    {
        
        string[] skipTypes = {
            "LightmassImportanceVolume",
            "SphereReflectionCapture", 
            "PostProcessVolume",
            "SkyLight",
            "ExponentialHeightFog",
            "DirectionalLight",
            "BP_Sky_Sphere_C",
            "BP_Material_swapper_C"
        };
        
        return !skipTypes.Contains(objData.type);
    }
    
    private bool CreateUnityObject(SceneObject objData, Transform parent)
    {
        GameObject newObj = new GameObject(objData.name);
        newObj.transform.SetParent(parent);
        
      
        Vector3 position = ConvertUEPositionToUnity(objData.position);
        Vector3 rotation = ConvertUERotationToUnity(objData.rotation);
        Vector3 scale = ConvertUEScaleToUnity(objData.scale);
        
        newObj.transform.position = position;
        newObj.transform.eulerAngles = rotation;
        newObj.transform.localScale = scale;
        
      
        if (!string.IsNullOrEmpty(objData.static_mesh))
        {
            string meshPath = ConvertUnrealPathToUnity(objData.static_mesh);
            GameObject meshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath);
            
            if (meshPrefab != null)
            {
                GameObject meshInstance = Instantiate(meshPrefab, newObj.transform);
                meshInstance.name = "Mesh";
                meshInstance.transform.localPosition = Vector3.zero;
                meshInstance.transform.localRotation = Quaternion.identity;
                return true;
            }
            else
            {
                Debug.LogWarning($"Mesh not found: {meshPath} for object {objData.name}");
            }
        }
        
        return false;
    }
    
    private Vector3 ConvertUEPositionToUnity(float[] uePosition)
    {
        if (uePosition == null || uePosition.Length != 3) return Vector3.zero;
        
        // Конвертация позиции: UE5 (X,Y,Z) → Unity (X,Z,Y)
        // UE5: X=Forward, Y=Right, Z=Up 
        // Unity: X=Right, Y=Up, Z=Forward
        return new Vector3(
            -uePosition[0] / 100f,
            uePosition[2]/ 100f,  
            uePosition[1] / 100f   
            /*uePosition[1] / 100f,  
            uePosition[2] / 100f,   
            uePosition[0] / 100f   */
            /*uePosition[1] / 100f,   // Y → X
            uePosition[2] / 100f,   // Z → Y
            -uePosition[0] / 100f   // -X → Z (инвертируем)*/
            
            
        );
    }
    
    private Vector3 ConvertUERotationToUnity(float[] ueRotation)
    {
        if (ueRotation == null || ueRotation.Length != 3) return Vector3.zero;
        
        // Конвертация вращения: UE5 (Pitch,Yaw,Roll) → Unity (X,Y,Z)
        return new Vector3(
            /*ueRotation[0],  // Pitch → X
            -ueRotation[1], // Yaw → -Y (инвертируем)
            ueRotation[2]   // Roll → Z*/
            
            /*ueRotation[0],  // Pitch → X 
            ueRotation[2], // Roll → -Y 
            ueRotation[1]   // Yaw → Z */
            
            ueRotation[0],
            ueRotation[1],
            ueRotation[2]
            
        );
    }
    
    private Vector3 ConvertUEScaleToUnity(float[] ueScale)
    {
        if (ueScale == null || ueScale.Length != 3) return Vector3.one;
        
      
        return new Vector3(ueScale[0], ueScale[1], ueScale[2]);
    }
    
    private string ConvertUnrealPathToUnity(string unrealPath)
    {
        if (string.IsNullOrEmpty(unrealPath)) return null;
        
       
        if (unrealPath.Contains("/Game/"))
        {
           
            string relativePath = unrealPath.Replace("/Game/", "");
            string assetName = relativePath.Split('/').Last().Split('.').First();
            return $"Assets/Asset/UE_Export/Meshes/{assetName}.fbx";
        }
        else if (unrealPath.Contains("/Engine/"))
        {
          
            if (unrealPath.Contains("Cube"))
                return "Assets/Asset/UE_Export/Meshes/Cube.fbx";
            if (unrealPath.Contains("Sphere")) 
                return "Assets/Asset/UE_Export/Meshes/Sphere.fbx";
            if (unrealPath.Contains("Cylinder"))
                return "Assets/Asset/UE_Export/Meshes/Cylinder.fbx";
            
            Debug.LogWarning($"Engine asset skipped: {unrealPath}");
            return null;
        }
        
        return unrealPath;
    }
    
    private void CleanUpEmptyObjects()
    {
       
        GameObject sceneRoot = GameObject.Find("Imported_UE_Scene");
        if (sceneRoot != null)
        {
            int removedCount = 0;
            foreach (Transform child in sceneRoot.transform)
            {
                if (child.childCount == 0) 
                {
                    DestroyImmediate(child.gameObject);
                    removedCount++;
                }
            }
            Debug.Log($"Removed {removedCount} empty objects");
        }
    }
}


[System.Serializable]
public class SceneDataWrapper
{
    public List<SceneObject> objects;
}

[System.Serializable]
public class SceneObject
{
    public string name;
    public string type;
    public float[] position;
    public float[] rotation;
    public float[] scale;
    public string static_mesh;
}