using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

public class UnrealAssetOrganizer : EditorWindow
{
    private string importRoot = "Assets/Asset/UE_Export";

    [MenuItem("Tools/Unreal Importer/Organize Assets")]
    public static void ShowWindow()
    {
        GetWindow<UnrealAssetOrganizer>("Unreal Asset Organizer");
    }

    void OnGUI()
    {
        GUILayout.Label("Unreal Engine to Unity Asset Organizer", EditorStyles.boldLabel);

        importRoot = EditorGUILayout.TextField("Import Path:", importRoot);

        if (GUILayout.Button("Organize Assets by Material"))
        {
            OrganizeAssets();
        }

        if (GUILayout.Button("Test Name Parsing"))
        {
            TestNameParsing();
        }

        if (GUILayout.Button("Create Materials Only"))
        {
            CreateMaterials();
        }
    }

    private void OrganizeAssets()
    {
        var materialInfo = OrganizeAssetsByMaterial();
        SetupTextureSettings(materialInfo);

        CreateMaterialFromOrganized(materialInfo);
        AssignMaterialsToMesh(materialInfo);

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Complete", "Assets organized Successfully!", "OK");
    }

    private Dictionary<string, MaterialInfo> OrganizeAssetsByMaterial()
    {
        string texturesPath = importRoot + "/Textures";
        string meshesPath = importRoot + "/Meshes";

        Dictionary<string, MaterialInfo> materialFolders = new Dictionary<string, MaterialInfo>();

        if (Directory.Exists(texturesPath))
        {
            foreach (string file in Directory.GetFiles(texturesPath))
            {
                if (IsTextureFile(file))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    ParsedAsset parsed = ParseAssetNames(fileName);

                    if (!string.IsNullOrEmpty(parsed.materialName))
                    {
                        string materialFolder = texturesPath + parsed.materialName + "/";
                        if (!Directory.Exists(materialFolder))
                            Directory.CreateDirectory(materialFolder);

                        string destFile = materialFolder + Path.GetFileName(file);
                        File.Move(file, destFile);

                        if (!materialFolders.ContainsKey(parsed.materialName))
                        {
                            materialFolders[parsed.materialName] = new MaterialInfo();
                        }

                        materialFolders[parsed.materialName].textures.Add(new TextureInfo
                        {
                            file = Path.GetFileName(file),
                            type = parsed.textureType,
                            path = destFile
                        });
                    }
                }
            }
        }

        if (Directory.Exists(meshesPath))
        {
            foreach (string file in Directory.GetFiles(meshesPath))
            {
                if (file.EndsWith(".fbx"))
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    ParsedAsset parsed = ParseAssetNames(fileName);

                    if (!string.IsNullOrEmpty(parsed.materialName) && materialFolders.ContainsKey(parsed.materialName))
                    {
                        materialFolders[parsed.materialName].meshes.Add(Path.GetFileName(file));
                    }
                }
            }
        }
        return materialFolders;
    }

    private ParsedAsset ParseAssetNames(string filename)
    {
        ParsedAsset result = new ParsedAsset();
        
        Regex pattern = new Regex(@"^(SM_[^_]+_.*?)_(MI_[^_]+_.*?)_(T_[^_]+_.*?)_(BaseColor|Normal|Metallic|Roughness|ORM|Emissive)");
        Match match = pattern.Match(filename);
        
        if (match.Success)
        {
            result.meshName = match.Groups[1].Value;
            result.materialName = match.Groups[2].Value;
            result.textureName = match.Groups[3].Value;
            result.textureType = match.Groups[4].Value;
        }
        else
        {
            Regex[] patterns = {
                new Regex(@"^(SM_.*?)_(MI_.*?)_(T_.*?)_(\w+)$"),
                new Regex(@"^(SM_.*?)_(MI_.*?)_.*_(BaseColor|Normal|Metallic|Roughness)", RegexOptions.IgnoreCase),
                new Regex(@"^(SM_.*?)_(MI_.*?)_(.*?)_(diffuse|normal|specular|roughness)", RegexOptions.IgnoreCase)
            };
            
            foreach (Regex altPattern in patterns)
            {
                match = altPattern.Match(filename);
                if (match.Success)
                {
                    result.meshName = match.Groups[1].Value;
                    result.materialName = match.Groups[2].Value;
                    if (match.Groups.Count >= 4)
                    {
                        result.textureName = match.Groups[3].Value;
                        result.textureType = match.Groups[4].Value;
                    }
                    break;
                }
            }
        }
        return result;
    }

    private void CreateMaterialFromOrganized(Dictionary<string, MaterialInfo> materialInfo)
    {
        string materialsPath = importRoot + "/Materials/";
        if (!Directory.Exists(materialsPath))
        {
            Directory.CreateDirectory(materialsPath);

            foreach (var kvp in materialInfo)
            {
                string materialPath = materialsPath + kvp.Key + ".mat";
                Material material = new Material(Shader.Find("Standard"));
                
                TextureInfo baseColor = kvp.Value.textures.Find(t => IsTextureType(t.type, "basecolor", "albedo", "diffuse"));
                TextureInfo normalMap = kvp.Value.textures.Find(t => IsTextureType(t.type, "normal", "nrm"));
                TextureInfo metallic = kvp.Value.textures.Find(t => IsTextureType(t.type, "metallic", "metalness"));
                TextureInfo roughness = kvp.Value.textures.Find(t => IsTextureType(t.type, "roughness"));
                
                if (baseColor != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColor.path);
                    if (tex != null) material.SetTexture("_MainTex", tex);
                }
            
                if (normalMap != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalMap.path);
                    if (tex != null)
                    {
                        material.SetTexture("_BumpMap", tex);
                        material.EnableKeyword("_NORMALMAP");
                    }
                }
            
                if (metallic != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(metallic.path);
                    if (tex != null)
                    {
                        material.SetTexture("_MetallicGlossMap", tex);
                        material.EnableKeyword("_METALLICGLOSSMAP");
                    }
                }
                AssetDatabase.CreateAsset(material, materialPath);
            }
        }
    }

    private void AssignMaterialsToMesh(Dictionary<string, MaterialInfo> materialInfo)
    {
        string materialsPath = importRoot + "Materials/";
        string meshesPath = importRoot + "Meshes/";

        foreach (var kvp in materialInfo)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialsPath + kvp.Key + ".mat");
            if (material == null) continue;
            
            foreach (string meshFile in kvp.Value.meshes)
            {
                GameObject mesh = AssetDatabase.LoadAssetAtPath<GameObject>(meshesPath + meshFile);
                if (mesh != null)
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(mesh) as GameObject;
                    if (instance != null)
                    {
                        Renderer renderer = instance.GetComponent<Renderer>();
                        if (renderer != null) renderer.material = material;
                        DestroyImmediate(instance);
                    }
                }
            }
        }
    }

    private bool IsTextureFile(string file)
    {
        return file.EndsWith(".png") || file.EndsWith(".jpg") || file.EndsWith(".jpeg") || file.EndsWith(".tga");
    }

    private bool IsTextureType(string type, params string[] keywords)
    {
        if (string.IsNullOrEmpty(type)) return false;
        string lowerType = type.ToLower();
        return keywords.Any(k => lowerType.Contains(k.ToLower()));
    }

    private void TestNameParsing()
    {
        string[] testNames = {
            "SM_Bookshelf_Door_R_MI_Bookshelf_clean_T_Bookshelf_clean_BaseColor",
            "SM_Bookshelf_MI_Bookshelf_clean_T_Bookshelf_clean_Normal",
            "SM_Cabinet_C_door_MI_Bookshelf_clean_T_Bookshelf_clean_Metallic"
        };
        foreach (string assetName in testNames)
        {
            ParsedAsset parsed = ParseAssetNames(assetName);
            Debug.Log($"Input: {assetName}\nMesh: {parsed.meshName}\nMaterial: {parsed.materialName}\nTexture: {parsed.textureName}\nType: {parsed.textureType}\n");
        }
        
    }

    private void CreateMaterials()
    {
        var materialInfo = OrganizeAssetsByMaterial();
        CreateMaterialFromOrganized(materialInfo);
        AssetDatabase.Refresh();
    }

    private void SetupTextureSettings(Dictionary<string, MaterialInfo> materialInfo)
    {
        foreach (var kvp in materialInfo)
        {
            foreach (TextureInfo texInfo in kvp.Value.textures)
            {
                TextureImporter importer = AssetImporter.GetAtPath(texInfo.path) as TextureImporter;
                if (importer)
                {
                    if (IsTextureType(texInfo.type, "normal", "nrm"))
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        importer.sRGBTexture = false;
                    }
                    else if (IsTextureType(texInfo.type, "metallic", "roughness"))
                    {
                        importer.sRGBTexture = false;
                    }
                    importer.SaveAndReimport();
                }
            }
        }
    }

    private void ProcessMaskMapTextures(Dictionary<string, MaterialInfo> materialInfo)
    {
        foreach (var kvp in materialInfo)
        {
            TextureInfo maskMap = kvp.Value.textures.Find(t =>
                t.type.Equals("MaskMap", System.StringComparison.OrdinalIgnoreCase) ||
                t.type.Equals("ORM", System.StringComparison.OrdinalIgnoreCase));

            if (maskMap != null)
            {
                Texture2D maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskMap.path);
                if (maskMapTexture != null)
                {
                    string basePath = Path.GetDirectoryName(maskMap.path);
                    string baseName = Path.GetFileNameWithoutExtension(maskMap.path);

                    CreateChannelTexture(maskMapTexture, 0, basePath + "/" + baseName + "_Occlusion.png"); // R
                    CreateChannelTexture(maskMapTexture, 1, basePath + "/" + baseName + "_Roughness.png"); // G
                    CreateChannelTexture(maskMapTexture, 2, basePath + "/" + baseName + "_Metallic.png"); // B

                    kvp.Value.textures.Remove(maskMap);

                    kvp.Value.textures.Add(new TextureInfo
                    {
                        file = baseName + "_Occlusion.png",
                        type = "Occlusion",
                        path = basePath + "/" + baseName + "_Occlusion.png"
                    });
                    kvp.Value.textures.Add(new TextureInfo
                    {
                        file = baseName + "_Roughness.png",
                        type = "Roughness",
                        path = basePath + "/" + baseName + "_Roughness.png"
                    });
                    kvp.Value.textures.Add(new TextureInfo
                    {
                        file = baseName + "_Metallic.png",
                        type = "Metallic",
                        path = basePath + "/" + baseName + "_Metallic.png"
                    });
                }
            }
        }
    }

    private void CreateChannelTexture(Texture2D source, int channel, string outputPath)
    {
        Texture2D newTex = new Texture2D(source.width, source.height, TextureFormat.R8, false);

        Color[] pixels = source.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            float value = channel == 0 ? pixels[i].r :
                channel == 1 ? pixels[i].g : pixels[i].b;
            newTex.SetPixel(i % source.width, i / source.width, new Color(value, value, value));
        }

        byte[] pngData = newTex.EncodeToPNG();
        File.WriteAllBytes(outputPath, pngData);
    }
}


public class ParsedAsset
{
    public string meshName;
    public string materialName;
    public string textureName;
    public string textureType;
}

public class MaterialInfo
{
    public List<TextureInfo> textures = new List<TextureInfo>();
    public HashSet<string> meshes = new HashSet<string>();
}

public class TextureInfo
{
    public string file;
    public string type;
    public string path;
}
    
