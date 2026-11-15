<p align="center">
  <img src="Castor.Studio/Assets/Brand/castor-icon.png" alt="Logo de Castor Studio" width="160">
</p>

<h1 align="center">Castor Studio</h1>

<p align="center"><em>Libérez le flot de vos événements</em></p>

Application Windows de production vidéo en direct : des scènes composées de sources (écrans,
fenêtres, caméras, caméras RTMP du réseau, audio, fichiers), l'enregistrement, le live sur Twitch
et YouTube, une mosaïque Multicam et une réalisation assistée par une IA locale. L'interface est
en Avalonia ; la vidéo passe par libobs, à travers le paquet C# `LibObs`.

## Installation

- Windows 10 ou 11, 64 bits.
- Télécharger `Castor-Studio-Setup-<version>.exe` depuis la page
  [Releases](https://github.com/Castor-Studio/Castor-Studio/releases). Le fichier `.sha256`
  publié à côté permet de vérifier le téléchargement.
- L'exécutable n'est pas signé : Windows SmartScreen affiche « Windows a protégé votre
  ordinateur ». Cliquer sur « Informations complémentaires », puis « Exécuter quand même ».
- Pour mettre à jour, lancer le nouvel installeur par-dessus l'ancien : les scènes, les
  paramètres et les comptes sont conservés (voir [Données de l'application](#données-de-lapplication)).
- Au premier démarrage de l'IA, l'application télécharge le worker de l'IA et ses modèles :
  une connexion Internet est nécessaire cette fois-là.

## Fonctionnalités

- **Scènes** : création, renommage, couleur, réordonnancement, sélection multiple, export et
  import en JSON, conservation d'une session à l'autre.
- **Sources** : écrans, fenêtres, caméras, caméras RTMP du réseau (trouvées par un scan du réseau
  local ou ajoutées par leur adresse), audio système, micros, fichiers vidéo ou audio lus en
  boucle.
- **Composition** : déplacer, redimensionner, faire pivoter, rogner et zoomer chaque source sur
  l'aperçu, ordre des plans, barre d'outils, menu du clic droit et raccourcis clavier.
- **Studio** : scène active, transitions (coupure, fondu, fondu au noir, balayage, glissement)
  avec une durée réglable, aperçu en plein écran, panneaux ancrables et détachables.
- **Enregistrement** : MP4, MKV ou WebM ; la scène active et d'autres scènes en même temps, chacune
  dans son propre fichier ; pause, reprise, annulation.
- **Live** : Twitch et YouTube, sur une plateforme ou les deux à la fois ; titre, description et
  visibilité du live YouTube ; enregistrement et live en même temps.
- **Multicam** : toutes les scènes en mosaïque (grille ou spotlight), mise à l'antenne depuis
  une tuile.
- **IA** : modes Agent (l'IA suggère une scène) et Auto (l'IA change de scène), modèles Football
  et Podcast, exécution sur le GPU ou le CPU.

Le détail de chaque fonctionnalité, avec les étapes pour la tester, est dans le plan de test
(ATP) de l'équipe.

## Signaler un problème

Ouvrir un ticket avec le modèle « Bug report » depuis
[Issues](https://github.com/Castor-Studio/Castor-Studio/issues/new/choose), en y joignant :

- la **version**, affichée en bas de `Paramètres → Général` ;
- les **journaux** : `Paramètres → Avancé → Ouvrir le dossier des journaux`. Un fichier par jour
  (`castor-AAAA-MM-JJ.log`) ; les 14 derniers jours d'utilisation sont gardés, ainsi que les
  14 derniers rapports de crash.

Après un plantage, l'application le signale au démarrage suivant et montre le rapport
correspondant dans l'Explorateur.

## Données de l'application

| Emplacement | Contenu |
|---|---|
| `%APPDATA%\castor-studio\settings.json` | Paramètres |
| `%APPDATA%\castor-studio\scenes.json` | Scènes, sources et composition |
| `%APPDATA%\castor-studio\dock-layout.json` | Disposition des panneaux, fenêtres détachées comprises |
| `%APPDATA%\castor-studio\providers.json` | Comptes connectés : nom, serveur et clé de stream |
| `%APPDATA%\castor-studio\tokens.dat` | Jetons de connexion des comptes, chiffrés pour l'utilisateur Windows (DPAPI) |
| `%APPDATA%\castor-studio\ai-device.json` | Processeur retenu pour l'IA (GPU ou repli sur le CPU) |
| `%LOCALAPPDATA%\Castor\logs` | Journaux et rapports de crash |
| `%LOCALAPPDATA%\Castor\worker\<version>` | Worker de l'IA |
| `%LOCALAPPDATA%\Castor\models` | Modèles de l'IA |

Supprimer `dock-layout.json` remet la disposition d'origine ; supprimer `scenes.json` repart
sans aucune scène.

## Développement

### Prérequis

- .NET SDK 10
- Windows x64

### Build

```powershell
./scripts/build-dotnet.ps1 -Configuration Debug
```

ou directement :

```powershell
dotnet build Castor-Studio.sln -c Debug
```

Les paquets `LibObs` et `Castor.AI` viennent de GitHub Packages (source `castor-github` de
`nuget.config`) : déclarer dans sa configuration NuGet utilisateur un jeton avec `read:packages`
pour cette source. Aucun jeton dans le repo.

Au premier démarrage de l'IA, l'app télécharge le worker de l'IA locale (`Castor.AI.Worker.exe`,
release `v<version>` de `Castor-Studio/Castor-AI-Worker`, même version que le paquet `Castor.AI`)
dans `%LOCALAPPDATA%\Castor\worker\<version>`. En dev, `CASTOR_WORKER_PATH` peut pointer vers un
worker compilé à part. Le worker télécharge ses modèles au premier démarrage d'un domaine
(`%LOCALAPPDATA%\Castor\models`).

### Tests et format

```powershell
dotnet test Castor-Studio.sln -c Release
dotnet format Castor-Studio.sln --verify-no-changes
```

Une partie des tests fait tourner le vrai runtime libobs (scènes, aperçus, enregistrements,
lives vers un serveur injoignable) : ils ont besoin de Windows. Les mêmes commandes tournent en
CI sur chaque pull request vers `dev` et `main`. Un plantage natif de libobs arrête tout le
processus de test : la CI lance les tests avec `--blame` pour nommer le test en cours à ce
moment-là.

### Publier une version

1. Merger `dev` dans `main` par une pull request.
2. Poser sur `main` un tag `vX.Y.Z` (ou `vX.Y.Z-beta`) et le pousser.

Le workflow `Release` vérifie que le tag est sur `main`, publie l'application en self-contained
`win-x64` avec la version du tag, construit l'installeur Inno Setup et son empreinte SHA-256,
puis crée la release GitHub avec ces deux fichiers.

## Architecture

Les scènes et leurs sources sont synchronisées avec libobs : création, renommage, suppression,
composition. L'aperçu, l'enregistrement et le live utilisent le runtime natif libobs
(`LibObsSceneRuntime`).

Dans la page Scènes, l'aperçu montre l'image composée par le moteur et, par-dessus, le cadre
de chaque source à la transformation qu'il lui applique — position, taille, rognage, rotation,
zoom et empilement. Son fonctionnement est décrit dans
[docs/canvas-de-composition.md](docs/canvas-de-composition.md).

### Espace de travail Studio

La page Studio est un espace de travail Dock.Avalonia composé de quatre panneaux : aperçu,
scène active, statut, diffusion et enregistrement. L'ajout d'un panneau est décrit dans
[docs/ajouter-un-panneau.md](docs/ajouter-un-panneau.md).

#### Détacher et rattacher un panneau

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

#### Retrouver un panneau

Dans le menu « Panneaux » de la barre de menus, « Afficher un panneau » liste les quatre
panneaux. En choisir un ouvre la page Studio et ramène le panneau à l'écran : celui qui est déjà
là reçoit le focus, celui qui est détaché voit sa fenêtre passer devant, et celui qui a disparu
de la disposition est recréé à sa place.

« Ramener tous les panneaux à leur place » repart de l'agencement d'origine et ferme les
fenêtres détachées. C'est le recours quand un espace de travail est devenu inutilisable.

#### Persistance

La disposition est enregistrée à la fermeture de l'application dans
`%APPDATA%\castor-studio\dock-layout.json`, fenêtres détachées comprises : position, taille
et contenu de chacune sont restaurés au démarrage suivant, une fois la fenêtre principale
affichée.

### Aperçu natif

Le panneau Aperçu affiche la scène active (`StudioWorkspaceViewModel.ActiveScene`, celle
qui part à l'enregistrement et au live) via `StudioPreview`, qui héberge un `ObsPreviewHost` :
un `NativeControlHost` avec un vrai HWND enfant, sur lequel `LibObsSceneRuntime` dessine
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

La page Scènes ne change jamais la scène active : y sélectionner une scène ne fait que choisir
celle qu'on édite et prévisualise, pour ne pas perturber ce qui est en cours de diffusion. Le
choix de la scène active reste au sélecteur « Scène active » de la page Studio et aux tuiles
de la page Multicam.

### Enregistrer et passer en direct

La scène active est rappelée dans la barre du haut, à côté du badge OFFLINE / REC / PAUSE /
EN DIRECT, quel que soit l'espace de travail affiché : c'est elle qui part à l'enregistrement
comme au live.

`ENREGISTRER` démarre l'enregistrement d'un clic. La flèche à côté choisit les scènes à
enregistrer : la scène active, qui suit les changements de scène, et d'autres scènes
enregistrées chacune dans son propre fichier. Le dossier et le format de sortie se règlent dans
Paramètres ; si un démarrage échoue, le message s'affiche dans une bulle au-dessus des boutons.

`DÉMARRER LE STREAM` ouvre une étape de confirmation : plateformes de destination, titre du
live, et pour YouTube la description et la visibilité. Chaque live YouTube est enveloppé dans
une diffusion créée puis clôturée par l'API YouTube. Sur plusieurs plateformes, les lives
partagent les mêmes encodeurs ; une plateforme qui échoue ou coupe n'arrête pas les autres.

Le live et l'enregistrement de la scène active montrent le canal programme d'OBS. Il porte
une transition plutôt qu'une scène : `StartRecordingAsync` et `StartStreamingAsync` le fixent
sur la scène active, et chaque changement de scène l'anime vers la suivante. Quand le live et
l'enregistrement tournent ensemble, ils partagent ce canal, l'audio et la vidéo lancés par le
premier ; le canal n'est libéré qu'avec la dernière sortie qui l'utilise. Chacun garde son
propre encodeur x264, ce qui double la charge processeur.
