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
La preview, l'enregistrement et le direct Twitch utilisent également le runtime natif LibObs.
Le live Twitch nécessite un compte connecté dans `Paramètres → Comptes`.

Dans la page Scènes, l'aperçu montre l'image composée par le moteur et, par-dessus, le cadre
de chaque source à la transformation qu'il lui applique — position, taille, rognage et
empilement. Son fonctionnement est décrit dans
[docs/canvas-de-composition.md](docs/canvas-de-composition.md).

## Espace de travail Studio

La page Studio est un espace de travail Dock.Avalonia composé de quatre panneaux : aperçu,
scène active, statut, diffusion et enregistrement. L'ajout d'un panneau est décrit dans
[docs/ajouter-un-panneau.md](docs/ajouter-un-panneau.md).

### Détacher et rattacher un panneau

Chaque panneau peut être détaché dans sa propre fenêtre, de trois manières :

- double-cliquer sur sa barre : pour l'aperçu, c'est la bande d'onglets, son onglet comme la
  place vide à côté ;
- ouvrir son menu et choisir « Détacher ». Pour les trois panneaux du bas, c'est la flèche à
  droite de leur barre, ou un clic droit dessus ; pour l'aperçu, un clic droit sur son onglet ;
- glisser sa barre et la relâcher hors de la zone d'ancrage : un autre écran, ou la barre de
  navigation en haut de la fenêtre.

Le glisser ne détache que si le panneau est relâché hors de la zone d'ancrage ; relâché
dessus, il s'y ancre, ce qui est bien le but. La fenêtre principale s'ouvrant maximisée, elle
ne laisse presque aucune zone libre : d'où les deux premiers gestes, qui fonctionnent quel
que soit le nombre d'écrans.

La rangée que le panneau laisse derrière lui se referme, et le panneau reste pleinement
fonctionnel dans sa nouvelle fenêtre.

Une fenêtre détachée est une fenêtre à part entière : sa propre entrée dans la barre des
tâches, libre de passer derrière la fenêtre principale et de vivre sur un autre écran. Elle se
ferme malgré tout avec l'application, plutôt que de rester ouverte sans elle. Elle n'a pas de
cadre Windows : la barre du panneau lui tient lieu de barre de titre, elle porte son nom et
sert à déplacer la fenêtre.

Pour rattacher un panneau, quatre gestes équivalents :

- choisir « Rattacher » dans son menu ;
- double-cliquer sur sa barre, le geste qui l'avait détaché ;
- glisser sa barre sur une zone d'accueil de la fenêtre principale ;
- fermer sa fenêtre.

Dans les quatre cas le panneau retourne à sa place d'origine plutôt que d'être perdu, avec la
taille qu'il avait dans la disposition par défaut, et la rangée qui l'accueillait est
reconstruite si elle avait disparu.

### Retrouver un panneau

Le menu « Panneaux » de la barre de menus liste les quatre panneaux. En choisir un ouvre la
page Studio et ramène le panneau à l'écran : celui qui est déjà là reçoit le focus, celui qui
est détaché voit sa fenêtre passer devant, et celui qui a disparu de la disposition est recréé
à sa place.

La dernière entrée, « Réinitialiser la disposition », repart de l'agencement d'origine et
ferme les fenêtres détachées. C'est le recours quand un espace de travail est devenu
inutilisable.

### Persistance

La disposition est enregistrée à la fermeture de l'application dans
`%APPDATA%/castor-studio/dock-layout.json`, fenêtres détachées comprises : position, taille
et contenu de chacune sont restaurés au démarrage suivant, une fois la fenêtre principale
affichée.

### Aperçu natif

Le panneau Aperçu affiche la scène active (`StudioWorkspaceViewModel.ActiveScene`, celle
qui part à l'enregistrement) via `StudioPreview`, qui héberge un `ObsPreviewHost` : un
`NativeControlHost` avec un vrai HWND enfant, sur lequel `LibObsSceneRuntime` dessine
directement. Il survit au détachement : Dock recycle la vue d'un panneau au lieu de la
reconstruire, et déplace donc la même instance entre la fenêtre principale et la fenêtre
flottante. Ce recyclage est partagé par fabrique, ce qui suppose une seule
`StudioDockFactory` pour l'espace de travail.

`LibObsSceneRuntime` tient un aperçu natif par fenêtre qui en demande un, indexé sur son
handle : la page Scènes et le panneau Studio peuvent afficher la même scène active en même
temps, chacun avec sa propre surface, y compris quand le panneau Studio est détaché dans sa
propre fenêtre. Supprimer une scène ou changer la résolution de base referme les aperçus
concernés (tous, dans le second cas, puisqu'ils partagent le même canevas OBS) ; chaque
fenêtre encore ouverte relance alors le sien.

Changer la scène active pendant un enregistrement re-pointe la sortie : le canal programme
OBS est fixé par `StartRecordingAsync` sur la scène active à ce moment-là, et
`SwitchRecordingScene` le redirige ensuite, sinon le fichier continuerait sur l'ancienne
scène pendant que l'aperçu suit la nouvelle. La page Scènes, elle, ne change jamais la scène
active : y sélectionner une scène ne fait que choisir celle qu'on édite et prévisualise, pour
ne pas perturber ce qui est en cours de diffusion. Le choix de la scène active reste au
sélecteur « Scène active » de la page Studio.

Le sélecteur de sources énumère les écrans, fenêtres, caméras, périphériques audio et
fichiers média, et autorise plusieurs sources dans une scène. L'enregistrement produit
des fichiers MP4, MKV ou WebM depuis la scène active dans le dossier configuré.
Les flux réseau et l'analyse IA restent indisponibles tant que leurs runtimes respectifs
ne sont pas implémentés.
