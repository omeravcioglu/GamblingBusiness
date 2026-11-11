#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor script to automatically configure portrait images as Sprites
/// Run this once to set up all your portrait images for use in the game
/// </summary>
public class PortraitImporter : EditorWindow
{
    [MenuItem("Tools/Configure Portrait Images as Sprites")]
    public static void ConfigurePortraits()
    {
        string malePath = "Assets/Resources/Portraits/Male";
        string femalePath = "Assets/Resources/Portraits/Female";
        
        int maleCount = ConfigureImagesInFolder(malePath);
        int femaleCount = ConfigureImagesInFolder(femalePath);
        
        AssetDatabase.Refresh();
        
        Debug.Log($"✅ Configured {maleCount} male and {femaleCount} female portraits as Sprites!");
        EditorUtility.DisplayDialog("Portrait Configuration Complete", 
            $"Configured {maleCount + femaleCount} portrait images as Sprites!\n\n" +
            $"Male: {maleCount}\nFemale: {femaleCount}", 
            "OK");
    }
    
    static int ConfigureImagesInFolder(string folderPath)
    {
        int count = 0;
        
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogWarning($"Folder not found: {folderPath}");
            return 0;
        }
        
        // Get all image files in the folder
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
        
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            
            if (importer != null)
            {
                // Configure as Sprite
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    
                    // Set max size if needed (optional, adjust as needed)
                    importer.maxTextureSize = 2048;
                    
                    // Save the changes
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    count++;
                }
            }
        }
        
        return count;
    }
}
#endif
