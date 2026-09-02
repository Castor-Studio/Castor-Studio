# Castor Studio

Application desktop Avalonia intégrant le package LibObs C# pour le cycle de vie des scènes.

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
Preview, périphériques matériels, enregistrement, streaming, scan réseau et analyse IA
restent indisponibles tant que `IStudioRuntime` n'a pas d'implémentation LibObs.
