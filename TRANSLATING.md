# Translating Steam Workshop Manager

Thank you for helping translate Steam Workshop Manager! This guide explains how to add a new language.

## How localization works

Language files are `.axaml` XML files stored in the **bundle folder**:

| OS | Path |
|---|---|
| Windows | `%AppData%\SteamWorkshopManager\bundle\` |
| macOS | `~/.config/SteamWorkshopManager/bundle/` |
| Linux | `~/.config/SteamWorkshopManager/bundle/` |

On first launch, the app extracts the built-in languages (`en-US.axaml`, `fr-FR.axaml`) into this folder. Any `.axaml` file placed here is automatically discovered and available in Settings.

## Adding a new language

### 1. Copy the English template

Copy `en-US.axaml` from the bundle folder (or from `SteamWorkshopManager/Resources/Languages/en-US.axaml` in the source) and rename it using the [BCP 47 language tag](https://en.wikipedia.org/wiki/IETF_language_tag) format:

```
<language>-<REGION>.axaml
```

Examples: `de-DE.axaml`, `ja-JP.axaml`, `pt-BR.axaml`, `zh-CN.axaml`

### 2. Update the metadata keys

Every language file **must** include these three keys at the top. They are used by the app to identify and display the language in Settings:

```xml
<!-- Language metadata -->
<x:String x:Key="LanguageCode">de-DE</x:String>
<x:String x:Key="LanguageNativeName">Deutsch</x:String>
<x:String x:Key="LanguageDisplayName">German</x:String>
```

| Key | Description | Example |
|---|---|---|
| `LanguageCode` | Must match the filename (without `.axaml`) | `de-DE` |
| `LanguageNativeName` | Language name **in that language** (shown in the UI) | `Deutsch` |
| `LanguageDisplayName` | Language name in English | `German` |

### 3. Translate the strings

Translate the value of each `<x:String>` entry. Do **not** change the `x:Key` attributes — only the text between the tags.

```xml
<!-- English -->
<x:String x:Key="Save">Save</x:String>

<!-- German translation -->
<x:String x:Key="Save">Speichern</x:String>
```

### 4. Test your translation

Drop your file into the bundle folder and restart the app. Your language should appear as a selectable card in **Settings > Language**.

## File format reference

The file must be a valid Avalonia `ResourceDictionary`:

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!-- Language metadata -->
    <x:String x:Key="LanguageCode">de-DE</x:String>
    <x:String x:Key="LanguageNativeName">Deutsch</x:String>
    <x:String x:Key="LanguageDisplayName">German</x:String>

    <!-- Translated strings -->
    <x:String x:Key="Save">Speichern</x:String>
    <!-- ... all other keys ... -->
</ResourceDictionary>
```

## Guidelines

- **Translate every key.** Missing keys will fall back to English, but a complete translation is preferred.
- **Keep placeholders intact.** Some strings contain `{0}`, `{1}`, etc. — these are replaced at runtime with dynamic values. Keep them in your translation.
- **Don't translate key names.** Only translate the text content, never the `x:Key` value.
- **Match the tone.** Keep translations concise and consistent with the existing UI style.

## Contributing

To contribute your translation to the project:

1. Fork the repository
2. Add your `.axaml` file to `SteamWorkshopManager/Resources/Languages/`
3. Open a pull request

We will add it to the built-in languages so all users can benefit from it.
