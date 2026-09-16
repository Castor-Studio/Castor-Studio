namespace CastorApplication.ViewModels.Scenes;

// 24x24 stroke icons (Tabler style) shared by the "Ajouter une source" dialog and the source
// list, so a source keeps the same icon from the moment it is picked.
public static class SourceIcons
{
    public const string Monitor = "M3 5a1 1 0 0 1 1 -1h16a1 1 0 0 1 1 1v10a1 1 0 0 1 -1 1h-16a1 1 0 0 1 -1 -1z M7 20h10 M9 16v4 M15 16v4";
    public const string Window = "M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z M3 9h18 M6 7v.01";
    public const string Camera = "M5 7h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2 M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0";
    public const string Volume = "M15 8a5 5 0 0 1 0 8 M17.7 5a9 9 0 0 1 0 14 M6 15h-2a1 1 0 0 1 -1 -1v-4a1 1 0 0 1 1 -1h2l3.5 -4.5a.8 .8 0 0 1 1.5 .5v14a.8 .8 0 0 1 -1.5 .5z";
    public const string Mic = "M9 5a3 3 0 0 1 6 0v5a3 3 0 0 1 -6 0z M5 10a7 7 0 0 0 14 0 M8 21h8 M12 17v4";
    public const string Folder = "M5 4h4l3 3h7a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2";
    public const string Movie = "M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z M8 4v16 M16 4v16 M4 8h4 M16 8h4 M4 16h4 M16 16h4 M4 12h16";
}
