using System;
using System.Collections.Generic;
using System.Linq;

using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Presentation;

/// <summary>One provider's header row and its models in the composer model picker.</summary>
internal sealed record TaskModelGroup(string Provider, IReadOnlyList<TaskModel> Models);

/// <summary>Groups the model catalog by provider for the composer picker, with optional search filtering.</summary>
internal static class TaskModelGrouping
{
    /// <summary>Build provider groups ordered alphabetically by display name. Models keep catalog order inside each group.
    /// A non-empty filter keeps models whose name, provider id, or provider display name contains it.</summary>
    internal static IReadOnlyList<TaskModelGroup> Build(IReadOnlyList<TaskModel> models, IReadOnlyList<TaskProvider> providers, string filter)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(providers);
        string needle = filter.Trim();
        return models
            .GroupBy(model => model.ProviderId, StringComparer.Ordinal)
            .Select(group =>
            {
                string display = providers.FirstOrDefault(p => p.Id == group.Key)?.Name ?? group.Key;
                return new TaskModelGroup(display, group
                    .Where(model => needle.Length == 0 || Contains(model.Name) || Contains(display) || Contains(group.Key))
                    .ToArray());

                bool Contains(string value) => value.Contains(needle, StringComparison.OrdinalIgnoreCase);
            })
            .Where(group => group.Models.Count > 0)
            .OrderBy(group => group.Provider, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>The first model offered by the current groups, used as the Enter-key target of the search box.</summary>
    internal static TaskModel? First(IReadOnlyList<TaskModelGroup> groups) => groups.Count == 0 || groups[0].Models.Count == 0 ? null : groups[0].Models[0];
}
