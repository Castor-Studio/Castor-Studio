# Ajouter un panneau à l'espace de travail Studio

La page Studio est un espace de travail [Dock.Avalonia](https://github.com/wieslawsoltes/Dock)
composé de panneaux détachables. Ce guide décrit l'ajout d'un panneau, de bout en bout, sur
l'exemple d'un panneau « Journal ».

Six fichiers sont concernés, dans cet ordre.

## 1. Déclarer le type du panneau

Dans `Castor.Studio/Docking/StudioDockables.cs`.

Les panneaux partagent le même `Context`, un `StudioViewModel`. C'est donc le type du
dockable, et non son contexte, qui permet de choisir la vue à afficher. Chaque panneau a
son type marqueur, vide.

```csharp
public sealed class LogTool : Tool;
```

Un panneau destiné à la zone centrale, comme l'aperçu, dérive de `Document` plutôt que de
`Tool`.

## 2. Déclarer les identifiants

Dans `Castor.Studio/Docking/StudioDockIds.cs`. Il en faut deux : un pour le panneau, un
pour le dock qui le contient.

```csharp
public const string LogDock = "LogDock";
public const string Log = "Log";
```

Ces identifiants sont écrits tels quels dans la disposition enregistrée sur disque. Les
renommer plus tard invalide les dispositions existantes.

## 3. Écrire la vue

Un `UserControl` dans `Castor.Studio/Views/Studio/LogPaneView.axaml`, sur le modèle de
`StatusPaneView`.

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:CastorApplication.ViewModels.Studio"
             x:Class="CastorApplication.Views.Studio.LogPaneView"
             x:DataType="vm:StudioViewModel">

	<!-- Moitié basse de la carte du panneau : les côtés et le bas sont arrondis ici,
	     le haut est dessiné par la barre de titre du dock (DockTheme.axaml). -->
	<Border Background="{DynamicResource AppSurface}" CornerRadius="0,0,8,8" Padding="12,8"
	        BorderBrush="{DynamicResource AppBorder}" BorderThickness="1,0,1,1">
		<!-- contenu -->
	</Border>

</UserControl>
```

## 4. Associer la vue au type

Dans `Castor.Studio/App.axaml`, avec les autres panneaux.

```xml
<DataTemplate DataType="{x:Type docking:LogTool}"><studioViews:LogPaneView DataContext="{Binding Context}"/></DataTemplate>
```

## 5. Placer le panneau dans la disposition

Dans `CreateLayout()` de `Castor.Studio/Docking/StudioDockFactory.cs`.

Créer le panneau, l'ajouter à la boucle qui applique les tailles minimales, puis
l'envelopper dans un dock.

```csharp
var log = new LogTool { Id = StudioDockIds.Log, Title = "Journal", CanClose = false, CanFloat = true };

var logDock = new ToolDock
{
    Id = StudioDockIds.LogDock,
    Title = "Journal",
    ActiveDockable = log,
    VisibleDockables = CreateList<IDockable>(log),
    CanClose = false,
    CanFloat = true,
    Proportion = 0.3,
};
```

`CanFloat` doit être posé sur les deux. Quand un dock ne contient qu'un panneau, sa bande
d'onglets est masquée et c'est sa barre de chrome qui sert de poignée : glisser cette barre
détache le dock, pas le panneau.

Reste à insérer le dock dans un conteneur. Le séparateur n'est pas décoratif, sans lui la
rangée n'est plus redimensionnable.

```csharp
var controlsRow = new ProportionalDock
{
    Id = StudioDockIds.ControlsRow,
    Orientation = Orientation.Horizontal,
    Proportion = 0.65,
    VisibleDockables = CreateList<IDockable>(
        statusDock, new ProportionalDockSplitter(),
        streamControlsDock, new ProportionalDockSplitter(),
        logDock),
};
```

## 6. Fournir le contexte

Dans `InitLayout()` de la même fabrique, sinon la vue n'a pas de `DataContext`.

```csharp
[StudioDockIds.Log] = () => paneContext,
```

`paneContext` est le `StudioViewModel` passé par `StudioDockViewModel`. Un panneau qui a
besoin de son propre ViewModel demande de faire descendre cet objet jusqu'à la fabrique, qui
n'en accepte qu'un aujourd'hui.

## Optionnel : la taille minimale

Dans `Castor.Studio/Docking/StudioDockSizing.cs`, en fractions de la zone de travail de
l'écran. Sans entrée, la valeur par défaut s'applique.

```csharp
[StudioDockIds.Log] = (0.14, 0.05),
```

## Ce qui fonctionne sans rien faire de plus

Le détachement et le rattachement. Dès lors que le panneau figure dans `CreateLayout()` avec
un identifiant et `CanFloat`, il peut être sorti dans sa propre fenêtre et y revenir : la
logique de retour relit une copie vierge de la disposition par défaut pour savoir où chaque
élément habite, et pour lui rendre sa taille. `CreateLayout` reste donc la seule description
de l'espace de travail.

Les gestes de détachement et de rattachement valent aussi pour lui : le double-clic est géré
une fois pour toutes dans `StudioDockView`, le glisser ne dépend que de `CanFloat`, et les
entrées « Détacher » et « Rattacher » viennent des menus déclarés dans `Styles/DockMenus.axaml`,
qui remplacent ceux de Dock pour tous les panneaux à la fois. `StudioPanelBar` décide de ce qui
compte comme barre de panneau, pour la fenêtre principale comme pour les fenêtres détachées ;
un panneau construit sur `Tool` ou `Document` y est reconnu sans rien ajouter.

L'enregistrement de la disposition, la restauration au démarrage et le thème sont également
automatiques. Le panneau apparaît aussi de lui-même dans le menu « Panneaux » de la barre de
menus, qui est construit à partir de la disposition par défaut.

## Deux pièges

**Une disposition enregistrée masque le nouveau panneau.** Le fichier
`%APPDATA%\castor-studio\dock-layout.json` est prioritaire sur `CreateLayout()`. Tant qu'il
existe, le panneau ajouté reste invisible. Le supprimer suffit pendant le développement,
mais les utilisateurs déjà équipés ne le verront pas non plus après mise à jour. Si le cas se
répète, il faudra versionner la disposition et repartir du défaut quand la version change.

**Un panneau fermable revient quand même.** `OnWindowClosing` rend systématiquement les
panneaux d'une fenêtre détachée à la fenêtre principale, parce qu'aucun panneau actuel ne peut
être fermé et qu'ils seraient sinon perdus. Ajouter un panneau avec `CanClose = true` demande
de conditionner ce retour aux seuls panneaux non fermables.

## Vérifier

```powershell
dotnet build Castor-Studio.sln -c Debug
dotnet test Castor-Studio.sln -c Debug
```

`Castor.Studio.Tests/StudioDockFloatingTests.cs` couvre le détachement et le rattachement à
partir de la disposition par défaut, sans fenêtre réelle : un panneau ajouté y est pris en
compte automatiquement.
