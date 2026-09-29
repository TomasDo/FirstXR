using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DentalNavigation.Tests
{
    public sealed class DentalHudFontTests
    {
        const string FontResource = "Fonts/DentalHud SDF";
        const string FontAssetPath = "Assets/Resources/Fonts/DentalHud SDF.asset";
        const string SourceFontPath = "Assets/Fonts/NotoSansCJK/NotoSansCJKsc-Regular.otf";
        const string RuntimeSourcePath = "Samples/XREAL XR Plugin/3.1.0/Interaction Basics/HelloMR";
        const string LegacyFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        const string RepresentativeHudText = "0123456789+-.: mm CT ° ↑ ↓ 颊舌近中远中方向数据缺失超深阈值—…";

        // Match comments before literals, so quoted text in comments never becomes required UI text.
        // Interpolated strings are included conservatively; their non-ASCII source text also needs glyphs.
        static readonly Regex SourceTokens = new Regex(
            @"(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)|(?<verbatim>@""(?:[^""]|"""")*"")|(?<quoted>""(?:\\.|[^""\\])*"")|(?<character>'(?:\\.|[^'\\])*')",
            RegexOptions.Compiled);

        [Test]
        public void BundledChineseFontIsTheDefaultAndGlobalFallback()
        {
            var font = LoadHudFont();

            Assert.That(TMP_Settings.defaultFontAsset, Is.SameAs(font));
            Assert.That(TMP_Settings.fallbackFontAssets, Does.Contain(font),
                "Existing scene labels with a Latin font still need the shared Chinese fallback.");
        }

        [Test]
        public void DynamicFontBundlesItsSourceAndPreservesBakedGlyphsForAndroid()
        {
            var font = LoadHudFont();

            Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic),
                "DynamicOS relies on fonts installed on the headset; use the bundled source instead.");
            Assert.That(font.sourceFontFile, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(font.sourceFontFile), Is.EqualTo(SourceFontPath));
            Assert.That(font.isMultiAtlasTexturesEnabled, Is.True);
            using (var serializedFont = new SerializedObject(font))
            {
                var clearOnBuild = serializedFont.FindProperty("m_ClearDynamicDataOnBuild");
                Assert.That(clearOnBuild, Is.Not.Null);
                Assert.That(clearOnBuild.boolValue, Is.False,
                    "The Android build must retain the baked HUD glyphs.");
            }
        }

        [Test]
        public void AllRuntimeChineseLiteralsAndHudSymbolsAlreadyHaveBakedGlyphs()
        {
            var sourceRoot = Path.Combine(Application.dataPath, RuntimeSourcePath);
            Assert.That(Directory.Exists(sourceRoot), Is.True);
            var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Replace('\\', '/').Contains("/GrpcGenerated/"))
                .ToArray();
            Assert.That(sourceFiles.Length, Is.GreaterThan(0));

            var required = new HashSet<uint>();
            foreach (var path in sourceFiles)
            {
                foreach (Match token in SourceTokens.Matches(File.ReadAllText(path)))
                {
                    if (token.Groups["comment"].Success || token.Groups["character"].Success)
                        continue;
                    CollectNonAsciiLiteralCharacters(token.Value,
                        token.Groups["verbatim"].Success, required);
                }
            }

            Assert.That(required, Does.Contain((uint)'颊'), "The scanner must find actual HUD labels.");
            Assert.That(required, Does.Contain((uint)'阈'));
            foreach (var character in RepresentativeHudText)
                required.Add(character);

            AssertBakedCharacters(LoadHudFont(), required);
        }

        [Test]
        public void FontDependenciesIncludeSourceShaderMaterialAndPopulatedAtlases()
        {
            var font = LoadHudFont();
            var dependencies = AssetDatabase.GetDependencies(FontAssetPath, true);

            Assert.That(dependencies, Does.Contain(SourceFontPath));
            Assert.That(font.material, Is.Not.Null);
            Assert.That(font.material.shader, Is.Not.Null);
            Assert.That(font.material.shader.name, Is.EqualTo("TextMeshPro/Mobile/Distance Field"));
            Assert.That(dependencies, Does.Contain(AssetDatabase.GetAssetPath(font.material.shader)));
            Assert.That(AssetDatabase.Contains(font.material), Is.True);
            Assert.That(dependencies, Does.Contain(AssetDatabase.GetAssetPath(font.material)));
            Assert.That(font.atlasTextures, Is.Not.Null.And.Not.Empty);
            Assert.That(font.atlasTextures.Length, Is.GreaterThanOrEqualTo(font.atlasTextureCount));
            Assert.That(font.material.mainTexture, Is.Not.Null);
            Assert.That(font.material.mainTexture.GetInstanceID(),
                Is.EqualTo(font.atlasTextures[0].GetInstanceID()),
                "Material and font must reference the same native atlas, including after reimport.");

            foreach (var atlas in font.atlasTextures.Take(font.atlasTextureCount))
            {
                Assert.That(atlas, Is.Not.Null);
                Assert.That(atlas.width, Is.GreaterThan(1));
                Assert.That(atlas.height, Is.GreaterThan(1));
                Assert.That(AssetDatabase.Contains(atlas), Is.True,
                    "Each populated atlas must be saved with the font, not exist only in editor memory.");
                Assert.That(dependencies, Does.Contain(AssetDatabase.GetAssetPath(atlas)));
            }
        }

        [Test]
        public void NewlyCreatedUiLabelsUseTheChineseDefaultAndProduceGlyphMeshes()
        {
            var font = LoadHudFont();
            const string text = "CT靶标颊舌近中远中";
            AssertBakedCharacters(font, text.Select(character => (uint)character));
            var root = CreateCanvas();
            try
            {
                var label = CreateLabel(root);
                Assert.That(label.font, Is.SameAs(font));
                label.text = text;
                label.ForceMeshUpdate(true, true);
                AssertRenderedCharacters(label, text, font, false);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ExistingLatinUiLabelsResolveChineseFallbackWithoutSquareReplacement()
        {
            var font = LoadHudFont();
            const string text = "CT靶标颊舌近中远中方向数据缺失超深阈值";
            AssertBakedCharacters(font, text.Select(character => (uint)character));
            var legacyAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LegacyFontPath);
            Assert.That(legacyAsset, Is.Not.Null);
            var legacy = Object.Instantiate(legacyAsset);
            var root = CreateCanvas();
            try
            {
                // Keep the saved Latin asset untouched and require the project-wide fallback path.
                legacy.fallbackFontAssetTable = new List<TMP_FontAsset>();
                Assert.That(legacy.characterLookupTable.ContainsKey('颊'), Is.False);
                var label = CreateLabel(root);
                label.font = legacy;
                label.text = text;
                label.ForceMeshUpdate(true, true);

                Assert.That(label.textInfo.characterInfo[0].fontAsset, Is.SameAs(legacy));
                AssertRenderedCharacters(label, text, font, true);
            }
            finally
            {
                Object.DestroyImmediate(root);
                // TMP destroys its material and atlas textures in OnDestroy. This shallow clone
                // borrows those saved assets, so detach them before disposing only the clone.
                legacy.material = null;
                legacy.atlasTextures = Array.Empty<Texture2D>();
                Object.DestroyImmediate(legacy);
            }
        }

        static TMP_FontAsset LoadHudFont()
        {
            var font = Resources.Load<TMP_FontAsset>(FontResource);
            Assert.That(font, Is.Not.Null, "The Chinese font must be included in Resources for player builds.");
            Assert.That(AssetDatabase.GetAssetPath(font), Is.EqualTo(FontAssetPath));
            return font;
        }

        static void AssertBakedCharacters(TMP_FontAsset font, IEnumerable<uint> required)
        {
            // Do not use TryAddCharacters or HasCharacters with tryAddCharacter: those can hide
            // a missing baked atlas by changing the font while the regression test runs.
            var missing = required.Distinct()
                .Where(character => !font.characterLookupTable.ContainsKey(character))
                .OrderBy(character => character)
                .Select(character => string.Format("U+{0:X4} ({1})", character,
                    char.ConvertFromUtf32((int)character)))
                .ToArray();
            Assert.That(missing, Is.Empty, "Missing baked glyphs: " + string.Join(", ", missing));
        }

        static void CollectNonAsciiLiteralCharacters(string literal, bool verbatim, ISet<uint> required)
        {
            for (var index = 0; index < literal.Length; index++)
            {
                uint codePoint;
                if (!verbatim && literal[index] == '\\' && index + 1 < literal.Length)
                {
                    var escape = literal[++index];
                    var digitCount = escape == 'u' ? 4 : escape == 'U' ? 8 : 0;
                    if (digitCount == 0 || index + digitCount >= literal.Length)
                        continue;
                    codePoint = uint.Parse(literal.Substring(index + 1, digitCount),
                        NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    index += digitCount;
                }
                else
                {
                    codePoint = (uint)char.ConvertToUtf32(literal, index);
                    if (char.IsHighSurrogate(literal[index]))
                        index++;
                }

                if (codePoint > 127 && codePoint <= 0x10FFFF &&
                    (codePoint > char.MaxValue || !char.IsControl((char)codePoint)))
                    required.Add(codePoint);
            }
        }

        static GameObject CreateCanvas()
        {
            var root = new GameObject("Font regression canvas", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            return root;
        }

        static TextMeshProUGUI CreateLabel(GameObject root)
        {
            var child = new GameObject("Font regression label", typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            var label = child.AddComponent<TextMeshProUGUI>();
            label.rectTransform.sizeDelta = new Vector2(4096f, 256f);
            label.fontSize = 32f;
            label.fontStyle = FontStyles.Normal;
            label.richText = false;
            return label;
        }

        static void AssertRenderedCharacters(TMP_Text label, string expected,
            TMP_FontAsset chineseFont, bool onlyNonAscii)
        {
            Assert.That(label.textInfo.characterCount, Is.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
            {
                var rendered = label.textInfo.characterInfo[index];
                Assert.That(rendered.character, Is.EqualTo(expected[index]), "Character " + index);
                Assert.That(rendered.textElement, Is.Not.Null, "Character " + index);
                Assert.That(rendered.textElement.unicode, Is.EqualTo((uint)expected[index]),
                    "The mesh must use the requested character, not TMP's square replacement.");
                Assert.That(rendered.isVisible, Is.True, "Character " + index);
                Assert.That(label.textInfo.meshInfo[rendered.materialReferenceIndex].vertexCount,
                    Is.GreaterThan(0));
                if (!onlyNonAscii || expected[index] > 127)
                    Assert.That(rendered.fontAsset, Is.SameAs(chineseFont), "Character " + index);
            }
        }
    }
}
