# Canvas de composition

Dans la page Scènes, l'aperçu montre l'image composée par le moteur **et**, par-dessus, le
cadre de chaque source composée, à la place et à la taille que le moteur lui donne. C'est ce
couple image + cadres qui forme le canvas de composition : la surface sur laquelle
s'appuie la manipulation directe des sources — déplacer, étirer, rogner.

## La règle

**Aucune valeur de transformation n'est calculée ni retenue côté interface.** Position,
échelle, rognage et empilement appartiennent au moteur. Une valeur recopiée ici finirait par
décrire autre chose que ce qui est réellement rendu — c'est déjà la règle de l'empilement
(voir `ISourceRuntime.GetSourceOrder`), le canvas ne fait que l'étendre à la géométrie.

Un geste n'y fait pas exception : il **propose** un placement, le moteur l'écrit ou le
refuse, et c'est sa réponse qui reste à l'écran. La seule chose montrée avant lui est le
cadre de la source saisie, le temps qu'il confirme.

## Pourquoi le moteur dessine, et pas Avalonia

L'aperçu de libobs est une **fenêtre Windows** posée sur la page. Elle recouvre tout ce
qu'Avalonia dessine au même endroit : un cadre en XAML par-dessus l'image est impossible,
quel que soit l'ordre des éléments. Les cadres sont donc tracés par le moteur lui-même, dans
la même image et la même projection que la scène — ils tombent sur l'image au pixel près,
sans décalage possible entre les deux.

C'est aussi ce qui rend la manipulation possible : la fenêtre native est transparente
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

L'overlay reprend la répartition que fait déjà la liste des sources, où la pastille garde sa
couleur pendant que le texte passe à l'accent : **la couleur dit l'identité, le poids et les
poignées disent l'état**.

- **Source composée** : un filet de 1 px, de la couleur de sa pastille dans la liste. Sur une
  composition qui se chevauche, c'est ce qui dit quel cadre est quelle ligne. Une couleur
  illisible rend l'accent par défaut : un cadre sans couleur exacte reste plus utile qu'une
  source sans cadre.
- **Source choisie** : le même cadre, dans la même couleur, épaissi à 2 px.
- **Ses poignées** : huit carrés en `AppAccentFg` à cœur `AppFg1`, quatre aux angles, quatre
  au milieu des côtés, centrés sur leur point donc à cheval sur le bord — c'est ce qui les
  rend saisissables des deux côtés du trait, et visibles sur une source collée au bord du
  canvas.

Tout l'overlay porte une ombre `AppBg` de 1 px de chaque côté. Un cœur coloré posé sur un
liseré sombre se lit sur n'importe quelle image ; le même trait seul disparaît dès que
l'image prend sa valeur. Les valeurs du thème sombre valent dans les deux thèmes, la zone
d'aperçu étant noire en clair comme en sombre.

Tout est tracé **vers l'intérieur** du rectangle de la source : un trait posé à cheval sur le
bord ferait paraître la source plus grande qu'elle n'est, alors que c'est justement sa taille
réelle qu'il montre. Et toutes les tailles sont pensées en pixels de l'écran puis converties
en pixels du canvas : une poignée se vise à la souris, elle ne suit pas l'échelle à laquelle
le canvas est réduit dans le panneau.

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

## Manipuler une source

| Geste | Où | Ce qui est écrit |
| --- | --- | --- |
| Déplacer | glisser une source | la position |
| Étirer | glisser une poignée de la source choisie | l'échelle (et la position, pour un bord haut ou gauche) |
| Rogner | **Alt** + glisser une poignée | le rognage (et la position, pour un bord haut ou gauche) |
| Annuler | **Échap** pendant le geste | le placement d'avant le geste |

- Appuyer sur une source la choisit et la saisit d'un même geste : pas besoin de cliquer une
  première fois pour la sélectionner.
- Les poignées passent avant les sources : elles sont peintes par-dessus tous les cadres, une
  poignée visible se saisit même quand une autre source est devant à cet endroit.
- Un angle garde les proportions ; **Maj** les libère. Un côté n'étire que son axe.
- La poignée tirée emmène son ou ses bords, le bord opposé reste en place. Au rognage, l'image
  reste immobile sous le bord qui avance : on découvre ou on cache, on ne décale rien.
- Une source ne se réduit pas sous 8 pixels du canvas — ses poignées se recouvriraient — ni
  ne se retourne. Un rognage ne descend pas sous zéro et laisse toujours un pixel de la source.
- Au survol, le curseur annonce ce qu'un clic saisirait : déplacement sur une source, flèche
  orientée sur une poignée.

Les poignées sont rendues dans le sens des aiguilles d'une montre depuis le coin haut-gauche,
et `CompositionHandle` suit le même ordre. Leur zone de prise est pensée en pixels de l'écran,
un peu plus large que le carré peint, puis convertie dans le repère du canvas.

### Un geste recalculé depuis son origine

`CompositionGeometry` calcule le placement demandé à partir de la transformation lue **au
début** du geste et du déplacement total du pointeur. Rien n'est cumulé mouvement après
mouvement : un geste ne dérive pas, quel que soit le nombre de mouvements, et un rognage en
pixels entiers de la source ne perd rien aux arrondis.

### Écrire dans le moteur

`ISourceRuntime.SetSourceTransform(sceneId, sourceId, placement)` écrit position, échelle et
rognage d'un seul tenant, sous le verrou du runtime, et rend la transformation que le moteur
compose ensuite. Il refuse avant d'approcher le moteur un nombre non fini, une échelle nulle
ou négative, un rognage négatif ou qui ne laisserait rien de la source. Si libobs échoue au
milieu des trois écritures, l'ancien placement est rendu tel quel : jamais un mélange des
deux.

Le pointeur bouge bien plus souvent que le moteur ne compose. Pendant un geste :

- le cadre suit **chaque** mouvement, sans attendre le moteur — c'est ce qui le garde collé
  au pointeur ;
- le moteur reçoit **au plus une écriture toutes les 16 ms** (une image à 60 i/s). La
  première part tout de suite ; une demande qui arrive plus tôt attend ;
- une minuterie de `StudioPreview`, à la même cadence, écrit la demande en attente quand le
  pointeur s'arrête, et le relâcher écrit la dernière ;
- la relecture périodique de l'aperçu ne ramène pas le cadre en arrière : elle rend les
  autres sources telles que le moteur les détient, et la source saisie là où le geste la
  demande.

Au relâcher, la composition est relue : ce qui reste à l'écran est ce que le moteur a
confirmé, pas ce qui a été demandé.

### Un refus, et rien ne diverge

Une écriture refusée arrête le geste là. La source reprend dans le moteur le placement
qu'elle avait au début du geste, la composition est relue, et le refus s'écrit sous la liste
des sources. Si le moteur refuse même ce retour, l'écran ne suit ni la demande ni le retour :
il montre ce que le moteur détient réellement. Dans tous les cas, ce qui est à l'écran après
un refus est une lecture du moteur.

Échap, une capture perdue (Alt+Tab, une fenêtre qui passe devant) ou la vue qui disparaît
en plein geste mènent au même retour : sans relâcher, rien ne dit où l'opérateur voulait
finir.

## Rester en phase avec le moteur

Rien ne prévient l'interface qu'une transformation a changé du côté du moteur. La composition
est donc relue :

- à chaque geste sur la liste des sources (ajout, retrait, déplacement), via `SyncSourceOrder` ;
- à la fin de chaque geste sur le canvas (relâcher, annulation, refus) ;
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

C'est pourquoi l'étirement écrit l'**échelle** : sans bounds, c'est la seule grandeur qui
fixe la taille rendue. Le binding n'expose pas non plus l'alignement d'un item ; le calcul
des gestes suppose celui que libobs donne par défaut, la position désignant le coin
haut-gauche de la source.

Les transformations vivent dans le moteur le temps de la session : une scène rouverte les
retrouve telles qu'écrites. L'export de scènes (`SceneCollectionService`) n'emporte encore
que les sources, pas leur placement.
