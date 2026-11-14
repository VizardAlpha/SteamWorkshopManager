using System;
using System.IO;
using System.Text;
using WorkshopManager.Core.Workshop;

namespace WorkshopManager.Services.Workshop;

/// <summary>Result of reading a change-notes file: the notes, or the localization key of what is wrong.</summary>
public sealed record BulkChangelogFileResult(BulkChangelog? Changelog, string? ErrorKey)
{
    public bool IsValid => Changelog is not null;
}

/// <summary>Reads the .txt or .md file holding the change notes of a bulk update.</summary>
public static class BulkChangelogFile
{
    public const long MaxBytes = 1024 * 1024;

    public static readonly string[] Extensions = [".txt", ".md"];

    public static BulkChangelogFileResult Read(string path)
    {
        var extension = Path.GetExtension(path);
        if (Array.FindIndex(Extensions, e => e.Equals(extension, StringComparison.OrdinalIgnoreCase)) < 0)
            return Invalid("BulkChangelogWrongType");

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return Invalid("BulkChangelogUnreadable");
            if (info.Length > MaxBytes) return Invalid("BulkChangelogTooLarge");

            var bytes = File.ReadAllBytes(path);
            if (!TryDecode(bytes, out var text)) return Invalid("BulkChangelogNotText");
            if (string.IsNullOrWhiteSpace(text)) return Invalid("BulkChangelogEmpty");

            var markdown = extension.Equals(".md", StringComparison.OrdinalIgnoreCase);
            return new BulkChangelogFileResult(BulkChangelog.Parse(text, markdown), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Invalid("BulkChangelogUnreadable");
        }
    }

    // Text only: a renamed binary has NUL bytes or is not valid UTF-8.
    internal static bool TryDecode(byte[] bytes, out string text)
    {
        text = string.Empty;
        if (Array.IndexOf(bytes, (byte)0) >= 0) return false;

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes).TrimStart('﻿');
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static BulkChangelogFileResult Invalid(string errorKey) => new(null, errorKey);
}
