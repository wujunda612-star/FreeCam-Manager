using FreeCamManager.Core.Models;

namespace FreeCamManager.ViewModels;

/// <summary>Editable settings row; persistence only occurs when the user saves.</summary>
public sealed class EditableRuleRow : ObservableObject
{
    private bool _enabled;
    private string _pattern;
    private string _field;
    private string _category;
    private string _folder;

    public EditableRuleRow(string pattern = "*", string field = "FileName",
        string category = "", string folder = "", bool enabled = true)
    {
        _pattern = pattern;
        _field = field;
        _category = category;
        _folder = folder;
        _enabled = enabled;
    }

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Pattern { get => _pattern; set => SetProperty(ref _pattern, value); }
    public string Field { get => _field; set => SetProperty(ref _field, value); }
    public string Category { get => _category; set => SetProperty(ref _category, value); }
    public string Folder { get => _folder; set => SetProperty(ref _folder, value); }

    public FilePatternRule ToPatternRule() => new() { Pattern = Pattern.Trim(), Enabled = Enabled };
    public ClassificationRule ToClassificationRule() => new()
    {
        Field = Field,
        Pattern = Pattern.Trim(),
        Category = Category.Trim(),
        Folder = Folder.Trim(),
        Enabled = Enabled
    };
    public static EditableRuleRow From(FilePatternRule x)
        => new(x.Pattern, enabled: x.Enabled);
    public static EditableRuleRow From(ClassificationRule x)
        => new(x.Pattern, x.Field, x.Category, x.Folder, x.Enabled);
}
