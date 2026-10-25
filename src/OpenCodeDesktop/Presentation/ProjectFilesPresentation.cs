using System;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

internal sealed record ProjectFilesPresentation(string Listing, string SelectedPath, string PreviewStatus, string Text)
{
    internal static ProjectFilesPresentation From(ProjectFilesState state)
    {
        string listing = state.Loading ? "Loading…" : state.Error ?? (state.Entries.Count == 0 ? "No files found." :
            state.Entries.Count + (state.Query.Length == 0 ? " entries" : " search results") + (state.Limited ? " · Limit reached; more may exist." : ""));
        string selected = state.SelectedPath is { } path ? FullPath(state.Location.Directory, path) : "Select a file to preview.";
        string status = state.Reading ? "Reading…" : state.PreviewError ?? (state.Preview is { } preview ?
            preview.Kind == FilePreviewKind.Unsupported ? "Binary or unsupported encoding. No text preview." :
            preview.Truncated ? "Partial preview · Content exceeds the preview limit." : preview.Text.Length == 0 ? "Empty text file · Read-only" : "Text preview · Read-only" : "");
        return new(listing, selected, status, state.Preview?.Text ?? "");
    }

    // Display composition only; never normalize paths with the client filesystem.
    internal static string FullPath(string root, string relative)
    {
        bool windows = root.Contains('\\') || root.Length > 1 && root[1] == ':';
        if (relative.StartsWith('/') || windows && (relative.StartsWith('\\') || relative.Length > 1 && relative[1] == ':'))
        {
            return relative;
        }
        return root.TrimEnd('/', '\\') + (windows && !root.Contains('/') ? "\\" : "/") + relative;
    }
}
