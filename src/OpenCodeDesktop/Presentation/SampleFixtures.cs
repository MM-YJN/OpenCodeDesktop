using System.Collections.Generic;

namespace OpenCodeDesktop.Presentation;

internal static class SampleFixtures
{
    public static IReadOnlyDictionary<string, string> Files { get; } = new Dictionary<string, string>
    {
        ["src/RecipeList.xaml"] = "<Grid RowDefinitions=\"Auto,*,Auto\">\n    <TextBox PlaceholderText=\"Filter recipes\" />\n    <ListView ItemsSource=\"{Binding Recipes}\" />\n</Grid>",
        ["src/RecipeFilter.cs"] = "// Sample only — no local path is read.\npublic void Search(string query)\n{\n    FilterRecipes(query);\n}",
        ["docs/notes.md"] = "# Recipe tags\n\nFilter recipes by category or ingredient.\nKeep uncategorized recipes visible.\n\nThis is embedded sample content.",
        ["assets/logo.png"] = "Binary / unsupported preview. Sample image content is not loaded.",
        ["missing/deleted.cs"] = "Unavailable: this sample file no longer exists on the sample server.",
    };

    public static string[] Changes(int comparison) => comparison switch
    {
        0 => ["src/RecipeList.xaml", "src/RecipeFilter.cs"],
        1 => ["docs/notes.md"],
        _ => [],
    };

    public static string Basis(int comparison) => comparison switch
    {
        0 => "Sample latest session turn: before turn → after turn",
        1 => "Sample working copy: HEAD → uncommitted edits",
        _ => "Sample branch: main…sample/recipe-tags (no changes)",
    };

    public static string Diff(string path, int comparison)
        => "--- a/" + path + "\n+++ b/" + path + "\n@@ -1,3 +1,4 @@\n"
        + (comparison == 1
            ? " # Recipe tags\n-Old notes\n+Filter recipes by category or ingredient.\n+Keep uncategorized recipes visible."
            : path.EndsWith(".cs", System.StringComparison.Ordinal)
                ? " // Sample recipe filter\n-ShowAll();\n+FilterRecipes(query);\n+UpdateEmptyState();"
                : " <Grid>\n-    <TextBlock Text=\"Recipes\" />\n+    <TextBox PlaceholderText=\"Filter recipes\" />\n+    <ListView ItemsSource=\"{Binding Recipes}\" />\n </Grid>");

    public static string Terminal(int selection) => selection == 0
        ? "Recorded sample output, no process\n> dotnet test\nSample: 12 passed, 0 failed\nThis output is illustrative, not a verification result."
        : "Recorded sample output, no process\n> build\nSample build waiting for input…\nNo shell or PTY has been started.";
}
