using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CodexVsix.Models;

public sealed class ChatMessage : INotifyPropertyChanged
{
    private const int MaxMessageTextLength = 60000;
    private const int MaxMessageDetailLength = 20000;
    private const string OmittedMiddleMarker = "\n\n[...]\n\n";
    private const string TruncatedDisplayNotice = "\n\n[Truncated in the Visual Studio extension to keep the chat responsive. The full transcript remains in Codex session history.]";

    private string _text;
    private string _displayText;
    private string _customDisplayText = string.Empty;
    private string? _title;
    private string? _detail;
    private bool _hasCustomDisplayText;
    private bool _isTextTruncated;
    private bool _isDetailTruncated;
    private bool _isCustomDisplayTextTruncated;
    private bool _renderMarkdown;

    public ChatMessage(
        bool isUser,
        string text,
        bool isEvent = false,
        string? title = null,
        string? detail = null,
        bool? supportsMarkdownText = null,
        bool supportsMarkdownDetail = false)
    {
        IsUser = isUser;
        IsEvent = isEvent;
        SupportsMarkdownText = supportsMarkdownText ?? (!isUser && !isEvent);
        SupportsMarkdownDetail = supportsMarkdownDetail;
        _title = title;
        _detail = ClampForDisplay(detail, MaxMessageDetailLength, out _isDetailTruncated);
        _text = ClampForDisplay(text, MaxMessageTextLength, out _isTextTruncated) ?? string.Empty;
        _displayText = BuildDisplayText(_text, _isTextTruncated);
        _renderMarkdown = SupportsMarkdownText || SupportsMarkdownDetail;
        PromptSkillNames.CollectionChanged += HandlePromptSkillNamesChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsUser { get; }

    public bool IsEvent { get; }

    public bool SupportsMarkdownText { get; }

    public bool SupportsMarkdownDetail { get; }

    public string? Title
    {
        get => _title;
        set
        {
            _title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTitle));
            OnPropertyChanged(nameof(HasHeader));
        }
    }

    public string? Detail
    {
        get => _detail;
        set
        {
            _detail = ClampForDisplay(value, MaxMessageDetailLength, out _isDetailTruncated);
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayDetail));
            OnPropertyChanged(nameof(HasDetail));
            OnPropertyChanged(nameof(IsDetailTruncated));
            OnPropertyChanged(nameof(CanToggleMarkdownView));
            OnPropertyChanged(nameof(HasHeader));
            OnPropertyChanged(nameof(ShowMarkdownDetail));
            OnPropertyChanged(nameof(ShowPlainDetail));
        }
    }

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    public string? DisplayDetail => BuildDisplayDetail(_detail, _isDetailTruncated);

    public bool IsTextTruncated => _isTextTruncated;

    public bool IsDetailTruncated => _isDetailTruncated;

    public bool HasHeader => HasTitle || CanToggleMarkdownView;

    public ObservableCollection<string> PromptSkillNames { get; } = new();

    public bool HasPromptSkillNames => PromptSkillNames.Count > 0;

    public string DisplayText
    {
        get => _displayText;
        private set
        {
            _displayText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDisplayText));
        }
    }

    public bool HasDisplayText => !string.IsNullOrWhiteSpace(DisplayText);

    public bool RenderMarkdown
    {
        get => _renderMarkdown;
        set
        {
            if (_renderMarkdown == value)
            {
                return;
            }

            _renderMarkdown = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTextMode));
            OnPropertyChanged(nameof(IsRenderedMode));
            OnPropertyChanged(nameof(ShowMarkdownText));
            OnPropertyChanged(nameof(ShowPlainDetail));
            OnPropertyChanged(nameof(ShowMarkdownDetail));
        }
    }

    public bool IsTextMode => !RenderMarkdown;

    public bool IsRenderedMode => RenderMarkdown;

    public bool CanToggleMarkdownView =>
        (SupportsMarkdownText && !string.IsNullOrWhiteSpace(Text))
        || (SupportsMarkdownDetail && HasDetail);

    public bool ShowMarkdownText => SupportsMarkdownText && RenderMarkdown;

    public bool ShowMarkdownDetail => SupportsMarkdownDetail && HasDetail && RenderMarkdown;

    public bool ShowPlainDetail => HasDetail && !ShowMarkdownDetail;

    public string Text
    {
        get => _text;
        set
        {
            _text = ClampForDisplay(value, MaxMessageTextLength, out _isTextTruncated) ?? string.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTextTruncated));
            OnPropertyChanged(nameof(CanToggleMarkdownView));
            OnPropertyChanged(nameof(HasHeader));
            OnPropertyChanged(nameof(ShowMarkdownText));
            RefreshDisplayText();
        }
    }

    public void ApplyPromptSkillDisplay(System.Collections.Generic.IEnumerable<string> skillNames, string? displayText)
    {
        _hasCustomDisplayText = false;
        PromptSkillNames.Clear();

        foreach (var skillName in skillNames.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            PromptSkillNames.Add(skillName);
        }

        _customDisplayText = ClampForDisplay(displayText, MaxMessageTextLength, out _isCustomDisplayTextTruncated) ?? string.Empty;
        _hasCustomDisplayText = PromptSkillNames.Count > 0
            || _isCustomDisplayTextTruncated
            || !string.Equals(_customDisplayText, _text, System.StringComparison.Ordinal);
        RefreshDisplayText();
    }

    private void HandlePromptSkillNamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasPromptSkillNames));
    }

    public void ToggleMarkdownView()
    {
        if (!CanToggleMarkdownView)
        {
            return;
        }

        RenderMarkdown = !RenderMarkdown;
    }

    public void SetMarkdownView(bool renderMarkdown)
    {
        if (!CanToggleMarkdownView)
        {
            return;
        }

        RenderMarkdown = renderMarkdown;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void RefreshDisplayText()
    {
        if (_hasCustomDisplayText)
        {
            DisplayText = BuildDisplayText(_customDisplayText, _isCustomDisplayTextTruncated || _isTextTruncated);
            return;
        }

        DisplayText = BuildDisplayText(_text, _isTextTruncated);
    }

    private static string BuildDisplayText(string value, bool truncated)
    {
        return truncated ? value + TruncatedDisplayNotice : value;
    }

    private static string? BuildDisplayDetail(string? value, bool truncated)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return truncated ? value + TruncatedDisplayNotice : value;
    }

    private static string? ClampForDisplay(string? value, int maxLength, out bool truncated)
    {
        truncated = false;
        if (value is null)
        {
            return null;
        }

        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        truncated = true;
        var markerBudget = OmittedMiddleMarker.Length;
        var contentBudget = System.Math.Max(0, maxLength - markerBudget);
        var headLength = contentBudget / 2;
        var tailLength = contentBudget - headLength;

        return value.Substring(0, headLength).TrimEnd()
            + OmittedMiddleMarker
            + value.Substring(value.Length - tailLength).TrimStart();
    }
}
