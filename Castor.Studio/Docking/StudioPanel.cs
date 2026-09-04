namespace CastorApplication.Docking;

// A panel the workspace is made of, as the default layout describes it. Enough for a menu to
// offer it and to ask for it back by id.
public sealed record StudioPanel(string Id, string Title);
