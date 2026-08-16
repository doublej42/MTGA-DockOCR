# MTGA Dock OCR

Windows desktop app that captures one or two deck screenshots, sends the reviewed image to Claude for card recognition, validates card names against MTGJSON data, and copies an MTG Arena deck list.

## Prerequisites

- Windows 10 version 1809 or newer
- .NET 10 SDK
- An Anthropic API key from https://platform.claude.com/settings/workspaces/default/keys costs aroudn 5 cents for each run.
- The current `AllPrintings.sqlite` MTGJSON database

## Card Database

Download the latest `AllPrintings.sqlite` from [MTGJSON all files](https://mtgjson.com/downloads/all-files/). Place it at the repository root:

```text
MTGA-DockOCR/
  AllPrintings.sqlite
```

The database is intentionally ignored by Git. The application project copies it into its runtime `Data` directory during build.

## Build

```powershell
dotnet build MTGADockOCR.slnx
dotnet test tests/MTGADockOCR.Tests/MTGADockOCR.Tests.csproj
```

## Run

```powershell
dotnet run --project src/MTGADockOCR/MTGADockOCR.csproj
```

Enter and save the Claude API key in the app. The key is encrypted with Windows DPAPI for the current user.

## Use

1. Use `Ctrl+Alt+D` to capture the foreground window.
2. Review the single screenshot, or press the hotkey again to add a second screenshot.
3. Select **Send to Claude**.
4. Review matched and unmatched card rows. Only database-matched cards appear in the MTGA deck list.
5. Select **Copy deck list** and paste it into MTG Arena.

The diagnostic log includes the Claude prompt and raw response, but never the API key.