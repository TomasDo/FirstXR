using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>Rebuilds the bundled Chinese HUD font without depending on device system fonts.</summary>
public static class DentalHudFontSetup
{
    const string SourcePath = "Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf";
    const string AssetPath = "Assets/Resources/Fonts/DentalHud SDF.asset";
    const string RuntimePath = "Assets/Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR";

    [MenuItem("XREAL/Fonts/Rebuild Chinese HUD Font")]
    public static void Rebuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(SourcePath) as TrueTypeFontImporter;
        if (importer == null)
            throw new InvalidOperationException("The bundled Noto Sans CJK source font is missing.");
        if (!importer.includeFontData)
        {
            importer.includeFontData = true;
            importer.SaveAndReimport();
        }

        var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (font == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            AssetDatabase.Refresh();
            font = TMP_FontAsset.CreateFontAsset(source, 64, 9, GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (font == null)
                throw new InvalidOperationException("Could not create the Chinese HUD font atlas.");
            font.name = "DentalHud SDF";
            AssetDatabase.CreateAsset(font, AssetPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
        }
        else
        {
            if (font.sourceFontFile != source)
                throw new InvalidOperationException("The HUD font references an unexpected source font.");
            font.ClearFontAssetData(true);
        }

        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.isMultiAtlasTexturesEnabled = true;
        // Ship the known UI glyphs; unknown names can still populate from the bundled OTF.
        var fontSettings = new SerializedObject(font);
        fontSettings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
        fontSettings.ApplyModifiedPropertiesWithoutUndo();
        if (!font.TryAddCharacters(CollectCharacters(), out string missing))
            throw new InvalidOperationException("HUD font is missing characters: " + missing);

        foreach (var atlas in font.atlasTextures.Take(font.atlasTextureCount))
        {
            if (!AssetDatabase.Contains(atlas))
                AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(atlas);
        }
        EditorUtility.SetDirty(font.material);
        EditorUtility.SetDirty(font);

        // Runtime-created HUD labels use the default. Scene labels with an explicit Latin
        // font use this global fallback if they later receive Chinese text.
        var settings = new SerializedObject(TMP_Settings.instance);
        settings.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
        var fallbacks = settings.FindProperty("m_fallbackFontAssets");
        var alreadyPresent = false;
        for (var i = 0; i < fallbacks.arraySize; i++)
            alreadyPresent |= fallbacks.GetArrayElementAtIndex(i).objectReferenceValue == font;
        if (!alreadyPresent)
        {
            var index = fallbacks.arraySize;
            fallbacks.InsertArrayElementAtIndex(index);
            fallbacks.GetArrayElementAtIndex(index).objectReferenceValue = font;
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(font);
        AssetDatabase.SaveAssetIfDirty(TMP_Settings.instance);
        Debug.Log($"Chinese HUD font ready: {font.characterTable.Count} characters, " +
            $"{font.atlasTextureCount} atlas(es), source font included.");
    }

    static string CollectCharacters()
    {
        var characters = new SortedSet<char>();
        for (var c = ' '; c <= '~'; c++)
            characters.Add(c);

        // Include all literal text in the product code, including conditional status/alarm
        // messages. ASCII escapes need no decoding; their printable glyphs are included above.
        var literals = new Regex("@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\\\r\\n])*\"");
        foreach (var path in Directory.GetFiles(RuntimePath, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            foreach (Match literal in literals.Matches(File.ReadAllText(path)))
                foreach (var c in literal.Value)
                    if (!char.IsControl(c) && !char.IsSurrogate(c))
                        characters.Add(c);
        }
        return new string(characters.ToArray());
    }
}
