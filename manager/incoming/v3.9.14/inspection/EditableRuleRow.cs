using FreeCamManager.Core.Models;

namespace FreeCamManager.ViewModels;

/// <summary>Editable settings row; persistence only occurs when the user saves.</summary>
public sealed class EditableRuleRow : ObservableObject
{
    private static readonly (string Value, string Label)[] FieldLabels =
    [
        ("FileName", "文件名"),
        ("ArtifactType", "文件类型"),
        ("BuildType", "构建类型"),
        ("Branch", "分支"),
        ("ReleaseState", "发布状态"),
        ("StableLike", "稳定版候选"),
        ("FeatureLike", "正式功能"),
        ("ExperimentLike", "实验 / 测试")
    ];

    private static readonly (string Value, string Label)[] BuiltInCategoryLabels =
    [
        ("Manager", "管理器"),
        ("IndexLibrary", "索引库"),
        ("StableCandidate", "稳定版候选"),
        ("Feature", "正式功能"),
        ("Experiment", "实验 / 测试")
    ];

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

    public static IReadOnlyList<string> AllFieldLabels { get; } = FieldLabels.Select(x => x.Label).ToArray();
    public static IReadOnlyList<string> DefaultCategoryLabels { get; } = BuiltInCategoryLabels.Select(x => x.Label).ToArray();

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Pattern { get => _pattern; set => SetProperty(ref _pattern, value); }
    public string Field
    {
        get => _field;
        set
        {
            if (!SetProperty(ref _field, value)) return;
            OnPropertyChanged(nameof(FieldLabel));
        }
    }
    public string FieldLabel
    {
        get => ToFieldLabel(Field);
        set => Field = FromFieldLabel(value);
    }
    public string Category
    {
        get => _category;
        set
        {
            if (!SetProperty(ref _category, value)) return;
            OnPropertyChanged(nameof(CategoryLabel));
        }
    }
    public string CategoryLabel
    {
        get => ToCategoryLabel(Category);
        set => Category = FromCategoryLabel(value);
    }
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

    public static string ToFieldLabel(string? value)
        => MapToLabel(FieldLabels, value);
    public static string FromFieldLabel(string? value)
        => MapFromLabel(FieldLabels, value);
    public static string ToCategoryLabel(string? value)
        => MapToLabel(BuiltInCategoryLabels, value);
    public static string FromCategoryLabel(string? value)
        => MapFromLabel(BuiltInCategoryLabels, value);

    private static string MapToLabel((string Value, string Label)[] map, string? raw)
    {
        var value = raw?.Trim() ?? "";
        var hit = map.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(hit.Label) ? value : hit.Label;
    }

    private static string MapFromLabel((string Value, string Label)[] map, string? raw)
    {
        var value = raw?.Trim() ?? "";
        var byLabel = map.FirstOrDefault(x => string.Equals(x.Label, value, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(byLabel.Value)) return byLabel.Value;
        var byValue = map.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(byValue.Value) ? value : byValue.Value;
    }
}
