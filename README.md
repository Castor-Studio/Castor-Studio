# Castor Studio

Application desktop Avalonia intégrant le package LibObs C# pour le cycle de vie des scènes et de leurs sources.

## Prérequis

- .NET SDK 10
- Windows x64

## Build

```powershell
./scripts/build-dotnet.ps1 -Configuration Debug
```

ou directement :

```powershell
dotnet build Castor-Studio.sln -c Debug
```

La création, le renommage et la suppression des scènes sont synchronisés avec LibObs.
Le sélecteur de sources énumère les écrans, fenêtres, caméras, périphériques audio et
fichiers média, et autorise plusieurs sources dans une scène. L'enregistrement produit
des fichiers MP4, MKV ou WebM depuis la scène active dans le dossier configuré.
Preview, streaming, flux réseau et analyse IA restent indisponibles tant que leurs
runtimes respectifs ne sont pas implémentés.
