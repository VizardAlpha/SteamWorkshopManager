using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using SteamWorkshopManager.Helpers;
using SteamWorkshopManager.Models;
using SteamWorkshopManager.Services.Log;

namespace SteamWorkshopManager.Services.Core;

public static class BundleService
{
    private static readonly Logger Log = new(nameof(BundleService), LogService.Instance);

    private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string BundlePath => AppPaths.Bundle;

    private static readonly string[] EmbeddedLanguages = ["en-US", "fr-FR"];
    private const string ReferenceLanguage = "en-US";

    public static void EnsureBundleExtracted()
    {
        Directory.CreateDirectory(BundlePath);
        var assembly = Assembly.GetExecutingAssembly();

        // Shipped languages: always overwritten. The in-repo .axaml is the
        // source of truth and edits/fixes propagate to users on next launch.
        foreach (var lang in EmbeddedLanguages)
        {
            var targetPath = Path.Combine(BundlePath, $"{lang}.axaml");
            try
            {
                using var stream = assembly.GetManifestResourceStream($"Languages.{lang}.axaml");
                if (stream is null)
                {
                    Log.Warning($"Embedded resource Languages.{lang}.axaml not found");
                    continue;
                }
                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to extract {lang}.axaml: {ex.Message}", ex);
            }
        }

        // Custom (user-added) languages: patch any key present in the reference
        // language but missing from the custom file, marking it with a
        // to-do comment so the translator knows to handle it. Existing keys are
        // never overwritten — the user's translation is preserved as-is.
        SyncCustomLanguagesWithReference();
    }

    private static void SyncCustomLanguagesWithReference()
    {
        if (!Directory.Exists(BundlePath)) return;

        var assembly = Assembly.GetExecutingAssembly();

        Dictionary<string, string> referenceKeys;
        try
        {
            using var refStream = assembly.GetManifestResourceStream($"Languages.{ReferenceLanguage}.axaml");
            if (refStream is null)
            {
                Log.Warning($"Reference language {ReferenceLanguage} not found, skipping custom sync");
                return;
            }
            var refDoc = XDocument.Load(refStream);
            if (refDoc.Root is null) return;
            referenceKeys = ParseElements(refDoc.Root);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not load reference language: {ex.Message}", ex);
            return;
        }

        var shippedSet = new HashSet<string>(EmbeddedLanguages, StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(BundlePath, "*.axaml"))
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            if (shippedSet.Contains(lang)) continue;

            try
            {
                PatchMissingKeys(file, referenceKeys, lang);
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not sync custom language {lang}: {ex.Message}");
            }
        }
    }

    private static void PatchMissingKeys(string filePath, Dictionary<string, string> referenceKeys, string lang)
    {
        var doc = XDocument.Load(filePath);
        if (doc.Root is null) return;

        var existingKeys = new HashSet<string>(
            doc.Root.Elements()
                .Select(e => e.Attribute(XNs + "Key")?.Value)
                .Where(k => k is not null)!,
            StringComparer.Ordinal
        );

        var added = 0;
        foreach (var (key, referenceValue) in referenceKeys)
        {
            if (existingKeys.Contains(key)) continue;

            doc.Root.Add(
                new XComment($" TODO translate: \"{EscapeForComment(referenceValue)}\" "),
                new XElement(
                    XNs + "String",
                    new XAttribute(XNs + "Key", key),
                    referenceValue
                )
            );
            added++;
        }

        if (added > 0)
        {
            doc.Save(filePath);
            Log.Info($"Added {added} missing key(s) to {lang}.axaml (TODO translate)");
        }
    }

    private static string EscapeForComment(string value)
    {
        // XML comments cannot contain "--"
        return value.Replace("--", "- -");
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
                Log.Warning($"Skipping invalid language file {file}: {ex.Message}");
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