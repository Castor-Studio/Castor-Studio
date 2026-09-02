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

## Espace de travail Studio

La page Studio est un espace de travail Dock.Avalonia composé de quatre panneaux : aperçu,
scène active, statut, diffusion et enregistrement.

### Détacher et rattacher un panneau

Chaque panneau peut être détaché dans sa propre fenêtre : glisser sa barre de titre et la
relâcher hors de la fenêtre principale, par exemple sur un second écran. La rangée qu'il
laisse derrière lui se referme, et le panneau reste pleinement fonctionnel dans sa nouvelle
fenêtre.

Une fenêtre détachée est une fenêtre à part entière : elle porte le nom du panneau dans sa
barre de titre système, possède sa propre entrée dans la barre des tâches, peut passer
derrière la fenêtre principale et vivre sur un autre écran. Elle se ferme malgré tout avec
l'application, plutôt que de rester ouverte sans elle.

Pour rattacher un panneau, deux gestes équivalents :

- glisser sa barre de panneau sur une zone d'accueil de la fenêtre principale ;
- fermer sa fenêtre. Les panneaux ne pouvant pas être fermés, ils retournent alors à leur
  place d'origine plutôt que d'être perdus, et la rangée qui les accueillait est reconstruite
  si elle avait disparu.

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
