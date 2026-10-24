using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCodeDesktop.Presentation;

// Deliberately has no service, filesystem, process, or persistence dependency.
internal sealed class SampleWorkspace
{
    public List<SampleProject> Projects { get; } = [];
    public SampleSession? Selected { get; set; }
    public IEnumerable<SampleSession> Sessions => Projects.SelectMany(project => project.Sessions);
    public IEnumerable<SampleSession> Attention => Sessions.Where(session => session.PermissionPending || session.QuestionPending);
    public int AttentionCount => Sessions.Sum(session => (session.PermissionPending ? 1 : 0) + (session.QuestionPending ? 1 : 0));

    public SampleWorkspace()
    {
        SampleProject orchard = new("Cinder Orchard", @"C:\Samples\cinder-orchard");
        orchard.Sessions.Add(new("Add recipe tags", "Waiting for approval") { PermissionPending = true });
        orchard.Sessions.Add(new("Clarify pantry quantities", "Waiting for answer") { QuestionPending = true });
        orchard.Sessions.Add(new("Improve ingredient matching", "Running"));
        SampleProject postbox = new("Lunar Postbox", @"C:\Samples\lunar-postbox");
        postbox.Sessions.Add(new("Refresh delivery cards", "Completed"));
        postbox.Sessions.Add(new("Investigate parcel retries", "Failed"));
        postbox.Sessions.Add(new("Explore label styles", "Interrupted") { Archived = true });
        Projects.Add(orchard);
        Projects.Add(postbox);
        Selected = orchard.Sessions[0];
    }

    public SampleProject? Owner(SampleSession session) => Projects.FirstOrDefault(project => project.Sessions.Contains(session));

    public SampleSession Create(SampleProject project)
    {
        SampleSession session = new("New sample task", "Draft") { IsNew = true };
        project.Sessions.Add(session);
        Selected = session;
        return session;
    }

    public SampleSession Fork(SampleSession source)
    {
        SampleSession fork = new(source.Title + " (fork)", "Draft")
        {
            IsNew = source.IsNew,
            Draft = source.Draft,
            Model = source.Model,
            Agent = source.Agent,
            Worktree = source.Worktree,
            Instructions = source.Instructions,
        };
        fork.Messages.AddRange(source.Messages);
        fork.Attachments.AddRange(source.Attachments);
        Owner(source)!.Sessions.Add(fork);
        Selected = fork;
        return fork;
    }

    public void Delete(SampleSession session)
    {
        Owner(session)?.Sessions.Remove(session);
        if (Selected == session)
        {
            Selected = Sessions.FirstOrDefault(candidate => !candidate.Archived);
        }
    }
}

internal sealed class SampleProject(string name, string directory)
{
    public string Name { get; } = name;
    public string Directory { get; } = directory;
    public bool Expanded { get; set; } = true;
    public List<SampleSession> Sessions { get; } = [];
}

internal sealed class SampleSession(string title, string execution)
{
    public string Title { get; set; } = title;
    public bool IsNew { get; set; }
    public string Execution { get; set; } = execution;
    public bool Archived { get; set; }
    public string Draft { get; set; } = "";
    public string Model { get; set; } = "Sample reasoning model";
    public string Agent { get; set; } = "Build (sample)";
    public int SubmissionMode { get; set; }
    public string Worktree { get; set; } = "main";
    public string Instructions { get; set; } = "Follow repository conventions. Explain changes and verification.";
    public bool PermissionPending { get; set; }
    public bool QuestionPending { get; set; }
    public string PermissionResult { get; set; } = "";
    public string QuestionResult { get; set; } = "";
    public string Answer { get; set; } = "";
    public int DetailsTab { get; set; }
    public string SelectedFile { get; set; } = "src/RecipeList.xaml";
    public int Comparison { get; set; }
    public string SelectedChange { get; set; } = "src/RecipeList.xaml";
    public double ConversationOffset { get; set; }
    public double[] DetailsOffsets { get; } = new double[3];
    public List<string> Messages { get; } = [];
    public List<string> Attachments { get; } = [];
    public List<SampleInput> PendingInputs { get; } = [];

    public bool Submit(string mode)
    {
        if (string.IsNullOrWhiteSpace(Draft) && Attachments.Count == 0)
        {
            return false;
        }

        string text = Draft.Trim();
        if (Attachments.Count > 0)
        {
            text += "\nReferences: " + string.Join(", ", Attachments);
        }

        if (mode == "Append locally")
        {
            Messages.Add("You · simulated local message\n" + text);
        }
        else
        {
            PendingInputs.Add(new(text, mode));
        }

        Draft = "";
        Attachments.Clear();
        return true;
    }

    public void RespondPermission(string choice)
    {
        PermissionPending = false;
        PermissionResult = "Sample choice: " + choice + ". No command was executed.";
        ReconcileAttention();
    }

    public bool RespondQuestion(bool cancel)
    {
        if (!cancel && string.IsNullOrWhiteSpace(Answer))
        {
            return false;
        }

        QuestionPending = false;
        QuestionResult = cancel ? "Sample question cancelled." : "Sample answer: " + Answer.Trim();
        ReconcileAttention();
        return true;
    }

    private void ReconcileAttention()
    {
        Execution = PermissionPending || QuestionPending ? "Waiting for input" : "Idle (sample response recorded)";
    }
}

internal sealed class SampleInput(string text, string mode)
{
    public string Text { get; } = text;
    public string Mode { get; set; } = mode;
}
