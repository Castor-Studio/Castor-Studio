# Canvas de composition

Dans la page Scènes, l'aperçu montre l'image composée par le moteur **et**, par-dessus, le
cadre de chaque source composée, à la place et à la taille que le moteur lui donne. C'est ce
couple image + cadres qui forme le canvas de composition : la surface sur laquelle
s'appuiera la manipulation directe des sources.

## La règle

**Aucune valeur de transformation n'est calculée ni retenue côté interface.** Position,
échelle, rognage et empilement appartiennent au moteur. Une valeur recopiée ici finirait par
décrire autre chose que ce qui est réellement rendu — c'est déjà la règle de l'empilement
(voir `ISourceRuntime.GetSourceOrder`), le canvas ne fait que l'étendre à la géométrie.

## Pourquoi le moteur dessine, et pas Avalonia

L'aperçu de libobs est une **fenêtre Windows** posée sur la page. Elle recouvre tout ce
qu'Avalonia dessine au même endroit : un cadre en XAML par-dessus l'image est impossible,
quel que soit l'ordre des éléments. Les cadres sont donc tracés par le moteur lui-même, dans
la même image et la même projection que la scène — ils tombent sur l'image au pixel près,
sans décalage possible entre les deux.

C'est aussi ce qui rendra la manipulation possible : la fenêtre native est déjà transparente
aux clics ([`ObsPreviewHost`](../Castor.Studio/Controls/ObsPreviewHost.cs)), les gestes
tombent donc sur Avalonia derrière, et les transformations lues ici donnent le hit-testing.

## Le chemin d'une transformation

1. `LibObsSceneRuntime.GetSceneComposition(sceneId)` lit, sous un seul verrou, la taille du
   canvas du moteur et chaque scene item : position, échelle, rognage, visibilité, taille de
   la source. Le rectangle composé (`Width`, `Height`) est déduit **là**, au contact du
   moteur et selon sa règle — taille de la source, moins le rognage, mise à l'échelle.
2. Le résultat est un `SceneComposition` : des `Guid` applicatifs et des nombres, jamais un
   handle natif. Les sources y sont rangées du premier plan vers l'arrière-plan, comme
   partout ailleurs dans l'application (rang 0 = premier plan).
3. `SceneCompositionViewModel` garde de cette lecture les sources réellement composées, dans
   le sens du dessin — de l'arrière-plan vers le premier plan. Une source masquée, ou sans
   image comme une source audio, n'a pas de cadre.
4. `ObsPreviewHost` rend cette liste au moteur pour sa propre surface
   (`IScenePreviewRuntime.SetCompositionOverlay`). `ObsPreviewGraphics` le peint après la
   scène, dans le repère du canvas.

## Le vocabulaire de l'overlay

Tout est tracé **vers l'intérieur** du rectangle de la source : un trait posé à cheval sur le
bord ferait paraître la source plus grande qu'elle n'est, alors que c'est justement sa taille
réelle qu'il montre. Et toutes les tailles sont pensées en pixels de l'écran puis converties
en pixels du canvas : une marque se vise à la souris, elle ne suit pas l'échelle à laquelle le
canvas est réduit dans le panneau.

L'overlay est **achromatique** — une encre claire sur une ombre sombre. Dans cette
application la couleur porte un état : le rouge du direct et de l'enregistrement, le bleu de
la sélection dans les listes. La géométrie n'emprunte pas ce vocabulaire, elle ne dit pas un
état mais une forme. Chaque marque est posée sur un fond sombre légèrement plus large, sans
quoi elle se perdrait sur une image claire.

- **Source composée** : quatre équerres d'angle, fines. Pas de cadre entier — marquer chaque
  source d'un cadre poserait un quadrillage sur l'image, qui est le sujet.
- **Source choisie** : un cadre découpé en dents contiguës, une sur deux en sombre. C'est ce
  qui le rend lisible sur n'importe quelle image, là où un trait d'une seule couleur
  disparaît dès que l'image a la même valeur.
- **Ses points d'accroche** : équerres plus franches aux angles, barres couchées au milieu
  des côtés. La forme dit le geste attendu — un L tire un coin, une barre tire un bord.

## Choisir une source

Un clic sur l'image choisit la source visée : celle qui est devant, puisque c'est celle que
l'opérateur voit à cet endroit. Cliquer à côté de toute source, ou en dehors de l'image, ne
choisit plus rien.

Le clic ne peut pas atterrir sur la surface native : elle se déclare transparente aux tests
de survol et l'hôte ne teste pas le survol. Il tombe donc sur le fond de `StudioPreview`, qui
le ramène dans le repère du canvas — un simple rapport de tailles, écrit à un seul endroit,
et qui est le changement de repère du moteur pris à l'envers.

La sélection est retenue **par identifiant**, jamais par rectangle : elle suit la source
quand le moteur la déplace, et tombe d'elle-même quand la source cesse d'être composée —
retirée, masquée, ou scène changée. Montrer des points d'accroche sur une source que le
moteur ne compose plus laisserait saisir ce qui n'est pas là.

Le geste lui-même — étirer, déplacer — viendra avec l'écriture des transformations côté
moteur. Les marques sont déjà rendues dans le sens des aiguilles d'une montre depuis le coin
haut-gauche : c'est l'ordre dont il se servira pour savoir quel bord il tire.

## Rester en phase avec le moteur

Rien ne prévient l'interface qu'une transformation a changé du côté du moteur. La composition
est donc relue :

- à chaque geste sur les sources (ajout, retrait, déplacement), via `SyncSourceOrder` ;
- à chaque sélection de scène, y compris une scène rouverte ou rechargée ;
- toutes les 250 ms tant que l'aperçu tourne, à la cadence de `ObsPreviewHost`.

Un refus de lecture laisse les derniers cadres en place — ils restent proches du vrai — et
s'écrit sous la liste des sources, pour ne pas passer pour un aperçu figé.

## Deux fils, aucun verrou partagé

Le thread graphique de libobs relit la liste des cadres à chaque image, pendant que celui de
l'interface la remplace. L'échange se fait par référence, sans verrou : **prendre le verrou
du runtime depuis le thread graphique s'interbloquerait** avec les opérations qui, elles,
attendent ce thread (la destruction d'un display, par exemple). Chaque lecture produit donc
une liste neuve, qui ne change plus une fois donnée.

Pour la même raison, un symbole graphique manquant ne remonte pas dans le thread de rendu :
au premier échec, les cadres sont abandonnés pour de bon et l'image continue seule.

## Qui a des cadres, qui n'en a pas

Seule la page Scènes en demande. Le panneau Studio et la grille multicam montrent l'image
nue : ce sont des vues de ce qui part à l'antenne, pas des vues d'édition. C'est le sens de
la propriété `Composition` sur `StudioPreview` — non renseignée, aucun cadre.

Les noms des sources ne sont pas écrits sur l'image : ils sont dans la liste sous l'aperçu,
avec la même pastille de couleur, et l'image reste ce qu'elle est.

## Ce que le moteur n'expose pas encore

libobs connaît des *bounds* : un cadre qui contraint la taille rendue d'une source. Le
binding LibObs ne les expose pas (`obs_sceneitem_get_bounds` n'est pas lié). Le jour où ils
le seront, ils changeront `Width` et `Height` dans `ReadTransform`, et rien ailleurs.
