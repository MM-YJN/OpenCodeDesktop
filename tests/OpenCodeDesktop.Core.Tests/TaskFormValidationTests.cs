using OpenCodeDesktop.Core.Workspaces;

namespace OpenCodeDesktop.Core.Tests;

public sealed class TaskFormValidationTests
{
    [Theory]
    [InlineData("number", 2.5, true)]
    [InlineData("integer", 2.5, false)]
    [InlineData("integer", 2.0, true)]
    [InlineData("number", 20.0, false)]
    [InlineData("number", double.NaN, false)]
    public void NumericBoundsAndTypesAreEnforced(string type, double value, bool valid)
    {
        TaskForm form = Form(new("x", type) { Required = true, Minimum = 1, Maximum = 4 });
        Assert.Equal(valid, TaskFormValidation.Validate(form, new Dictionary<string, object> { ["x"] = value }) is null);
    }
    [Fact]
    public void BooleanFalseIsAValidAnswerButExternalRequiresExplicitTrue()
    {
        Assert.Null(TaskFormValidation.Validate(Form(new("x", "boolean") { Required = true }), Answers(false)));
        Assert.NotNull(TaskFormValidation.Validate(Form(new("x", "external") { Url = "https://example.invalid" }), Answers(false)));
        Assert.Null(TaskFormValidation.Validate(Form(new("x", "external") { Url = "https://example.invalid" }), Answers(true)));
        Assert.NotNull(TaskFormValidation.Validate(Form(new("x", "external") { Url = "file:///C:/x" }), Answers(true)));
    }
    [Fact]
    public void HiddenRequiredFieldsAreNotSilentlyOmittedAndConditionsUseTypedValues()
    {
        Assert.Contains("hidden", TaskFormValidation.Validate(Form(new("x", "string") { Required = true, Hidden = true }), new Dictionary<string, object>())!, StringComparison.Ordinal);
        TaskForm form = Form(new("x", "string") { Required = true, When = [new("enabled", "eq", true)] });
        Assert.Null(TaskFormValidation.Validate(form, new Dictionary<string, object> { ["enabled"] = false }));
        Assert.NotNull(TaskFormValidation.Validate(form, new Dictionary<string, object> { ["enabled"] = true }));
    }
    [Theory]
    [InlineData("email", "a@example.invalid", true)]
    [InlineData("email", "bad", false)]
    [InlineData("uri", "https://example.invalid", true)]
    [InlineData("date", "2026-02-31", false)]
    [InlineData("date", "2026-10-24", true)]
    [InlineData("date-time", "2026-10-24T10:00:00+08:00", true)]
    [InlineData("date-time", "2026-10-24", false)]
    public void DeclaredFormatsAreValidated(string format, string text, bool valid)
        => Assert.Equal(valid, TaskFormValidation.Validate(Form(new("x", "string") { Format = format }), Answers(text)) is null);
    [Fact]
    public void OptionsCustomBoundsPatternsAndUnknownFieldsAreHandled()
    {
        TaskField field = new("x", "string") { Options = [new("a", "A")], MinLength = 1, MaxLength = 3, Pattern = "^[a-z]+$" };
        Assert.NotNull(TaskFormValidation.Validate(Form(field), Answers("b")));
        Assert.Null(TaskFormValidation.Validate(Form(field with { Custom = true }), Answers("b")));
        Assert.NotNull(TaskFormValidation.Validate(Form(field with { Pattern = "[" }), Answers("a")));
        TaskField multi = new("x", "multiselect") { Options = [new("a", "A"), new("b", "B")], MinItems = 1, MaxItems = 1 };
        Assert.Null(TaskFormValidation.Validate(Form(multi), Answers(new[] { "a" })));
        Assert.NotNull(TaskFormValidation.Validate(Form(multi), Answers(new[] { "a", "b" })));
        Assert.NotNull(TaskFormValidation.Validate(Form(new("x", "future")), Answers("value")));
    }
    private static TaskForm Form(TaskField field) => new("frm", "s", "Question", [field]);
    private static Dictionary<string, object> Answers(object value) => new() { ["x"] = value };
}
