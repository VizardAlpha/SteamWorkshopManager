using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using SteamWorkshopManager.Models;

namespace SteamWorkshopManager.Services;

public static class BundleService
{
    private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static string BundlePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SteamWorkshopManager",
        "bundle"
    );

    private static readonly string[] EmbeddedLanguages = ["en-US", "fr-FR"];

    public static void EnsureBundleExtracted()
    {
        Directory.CreateDirectory(BundlePath);

        var assembly = Assembly.GetExecutingAssembly();

        foreach (var lang in EmbeddedLanguages)
        {
            var targetPath = Path.Combine(BundlePath, $"{lang}.axaml");

            try
            {
                using var stream = assembly.GetManifestResourceStream($"Languages.{lang}.axaml");
                if (stream is null)
                {
                    Console.WriteLine($"[WARN] Embedded resource Languages.{lang}.axaml not found");
                    continue;
                }

                if (!File.Exists(targetPath))
                {
                    using var fileStream = File.Create(targetPath);
                    stream.CopyTo(fileStream);
                }
                else
                {
                    // Merge new keys from embedded resource into existing file
                    MergeNewKeys(stream, targetPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to extract {lang}.axaml: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Adds any keys present in the embedded resource but missing from the existing bundle file.
    /// </summary>
    private static void MergeNewKeys(Stream embeddedStream, string targetPath)
    {
        var embeddedDoc = XDocument.Load(embeddedStream);
        var targetDoc = XDocument.Load(targetPath);

        if (embeddedDoc.Root is null || targetDoc.Root is null) return;

        var existingKeys = new HashSet<string>(
            targetDoc.Root.Elements()
                .Select(e => e.Attribute(XNs + "Key")?.Value)
                .Where(k => k is not null)!);

        var added = 0;
        foreach (var element in embeddedDoc.Root.Elements())
        {
            var key = element.Attribute(XNs + "Key")?.Value;
            if (key is null || existingKeys.Contains(key)) continue;

            targetDoc.Root.Add(element);
            added++;
        }

        if (added > 0)
        {
            targetDoc.Save(targetPath);
            Console.WriteLine($"[INFO] Merged {added} new keys into {Path.GetFileName(targetPath)}");
        }
    }

    public static List<LanguageInfo> DiscoverLanguages()
    {
        var languages = new List<LanguageInfo>();

        if (!Directory.Exists(BundlePath)) return languages;

        foreach (var file in Directory.GetFiles(BundlePath, "*.axaml"))
        {
            try
            {
                var doc = XDocument.Load(file);
                var root = doc.Root;
                if (root is null) continue;

                var entries = ParseElements(root);

                if (entries.TryGetValue("LanguageCode", out var code) &&
                    entries.TryGetValue("LanguageNativeName", out var nativeName) &&
                    entries.TryGetValue("LanguageDisplayName", out var displayName))
                {
                    languages.Add(new LanguageInfo(code, nativeName, displayName, file));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Skipping invalid language file {file}: {ex.Message}");
            }
        }

        return languages.OrderBy(l => l.Code).ToList();
    }

    public static Dictionary<string, string> ParseLanguageFile(string path)
    {
        var doc = XDocument.Load(path);
        return doc.Root is null ? [] : ParseElements(doc.Root);
    }

    private static Dictionary<string, string> ParseElements(XElement root)
    {
        var result = new Dictionary<string, string>();

        foreach (var element in root.Elements())
        {
            var keyAttr = element.Attribute(XNs + "Key");
            if (keyAttr is null) continue;
            result[keyAttr.Value] = element.Value;
        }

        return result;
    }
}
