# Castor Studio

## Build

Use the root build script:

```powershell
./scripts/build-all.ps1 -Configuration Debug
```

For release:

```powershell
./scripts/build-all.ps1 -Configuration Release
```

The .NET-only step is also available after the native DLL exists:

```powershell
./scripts/build-dotnet.ps1 -Configuration Debug -Arch x64
```

This builds:

1. the native C/C++ DLL with CMake
2. the .NET/Avalonia solution

Do not use `dotnet build` directly unless the native DLL has already been built.
