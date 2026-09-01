# Castor Studio

Application desktop Avalonia préparée pour une future intégration du package LibObs C#.

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

La version actuelle conserve les interactions UI locales. Preview, périphériques matériels,
enregistrement, streaming, scan réseau et analyse IA restent indisponibles tant que
`IStudioRuntime` n'a pas d'implémentation LibObs.
