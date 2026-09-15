# Canvas de composition

Le canvas de composition dessine, dans la page Scènes, chaque source de la scène
sélectionnée à la place et à la taille que le moteur lui donne. Il ne reproduit pas une
mise en page décidée par l'interface : il relit le moteur et pose ce qu'il y trouve.

C'est la surface sur laquelle s'appuieront la manipulation directe des sources et l'aperçu
enrichi ; d'où la règle qui tient tout ce qui suit.

## La règle

**Aucune valeur de transformation n'est calculée ni retenue côté interface.** Position,
échelle, rognage et empilement appartiennent au moteur. Une valeur recopiée ici finirait par
décrire autre chose que ce qui est réellement rendu — c'est déjà la règle de l'empilement
(voir `ISourceRuntime.GetSourceOrder`), le canvas ne fait que l'étendre à la géométrie.

## Le chemin d'une transformation

1. `LibObsSceneRuntime.GetSceneComposition(sceneId)` lit, sous un seul verrou, la taille du
   canvas du moteur et chaque scene item : position, échelle, rognage, visibilité, taille de
   la source. Le rectangle composé (`Width`, `Height`) est déduit **là**, au contact du
   moteur et selon sa règle — taille de la source, moins le rognage, mise à l'échelle.
2. Le résultat est un `SceneComposition` : des `Guid` applicatifs et des nombres, jamais un
   handle natif. Les sources y sont rangées du premier plan vers l'arrière-plan, comme
   partout ailleurs dans l'application (rang 0 = premier plan).
3. `SceneCompositionViewModel` transforme cette lecture en calques, parcourus dans le sens du
   dessin — de l'arrière-plan vers le premier plan. Les calques existants sont recalés sur
   place plutôt que recréés : le canvas se relit plusieurs fois par seconde.
4. `SceneCompositionCanvas` pose chaque calque sur un `Canvas` aux dimensions du moteur, et
   met le tout à l'échelle du panneau avec un seul `Viewbox`. Les coordonnées liées dans le
   XAML sont donc exactement celles lues à l'étape 1.

## Rester en phase avec le moteur

Rien ne prévient l'interface qu'une transformation a changé du côté du moteur. Le canvas
relit donc :

- à chaque geste sur les sources (ajout, retrait, déplacement), via `SyncSourceOrder` ;
- à chaque sélection de scène, y compris une scène rouverte ou rechargée ;
- toutes les 250 ms tant qu'il est affiché, ce qui est la cadence de `SceneCompositionCanvas`.

Une source que le moteur ne compose pas n'est pas dessinée : une source masquée, ou une
source sans image comme une source audio.

## Canvas ou aperçu direct

Les deux occupent la même place dans la page Scènes et une bascule choisit lequel s'affiche.
Ils ne peuvent pas être superposés : l'aperçu de libobs est une fenêtre Windows posée sur la
page, elle recouvre tout ce qu'Avalonia dessine au même endroit. Masquer l'aperçu ne suffit
pas non plus à l'arrêter — sa surface reste dans l'arbre visuel — c'est pourquoi la page ne
lui donne aucune scène (`LivePreviewScene`) tant que le canvas est affiché.

## Ce que le moteur n'expose pas encore

libobs connaît des *bounds* : un cadre qui contraint la taille rendue d'une source. Le
binding LibObs ne les expose pas (`obs_sceneitem_get_bounds` n'est pas lié). Le jour où ils
le seront, ils changeront `Width` et `Height` dans `ReadTransform`, et rien dans l'interface.
