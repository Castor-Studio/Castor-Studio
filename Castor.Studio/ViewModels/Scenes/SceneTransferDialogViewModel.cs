using System.Collections.ObjectModel;
using System.ComponentModel;
using CastorApplication.Models.Studio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CastorApplication.ViewModels.Scenes;

public enum SceneTransferMode
{
    Import,
    Export
}

// One scene offered by the import or export dialog.
public sealed partial class SceneTransferItem : ObservableObject
{
    public SceneDefinition Definition { get; }
    public string Name => Definition.Name;
    public string Detail { get; }

    [ObservableProperty] private bool _isChecked = true;

    // Set on import only, before anything is written: what the scene runs into in the project.
    [ObservableProperty] private string _collision = "";

    public bool HasCollision => Collision.Length > 0;

    // Same scene as one already in the project (same identity, not just the same name).
    public bool IsAlreadyInProject { get; internal set; }

    public SceneTransferItem(SceneDefinition definition, string origin = "")
    {
        Definition = definition;
        var sources = definition.Sources.Count switch
        {
            0 => "aucune source",
            1 => "1 source",
            var count => $"{count} sources"
        };
        Detail = origin.Length == 0 ? sources : $"{sources} · {origin}";
    }

    partial void OnCollisionChanged(string value) => OnPropertyChanged(nameof(HasCollision));
}

// The step between choosing a file and writing anything: which scenes are imported, or exported.
// Closing it any other way than confirming writes nothing.
public sealed partial class SceneTransferDialogViewModel : ViewModelBase
{
    private readonly IReadOnlyCollection<SceneItemViewModel> _existing;

    public SceneTransferMode Mode { get; }
    public ObservableCollection<SceneTransferItem> Items { get; } = [];

    // Files that could not be read, shown above the list; the others are still offered.
    public IReadOnlyList<string> FileErrors { get; }
    public bool HasFileErrors => FileErrors.Count > 0;
    public string FileErrorsText => string.Join("\n", FileErrors);

    public string Title => Mode == SceneTransferMode.Import ? "IMPORTER DES SCÈNES" : "EXPORTER DES SCÈNES";
    public string ConfirmLabel => Mode == SceneTransferMode.Import ? "Importer" : "Exporter…";

    public int CheckedCount => Items.Count(item => item.IsChecked);
    public bool CanConfirm => CheckedCount > 0;
    public bool AreAllChecked => Items.Count > 0 && Items.All(item => item.IsChecked);

    public string Summary
    {
        get
        {
            var verb = Mode == SceneTransferMode.Import ? "importée" : "exportée";
            return CheckedCount switch
            {
                0 => "Aucune scène cochée.",
                1 => $"1 scène sur {Items.Count} sera {verb}.",
                var count => $"{count} scènes sur {Items.Count} seront {verb}s."
            };
        }
    }

    public event Action<bool>? CloseRequested;

    private SceneTransferDialogViewModel(
        SceneTransferMode mode,
        IReadOnlyCollection<SceneItemViewModel> existing,
        IEnumerable<SceneTransferItem> items,
        IReadOnlyList<string> fileErrors)
    {
        Mode = mode;
        _existing = existing;
        FileErrors = fileErrors;
        foreach (var item in items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
            Items.Add(item);
        }

        RefreshCollisions();
    }

    // Everything found in the files is checked. Collisions are flagged against the project and
    // against the other checked scenes of the same import.
    public static SceneTransferDialogViewModel ForImport(
        IReadOnlyCollection<SceneItemViewModel> existing,
        IEnumerable<SceneTransferItem> found,
        IReadOnlyList<string>? fileErrors = null) =>
        new(SceneTransferMode.Import, existing, found, fileErrors ?? []);

    // The current selection is pre-checked; with nothing selected, every scene is, which is
    // what the export has always written.
    public static SceneTransferDialogViewModel ForExport(IReadOnlyCollection<SceneItemViewModel> scenes)
    {
        var anySelected = scenes.Any(scene => scene.IsMultiSelected);
        var items = scenes.Select(scene => new SceneTransferItem(scene.ToDefinition())
        {
            IsChecked = !anySelected || scene.IsMultiSelected
        });
        return new(SceneTransferMode.Export, [], items, []);
    }

    public IReadOnlyList<SceneTransferItem> CheckedItems => Items.Where(item => item.IsChecked).ToList();

    [RelayCommand]
    private void ToggleAll()
    {
        var check = !AreAllChecked;
        foreach (var item in Items) item.IsChecked = check;
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    [RelayCommand]
    private void Confirm()
    {
        if (CanConfirm) CloseRequested?.Invoke(true);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SceneTransferItem.IsChecked)) return;

        RefreshCollisions();
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(AreAllChecked));
        OnPropertyChanged(nameof(Summary));
    }

    private void RefreshCollisions()
    {
        if (Mode != SceneTransferMode.Import) return;

        var existingIds = _existing.Select(scene => scene.Id).ToHashSet();
        var existingNames = _existing.Select(scene => Normalize(scene.Name)).ToHashSet();
        var checkedNames = Items
            .Where(item => item.IsChecked)
            .GroupBy(item => Normalize(item.Name))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var item in Items)
        {
            item.IsAlreadyInProject = existingIds.Contains(item.Definition.Id);
            var name = Normalize(item.Name);
            item.Collision = item.IsAlreadyInProject
                ? "Déjà dans le projet : elle sera importée comme copie."
                : existingNames.Contains(name)
                    ? "Une scène du projet porte déjà ce nom : les deux seront conservées."
                    : item.IsChecked && checkedNames.Contains(name)
                        ? "Ce nom revient plusieurs fois dans l'import."
                        : "";
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
