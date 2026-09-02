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

## Espace de travail Studio

La page Studio est un espace de travail Dock.Avalonia composé de quatre panneaux : aperçu,
scène active, statut, diffusion et enregistrement.

### Détacher et rattacher un panneau

Chaque panneau peut être détaché dans sa propre fenêtre : glisser sa barre de titre hors de
la fenêtre principale. La rangée qu'il laisse derrière lui se referme, et le panneau reste
pleinement fonctionnel dans sa nouvelle fenêtre.

Pour le rattacher, deux gestes équivalents :

- le glisser sur une zone d'accueil de la fenêtre principale ;
- fermer sa fenêtre. Les panneaux ne pouvant pas être fermés, ils retournent alors à leur
  place d'origine plutôt que d'être perdus, et la rangée qui les accueillait est reconstruite
  si elle avait disparu.

Les fenêtres détachées appartiennent à la fenêtre principale : elles la suivent lorsqu'elle
est réduite ou restaurée, et se ferment avec elle.

### Persistance

La disposition est enregistrée à la fermeture de l'application dans
`%APPDATA%/castor-studio/dock-layout.json`, fenêtres détachées comprises : position, taille
et contenu de chacune sont restaurés au démarrage suivant, une fois la fenêtre principale
affichée.

### Aperçu natif

L'aperçu est encore un composant Avalonia sans contenu natif. Lorsqu'un hôte natif
(`NativeControlHost` sur une fenêtre enfant DirectX) sera introduit, il survivra au
détachement : Dock recycle la vue d'un panneau au lieu de la reconstruire, et déplace donc
la même instance entre la fenêtre principale et la fenêtre flottante. Ce recyclage est
partagé par fabrique, ce qui suppose une seule `StudioDockFactory` pour l'espace de travail.
