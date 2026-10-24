using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace OpenCodeDesktop.Core.Workspaces;

public static class TaskFormValidation
{
    public static bool Visible(TaskField field, IReadOnlyDictionary<string, object> answers)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(answers);
        return field.When.All(c =>
        c.Operator switch
        {
            "eq" => answers.TryGetValue(c.Key, out object? value) && Equal(value, c.Value),
            "neq" => !answers.TryGetValue(c.Key, out object? value) || !Equal(value, c.Value),
            _ => false
        });
    }

    private static bool Equal(object left, object right) => left is double a && right is double b ? a == b : Equals(left, right);

    public static string? Validate(TaskForm form, IReadOnlyDictionary<string, object> answers)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(answers);
        if (form.Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != form.Fields.Count)
        {
            return "This form has duplicate field keys. Cancel it or refresh.";
        }

        foreach (TaskField field in form.Fields)
        {
            string label = field.Title ?? field.Key;
            if (field.When.Any(c => c.Operator is not ("eq" or "neq")))
            {
                return label + ": unsupported condition. Cancel or refresh.";
            }

            if (!Visible(field, answers))
            {
                continue;
            }

            if (field.Type is not ("string" or "number" or "integer" or "boolean" or "multiselect" or "external"))
            {
                return label + ": unsupported control. Cancel or refresh.";
            }

            if (field.Type == "external" && (!Uri.TryCreate(field.Url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")))
            {
                return label + ": unsupported external address. Cancel or refresh.";
            }

            if (!answers.TryGetValue(field.Key, out object? value) || value is string { Length: 0 })
            {
                if (field.Required || field.Type == "external")
                {
                    return label + (field.Hidden ? ": a required hidden value is unavailable. Cancel or refresh." : ": an answer is required.");
                }

                continue;
            }
            if (!ValueValid(field, value))
            {
                return label + ": the answer does not satisfy the declared type, options, format or bounds.";
            }
        }
        return null;
    }

    private static bool ValueValid(TaskField field, object value) => field.Type switch
    {
        "string" => value is string text && StringValid(field, text),
        "number" or "integer" => value is double number && double.IsFinite(number) &&
            (field.Type != "integer" || Math.Truncate(number) == number) &&
            (!field.Minimum.HasValue || number >= field.Minimum) && (!field.Maximum.HasValue || number <= field.Maximum),
        "boolean" => value is bool,
        "external" => value is true,
        "multiselect" => value is string[] values && values.Distinct(StringComparer.Ordinal).Count() == values.Length &&
            (!field.MinItems.HasValue || values.Length >= field.MinItems) && (!field.MaxItems.HasValue || values.Length <= field.MaxItems) &&
            (field.Custom || values.All(v => field.Options.Any(o => o.Value == v))),
        _ => false,
    };

    private static bool StringValid(TaskField field, string text)
    {
        if (field.MinLength.HasValue && text.Length < field.MinLength || field.MaxLength.HasValue && text.Length > field.MaxLength ||
            field.Options.Count > 0 && !field.Custom && !field.Options.Any(o => o.Value == text))
        {
            return false;
        }

        if (field.Pattern is { } pattern)
        {
            try
            {
                if (!Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
        return field.Format switch
        {
            null => true,
            "email" => System.Net.Mail.MailAddress.TryCreate(text, out MailAddress? mail) && mail.Address == text,
            "uri" => Uri.TryCreate(text, UriKind.Absolute, out _),
            "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "date-time" => text.Contains('T', StringComparison.Ordinal) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => false,
        };
    }
}
