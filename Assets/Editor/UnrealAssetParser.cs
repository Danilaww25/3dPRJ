using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace Editor
{
    public class UnrealAssetParser : EditorWindow
    {
        private static readonly int MetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScale = Shader.PropertyToID("_BumpScale");
        private static readonly int Metallic = Shader.PropertyToID("_Metallic");
        private static readonly int GlossMapScale = Shader.PropertyToID("_GlossMapScale");
        private static readonly int Glossiness = Shader.PropertyToID("_Glossiness");
        private static readonly int OcclusionMap = Shader.PropertyToID("_OcclusionMap");
        private static readonly int OcclusionStrength = Shader.PropertyToID("_OcclusionStrength");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        
        private string importRoot = "Assets/UE_Export/";

        [MenuItem("Tools/Unreal Importer/Organize Assets Ver2")]
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

            if (GUILayout.Button("Process ORM Textures Only"))
            {
                ProcessTexturesORM();
            }
        }

        private void OrganizeAssets()
        {
            try
            {
                Debug.Log("Organizing assets by materials...");
                var materialInfo = OrganizeAssetsByMaterial();

                Debug.Log("Processing ORM textures...");
                ProcessTexturesORM(materialInfo);

                Debug.Log("Configuring texture import settings...");
                SetupTextureSettings(materialInfo);

                VerifyTextureImportSettings();

                Debug.Log("Creating materials with textures...");
                CreateMaterialsFromOrganized(materialInfo);

                Debug.Log("Assigning materials to meshes...");
                AssignMaterialsToMeshes(materialInfo);

                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();

                Debug.Log("Process completed successfully!");
                EditorUtility.DisplayDialog("Success",
                    "Assets organized successfully!\nMaterials created with textures.", "OK");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error: {e.Message}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("Error", $"Process failed: {e.Message}", "OK");
            }
        }

        private Dictionary<string, MaterialInfo> OrganizeAssetsByMaterial()
        {
            string texturesPath = importRoot + "Textures/";
            string meshesPath = importRoot + "Meshes/";

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

                        if (!string.IsNullOrEmpty(parsed.materialName) &&
                            materialFolders.ContainsKey(parsed.materialName))
                        {
                            materialFolders[parsed.materialName].meshes.Add(Path.GetFileName(file));
                        }
                    }
                }
            }

            return materialFolders;
        }

        private void ProcessTexturesORM(Dictionary<string, MaterialInfo> materialInfo = null)
        {
            if (materialInfo == null)
            {
                materialInfo = OrganizeAssetsByMaterial();
            }

            foreach (var kvp in materialInfo)
            {
                List<TextureInfo> ormTextures = kvp.Value.textures
                    .Where(t => t.type != null && (t.type.Equals("ORM", System.StringComparison.OrdinalIgnoreCase) ||
                                                   t.type.Equals("MaskMap", System.StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (TextureInfo ormTexture in ormTextures)
                {
                    Debug.Log($"Processing ORM texture: {ormTexture.file} for material {kvp.Key}");

                    Texture2D ormTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ormTexture.path);
                    if (ormTex != null)
                    {
                        string basePath = Path.GetDirectoryName(ormTexture.path);
                        string baseName = Path.GetFileNameWithoutExtension(ormTexture.path);

                        CreateChannelTexture(ormTex, 0, basePath + "/" + baseName + "_Occlusion.png",
                            "Occlusion"); // R - Occlusion
                        CreateChannelTexture(ormTex, 1, basePath + "/" + baseName + "_Roughness.png",
                            "Roughness"); // G - Roughness
                        CreateChannelTexture(ormTex, 2, basePath + "/" + baseName + "_Metallic.png",
                            "Metallic"); // B - Metallic

                        kvp.Value.textures.Remove(ormTexture);

                        if (ormTexture.path != null)
                        {
                            File.Delete(ormTexture.path);
                            File.Delete(ormTexture.path + ".meta");

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

                        Debug.Log($"Split ORM texture into separate channels for material {kvp.Key}");
                    }
                }
            }

            AssetDatabase.Refresh();
        }

        private void CreateChannelTexture(Texture2D source, int channel, string outputPath, string channelName)
        {
            try
            {
                Texture2D newTex = new Texture2D(source.width, source.height, TextureFormat.R8, false);

                Color[] sourcePixels = source.GetPixels();
                Color[] newPixels = new Color[sourcePixels.Length];

                for (int i = 0; i < sourcePixels.Length; i++)
                {
                    float value = channel == 0 ? sourcePixels[i].r :
                        channel == 1 ? sourcePixels[i].g : sourcePixels[i].b;
                    newPixels[i] = new Color(value, value, value);
                }

                newTex.SetPixels(newPixels);
                newTex.Apply();

                byte[] pngData = newTex.EncodeToPNG();
                File.WriteAllBytes(outputPath, pngData);

                Debug.Log($"Created {channelName} texture: {outputPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error creating {channelName} texture: {e.Message}");
            }
        }

        private ParsedAsset ParseAssetNames(string filename)
        {
            ParsedAsset result = new ParsedAsset();

            Regex pattern =
                new Regex(
                    @"^(SM_[^_]+_.*?)_(MI_[^_]+_.*?)_(T_[^_]+_.*?)_(BaseColor|Normal|Metallic|Roughness|ORM|MaskMap|Emissive|Occlusion)");
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
                Regex[] patterns =
                {
                    new Regex(@"^(SM_.*?)_(MI_.*?)_(T_.*?)_(\w+)$"),
                    new Regex(@"^(SM_.*?)_(MI_.*?)_.*_(BaseColor|Normal|Metallic|Roughness|ORM|MaskMap)",
                        RegexOptions.IgnoreCase),
                    new Regex(@"^(SM_.*?)_(MI_.*?)_(.*?)_(diffuse|normal|specular|roughness|occlusion|orm)",
                        RegexOptions.IgnoreCase)
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

        private void CreateMaterialsFromOrganized(Dictionary<string, MaterialInfo> materialInfo)
        {
            string materialsPath = importRoot + "Materials/";
            if (!Directory.Exists(materialsPath))
                Directory.CreateDirectory(materialsPath);

            foreach (var kvp in materialInfo)
            {
                string materialPath = materialsPath + kvp.Key + ".mat";

                Material material = new Material(Shader.Find("Standard"));

                TextureInfo baseColor = FindTexture(kvp.Value.textures, "basecolor", "albedo", "diffuse");
                TextureInfo normalMap = FindTexture(kvp.Value.textures, "normal", "nrm");
                TextureInfo metallic = FindTexture(kvp.Value.textures, "metallic", "metalness");
                TextureInfo roughness = FindTexture(kvp.Value.textures, "roughness");
                TextureInfo occlusion = FindTexture(kvp.Value.textures, "occlusion");
                TextureInfo emissive = FindTexture(kvp.Value.textures, "emissive", "emission");

                if (baseColor != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColor.path);
                    if (tex != null)
                    {
                        material.SetTexture(MainTex, tex);
                        Debug.Log($"Assigned BaseColor: {baseColor.file} to material {kvp.Key}");
                    }
                }

                if (normalMap != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalMap.path);
                    if (tex != null)
                    {
                        material.SetTexture(BumpMap, tex);
                        material.EnableKeyword("_NORMALMAP");
                        material.SetFloat(BumpScale, 1.0f);
                        Debug.Log($"Assigned Normal: {normalMap.file} to material {kvp.Key}");
                    }
                }

                if (metallic != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(metallic.path);
                    if (tex != null)
                    {
                        material.SetTexture(MetallicGlossMap, tex);
                        material.EnableKeyword("_METALLICGLOSSMAP");
                        material.SetFloat(Metallic, 1.0f);
                        material.SetFloat(GlossMapScale, 1.0f);
                        Debug.Log($"Assigned Metallic: {metallic.file} to material {kvp.Key}");
                    }
                }
                else
                {
                    material.SetFloat(Metallic, 0f);
                }

                if (roughness != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(roughness.path);
                    if (tex != null)
                    {
                        material.SetFloat(Glossiness, 0.7f);
                        Debug.Log($"Assigned Roughness: {roughness.file} to material {kvp.Key}");
                    }
                }

                if (occlusion != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(occlusion.path);
                    if (tex != null)
                    {
                        material.SetTexture(OcclusionMap, tex);
                        material.SetFloat(OcclusionStrength, 1.0f);
                        Debug.Log($"Assigned Occlusion: {occlusion.file} to material {kvp.Key}");
                    }
                }

                if (emissive != null)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(emissive.path);
                    if (tex)
                    {
                        material.SetTexture(EmissionMap, tex);
                        material.EnableKeyword("_EMISSION");
                        material.SetColor(EmissionColor, Color.white);
                        Debug.Log($"Assigned Emissive: {emissive.file} to material {kvp.Key}");
                    }
                }

                AssetDatabase.CreateAsset(material, materialPath);
                Debug.Log($"Created material: {materialPath}");
            }
        }

        private TextureInfo FindTexture(List<TextureInfo> textures, params string[] keywords)
        {
            foreach (var texture in textures)
            {
                if (string.IsNullOrEmpty(texture.type)) continue;

                string lowerType = texture.type.ToLower();
                foreach (string keyword in keywords)
                {
                    if (lowerType.Contains(keyword.ToLower()))
                    {
                        return texture;
                    }
                }
            }

            return null;
        }

        private void AssignMaterialsToMeshes(Dictionary<string, MaterialInfo> materialInfo)
        {
            string materialsPath = importRoot + "Materials/";
            string meshesPath = importRoot + "Meshes/";
            int assignments = 0;

            foreach (var kvp in materialInfo)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialsPath + kvp.Key + ".mat");
                if (!material) continue;

                foreach (string meshFile in kvp.Value.meshes)
                {
                    string meshPath = meshesPath + meshFile;
                    GameObject meshPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath);

                    if (meshPrefab)
                    {
                        GameObject instance = PrefabUtility.InstantiatePrefab(meshPrefab) as GameObject;
                        if (instance)
                        {
                            Renderer renderer = instance.GetComponent<Renderer>();
                            if (renderer)
                            {
                                renderer.material = material;
                                assignments++;
                                Debug.Log($"Assigned material {kvp.Key} to mesh {meshFile}");
                            }

                            DestroyImmediate(instance);
                        }
                    }
                }
            }

            Debug.Log($"Made {assignments} material assignments");
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
                        string lowerType = texInfo.type.ToLower();

                        if (lowerType.Contains("normal") || lowerType.Contains("nrm"))
                        {
                            importer.textureType = TextureImporterType.NormalMap;
                            importer.sRGBTexture = false;
                        }
                        else if (lowerType.Contains("metallic") || lowerType.Contains("roughness") ||
                                 lowerType.Contains("occlusion"))
                        {
                            importer.sRGBTexture = false;
                        }

                        importer.SaveAndReimport();
                    }
                }
            }
        }

        private void VerifyTextureImportSettings()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            string[] allTextures = Directory.GetFiles(importRoot + "Textures/", "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".tga"))
                .ToArray();

            foreach (string texturePath in allTextures)
            {
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (importer)
                {
                    string filename = Path.GetFileName(texturePath).ToLower();

                    if (filename.Contains("normal") || filename.Contains("nrm"))
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        importer.sRGBTexture = false;
                    }
                    else if (filename.Contains("metallic") || filename.Contains("roughness") ||
                             filename.Contains("occlusion"))
                    {
                        importer.sRGBTexture = false;
                    }

                    importer.SaveAndReimport();
                }
            }
        }

        private bool IsTextureFile(string file)
        {
            return file.EndsWith(".png") || file.EndsWith(".jpg") || file.EndsWith(".tga");
        }

        private void TestNameParsing()
        {
            string[] testNames =
            {
                "SM_Bookshelf_Door_R_MI_Bookshelf_clean_T_Bookshelf_clean_BaseColor",
                "SM_Bookshelf_MI_Bookshelf_clean_T_Bookshelf_clean_Normal",
                "SM_Cabinet_C_door_MI_Bookshelf_clean_T_Bookshelf_clean_Metallic",
                "SM_Table_MI_Wood_Table_T_Wood_Table_ORM",
                "SM_Chair_MI_Metal_Chair_T_Metal_Chair_MaskMap"
            };

            foreach (string name in testNames)
            {
                ParsedAsset parsed = ParseAssetNames(name);
                Debug.Log(
                    $"Input: {name}\nMesh: {parsed.meshName}\nMaterial: {parsed.materialName}\nTexture: {parsed.textureName}\nType: {parsed.textureType}\n");
            }
        }

        private void CreateMaterials()
        {
            var materialInfo = OrganizeAssetsByMaterial();
            CreateMaterialsFromOrganized(materialInfo);
            AssetDatabase.Refresh();
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
}